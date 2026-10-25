using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Prokjs.TextureOptimizer
{
/// <summary>Explicit, conservative scene inspection and reversible mesh-only LOD authoring.</summary>
public static class WorldLodOptimizerBackend
{
    public enum Scope { LoadedScenes, SelectedObjects }
    [Serializable] public sealed class ScanOptions
    {
        public Scope scope;
        public bool includeInactive;
        public GameObject[] roots;
    }
    [Serializable] public sealed class Row
    {
        public GameObject root;
        public LODGroup group;
        public MeshRenderer[] sources;
        public string name, path, warning;
        public bool editable, recommended;
        public long vertices, triangles;
        public string[] existingLevels;
    }
    [Serializable] public sealed class Settings
    {
        public float[] reductions = { 0f, 50f, 75f };
        public float[] transitions = { .6f, .3f, .1f };
    }
    public sealed class PreviewMesh
    {
        public Mesh source, mesh;
        public MeshRenderer renderer;
        public int level;
        public float reduction;
        public string notice;
    }
    public sealed class Plan : IDisposable
    {
        public List<Row> rows = new List<Row>();
        public List<PreviewMesh> meshes = new List<PreviewMesh>();
        public List<string> notices = new List<string>();
        public Settings settings;
        internal readonly Dictionary<Row, string> fingerprints = new Dictionary<Row, string>();
        internal readonly Dictionary<Mesh, string> meshHashes = new Dictionary<Mesh, string>();
        internal readonly Dictionary<Mesh, string> previewHashes = new Dictionary<Mesh, string>();
        internal string settingsHash;
        internal bool disposed, applied;
        public void Dispose()
        {
            if (disposed) return;
            foreach (Mesh mesh in meshes.Select(p => p.mesh).Where(m => m != null).Distinct())
                if (!EditorUtility.IsPersistent(mesh)) Object.DestroyImmediate(mesh);
            meshes.Clear();
            disposed = true;
        }
    }

    private const string OutputFolder = "Assets/prokjs/WorldOptimizerGenerated";
    private const string ReceiptFolder = "UserSettings/ProkjsWorldOptimizerBatches";
    private const string ProxyPrefix = "LodOptimizer_LOD";
    private const string Operation = "World LOD optimizer";

    private sealed class Inventory
    {
        public readonly List<GameObject> roots = new List<GameObject>();
        public readonly List<Renderer> renderers = new List<Renderer>();
        public readonly List<LODGroup> groups = new List<LODGroup>();
        public readonly Dictionary<Renderer, List<LODGroup>> owners = new Dictionary<Renderer, List<LODGroup>>();
        public readonly HashSet<Renderer> bound = new HashSet<Renderer>();
    }
    [Serializable] private sealed class LevelState
    {
        public float height, fade;
        public string[] renderers;
    }
    [Serializable] private sealed class GroupState
    {
        public bool enabled, animate, billboard;
        public LODFadeMode fadeMode;
        public Vector3 referencePoint;
        public float size;
        public LevelState[] levels;
    }
    [Serializable] private sealed class ProxyState
    {
        public string id, fingerprint;
    }
    [Serializable] private sealed class Entry
    {
        public string root, group, expectedGroup, expectedSources, state = "Preparing";
        public bool createdGroup;
        public GroupState before;
        public string[] sources;
        public List<ProxyState> proxies = new List<ProxyState>();
    }
    [Serializable] private sealed class AssetState
    {
        public string path, guid, hash;
    }
    [Serializable] private sealed class Receipt
    {
        public int version = 1;
        public string project, id, status;
        public List<Entry> entries = new List<Entry>();
        public List<AssetState> assets = new List<AssetState>();
    }

    private static string ProjectPath { get { return Application.dataPath.Replace('\\', '/'); } }
    private static void GuardMutation()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Wait for an idle Unity editor in Edit mode.");
    }
    private static void Progress(string detail, int index, int count)
    {
        if (EditorUtility.DisplayCancelableProgressBar(Operation, detail, count == 0 ? 0f : (float)index / count))
            throw new OperationCanceledException("LOD operation canceled; no further objects were processed.");
    }
    private static GameObject[] ScopeRoots(ScanOptions options)
    {
        if (options == null) throw new ArgumentNullException("options");
        if (options.scope == Scope.SelectedObjects)
        {
            GameObject[] selected = options.roots ?? Selection.gameObjects;
            if (selected.Length == 0) throw new ArgumentException("Select scene objects before analyzing selection.");
            return selected.Where(r => r != null).Distinct().ToArray();
        }
        return LoadedRoots().ToArray();
    }
    private static List<GameObject> LoadedRoots()
    {
        var roots = new List<GameObject>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.IsValid() && scene.isLoaded) roots.AddRange(scene.GetRootGameObjects());
        }
        return roots;
    }
    private static Inventory Inspect(GameObject[] requested)
    {
        var inventory = new Inventory();
        inventory.roots.AddRange(LoadedRoots());
        foreach (var root in requested)
            if (!inventory.roots.Any(r => root == r || root.transform.IsChildOf(r.transform))) inventory.roots.Add(root);
        inventory.renderers.AddRange(inventory.roots.SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).Distinct());
        inventory.groups.AddRange(inventory.roots.SelectMany(r => r.GetComponentsInChildren<LODGroup>(true)).Distinct());
        foreach (LODGroup group in inventory.groups)
            foreach (Renderer renderer in group.GetLODs().SelectMany(l => l.renderers).Where(r => r != null).Distinct())
            {
                if (!inventory.owners.TryGetValue(renderer, out var owners)) inventory.owners.Add(renderer, owners = new List<LODGroup>());
                owners.Add(group);
            }
        var meshUsers = inventory.renderers.Where(r => MeshOf(r) != null).GroupBy(MeshOf).ToDictionary(g => g.Key, g => g.ToArray());
        Component[] components = inventory.roots.SelectMany(r => r.GetComponentsInChildren<Component>(true)).Distinct().ToArray();
        var visitedReferences = new HashSet<Object>();
        var visitedHierarchies = new HashSet<GameObject>();
        for (int i = 0; i < components.Length; i++)
        {
            if ((i & 31) == 0) Progress("Inspecting script/animation ownership", i, components.Length);
            Component component = components[i];
            if (component == null || !(component is MonoBehaviour) && !(component is Animator) && !(component is Animation)) continue;
            using (var serialized = new SerializedObject(component))
            {
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference || property.name == "m_GameObject" || property.name == "m_Script") continue;
                    Object target = property.objectReferenceValue;
                    if (target == null || !visitedReferences.Add(target)) continue;
                    if (target is Mesh mesh && meshUsers.TryGetValue(mesh, out var users))
                        foreach (var user in users) inventory.bound.Add(user);
                    if (target is Renderer renderer) inventory.bound.Add(renderer);
                    GameObject referenced = target as GameObject;
                    if (target is Component referencedComponent) referenced = referencedComponent.gameObject;
                    if (referenced != null && visitedHierarchies.Add(referenced))
                        foreach (Renderer child in referenced.GetComponentsInChildren<Renderer>(true)) inventory.bound.Add(child);
                }
            }
        }
        return inventory;
    }

    public static List<Row> Scan(ScanOptions options)
    {
        try
        {
            GameObject[] roots = ScopeRoots(options);
            Inventory inventory = Inspect(roots);
            var rows = new List<Row>();
            var seen = new HashSet<Object>();
            var renderers = roots.SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).Distinct().ToArray();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!options.includeInactive && !renderer.gameObject.activeInHierarchy) continue;
                Progress("Analyzing " + renderer.name, i, renderers.Length);
                LODGroup group = inventory.owners.TryGetValue(renderer, out var owners) ? owners[0] : null;
                if (!seen.Add(group != null ? (Object)group : renderer)) continue;
                rows.Add(MakeRow(renderer, group, inventory));
            }
            // Include empty groups as honest unsupported rows rather than silently dropping them.
            foreach (LODGroup group in roots.SelectMany(r => r.GetComponentsInChildren<LODGroup>(true)).Distinct())
                if ((options.includeInactive || group.gameObject.activeInHierarchy) && seen.Add(group)) rows.Add(MakeRow(null, group, inventory));
            var repetitions = rows.SelectMany(r => r.sources).Select(MeshOf).Where(m => m != null).GroupBy(m => m).ToDictionary(g => g.Key, g => g.Count());
            foreach (Row row in rows)
            {
                int repeated = row.sources.Where(s => MeshOf(s) != null).Select(s => repetitions[MeshOf(s)]).DefaultIfEmpty(0).Max();
                if (repeated > 1) row.warning += "\nRepeated source mesh: up to " + repeated + " instances in this scope; ranked using aggregate triangle work. Mesh assets are reused at equal requested reductions.";
            }
            return rows.OrderByDescending(r => r.sources.Sum(s => MeshOf(s) == null ? 0L : Triangles(MeshOf(s)) * repetitions[MeshOf(s)]))
                .ThenByDescending(r => r.triangles).ThenByDescending(r => r.sources.Sum(s => s.sharedMaterials.Length)).ThenBy(r => r.path, StringComparer.Ordinal).ToList();
        }
        finally { EditorUtility.ClearProgressBar(); }
    }
    private static Row MakeRow(Renderer renderer, LODGroup group, Inventory inventory)
    {
        LOD[] levels = group == null ? new LOD[0] : group.GetLODs();
        Renderer[] sources = group == null ? new[] { renderer } : levels.Length == 0 ? new Renderer[0] : levels[0].renderers.Where(r => r != null).Distinct().ToArray();
        var row = new Row {
            root = group == null ? renderer.gameObject : group.gameObject, group = group,
            sources = sources.OfType<MeshRenderer>().ToArray(),
            existingLevels = levels.Select((l, i) => "LOD" + i + ": " + l.renderers.Count(r => r != null) + " renderer(s), " + l.renderers.Where(r => r != null).Sum(r => Triangles(MeshOf(r))) + " triangles, transition " + l.screenRelativeTransitionHeight.ToString("0.###")).ToArray()
        };
        row.name = row.root.name;
        row.path = HierarchyPath(row.root.transform);
        row.vertices = sources.Sum(r => MeshOf(r) == null ? 0L : MeshOf(r).vertexCount);
        row.triangles = sources.Sum(r => Triangles(MeshOf(r)));
        var reasons = new List<string>();
        string sceneReason = SceneReason(row.root);
        if (sceneReason != null) reasons.Add(sceneReason);
        if (sources.Length == 0) reasons.Add("No populated LOD0 renderer; cannot reconstruct authored high-detail geometry.");
        if (group != null)
        {
            if (!group.enabled) reasons.Add("LODGroup is disabled; preserve its authored ownership.");
            if (group.transform.GetComponentsInChildren<LODGroup>(true).Any(g => g != group) || AncestorGroup(group.transform.parent) != null)
                reasons.Add("Nested LOD groups require manual ownership resolution.");
            if (levels.SelectMany(l => l.renderers).Any(r => r == null)) reasons.Add("Existing group has missing renderer references; repair them manually first.");
            foreach (Renderer member in levels.SelectMany(l => l.renderers).Where(r => r != null).Distinct())
            {
                if (!(member is MeshRenderer)) reasons.Add("Skinned/particle/line/procedural LOD members are not supported.");
                if (member.gameObject.scene != row.root.scene || !member.transform.IsChildOf(group.transform))
                    reasons.Add("Existing group has external/cross-scene renderer membership; inspection-only.");
                if (inventory.owners.TryGetValue(member, out var memberships) && memberships.Count != 1)
                    reasons.Add("A renderer belongs to competing LOD groups.");
            }
        }
        foreach (Renderer source in sources)
        {
            string reason = SourceReason(source, inventory);
            if (reason != null) reasons.Add(source.name + ": " + reason);
            if (group == null && (AncestorGroup(source.transform) != null || source.GetComponentsInChildren<LODGroup>(true).Length > 0))
                reasons.Add("Ungrouped renderer overlaps an existing ancestor/descendant LOD owner.");
        }
        row.editable = reasons.Count == 0;
        bool adequate = levels.Length >= 3 && levels.Take(3).All(l => l.renderers.Any(r => r != null));
        row.recommended = row.editable && row.triangles >= 1000 && !adequate;
        if (row.editable) reasons.Add(adequate ? "Existing populated LOD coverage retained; only missing requested levels can be filled." : row.triangles >= 1000 ? "Recommended by static geometry estimate (>=1,000 triangles); not measured frame-time or FPS." : "Safe manual candidate, below the automatic 1,000-triangle threshold.");
        reasons.Add("LOD0 has " + sources.Sum(s => s.sharedMaterials.Length) + " material slot(s); static counts do not measure draw calls or overdraw.");
        row.warning = string.Join("\n", reasons.Distinct().ToArray());
        return row;
    }
    private static string SceneReason(GameObject root)
    {
        if (root == null || EditorUtility.IsPersistent(root) || PrefabUtility.IsPartOfPrefabAsset(root)) return "Prefab/project assets are read-only; select a loaded scene instance.";
        if (!root.scene.IsValid() || !root.scene.isLoaded || EditorSceneManager.IsPreviewScene(root.scene)) return "Only loaded non-preview scene instances may be modified.";
        if (string.IsNullOrEmpty(root.scene.path)) return "Save this scene before authoring persistent reversible LODs.";
        if ((root.hideFlags & (HideFlags.NotEditable | HideFlags.DontSave)) != 0) return "Hidden/noneditable/temporary scene object is read-only.";
        return null;
    }
    private static string SourceReason(Renderer source, Inventory inventory)
    {
        if (!(source is MeshRenderer renderer)) return "Only static MeshRenderer + MeshFilter geometry is supported; no skinned/particle/procedural substitution.";
        if (SceneReason(source.gameObject) != null) return SceneReason(source.gameObject);
        if (WorldOptimizerPolicy.IsGenerated(source)) return "Generated/regenerator-owned geometry is read-only.";
        if (WorldOptimizerPolicy.IsGraphics(source)) return "UI, text, signage or readable graphics must retain original geometry.";
        if (WorldOptimizerPolicy.HasDynamicAncestor(source.transform)) return "Pickup/Udon/animation/dynamic hierarchy owns this renderer.";
        for (Transform t = source.transform; t != null; t = t.parent)
            if (t.GetComponents<MonoBehaviour>().Any() || t.GetComponents<Component>().Any(c => c == null)) return "Script-controlled hierarchy or missing script; renderer substitution is not safe.";
        if (inventory.bound.Contains(source)) return "A script/animation references this renderer, mesh, filter or hierarchy; preserve its ownership.";
        if (!source.enabled || source.forceRenderingOff) return "Disabled/force-off authored renderer must not be bypassed by proxies.";
        if (source.HasPropertyBlock() || renderer.additionalVertexStreams != null) return "Property blocks/additional vertex streams cannot be safely inherited.";
        if (source.lightmapIndex >= 0 && source.lightmapIndex < 65534 || source.realtimeLightmapIndex >= 0 && source.realtimeLightmapIndex < 65534)
            return "Lightmap-baked/realtime-lightmapped renderer is inspection-only; reduced interpolation cannot guarantee the authored lightmap UV result.";
        Mesh mesh = MeshOf(source);
        if (mesh == null || !mesh.isReadable || mesh.vertexCount < 3) return "Mesh is missing/non-readable/empty; original import settings are never changed.";
        if (mesh.blendShapeCount != 0 || mesh.bindposes.Length != 0 || mesh.HasVertexAttribute(VertexAttribute.BlendIndices) || mesh.HasVertexAttribute(VertexAttribute.BlendWeight)) return "Skin, blendshape or bind-pose mesh is not supported.";
        if (renderer.sharedMaterials.Length != mesh.subMeshCount) return "Material/submesh mapping is not one-to-one.";
        for (int i = 0; i < mesh.subMeshCount; i++)
            if (mesh.GetTopology(i) != MeshTopology.Triangles || mesh.GetIndexCount(i) == 0) return "Only populated triangle submeshes are supported.";
        foreach (Material material in renderer.sharedMaterials)
        {
            if (material == null || material.shader == null || material.renderQueue > 2500 || material.GetTag("DisableBatching", false, "False") == "True" || material.IsKeywordEnabled("_ALPHATEST_ON") || material.IsKeywordEnabled("_ALPHABLEND_ON") || material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"))
                return "Transparent/cutout/deforming/unknown material retains original geometry.";
            string shader = material.shader.name;
            if (shader != "Standard" && shader != "Unlit/Texture" && shader != "Unlit/Color")
                return "Custom shader is outside the verified built-in non-displacing opaque allowlist.";
        }
        return null;
    }

    public static Plan Preview(IList<Row> selected, Settings settings)
    {
        if (selected == null) throw new ArgumentNullException("selected");
        ValidateSettings(settings);
        var plan = new Plan { settings = new Settings { reductions = (float[])settings.reductions.Clone(), transitions = (float[])settings.transitions.Clone() } };
        var cache = new Dictionary<string, Mesh>();
        try
        {
            Inventory inventory = Inspect(selected.Where(r => r != null && r.root != null).Select(r => r.root).ToArray());
            foreach (Row old in selected.Where(r => r != null).Distinct())
            {
                if (old.root == null) { plan.notices.Add("An analyzed object no longer exists; analyze again."); continue; }
                if (old.group == null && old.root.GetComponent<Renderer>() == null) { plan.notices.Add(old.path + ": renderer/LOD owner was removed; analyze again."); continue; }
                Row row = MakeRow(old.group == null ? old.root.GetComponent<Renderer>() : null, old.group, inventory);
                if (!row.editable) { plan.notices.Add(row.path + ": skipped. " + row.warning); continue; }
                string transitionReason = TransitionReason(row, plan.settings);
                if (transitionReason != null) { plan.notices.Add(row.path + ": " + transitionReason); continue; }
                var pending = new List<PreviewMesh>();
                bool useful = true;
                for (int level = 1; level < settings.reductions.Length; level++)
                {
                    if (!MissingLevel(row.group, level)) continue;
                    foreach (MeshRenderer renderer in row.sources)
                    {
                        Progress("Preview " + row.name + " / LOD" + level, plan.rows.Count, selected.Count);
                        Mesh source = MeshOf(renderer);
                        if (!plan.meshHashes.ContainsKey(source)) plan.meshHashes.Add(source, MeshHash(source));
                        string key = source.GetInstanceID() + ":" + settings.reductions[level].ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                        if (!cache.TryGetValue(key, out Mesh reduced))
                        {
                            reduced = WorldLodMeshReducer.ReduceVertices(source, 1f - settings.reductions[level] / 100f);
                            if (reduced == null) throw new InvalidOperationException("Mesh reducer returned no geometry for " + renderer.name);
                            reduced.name = source.name + "_LOD" + level + "_" + settings.reductions[level].ToString("0.#") + "pct";
                            reduced.hideFlags = HideFlags.HideAndDontSave;
                            cache.Add(key, reduced);
                        }
                        if (reduced.vertexCount >= source.vertexCount || Triangles(reduced) >= Triangles(source) || reduced.vertexCount < 3 || reduced.subMeshCount != source.subMeshCount) useful = false;
                        int target = Mathf.CeilToInt(source.vertexCount * (1f - settings.reductions[level] / 100f));
                        string notice = "Requested " + settings.reductions[level].ToString("0.#") + "% fewer vertices (target " + target + "); actual " + reduced.vertexCount + " / " + source.vertexCount + " vertices, " + Triangles(reduced) + " triangles.";
                        if (reduced.vertexCount > target) notice += " Conservative topology/shape constraints prevented the full requested reduction.";
                        pending.Add(new PreviewMesh { source = source, mesh = reduced, renderer = renderer, level = level, reduction = settings.reductions[level], notice = notice });
                    }
                }
                for (int level = 1; level < settings.reductions.Length; level++)
                {
                    if (!MissingLevel(row.group, level)) continue;
                    Mesh[] current = pending.Where(p => p.level == level).Select(p => p.mesh).ToArray();
                    Mesh[] previous;
                    if (level == 1) previous = row.sources.Select(MeshOf).ToArray();
                    else if (!MissingLevel(row.group, level - 1)) previous = row.group.GetLODs()[level - 1].renderers.Where(r => r != null).Select(MeshOf).ToArray();
                    else previous = pending.Where(p => p.level == level - 1).Select(p => p.mesh).ToArray();
                    if (current.Sum(m => (long)m.vertexCount) >= previous.Sum(m => m == null ? 0L : m.vertexCount) ||
                        current.Sum(Triangles) >= previous.Sum(Triangles)) useful = false;
                }
                if (pending.Count == 0) { plan.notices.Add(row.path + ": all requested levels already populated; unchanged."); continue; }
                if (!useful) { plan.notices.Add(row.path + ": skipped: topology/shape constraints prevent useful vertex AND triangle reduction against an original or preceding level; no unhelpful or partial LOD set will be authored."); continue; }
                plan.rows.Add(row);
                plan.meshes.AddRange(pending);
                plan.fingerprints.Add(row, RowFingerprint(row));
            }
            foreach (var mesh in cache.Values.Distinct()) if (!plan.meshes.Any(p => p.mesh == mesh)) Object.DestroyImmediate(mesh);
            foreach (Mesh mesh in plan.meshes.Select(p => p.mesh).Distinct()) plan.previewHashes.Add(mesh, MeshHash(mesh));
            plan.settingsHash = Hash(JsonUtility.ToJson(plan.settings));
            for (int level = 1; level < plan.settings.reductions.Length; level++)
            {
                long baseline = plan.rows.Sum(r => r.triangles), output = 0;
                foreach (Row row in plan.rows)
                {
                    var generated = plan.meshes.Where(p => row.sources.Contains(p.renderer) && p.level == level).ToArray();
                    output += generated.Length != 0 ? generated.Sum(p => Triangles(p.mesh)) : row.group.GetLODs()[level].renderers.Sum(r => Triangles(MeshOf(r)));
                }
                plan.notices.Add("LOD" + level + " static simultaneous geometry: " + output + " triangles versus these objects' " + baseline + " LOD0 triangles (" + (baseline - output) + " fewer). Levels are alternatives, not additive simultaneous savings.");
            }
            plan.notices.Add("Static geometry preview only: no CPU/GPU timing, FPS prediction, gameplay validation or automatic non-LOD fixes. Shape-preserving endpoint reduction can stop above the selected target; inspect the preview.");
            return plan;
        }
        catch
        {
            plan.Dispose();
            foreach (Mesh mesh in cache.Values.Distinct()) if (mesh != null && !EditorUtility.IsPersistent(mesh)) Object.DestroyImmediate(mesh);
            throw;
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    /// <summary>Checks preview inputs only; Apply independently rechecks full scene ownership.</summary>
    public static bool IsPreviewCurrent(Plan plan)
    {
        try
        {
            if (plan == null || plan.disposed || plan.applied || plan.settings == null ||
                Hash(JsonUtility.ToJson(plan.settings)) != plan.settingsHash) return false;
            foreach (Row row in plan.rows)
                if (row == null || row.root == null || !plan.fingerprints.TryGetValue(row, out string fingerprint) ||
                    RowFingerprint(row) != fingerprint) return false;
            foreach (var pair in plan.meshHashes)
                if (pair.Key == null || MeshHash(pair.Key) != pair.Value) return false;
            foreach (var pair in plan.previewHashes)
                if (pair.Key == null || EditorUtility.IsPersistent(pair.Key) || MeshHash(pair.Key) != pair.Value) return false;
            return true;
        }
        catch { return false; }
    }
    private static void ValidateSettings(Settings settings)
    {
        if (settings == null || settings.reductions == null || settings.transitions == null || settings.reductions.Length != settings.transitions.Length || settings.reductions.Length < 2 || settings.reductions.Length > 8)
            throw new ArgumentException("Choose 2–8 total levels with matching reduction/transition arrays.");
        if (settings.reductions[0] != 0f) throw new ArgumentException("LOD0 must retain the original mesh (0% reduction).");
        for (int i = 0; i < settings.reductions.Length; i++)
        {
            float reduction = settings.reductions[i], transition = settings.transitions[i];
            if (float.IsNaN(reduction) || float.IsInfinity(reduction) || reduction < 0 || reduction >= 100 || i > 0 && reduction <= settings.reductions[i - 1]) throw new ArgumentException("Reductions must increase from 0% and remain below 100%.");
            if (float.IsNaN(transition) || float.IsInfinity(transition) || transition < 0 || transition > 1 || i > 0 && transition >= settings.transitions[i - 1]) throw new ArgumentException("Screen-relative transitions must decrease within 0–1.");
        }
    }
    private static bool MissingLevel(LODGroup group, int level)
    {
        if (group == null) return true;
        LOD[] levels = group.GetLODs();
        return level >= levels.Length || !levels[level].renderers.Any(r => r != null);
    }
    private static string TransitionReason(Row row, Settings settings)
    {
        if (row.group == null) return null;
        LOD[] existing = row.group.GetLODs();
        int count = Math.Max(existing.Length, settings.transitions.Length);
        float previous = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            float height = i < existing.Length && (i == 0 || existing[i].renderers.Any(r => r != null)) ? existing[i].screenRelativeTransitionHeight : i < settings.transitions.Length ? settings.transitions[i] : existing[i].screenRelativeTransitionHeight;
            if (height >= previous) return "Requested missing-level transitions conflict with retained populated levels. Adjust transitions; existing levels are never changed.";
            previous = height;
        }
        return null;
    }

    public static string Apply(Plan plan)
    {
        GuardMutation();
        if (plan == null || plan.disposed || plan.applied) throw new InvalidOperationException("Create a fresh, unapplied preview first.");
        if (plan.rows.Count == 0) return "No safe missing LOD levels in this preview; nothing changed.";
        ValidateSettings(plan.settings);
        if (Hash(JsonUtility.ToJson(plan.settings)) != plan.settingsHash) throw new InvalidOperationException("Settings changed after preview; preview again.");
        Receipt receipt = null;
        string receiptPath = null;
        var createdAssets = new List<string>();
        int undoGroup = -1;
        try
        {
            Inventory inventory = Inspect(plan.rows.Select(r => r.root).Where(r => r != null).ToArray());
            foreach (Row row in plan.rows)
            {
                if (row.root == null || RowFingerprint(row) != plan.fingerprints[row]) throw new InvalidOperationException("Source/LOD/transform state changed since preview. Analyze and preview again.");
                Row current = MakeRow(row.group == null ? row.root.GetComponent<Renderer>() : null, row.group, inventory);
                if (!current.editable || TransitionReason(current, plan.settings) != null) throw new InvalidOperationException("Ownership/safety changed: " + row.path + "\n" + current.warning);
            }
            foreach (var pair in plan.meshHashes)
                if (pair.Key == null || MeshHash(pair.Key) != pair.Value) throw new InvalidOperationException("Source mesh changed since preview; analyze again.");
            foreach (var pair in plan.previewHashes)
                if (pair.Key == null || EditorUtility.IsPersistent(pair.Key) || MeshHash(pair.Key) != pair.Value) throw new InvalidOperationException("Preview geometry changed or was consumed; preview again.");
            EnsureFolder(OutputFolder);
            Directory.CreateDirectory(ReceiptFolder);
            receipt = new Receipt { project = ProjectPath, id = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "_" + Guid.NewGuid().ToString("N"), status = "Applying" };
            receiptPath = ReceiptFolder + "/" + receipt.id + ".json";
            foreach (Row row in plan.rows)
                receipt.entries.Add(new Entry { root = Id(row.root), group = Id(row.group), createdGroup = row.group == null, before = row.group == null ? null : Capture(row.group), sources = row.sources.Select(Id).ToArray(), expectedSources = SourcesFingerprint(row.sources) });
            SaveReceipt(receipt, receiptPath);
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Operation);
            var assets = new Dictionary<Mesh, Mesh>();
            int index = 0;
            foreach (Mesh preview in plan.meshes.Select(p => p.mesh).Distinct())
            {
                Progress("Saving generated mesh assets", index++, plan.meshes.Count);
                Mesh asset = Object.Instantiate(preview);
                asset.hideFlags = HideFlags.None;
                string path = AssetDatabase.GenerateUniqueAssetPath(OutputFolder + "/" + receipt.id + "_" + index + ".asset");
                try { AssetDatabase.CreateAsset(asset, path); }
                catch { Object.DestroyImmediate(asset); throw; }
                createdAssets.Add(path);
                assets.Add(preview, asset);
                receipt.assets.Add(new AssetState { path = path, guid = AssetDatabase.AssetPathToGUID(path), hash = MeshHash(asset) });
                SaveReceipt(receipt, receiptPath);
            }
            for (int r = 0; r < plan.rows.Count; r++)
            {
                Row row = plan.rows[r];
                Entry entry = receipt.entries[r];
                Progress("Applying " + row.path, r, plan.rows.Count);
                LODGroup group = row.group;
                if (group == null)
                {
                    group = Undo.AddComponent<LODGroup>(row.root);
                    group.fadeMode = LODFadeMode.None;
                    group.animateCrossFading = false;
                }
                else Undo.RegisterCompleteObjectUndo(group, Operation);
                entry.group = Id(group);
                SaveReceipt(receipt, receiptPath);
                LOD[] before = row.group == null ? new LOD[0] : group.GetLODs();
                int count = Math.Max(before.Length, plan.settings.reductions.Length);
                var levels = new LOD[count];
                for (int level = 0; level < count; level++)
                {
                    if (row.group != null && level < before.Length && (level == 0 || before[level].renderers.Any(x => x != null))) { levels[level] = before[level]; continue; }
                    if (level == 0) { levels[level] = new LOD(plan.settings.transitions[0], row.sources.Cast<Renderer>().ToArray()); continue; }
                    if (level >= plan.settings.reductions.Length) { levels[level] = before[level]; continue; }
                    var proxies = new List<Renderer>();
                    foreach (PreviewMesh preview in plan.meshes.Where(p => row.sources.Contains(p.renderer) && p.level == level))
                    {
                        GameObject go = CreateProxyInScene(ProxyPrefix + level + "_" + receipt.id + "_" + preview.renderer.name, preview.renderer.gameObject.scene);
                        Undo.RegisterCreatedObjectUndo(go, Operation);
                        go.layer = preview.renderer.gameObject.layer;
                        go.tag = "Untagged";
                        go.transform.SetParent(preview.renderer.transform, false);
                        GameObjectUtility.SetStaticEditorFlags(go, 0);
                        go.AddComponent<MeshFilter>().sharedMesh = assets[preview.mesh];
                        MeshRenderer proxy = go.AddComponent<MeshRenderer>();
                        EditorUtility.CopySerialized(preview.renderer, proxy);
                        proxy.hideFlags = HideFlags.None;
                        // Never copy gameplay components, material instances, colliders, Udon or scripts.
                        proxies.Add(proxy);
                        entry.proxies.Add(new ProxyState { id = Id(go), fingerprint = ProxyFingerprint(go) });
                        SaveReceipt(receipt, receiptPath);
                    }
                    if (proxies.Count != row.sources.Length) throw new InvalidOperationException("Incomplete preview membership; no partial row may be applied.");
                    levels[level] = new LOD(plan.settings.transitions[level], proxies.ToArray());
                    if (level < before.Length) levels[level].fadeTransitionWidth = before[level].fadeTransitionWidth;
                }
                group.SetLODs(levels);
                if (row.group == null) group.RecalculateBounds();
                entry.expectedGroup = GroupFingerprint(group);
                entry.state = "Applied";
                PrefabUtility.RecordPrefabInstancePropertyModifications(group);
                EditorUtility.SetDirty(group);
                EditorSceneManager.MarkSceneDirty(row.root.scene);
                SaveReceipt(receipt, receiptPath);
            }
            AssetDatabase.SaveAssets();
            receipt.status = "Applied";
            SaveReceipt(receipt, receiptPath);
            Undo.CollapseUndoOperations(undoGroup);
            plan.applied = true;
            return "Applied " + plan.rows.Count + " object/group(s), " + plan.meshes.Count + " proxy renderer(s), " + receipt.assets.Count + " new mesh asset(s). Originals, materials, colliders and scripts unchanged. Existing populated levels retained. Save the modified scenes to persist the result.\nReceipt: " + receiptPath + "\nUndo is available; Restore Last Batch also survives editor restart after saving scenes.";
        }
        catch (Exception exception)
        {
            if (undoGroup >= 0) Undo.RevertAllDownToGroup(undoGroup);
            foreach (string path in createdAssets) AssetDatabase.DeleteAsset(path);
            if (receipt != null && receiptPath != null) { receipt.status = "RolledBack"; SaveReceipt(receipt, receiptPath); }
            if (exception is OperationCanceledException) throw;
            throw new InvalidOperationException("LOD apply failed; the current batch was rolled back. " + exception.Message, exception);
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    public static string RestoreLastBatch()
    {
        GuardMutation();
        if (!Directory.Exists(ReceiptFolder)) return "No LOD optimizer batch receipt exists.";
        string[] paths = Directory.GetFiles(ReceiptFolder, "*.json").OrderByDescending(p => p, StringComparer.Ordinal).ToArray();
        Receipt receipt = null;
        string receiptPath = null;
        foreach (string path in paths)
        {
            var candidate = JsonUtility.FromJson<Receipt>(File.ReadAllText(path));
            if (candidate == null || candidate.project != ProjectPath || candidate.version != 1) continue;
            if (candidate.status == "Restored" || candidate.status == "PartialRestore")
                foreach (Entry entry in candidate.entries.Where(e => e.state == "Restored"))
                {
                    LODGroup existing = Resolve<LODGroup>(entry.group);
                    if (existing != null && GroupFingerprint(existing) == entry.expectedGroup && entry.proxies.All(p => Resolve<GameObject>(p.id) != null))
                        entry.state = "Applied"; // Undo of a restore reinstates the applied scene state.
                }
            if (candidate.status == "Applied" || candidate.status == "PartialRestore" || candidate.status == "Applying" || candidate.entries.Any(e => e.state == "Applied")) { receipt = candidate; receiptPath = path; break; }
        }
        if (receipt == null) return "No unrestored LOD optimizer batch exists.";
        int restored = 0, conflicts = 0;
        string receiptBefore = JsonUtility.ToJson(receipt);
        var messages = new List<string>();
        Undo.IncrementCurrentGroup();
        int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Restore LOD optimizer batch");
        try
        {
            for (int i = receipt.entries.Count - 1; i >= 0; i--)
            {
                Entry entry = receipt.entries[i];
                if (entry.state == "Restored") continue;
                Progress("Restoring LOD batch " + receipt.id, receipt.entries.Count - 1 - i, receipt.entries.Count);
                GameObject root = Resolve<GameObject>(entry.root);
                LODGroup group = Resolve<LODGroup>(entry.group);
                GameObject[] proxies = entry.proxies.Select(p => Resolve<GameObject>(p.id)).ToArray();
                if (root == null || SceneReason(root) != null) { conflicts++; messages.Add("Scene/object unavailable; load its saved scene before restoring: " + entry.root); continue; }
                if (proxies.All(p => p == null) && (group == null && entry.createdGroup || group != null && entry.before != null && GroupFingerprint(group) == Hash(JsonUtility.ToJson(entry.before))))
                {
                    // A normal Undo may already have removed all this entry's owned objects.
                    entry.state = "Restored"; restored++; SaveReceipt(receipt, receiptPath); continue;
                }
                string conflict = RestoreConflict(entry, root, group, proxies);
                if (conflict != null) { conflicts++; messages.Add(root.name + ": " + conflict + " Nothing removed."); continue; }
                if (entry.createdGroup) Undo.DestroyObjectImmediate(group);
                else
                {
                    Undo.RegisterCompleteObjectUndo(group, "Restore LOD optimizer batch");
                    RestoreGroup(group, entry.before);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(group);
                    EditorUtility.SetDirty(group);
                }
                foreach (GameObject proxy in proxies) Undo.DestroyObjectImmediate(proxy);
                EditorSceneManager.MarkSceneDirty(root.scene);
                entry.state = "Restored";
                restored++;
                SaveReceipt(receipt, receiptPath);
            }
            receipt.status = receipt.entries.All(e => e.state == "Restored") ? "Restored" : "PartialRestore";
            SaveReceipt(receipt, receiptPath);
            // Assets are retained while any saved or loaded content references them. This also makes restore Undo safe.
            // Deleting assets here would invalidate redo/undo mesh references, so batch-owned assets remain as recovery evidence.
            messages.Add("Generated mesh assets retained intentionally for Undo and any saved-scene references; no original asset was changed or deleted.");
            Undo.CollapseUndoOperations(undo);
            return "Restored " + restored + " object/group(s); conflicts " + conflicts + ". Save modified scenes.\nReceipt: " + receiptPath + "\n" + string.Join("\n", messages.ToArray());
        }
        catch (OperationCanceledException)
        {
            receipt.status = "PartialRestore";
            SaveReceipt(receipt, receiptPath);
            Undo.CollapseUndoOperations(undo);
            throw;
        }
        catch (Exception exception)
        {
            Undo.RevertAllDownToGroup(undo);
            receipt = JsonUtility.FromJson<Receipt>(receiptBefore);
            SaveReceipt(receipt, receiptPath);
            throw new InvalidOperationException("LOD restore failed; this restore operation was rolled back. " + exception.Message, exception);
        }
        finally { EditorUtility.ClearProgressBar(); }
    }
    private static string RestoreConflict(Entry entry, GameObject root, LODGroup group, GameObject[] proxies)
    {
        if (entry.state != "Applied" || group == null || string.IsNullOrEmpty(entry.expectedGroup)) return "Incomplete batch or missing LOD owner needs manual inspection.";
        if (GroupFingerprint(group) != entry.expectedGroup) return "LOD settings/membership changed since apply.";
        if (entry.sources == null || SourcesFingerprint(entry.sources.Select(Resolve<MeshRenderer>)) != entry.expectedSources) return "Original renderer/transform/mesh reference state changed since apply.";
        if (proxies.Any(p => p == null)) return "An owned proxy is missing.";
        for (int i = 0; i < proxies.Length; i++)
            if (ProxyFingerprint(proxies[i]) != entry.proxies[i].fingerprint) return "An owned proxy was modified or has foreign components/children.";
        var owned = new HashSet<Renderer>(proxies.Select(p => p.GetComponent<Renderer>()));
        foreach (GameObject sceneRoot in LoadedRoots())
            foreach (LODGroup other in sceneRoot.GetComponentsInChildren<LODGroup>(true))
                if (other != group && other.GetLODs().SelectMany(l => l.renderers).Any(owned.Contains)) return "Another LODGroup now references an owned proxy.";
        if (entry.before != null)
            foreach (LevelState level in entry.before.levels)
                if (level.renderers.Any(id => !string.IsNullOrEmpty(id) && Resolve<Renderer>(id) == null)) return "A previous renderer reference is missing; exact restoration is not possible.";
        // Do not delete proxy objects newly referenced by gameplay scripts.
        Inventory inventory = Inspect(new[] { root });
        if (owned.Any(inventory.bound.Contains)) return "A script/animation now references an owned proxy.";
        foreach (GameObject proxy in proxies)
            for (Transform t = proxy.transform; t != null; t = t.parent)
                if (t.GetComponents<MonoBehaviour>().Any() || t.GetComponents<Component>().Any(c => c == null)) return "A script-controlled or missing-script hierarchy now owns a proxy.";
        return null;
    }

    public static List<string> AnalyzeOtherCosts(ScanOptions options)
    {
        try
        {
            GameObject[] roots = ScopeRoots(options);
            GameObject[] objects = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).Distinct().Where(g => options.includeInactive || g.activeInHierarchy).ToArray();
            var lights = objects.SelectMany(g => g.GetComponents<Light>()).ToArray();
            var renderers = objects.SelectMany(g => g.GetComponents<Renderer>()).ToArray();
            var scripts = objects.SelectMany(g => g.GetComponents<MonoBehaviour>()).ToArray();
            int realtime = lights.Count(l => l.lightmapBakeType == LightmapBakeType.Realtime);
            int mixed = lights.Count(l => l.lightmapBakeType == LightmapBakeType.Mixed);
            int shadows = lights.Count(l => l.shadows != LightShadows.None && l.lightmapBakeType != LightmapBakeType.Baked);
            int transparent = renderers.Count(r => r.sharedMaterials.Any(m => m != null && (m.renderQueue > 2500 || m.IsKeywordEnabled("_ALPHABLEND_ON") || m.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"))));
            int cutout = renderers.Count(r => r.sharedMaterials.Any(m => m != null && (m.renderQueue >= 2450 && m.renderQueue <= 2500 || m.IsKeywordEnabled("_ALPHATEST_ON"))));
            return new List<string> {
                "NOT MEASURED: this is a static component inventory, not CPU/GPU profiling, FPS, actual draw calls, simulation cost, overdraw or network traffic. Counts include disabled components on included objects.",
                "Lights: " + lights.Length + " total; " + realtime + " realtime; " + mixed + " mixed; " + shadows + " realtime/mixed with shadows configured; " + lights.Count(l => l.enabled && l.gameObject.activeInHierarchy && l.shadows != LightShadows.None && l.lightmapBakeType != LightmapBakeType.Baked) + " of those currently enabled/active.",
                "Particles: " + objects.Sum(g => g.GetComponents<ParticleSystem>().Length) + " systems; " + renderers.Count(r => r is ParticleSystemRenderer) + " particle renderers. No emission/overdraw timing was sampled.",
                "Material indicators: " + transparent + " renderer(s) with transparent-queue/alpha-blend materials; " + cutout + " with alpha-test/cutout indicators. Shader classification is not a measured pixel cost.",
                "Scripts: " + scripts.Count(s => s != null) + " MonoBehaviour components, including " + scripts.Count(s => s != null && IsUdon(s.GetType())) + " Udon/UdonSharp components (proxy and backing components may both be counted); " + scripts.Count(s => s == null) + " missing-script slots. No execution frequency or runtime workload was measured.",
                "No non-LOD settings were changed. Use the Unity Profiler and VRChat client measurements before changing lights, particles, materials or scripts."
            };
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    private static bool IsUdon(Type type)
    {
        for (Type t = type; t != null; t = t.BaseType)
            if (t.Name == "UdonSharpBehaviour" || (t.FullName ?? "").StartsWith("VRC.Udon.", StringComparison.Ordinal)) return true;
        return false;
    }
    private static LODGroup AncestorGroup(Transform transform)
    {
        for (Transform t = transform; t != null; t = t.parent) { var group = t.GetComponent<LODGroup>(); if (group != null) return group; }
        return null;
    }
    private static Mesh MeshOf(Renderer renderer)
    {
        if (renderer == null) return null;
        if (renderer is SkinnedMeshRenderer skin) return skin.sharedMesh;
        var filter = renderer.GetComponent<MeshFilter>();
        return filter == null ? null : filter.sharedMesh;
    }
    private static long Triangles(Mesh mesh)
    {
        if (mesh == null) return 0;
        long count = 0;
        for (int i = 0; i < mesh.subMeshCount; i++) if (mesh.GetTopology(i) == MeshTopology.Triangles) count += (long)mesh.GetIndexCount(i) / 3;
        return count;
    }
    private static string HierarchyPath(Transform transform)
    {
        string path = transform.name + "[" + transform.GetSiblingIndex() + "]";
        while (transform.parent != null) { transform = transform.parent; path = transform.name + "[" + transform.GetSiblingIndex() + "]/" + path; }
        return path;
    }
    private static string Id(Object value) { return value == null ? "" : GlobalObjectId.GetGlobalObjectIdSlow(value).ToString(); }
    private static T Resolve<T>(string id) where T : Object
    {
        if (string.IsNullOrEmpty(id) || !GlobalObjectId.TryParse(id, out var global)) return null;
        return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(global) as T;
    }
    private static GroupState Capture(LODGroup group)
    {
        return new GroupState { enabled = group.enabled, animate = group.animateCrossFading, billboard = group.lastLODBillboard, fadeMode = group.fadeMode, referencePoint = group.localReferencePoint, size = group.size,
            levels = group.GetLODs().Select(l => new LevelState { height = l.screenRelativeTransitionHeight, fade = l.fadeTransitionWidth, renderers = l.renderers.Select(Id).ToArray() }).ToArray() };
    }
    private static void RestoreGroup(LODGroup group, GroupState state)
    {
        group.SetLODs(state.levels.Select(l => new LOD(l.height, l.renderers.Select(Resolve<Renderer>).ToArray()) { fadeTransitionWidth = l.fade }).ToArray());
        group.enabled = state.enabled;
        group.animateCrossFading = state.animate;
        group.lastLODBillboard = state.billboard;
        group.fadeMode = state.fadeMode;
        group.localReferencePoint = state.referencePoint;
        group.size = state.size;
    }
    private static string GroupFingerprint(LODGroup group) { return Hash(JsonUtility.ToJson(Capture(group))); }
    private static string SourcesFingerprint(IEnumerable<MeshRenderer> sources)
    {
        return Hash(string.Join("\n", sources.Select(r => r == null ? "missing" : Id(r) + ":" + ComponentFingerprint(r) + ":" + Id(MeshOf(r)) + ":" + MeshHash(MeshOf(r)) + ":" + TransformFingerprint(r.transform)).ToArray()));
    }
    private static string RowFingerprint(Row row)
    {
        return Hash(Id(row.root) + "|" + (row.group == null ? "none" : Id(row.group) + GroupFingerprint(row.group)) + "|" + SourcesFingerprint(row.sources));
    }
    private static string TransformFingerprint(Transform transform)
    {
        var text = new StringBuilder();
        for (Transform t = transform; t != null; t = t.parent)
            text.Append(Id(t)).Append('|').Append(JsonUtility.ToJson(t.localPosition)).Append('|').Append(JsonUtility.ToJson(t.localRotation))
                .Append('|').Append(JsonUtility.ToJson(t.localScale)).Append('|').Append(t.gameObject.activeSelf).Append('|')
                .Append((int)GameObjectUtility.GetStaticEditorFlags(t.gameObject));
        return Hash(text.ToString());
    }
    private static string ProxyFingerprint(GameObject go)
    {
        if (go == null) return "missing";
        Component[] components = go.GetComponents<Component>();
        var renderer = go.GetComponent<MeshRenderer>();
        var filter = go.GetComponent<MeshFilter>();
        if (components.Length != 3 || renderer == null || filter == null || go.transform.childCount != 0 || renderer.HasPropertyBlock()) return "foreign-components-children-or-property-block";
        return Hash(Id(go) + "|" + go.name + "|" + go.layer + "|" + go.tag + "|" + go.hideFlags + "|" + TransformFingerprint(go.transform) + "|" + ComponentFingerprint(renderer) + "|" + Id(filter.sharedMesh) + "|" + (filter.sharedMesh == null ? "missing" : MeshHash(filter.sharedMesh)));
    }
    private static string ComponentFingerprint(Component component)
    {
        // Native SerializedObject iteration also exposes transient editor/cache properties.
        // Hash only the persistent state Unity writes, retaining every authored serialized field.
        string json = EditorJsonUtility.ToJson(component);
        string normalized = System.Text.RegularExpressions.Regex.Replace(json,
            @"\{\s*""instanceID""\s*:\s*(-?\d+)\s*\}", match =>
            {
                int instanceId = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                Object reference = instanceId == 0 ? null : EditorUtility.InstanceIDToObject(instanceId);
                if (instanceId != 0 && reference == null)
                    throw new InvalidOperationException("Cannot fingerprint an unresolved serialized object reference on " + component.name + ".");
                // Asset guid/fileID references already emitted by EditorJsonUtility remain unchanged.
                return "{\"globalObjectId\":\"" + Id(reference) + "\"}";
            }, System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return Hash(normalized);
    }
    private static string MeshHash(Mesh mesh)
    {
        if (mesh == null || !mesh.isReadable) return "unreadable";
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(mesh.vertexCount); writer.Write(mesh.subMeshCount); writer.Write((int)mesh.indexFormat);
            foreach (Vector3 value in mesh.vertices) { writer.Write(value.x); writer.Write(value.y); writer.Write(value.z); }
            foreach (Vector3 value in mesh.normals) { writer.Write(value.x); writer.Write(value.y); writer.Write(value.z); }
            foreach (Vector4 value in mesh.tangents) { writer.Write(value.x); writer.Write(value.y); writer.Write(value.z); writer.Write(value.w); }
            foreach (Color value in mesh.colors) { writer.Write(value.r); writer.Write(value.g); writer.Write(value.b); writer.Write(value.a); }
            for (int channel = 0; channel < 8; channel++)
            {
                var uv = new List<Vector4>(); mesh.GetUVs(channel, uv); writer.Write(uv.Count);
                foreach (var value in uv) { writer.Write(value.x); writer.Write(value.y); writer.Write(value.z); writer.Write(value.w); }
            }
            for (int sub = 0; sub < mesh.subMeshCount; sub++) { writer.Write((int)mesh.GetTopology(sub)); foreach (int index in mesh.GetIndices(sub)) writer.Write(index); }
            writer.Flush(); stream.Position = 0;
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
    private static string Hash(string text)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
    }
    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++) { string next = current + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]); current = next; }
    }
    private static GameObject CreateProxyInScene(string name, Scene scene)
    {
        Scene previous = SceneManager.GetActiveScene();
        bool changed = previous != scene;
        if (changed && !SceneManager.SetActiveScene(scene))
            throw new InvalidOperationException("Cannot activate the source scene for LOD proxy creation.");
        try { return new GameObject(name); }
        finally
        {
            if (changed) SceneManager.SetActiveScene(previous);
        }
    }

    private static void SaveReceipt(Receipt receipt, string path)
    {
        string temporary = path + ".writing";
        File.WriteAllText(temporary, JsonUtility.ToJson(receipt, true));
        if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
    }
}

}
