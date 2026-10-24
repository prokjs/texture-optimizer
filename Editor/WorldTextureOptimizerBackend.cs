using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Prokjs.TextureOptimizer
{
/// <summary>Explicit editor-only inspection and reversible resolution-cap operations. No runtime hooks.</summary>
public static class WorldTextureOptimizerBackend
{
    public enum Scope { LoadedScenes, SelectedObjects, Folder, ProjectAssets }
    public enum Target { Default, Standalone, Android }
    public static readonly int[] Sizes = { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384 };
    public const string BackupDirectory = "UserSettings/ProkjsTextureOptimizerBackups";

    [Serializable] public sealed class ScanOptions
    {
        public Scope scope = Scope.LoadedScenes;
        public Target target = Target.Default;
        public bool includeInactive;
        public string folder = "Assets";
        public GameObject[] roots;
    }
    [Serializable] public sealed class Row
    {
        public string assetPath, name, guid, lockedReason, warning, importerHash, loadedFormat, caps;
        public Texture texture;
        public int sourceWidth, sourceHeight, width, height, defaultCap, standaloneCap, androidCap, effectiveCap;
        public int targetWidth, targetHeight, proposedMaxSize, references;
        public bool standaloneOverride, androidOverride, selected, mipmaps;
        public TextureImporterNPOTScale npotScale;
        public long currentBytes = -1, targetBytes = -1;
        public bool Editable { get { return string.IsNullOrEmpty(lockedReason); } }
    }
    [Serializable] public sealed class ScanResult
    {
        public List<Row> rows = new List<Row>();
        public List<string> errors = new List<string>();
        public bool canceled;
        public string scopeDescription;
    }
    [Serializable] public sealed class Change
    {
        public string assetPath, expectedImporterHash;
        public int maxSize;
        public Change(string path, int size, string hash) { assetPath = path; maxSize = size; expectedImporterHash = hash; }
    }
    [Serializable] public sealed class Entry
    {
        public string assetPath, guid, sourceHash, beforeHash, expectedAfterHash, beforePlatformJson;
        public string state = "Planned", message;
        public int beforeCap, targetCap;
        public bool beforeHadPlatformRecord;
    }
    [Serializable] public sealed class Batch
    {
        public int schemaVersion = 1;
        public string id, projectPath, createdUtc, backupPath, status = "Preview";
        public Target target;
        public List<Entry> entries = new List<Entry>();
        public List<string> notices = new List<string>();
    }
    [Serializable] public sealed class Outcome
    {
        public string backupPath;
        public int applied, restored, unchanged, conflicts, failed, pending;
        public bool canceled;
        public List<string> messages = new List<string>();
        public override string ToString()
        {
            return "Applied " + applied + "; restored " + restored + "; unchanged " + unchanged +
                "; conflicts " + conflicts + "; failed " + failed + "; pending " + pending +
                (canceled ? "; CANCELED" : "") + "\nBackup: " + backupPath + "\n" + string.Join("\n", messages.ToArray());
        }
    }

    public static string PlatformName(Target target) { return target == Target.Default ? "DefaultTexturePlatform" : target.ToString(); }
    public static string ImporterHash(TextureImporter importer) { return HashText(EditorJsonUtility.ToJson(importer)); }
    private static string HashText(string text)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
    }
    private static string SourceHash(string path)
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }
    private static bool Cancel(bool progress, string operation, string detail, int index, int count)
    {
        return progress && EditorUtility.DisplayCancelableProgressBar(operation, detail, count == 0 ? 0 : (float)index / count);
    }

    /// <summary>Read-only. Scans only on an explicit call; caller owns refreshing stale rows.</summary>
    public static ScanResult Scan(ScanOptions options, bool showProgress = false)
    {
        if (options == null) throw new ArgumentNullException("options");
        var result = new ScanResult();
        result.scopeDescription = options.scope + "; inactive=" + options.includeInactive + "; chosen cap target=" + options.target + "; loaded editor platform=" + EditorUserBuildSettings.activeBuildTarget;
        var textures = new Dictionary<Texture, int>();
        var materials = new HashSet<Material>();
        var generated = GeneratedLightingTextures();
        try
        {
            if (options.scope == Scope.Folder || options.scope == Scope.ProjectAssets)
            {
                string folder = options.scope == Scope.ProjectAssets ? "Assets" : options.folder;
                if (string.IsNullOrEmpty(folder) || !(folder == "Assets" || folder.StartsWith("Assets/", StringComparison.Ordinal)) || !AssetDatabase.IsValidFolder(folder))
                    throw new ArgumentException("Choose a valid folder under Assets. Project scope also deliberately excludes Packages.");
                var guids = AssetDatabase.FindAssets("t:Texture", new[] { folder });
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (Cancel(showProgress, "Texture inventory", path, i, guids.Length)) { result.canceled = true; break; }
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        var texture = asset as Texture;
                        if (texture != null) AddTexture(textures, texture);
                    }
                }
            }
            else
            {
                var roots = new List<GameObject>();
                if (options.scope == Scope.SelectedObjects)
                {
                    if (options.roots != null) roots.AddRange(options.roots);
                    else roots.AddRange(Selection.gameObjects);
                    if (roots.Count == 0) throw new ArgumentException("Select one or more scene objects or prefab assets, then scan.");
                }
                else
                {
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var scene = SceneManager.GetSceneAt(i);
                        if (scene.isLoaded && scene.IsValid()) roots.AddRange(scene.GetRootGameObjects());
                    }
                    AddMaterial(materials, RenderSettings.skybox);
                }
                var visited = new HashSet<int>();
                for (int i = 0; i < roots.Count; i++)
                {
                    var root = roots[i];
                    if (root == null) continue;
                    if (Cancel(showProgress, "Texture inventory", root.name, i, roots.Count)) { result.canceled = true; break; }
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        var go = transform.gameObject;
                        if (!visited.Add(go.GetInstanceID()) || (!options.includeInactive && !go.activeInHierarchy)) continue;
                        foreach (var renderer in go.GetComponents<Renderer>())
                            foreach (var material in renderer.sharedMaterials) AddMaterial(materials, material);
                        foreach (var terrain in go.GetComponents<Terrain>())
                        {
                            AddMaterial(materials, terrain.materialTemplate);
                            var data = terrain.terrainData;
                            if (data == null) continue;
                            foreach (var layer in data.terrainLayers)
                            {
                                if (layer == null) continue;
                                AddTexture(textures, layer.diffuseTexture);
                                AddTexture(textures, layer.normalMapTexture);
                                AddTexture(textures, layer.maskMapTexture);
                            }
                        }
                        foreach (var graphic in go.GetComponents<Graphic>())
                        {
                            AddMaterial(materials, graphic.material);
                            AddTexture(textures, graphic.mainTexture);
                        }
                        foreach (var light in go.GetComponents<Light>()) AddTexture(textures, light.cookie);
                    }
                }
                foreach (var material in materials)
                    foreach (string property in material.GetTexturePropertyNames()) AddTexture(textures, material.GetTexture(property));
            }
            // One importer operation per asset path, even when multiple materials/sub-assets reference it.
            var byPath = new Dictionary<string, Row>(StringComparer.Ordinal);
            int n = 0;
            foreach (var pair in textures)
            {
                if (Cancel(showProgress, "Inspecting textures", pair.Key.name, n++, textures.Count)) { result.canceled = true; break; }
                try
                {
                    string path = AssetDatabase.GetAssetPath(pair.Key);
                    string key = string.IsNullOrEmpty(path) ? "instance:" + pair.Key.GetInstanceID() : path;
                    Row existing;
                    if (byPath.TryGetValue(key, out existing)) { existing.references += pair.Value; continue; }
                    var row = InspectTexture(pair.Key, options.target, generated);
                    row.references = pair.Value;
                    byPath.Add(key, row);
                    result.rows.Add(row);
                }
                catch (Exception ex) { result.errors.Add(pair.Key.name + ": " + ex.Message); }
            }
            result.rows.Sort((a, b) => b.currentBytes.CompareTo(a.currentBytes));
            return result;
        }
        finally { if (showProgress) EditorUtility.ClearProgressBar(); }
    }
    private static void AddTexture(Dictionary<Texture, int> textures, Texture texture)
    {
        if (texture == null) return;
        int count;
        textures.TryGetValue(texture, out count);
        textures[texture] = count + 1;
    }
    private static void AddMaterial(HashSet<Material> materials, Material material) { if (material != null) materials.Add(material); }
    private static HashSet<Texture> GeneratedLightingTextures()
    {
        var textures = new HashSet<Texture>();
        foreach (var data in LightmapSettings.lightmaps)
        {
            if (data.lightmapColor != null) textures.Add(data.lightmapColor);
            if (data.lightmapDir != null) textures.Add(data.lightmapDir);
            if (data.shadowMask != null) textures.Add(data.shadowMask);
        }
        foreach (var probe in Resources.FindObjectsOfTypeAll<ReflectionProbe>())
        {
            if (probe.bakedTexture != null) textures.Add(probe.bakedTexture);
            if (probe.realtimeTexture != null) textures.Add(probe.realtimeTexture);
        }
        return textures;
    }

    public static Row InspectAsset(string assetPath, Target target)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
        if (texture == null) throw new ArgumentException("Not a texture asset: " + assetPath);
        return InspectTexture(texture, target, GeneratedLightingTextures());
    }
    private static Row InspectTexture(Texture texture, Target target, HashSet<Texture> generated)
    {
        var row = new Row { texture = texture, name = texture.name, assetPath = AssetDatabase.GetAssetPath(texture), width = texture.width, height = texture.height };
        row.guid = AssetDatabase.AssetPathToGUID(row.assetPath);
        var texture2D = texture as Texture2D;
        if (texture2D != null)
        {
            row.loadedFormat = texture2D.graphicsFormat.ToString();
            row.currentBytes = EstimateBytes(texture2D, texture.width, texture.height, texture2D.mipmapCount > 1);
        }
        else row.loadedFormat = texture.GetType().Name;
        var importer = AssetImporter.GetAtPath(row.assetPath) as TextureImporter;
        if (!row.assetPath.StartsWith("Assets/", StringComparison.Ordinal)) row.lockedReason = "Packages, built-in or transient asset: read-only here.";
        else if (texture2D == null) row.lockedReason = "Only imported 2D textures are supported; render textures, arrays and cubemaps are locked.";
        else if (generated.Contains(texture) || IsGeneratedPath(row.assetPath)) row.lockedReason = "Generated lighting/reflection asset: regenerate with its owning workflow.";
        else if (importer == null) row.lockedReason = "No supported TextureImporter (generated/embedded/native asset).";
        else if (AssetDatabase.IsSubAsset(texture)) row.lockedReason = "Embedded/sub-asset texture: importer is shared with another asset.";
        else if (!AssetDatabase.IsOpenForEdit(row.assetPath, StatusQueryOptions.UseCachedIfPossible) || !AssetDatabase.IsOpenForEdit(row.assetPath + ".meta", StatusQueryOptions.UseCachedIfPossible)) row.lockedReason = "Asset or importer is not open for edit; check version-control checkout.";
        else if (File.Exists(row.assetPath + ".meta") && (File.GetAttributes(row.assetPath + ".meta") & FileAttributes.ReadOnly) != 0) row.lockedReason = "Importer metadata is read-only.";
        if (importer == null) return row;
        row.importerHash = ImporterHash(importer);
        importer.GetSourceTextureWidthAndHeight(out row.sourceWidth, out row.sourceHeight);
        var defaults = importer.GetDefaultPlatformTextureSettings();
        var standalone = importer.GetPlatformTextureSettings("Standalone");
        var android = importer.GetPlatformTextureSettings("Android");
        row.defaultCap = defaults.maxTextureSize;
        row.standaloneOverride = standalone.overridden;
        row.androidOverride = android.overridden;
        row.standaloneCap = standalone.overridden ? standalone.maxTextureSize : defaults.maxTextureSize;
        row.androidCap = android.overridden ? android.maxTextureSize : defaults.maxTextureSize;
        var settings = target == Target.Default ? defaults : importer.GetPlatformTextureSettings(PlatformName(target));
        row.effectiveCap = target == Target.Default || !settings.overridden ? defaults.maxTextureSize : settings.maxTextureSize;
        row.caps = "D " + row.defaultCap + " | S " + row.standaloneCap + (standalone.overridden ? "*" : " inherited") + " | A " + row.androidCap + (android.overridden ? "*" : " inherited");
        row.mipmaps = importer.mipmapEnabled;
        row.npotScale = importer.npotScale;
        if (importer.textureShape != TextureImporterShape.Texture2D) row.lockedReason = "Non-2D importer shape is unsupported.";
        if (importer.textureType == TextureImporterType.Lightmap || importer.textureType.ToString() == "DirectionalLightmap" || importer.textureType.ToString() == "Shadowmask") row.lockedReason = "Lighting importer: use lighting workflow, not resolution-cap edits.";
        if (importer.textureType == TextureImporterType.Sprite) row.lockedReason = "Sprite/atlas inputs are locked to preserve packing, slicing and UI layout.";
        if (row.sourceWidth <= 0 || row.sourceHeight <= 0) row.lockedReason = "Source dimensions unavailable; cannot enforce no-upscale policy.";
        else if (LargestAllowedCap(row.sourceWidth, row.sourceHeight) == 0) row.lockedReason = "Source is smaller than the minimum offered 32-pixel cap.";
        if (target != Target.Default && !settings.overridden && !InheritedFormatSupported(importer, defaults, target))
            row.lockedReason = "Inherited explicit texture format is unsupported by the chosen platform; select a supported format deliberately in Inspector.";
        if (target == Target.Default && (standalone.overridden || android.overridden)) row.warning = "Default is masked by " + (standalone.overridden ? "Standalone " : "") + (android.overridden ? "Android " : "") + "override(s); those platforms do not change.";
        else if (target != Target.Default && !settings.overridden) row.warning = "Apply enables the " + target + " override using inherited Default settings; inactive settings are backed up exactly.";
        row.proposedMaxSize = Mathf.Min(row.effectiveCap, LargestAllowedCap(row.sourceWidth, row.sourceHeight));
        Dimensions(importer, row.effectiveCap, out row.targetWidth, out row.targetHeight);
        row.targetBytes = texture2D == null ? -1 : EstimateBytes(texture2D, row.targetWidth, row.targetHeight, row.mipmaps);
        return row;
    }
    private static bool IsGeneratedPath(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith("Lightmap-", StringComparison.OrdinalIgnoreCase) || name.StartsWith("ReflectionProbe-", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".renderTexture", StringComparison.OrdinalIgnoreCase);
    }
    private static bool InheritedFormatSupported(TextureImporter importer, TextureImporterPlatformSettings defaults, Target target)
    {
        return defaults.format == TextureImporterFormat.Automatic ||
            TextureImporter.IsPlatformTextureFormatValid(importer.textureType, target == Target.Android ? BuildTarget.Android : BuildTarget.StandaloneWindows64, defaults.format);
    }
    public static int LargestAllowedCap(int sourceWidth, int sourceHeight)
    {
        int maximum = Mathf.Max(sourceWidth, sourceHeight), cap = 0;
        foreach (int size in Sizes) if (size <= maximum) cap = size;
        return cap;
    }
    public static bool IsAllowedSize(int size) { return Array.IndexOf(Sizes, size) >= 0; }
    private static void Dimensions(TextureImporter importer, int cap, out int width, out int height)
    {
        importer.GetSourceTextureWidthAndHeight(out width, out height);
        if (importer.npotScale != TextureImporterNPOTScale.None)
        {
            width = Npot(width, importer.npotScale);
            height = Npot(height, importer.npotScale);
        }
        float scale = Mathf.Min(1f, (float)cap / Mathf.Max(1, Mathf.Max(width, height)));
        width = Mathf.Max(1, Mathf.RoundToInt(width * scale));
        height = Mathf.Max(1, Mathf.RoundToInt(height * scale));
    }
    private static int Npot(int value, TextureImporterNPOTScale scale)
    {
        if (scale == TextureImporterNPOTScale.ToNearest) return Mathf.ClosestPowerOfTwo(value);
        int larger = Mathf.NextPowerOfTwo(value);
        return scale == TextureImporterNPOTScale.ToLarger ? larger : (larger == value ? value : larger / 2);
    }
    /// <summary>Full GPU block-allocation estimate, NOT observed residency, CPU memory or on-disk Crunch size.</summary>
    public static long EstimateBytes(Texture2D texture, int width, int height, bool mipmaps)
    {
        if (texture == null || width <= 0 || height <= 0 || texture.graphicsFormat == GraphicsFormat.None) return -1;
        var format = texture.graphicsFormat;
        int blockWidth = (int)GraphicsFormatUtility.GetBlockWidth(format), blockHeight = (int)GraphicsFormatUtility.GetBlockHeight(format);
        long blockBytes = GraphicsFormatUtility.GetBlockSize(format);
        if (blockWidth <= 0 || blockHeight <= 0 || blockBytes <= 0) return -1;
        long total = 0;
        do
        {
            total += ((width + blockWidth - 1L) / blockWidth) * ((height + blockHeight - 1L) / blockHeight) * blockBytes;
            if (!mipmaps || (width == 1 && height == 1)) break;
            width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
        } while (true);
        return total;
    }
    public static long ProposedBytes(Row row, int cap)
    {
        if (row == null || !row.Editable || !IsAllowedSize(cap)) return -1;
        int width = row.sourceWidth, height = row.sourceHeight;
        if (row.npotScale != TextureImporterNPOTScale.None)
        {
            width = Npot(width, row.npotScale); height = Npot(height, row.npotScale);
        }
        float scale = Mathf.Min(1f, (float)cap / Mathf.Max(1, Mathf.Max(width, height)));
        width = Mathf.Max(1, Mathf.RoundToInt(width * scale)); height = Mathf.Max(1, Mathf.RoundToInt(height * scale));
        return EstimateBytes(row.texture as Texture2D, width, height, row.mipmaps);
    }

    /// <summary>Read-only preview. Refuses stale scans, unsupported assets, duplicate paths, no-upsizing and no-op requests.</summary>
    public static Batch CreatePlan(Target target, IList<Change> changes)
    {
        RequireEditMode();
        if (changes == null) throw new ArgumentNullException("changes");
        var batch = new Batch { id = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N"), projectPath = Path.GetFullPath("."), createdUtc = DateTime.UtcNow.ToString("o"), target = target };
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            if (change == null || !paths.Add(change.assetPath)) throw new ArgumentException("Null or duplicate texture request.");
            var row = InspectAsset(change.assetPath, target);
            if (!row.Editable) throw new InvalidOperationException(change.assetPath + ": " + row.lockedReason);
            if (string.IsNullOrEmpty(change.expectedImporterHash) || change.expectedImporterHash != row.importerHash) throw new InvalidOperationException(change.assetPath + ": importer changed since scan. Rescan before applying.");
            if (!IsAllowedSize(change.maxSize) || change.maxSize > LargestAllowedCap(row.sourceWidth, row.sourceHeight)) throw new ArgumentException(change.assetPath + ": cap must be an offered size no larger than the source's longest edge.");
            if (change.maxSize > row.effectiveCap) throw new ArgumentException(change.assetPath + ": optimizer never increases an effective cap. Use Inspector for deliberate resolution increases.");
            if (change.maxSize == row.effectiveCap) { batch.notices.Add(change.assetPath + ": unchanged cap; skipped (no override created)."); continue; }
            var importer = (TextureImporter)AssetImporter.GetAtPath(change.assetPath);
            var platform = target == Target.Default ? importer.GetDefaultPlatformTextureSettings() : importer.GetPlatformTextureSettings(PlatformName(target));
            batch.entries.Add(new Entry { assetPath = change.assetPath, guid = row.guid, sourceHash = SourceHash(change.assetPath), beforeHash = row.importerHash, beforeCap = platform.maxTextureSize, targetCap = change.maxSize, beforePlatformJson = JsonUtility.ToJson(platform), beforeHadPlatformRecord = HasPlatformRecord(importer, target) });
            if (!string.IsNullOrEmpty(row.warning)) batch.notices.Add(change.assetPath + ": " + row.warning);
        }
        batch.backupPath = BackupDirectory + "/" + batch.id + ".json";
        return batch;
    }
    private static bool HasPlatformRecord(TextureImporter importer, Target target)
    {
        if (target == Target.Default) return true;
        using (var serialized = new SerializedObject(importer))
        {
            var platforms = serialized.FindProperty("m_PlatformSettings");
            if (platforms == null || !platforms.isArray) throw new InvalidOperationException("Cannot safely inspect platform record presence in this Unity version.");
            for (int i = 0; i < platforms.arraySize; i++)
            {
                var name = platforms.GetArrayElementAtIndex(i).FindPropertyRelative("m_BuildTarget");
                if (name == null) throw new InvalidOperationException("Cannot safely inspect platform record names in this Unity version.");
                if (name.stringValue == PlatformName(target)) return true;
            }
            return false;
        }
    }
    private static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Wait for Edit mode and all imports/compilation to finish.");
    }
    private static void SaveBatch(Batch batch)
    {
        Directory.CreateDirectory(BackupDirectory);
        string temporary = batch.backupPath + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            writer.Write(JsonUtility.ToJson(batch, true));
            writer.Flush(); stream.Flush(true);
        }
        if (File.Exists(batch.backupPath)) File.Replace(temporary, batch.backupPath, null);
        else File.Move(temporary, batch.backupPath);
    }
    private static string CurrentPath(Entry entry)
    {
        string path = AssetDatabase.GUIDToAssetPath(entry.guid);
        return string.IsNullOrEmpty(path) ? entry.assetPath : path;
    }
    private static bool MatchesSource(Entry entry, string path) { return File.Exists(path) && SourceHash(path) == entry.sourceHash; }
    private static void SetCap(TextureImporter importer, Target target, int cap)
    {
        if (target == Target.Default) importer.maxTextureSize = cap;
        else
        {
            var platform = importer.GetPlatformTextureSettings(PlatformName(target));
            if (!platform.overridden)
            {
                // Snapshot effective inherited behavior, never activate stale inactive compression fields.
                platform = JsonUtility.FromJson<TextureImporterPlatformSettings>(JsonUtility.ToJson(importer.GetDefaultPlatformTextureSettings()));
                platform.name = PlatformName(target);
            }
            platform.maxTextureSize = cap;
            platform.overridden = true;
            importer.SetPlatformTextureSettings(platform);
        }
    }

    /// <summary>Call only after presenting the complete plan and receiving explicit user confirmation. Partial batches are journaled per asset.</summary>
    public static Outcome ApplyPlan(Batch batch, bool showProgress = false)
    {
        RequireEditMode();
        if (batch == null || batch.status != "Preview" || Path.GetFullPath(".") != batch.projectPath) throw new InvalidOperationException("Invalid, already used or foreign-project plan.");
        var result = new Outcome { backupPath = batch.backupPath };
        if (batch.entries.Count == 0) return result;
        batch.status = "Applying";
        SaveBatch(batch); // Durable backup must exist before any importer changes.
        try
        {
            for (int i = 0; i < batch.entries.Count; i++)
            {
                var entry = batch.entries[i];
                if (Cancel(showProgress, "Applying texture caps", entry.assetPath, i, batch.entries.Count)) { result.canceled = true; break; }
                try
                {
                    string path = CurrentPath(entry);
                    var row = InspectAsset(path, batch.target);
                    if (!row.Editable || row.importerHash != entry.beforeHash || !MatchesSource(entry, path))
                    {
                        entry.state = "Conflict"; entry.message = "Changed, moved, locked or source edited since preview. No write.";
                        result.conflicts++; result.messages.Add(path + ": " + entry.message); SaveBatch(batch); continue;
                    }
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    entry.state = "Applying";
                    // Compute and durably record the intended importer state before reimport can fail.
                    SetCap(importer, batch.target, entry.targetCap);
                    entry.expectedAfterHash = ImporterHash(importer);
                    try { SaveBatch(batch); }
                    catch
                    {
                        RestoreSettings(importer, batch.target, entry);
                        throw;
                    }
                    importer.SaveAndReimport();
                    entry.expectedAfterHash = ImporterHash((TextureImporter)AssetImporter.GetAtPath(path));
                    entry.state = "Applied"; entry.message = "Cap applied and reimport completed.";
                    result.applied++;
                    SaveBatch(batch);
                }
                catch (Exception ex)
                {
                    // Keep Applying entries recoverable if a reimport throws. Never pretend they are untouched.
                    entry.message = ex.Message;
                    result.failed++; result.messages.Add(entry.assetPath + ": " + ex.Message + " (see backup state)");
                    SaveBatch(batch);
                }
            }
            foreach (var entry in batch.entries) if (entry.state == "Planned") result.pending++;
            return result;
        }
        finally
        {
            if (showProgress) EditorUtility.ClearProgressBar();
            batch.status = result.canceled ? "Canceled" : (result.failed > 0 || result.conflicts > 0 ? "Partial" : "Completed");
            SaveBatch(batch);
        }
    }
    private static void RestoreSettings(TextureImporter importer, Target target, Entry entry)
    {
        if (target == Target.Default) importer.maxTextureSize = entry.beforeCap;
        else if (!entry.beforeHadPlatformRecord) importer.ClearPlatformTextureSettings(PlatformName(target));
        else importer.SetPlatformTextureSettings(JsonUtility.FromJson<TextureImporterPlatformSettings>(entry.beforePlatformJson));
    }
    public static Batch ReadLastBatch()
    {
        if (!Directory.Exists(BackupDirectory)) return null;
        var files = Directory.GetFiles(BackupDirectory, "*.json");
        Array.Sort(files, StringComparer.Ordinal);
        if (files.Length == 0) return null;
        return ReadBatch(files[files.Length - 1]);
    }
    public static Batch ReadBatch(string path)
    {
        string full = Path.GetFullPath(path), root = Path.GetFullPath(BackupDirectory) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Backup must belong to this project's optimizer backup directory.");
        var batch = JsonUtility.FromJson<Batch>(File.ReadAllText(full));
        if (batch == null || batch.schemaVersion != 1 || batch.projectPath != Path.GetFullPath(".")) throw new InvalidOperationException("Unsupported or foreign-project backup.");
        batch.backupPath = path;
        return batch;
    }
    /// <summary>Safe per-entry restore: refuses any importer/source modification since Apply. Never overwrites unrelated subsequent edits.</summary>
    public static Outcome RestoreLastBatch(bool showProgress = false)
    {
        var batch = ReadLastBatch();
        if (batch == null) throw new InvalidOperationException("No saved optimizer batch exists.");
        return RestoreBatch(batch.backupPath, showProgress);
    }
    public static Outcome RestoreBatch(string backupPath, bool showProgress = false)
    {
        RequireEditMode();
        var batch = ReadBatch(backupPath);
        var result = new Outcome { backupPath = backupPath };
        try
        {
            for (int i = 0; i < batch.entries.Count; i++)
            {
                var entry = batch.entries[i];
                if (entry.state != "Applied" && entry.state != "Applying" && entry.state != "RestoreConflict") { result.unchanged++; continue; }
                if (Cancel(showProgress, "Restoring texture caps", entry.assetPath, i, batch.entries.Count)) { result.canceled = true; break; }
                try
                {
                    string path = CurrentPath(entry);
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    var row = importer == null ? null : InspectAsset(path, batch.target);
                    if (importer == null || row == null || !row.Editable || !MatchesSource(entry, path))
                    {
                        entry.state = "RestoreConflict"; entry.message = "Asset missing/locked or source changed. Restore refused.";
                        result.conflicts++; result.messages.Add(path + ": " + entry.message); SaveBatch(batch); continue;
                    }
                    string hash = ImporterHash(importer);
                    if (hash == entry.beforeHash)
                    {
                        entry.state = "Restored"; entry.message = "Already matches original importer.";
                        result.unchanged++; SaveBatch(batch); continue;
                    }
                    if (string.IsNullOrEmpty(entry.expectedAfterHash) || hash != entry.expectedAfterHash)
                    {
                        entry.state = "RestoreConflict"; entry.message = "Importer edited since Apply (or uncertain interrupted import). Restore refused; retain backup for manual review.";
                        result.conflicts++; result.messages.Add(path + ": " + entry.message); SaveBatch(batch); continue;
                    }
                    RestoreSettings(importer, batch.target, entry);
                    importer.SaveAndReimport();
                    entry.state = "Restored"; entry.message = "Original resolution/override settings restored.";
                    result.restored++; SaveBatch(batch);
                }
                catch (Exception ex)
                {
                    result.failed++; entry.message = ex.Message;
                    result.messages.Add(entry.assetPath + ": " + ex.Message); SaveBatch(batch);
                }
            }
            foreach (var entry in batch.entries) if (entry.state == "Applied" || entry.state == "Applying") result.pending++;
            return result;
        }
        finally
        {
            if (showProgress) EditorUtility.ClearProgressBar();
            // Do not overwrite Apply's status: entry states retain complete partial/canceled restore history.
            SaveBatch(batch);
        }
    }
}

}
