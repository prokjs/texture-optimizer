using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Prokjs.TextureOptimizer
{
/// <summary>Explicit, reversible static occluder authoring. Never bakes or hides renderers itself.</summary>
public static class WorldOcclusionOptimizerBackend
{
    private const string ReceiptPath = "UserSettings/ProkjsWorldOptimizerBatches/LastSceneSetup.json";
    [Serializable] private sealed class Receipt
    {
        public string objectId;
        public int before, after, receiveBefore, receiveAfter;
        public bool floor;
    }

    private static void Guard(GameObject obj)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling)
            throw new InvalidOperationException("Occlusion authoring requires idle Edit mode.");
        if (obj == null || !obj.scene.IsValid() || !obj.scene.isLoaded || EditorUtility.IsPersistent(obj))
            throw new InvalidOperationException("Select one loaded scene object, not a project asset.");
        if (string.IsNullOrEmpty(obj.scene.path))
            throw new InvalidOperationException("Save the scene before creating a restorable occluder change.");
    }

    private static string Ineligible(GameObject obj)
    {
        var renderer = obj.GetComponent<MeshRenderer>();
        var filter = obj.GetComponent<MeshFilter>();
        if (renderer == null || filter == null || filter.sharedMesh == null)
            return "Select the actual MeshRenderer object, not a container. No geometry was inferred from child bounds.";
        if (!renderer.enabled || renderer.forceRenderingOff || !obj.activeInHierarchy || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
            return "The renderer is inactive, hidden or shadows-only; it cannot safely stand in for a visible blocker.";
        for (Transform t = obj.transform; t != null; t = t.parent)
        {
            if (t.GetComponent<Rigidbody>() != null || t.GetComponent<Animator>() != null || t.GetComponent<Animation>() != null ||
                t.GetComponent<Cloth>() != null || t.GetComponent<Canvas>() != null || t.GetComponent<CharacterController>() != null)
                return "Moving, animated, cloth and UI objects are not supported as baked static occluders.";
            foreach (Component component in t.GetComponents<Component>())
            {
                if (component == null) return "Missing scripts prevent verifying static ownership.";
                string type = component.GetType().FullName ?? "";
                if (component is MonoBehaviour || type.Contains("VRCObjectSync") || type.Contains("VRC_Pickup") || type.Contains("VRCPickup") || type.Contains("VRCStation"))
                    return "A script, Udon behaviour, pickup or station controls this hierarchy. Keep its existing visibility policy.";
            }
        }
        Material[] materials = renderer.sharedMaterials;
        if (materials.Length == 0) return "No opaque material is assigned.";
        foreach (Material material in materials)
        {
            if (material == null || material.shader == null) return "Missing material/shader.";
            string shader = material.shader.name;
            if (material.renderQueue > 2000 || material.GetTag("RenderType", false, "Opaque") != "Opaque" ||
                material.HasProperty("_Mode") && material.GetFloat("_Mode") != 0f)
                return "Transparent and alpha-cutout surfaces must not be treated as solid blockers.";
            if (shader != "Standard" && shader != "Unlit/Color")
                return "Unknown shader opacity: use Unity's Occlusion window after manually verifying its rendering, rather than automatic approval.";
        }
        var groups = obj.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<LODGroup>(true));
        foreach (var group in groups)
        {
            LOD[] levels = group.GetLODs();
            for (int i = 1; i < levels.Length; i++)
                if (levels[i].renderers.Contains(renderer)) return "This renderer is a lower LOD. Select its original LOD0 geometry for occluder authoring.";
        }
        return null;
    }

    public static string Analyze(GameObject obj)
    {
        Guard(obj);
        string reason = Ineligible(obj);
        bool marked = (GameObjectUtility.GetStaticEditorFlags(obj) & StaticEditorFlags.OccluderStatic) != 0;
        return "Occluder Static: " + (marked ? "ON" : "OFF") + ". " +
            (reason ?? "Eligible fixed opaque geometry. Baking uses the actual mesh, not a solid approximation of its bounding box.") +
            "\nThis marks only this object. Other objects need appropriate Occludee settings, and static occlusion data must be baked for the scene. It does not instantly hide everything behind the object. Gaps, sightlines, cameras and VR views still matter. Existing runtime visibility controllers are separate and are not rewritten.";
    }

    public static string SetOccluder(GameObject obj, bool enabled)
    {
        Guard(obj);
        if (enabled)
        {
            string reason = Ineligible(obj);
            if (reason != null) throw new InvalidOperationException(reason);
        }
        StaticEditorFlags before = GameObjectUtility.GetStaticEditorFlags(obj);
        StaticEditorFlags after = enabled ? before | StaticEditorFlags.OccluderStatic : before & ~StaticEditorFlags.OccluderStatic;
        if (before == after) return "Occluder setting is already " + (enabled ? "ON" : "OFF") + "; no change.";
        var receipt = new Receipt { objectId = GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString(), before = (int)before, after = (int)after };
        Directory.CreateDirectory(Path.GetDirectoryName(ReceiptPath));
        string temporary = ReceiptPath + ".tmp";
        File.WriteAllText(temporary, JsonUtility.ToJson(receipt, true));
        if (File.Exists(ReceiptPath)) File.Replace(temporary, ReceiptPath, null); else File.Move(temporary, ReceiptPath);
        Undo.RecordObject(obj, WorldOptimizerLocalization.Tr("Set occluder flag"));
        GameObjectUtility.SetStaticEditorFlags(obj, after);
        PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
        EditorSceneManager.MarkSceneDirty(obj.scene);
        return "Marked " + obj.name + " as Occluder Static " + (enabled ? "ON" : "OFF") + ". Scene left unsaved; Undo and Restore Last Occluder Change are available. No bake was started; existing bake data has not been regenerated.";
    }

    public static string RestoreLastChange()
    {
        if (!File.Exists(ReceiptPath)) return "No recorded occluder change to restore.";
        var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(ReceiptPath));
        if (receipt == null || !GlobalObjectId.TryParse(receipt.objectId, out GlobalObjectId id)) throw new InvalidOperationException("Invalid occluder receipt.");
        var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
        Guard(obj);
        int current = (int)GameObjectUtility.GetStaticEditorFlags(obj);
        var renderer = obj.GetComponent<MeshRenderer>();
        if (current == receipt.before && (!receipt.floor || renderer != null && (int)renderer.receiveGI == receipt.receiveBefore))
            return "Settings already restored (possibly by Undo).";
        if (current != receipt.after || receipt.floor && (renderer == null || (int)renderer.receiveGI != receipt.receiveAfter))
            throw new InvalidOperationException("Settings changed after this tool's edit; restore refused to preserve your changes.");
        Undo.RecordObject(obj, WorldOptimizerLocalization.Tr("Restore optimizer scene settings"));
        if (receipt.floor) { Undo.RecordObject(renderer, WorldOptimizerLocalization.Tr("Restore floor lightmap mode")); renderer.receiveGI = (ReceiveGI)receipt.receiveBefore; PrefabUtility.RecordPrefabInstancePropertyModifications(renderer); }
        GameObjectUtility.SetStaticEditorFlags(obj, (StaticEditorFlags)receipt.before);
        PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
        EditorSceneManager.MarkSceneDirty(obj.scene);
        return "Restored original static flags on " + obj.name + ". Scene left unsaved; no bake was started.";
    }

    public static string AnalyzeFloor(GameObject obj)
    {
        Guard(obj);
        string reason = Ineligible(obj);
        var renderer = obj.GetComponent<MeshRenderer>();
        var mesh = obj.GetComponent<MeshFilter>();
        if (reason == null && renderer.sharedMaterials.Any(material => material.shader.name != "Standard"))
            reason = "This shader does not have a verified baked-lighting receiver path (Unlit materials do not display floor shadows).";
        if (reason == null && (!mesh.sharedMesh.isReadable || mesh.sharedMesh.uv2.Length != mesh.sharedMesh.vertexCount))
            reason = "A readable mesh with authored lightmap UV2 is required. This tool will not rewrite a shared mesh/importer to guess UVs.";
        return (reason ?? "Eligible fixed opaque floor renderer with lightmap UV2.") +
            "\nPrepare changes only this renderer's Contribute GI flag and Receive GI mode. Existing Baked/Mixed lights and shadow-casting geometry determine the result. Baking is scene-wide, not floor-only; it can take a long time. No lights, UVs, colliders or runtime scripts are changed.";
    }

    public static string PrepareFloor(GameObject obj)
    {
        Guard(obj);
        string report = AnalyzeFloor(obj);
        if (!report.StartsWith("Eligible", StringComparison.Ordinal)) throw new InvalidOperationException(report);
        var renderer = obj.GetComponent<MeshRenderer>();
        var before = GameObjectUtility.GetStaticEditorFlags(obj);
        var after = before | StaticEditorFlags.ContributeGI;
        if (before == after && renderer.receiveGI == ReceiveGI.Lightmaps)
            return "Floor already contributes GI and receives lightmaps. No setup changed; no bake started.";
        var receipt = new Receipt { objectId = GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString(),
            before = (int)before, after = (int)after, floor = true,
            receiveBefore = (int)renderer.receiveGI, receiveAfter = (int)ReceiveGI.Lightmaps };
        Directory.CreateDirectory(Path.GetDirectoryName(ReceiptPath));
        string temporary = ReceiptPath + ".tmp";
        File.WriteAllText(temporary, JsonUtility.ToJson(receipt, true));
        if (File.Exists(ReceiptPath)) File.Replace(temporary, ReceiptPath, null); else File.Move(temporary, ReceiptPath);
        Undo.RecordObjects(new UnityEngine.Object[] { obj, renderer }, WorldOptimizerLocalization.Tr("Prepare baked floor shadows"));
        GameObjectUtility.SetStaticEditorFlags(obj, after);
        renderer.receiveGI = ReceiveGI.Lightmaps;
        PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        EditorSceneManager.MarkSceneDirty(obj.scene);
        return "Floor prepared for baked lightmap shadows. Scene left unsaved. Restore Last Scene Setup Change recovers its previous flags/GI mode. No bake or light-mode conversion started.";
    }

    public static string StartLightingBake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Lighting bake requires an idle editor in Edit mode.");
        if (Lightmapping.isRunning) throw new InvalidOperationException("A lighting bake is already running; use Cancel Lighting Bake if needed.");
        if (UnityEngine.SceneManagement.SceneManager.sceneCount != 1 || !scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path) || scene.isDirty)
            throw new InvalidOperationException("Save one loaded scene before baking; this tool never saves or closes scenes for you.");
        var lights = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Light>(true));
        if (!lights.Any(light => light.enabled && light.gameObject.activeInHierarchy &&
            light.lightmapBakeType != LightmapBakeType.Realtime && light.shadows != LightShadows.None))
            throw new InvalidOperationException("No enabled Baked/Mixed shadow-casting light exists. Configure lights explicitly; no realtime light was converted.");
        if (!Lightmapping.BakeAsync()) throw new InvalidOperationException("Unity refused to start the scene lighting bake. Inspect its Console; no fallback bake was started.");
        return "Unity scene-wide lighting bake started with existing settings. This is not a completed bake or a floor-only bake. Cancel Lighting Bake is available; setup restoration does not roll back generated lighting data.";
    }

    public static string CancelLightingBake()
    {
        if (!Lightmapping.isRunning) return "No lighting bake is running.";
        Lightmapping.Cancel();
        return "Lighting bake cancellation requested. Existing/generated lighting data is not represented as restored.";
    }

    public static void OpenOcclusionWindow()
    {
        EditorApplication.ExecuteMenuItem("Window/Rendering/Occlusion Culling");
    }
}

}
