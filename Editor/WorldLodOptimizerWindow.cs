using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Backend = Prokjs.TextureOptimizer.WorldLodOptimizerBackend;
using static Prokjs.TextureOptimizer.WorldOptimizerLocalization;

namespace Prokjs.TextureOptimizer
{
/// <summary>Explicit, read-only analysis and mesh previews before a confirmed LOD batch.</summary>
public sealed class WorldLodOptimizerWindow : EditorWindow
{
    [SerializeField] private Backend.Scope scope = Backend.Scope.LoadedScenes;
    [SerializeField] private bool includeInactive;
    [SerializeField] private int lowerLevels = 2;
    [SerializeField] private float[] reductions = { 0f, 50f, 75f, 87.5f };
    [SerializeField] private float[] transitions = { .6f, .3f, .1f, .03f };
    [SerializeField] private TreeViewState treeState;
    private readonly List<Backend.Row> rows = new List<Backend.Row>();
    private readonly HashSet<Backend.Row> checkedRows = new HashSet<Backend.Row>();
    private List<string> otherCosts = new List<string>();
    private CostTable table;
    private SearchField search;
    private Backend.Plan plan;
    private Backend.Row inspectedRow;
    private PreviewRenderUtility meshPreview;
    private Material previewMaterial;
    private Vector2 scroll, sceneToolsScroll, noticeScroll;
    private EditorWindow hostWindow;
    private Vector2 orbit = new Vector2(-20f, -25f);
    private float zoom = 1f;
    private int previewIndex;
    private int[] scannedRoots;
    private bool analyzed, stale, busy, applying, showDetails = true, showOtherCosts = true;
    private string status = "Choose a scope, then Analyze. Opening this window never scans or changes the world.";
    private bool showOcclusion = true;
    private string occlusionStatus = "Select a scene object, then Analyze Occluder to inspect its current flags and suitability.";
    private bool showFloorShadows = true;
    private string floorShadowStatus = "Select a static floor mesh, then Analyze Floor before preparing it to receive baked shadows.";
    private MessageType statusType = MessageType.Info;
    private string[] previewLabels;
    private static readonly string[] ScopeLabels = Enum.GetNames(typeof(Backend.Scope));
    private const string EstimateDisclaimer = "Static geometry and configuration estimates only — not GPU/CPU profiling, measured frame time, or an FPS prediction. High triangle counts alone do not prove a bottleneck.";

    public static void Open()
    {
        WorldTextureOptimizerWindow.OpenLODTab();
    }

    internal void SetHost(EditorWindow host) { hostWindow = host; }
    internal void NotifySelectionChanged() { OnSelectionChange(); }
    private void RefreshLanguage()
    {
        if (table != null) table.RefreshLanguage();
        RequestRepaint();
    }

    private void RequestRepaint()
    {
        if (hostWindow != null) hostWindow.Repaint();
        else Repaint();
    }

    private void OnEnable()
    {
        if (treeState == null) treeState = new TreeViewState();
        search = new SearchField();
        table = new CostTable(treeState, this);
        LanguageChanged += RefreshLanguage;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        EditorApplication.hierarchyChanged += WorldChanged;
        EditorApplication.projectChanged += WorldChanged;
        Undo.undoRedoPerformed += WorldChanged;
    }

    private void OnDisable()
    {
        LanguageChanged -= RefreshLanguage;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.hierarchyChanged -= WorldChanged;
        EditorApplication.projectChanged -= WorldChanged;
        Undo.undoRedoPerformed -= WorldChanged;
        DiscardPlan();
        ReleaseVisualPreview();
    }

    private void PlayModeChanged(PlayModeStateChange change)
    {
        InvalidateAnalysis("Play mode changed. Analyze again in Edit mode before applying a batch.");
    }

    private void WorldChanged()
    {
        if (busy || applying) return;
        if (plan != null)
        {
            try
            {
                // PreviewRenderUtility owns hidden cameras/lights that also raise hierarchyChanged.
                // Preserve the displayed plan only while its captured sources and settings still match.
                // Apply performs the backend's full ownership and safety validation separately.
                if (Backend.IsPreviewCurrent(plan))
                {
                    RequestRepaint();
                    return;
                }
            }
            catch (Exception ex)
            {
                InvalidateAnalysis("Preview validation failed: " + ex.Message + ". Analyze again before applying.");
                return;
            }
        }
        InvalidateAnalysis("Scene, assets, or Undo state changed. Analyze again before generating or applying another preview.");
    }

    private void OnSelectionChange()
    {
        if (scope == Backend.Scope.SelectedObjects && analyzed && !SameRoots(Selection.gameObjects))
            InvalidateAnalysis("The Unity selection changed. Analyze again to use the new selected roots.");
        RequestRepaint();
    }

    private bool SameRoots(GameObject[] roots)
    {
        if (scannedRoots == null || scannedRoots.Length != roots.Length) return false;
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null || Array.IndexOf(scannedRoots, roots[i].GetInstanceID()) < 0) return false;
        }
        return true;
    }

    private void InvalidateAnalysis(string message)
    {
        DiscardPlan();
        if (analyzed)
        {
            stale = true;
            SetStatus(message, MessageType.Warning);
        }
        RequestRepaint();
    }

    internal void DrawEmbedded(bool sceneToolsOnly)
    {
        using (var view = new EditorGUILayout.ScrollViewScope(sceneToolsOnly ? sceneToolsScroll : scroll))
        {
            if (sceneToolsOnly) sceneToolsScroll = view.scrollPosition;
            else scroll = view.scrollPosition;
            EditorGUILayout.LabelField(Tr(sceneToolsOnly ? "Occlusion & Baked Floor Shadows" : "LOD & Static Cost Analysis"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(Tr(EstimateDisclaimer), MessageType.Info);
            if (!sceneToolsOnly)
                EditorGUILayout.HelpBox(Tr("Only confirmed Apply or Restore changes the project. LOD0 keeps the original renderers; generated lower levels contain MeshFilter + MeshRenderer only. Source meshes, materials, colliders and gameplay components are not replaced. No automatic scene saving."), MessageType.None);
            bool play = EditorApplication.isPlayingOrWillChangePlaymode;
            if (play) EditorGUILayout.HelpBox(Tr("Edit mode only. Analysis and project-changing controls are disabled while entering or running Play mode."), MessageType.Warning);
            using (new EditorGUI.DisabledScope(busy || play))
            {
                if (sceneToolsOnly)
                {
                    DrawOcclusion();
                    DrawFloorShadows();
                }
                else
                {
                    DrawScope();
                    DrawInventory();
                    DrawDetails();
                    DrawOtherCosts();
                    DrawSettings();
                    DrawActions();
                }
            }
            if (!sceneToolsOnly)
            {
                DrawPreview();
                EditorGUILayout.HelpBox(Tr(status), statusType);
            }
            GUILayout.Space(8f);
        }
    }

    private void DrawScope()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            scope = (Backend.Scope)EditorGUILayout.Popup(Tr("Analyze scope"), (int)scope, Tr(ScopeLabels));
            includeInactive = EditorGUILayout.ToggleLeft(Tr("Include inactive objects"), includeInactive, GUILayout.Width(180f));
            if (EditorGUI.EndChangeCheck()) InvalidateAnalysis("Scope changed. Analyze again; the list is the previous snapshot.");
            if (GUILayout.Button(Tr("Analyze"), GUILayout.Width(105f))) Analyze();
        }
        if (scope == Backend.Scope.SelectedObjects)
            EditorGUILayout.LabelField(Tr("Selected roots: " + Selection.gameObjects.Length + ". Children are included; overlapping selections are deduplicated."), EditorStyles.wordWrappedMiniLabel);
        if (stale) EditorGUILayout.HelpBox(Tr("Analysis is out of date. Analyze again before generating a preview. Existing results remain available for inspection."), MessageType.Warning);
    }

    private void DrawInventory()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            table.searchString = search.OnToolbarGUI(table.searchString);
            if (GUILayout.Button(Tr("Select recommended"), EditorStyles.toolbarButton, GUILayout.Width(145f))) SelectRecommended();
            if (GUILayout.Button(Tr("Clear selection"), EditorStyles.toolbarButton, GUILayout.Width(100f)))
            {
                checkedRows.Clear();
                DiscardPlan();
                RequestRepaint();
            }
        }
        Rect rect = GUILayoutUtility.GetRect(0f, 100000f, 220f, 220f, GUILayout.ExpandWidth(true));
        table.OnGUI(rect);
        long vertices = 0, triangles = 0;
        int recommended = 0;
        foreach (var row in rows)
        {
            if (row.recommended && row.editable) recommended++;
            if (!checkedRows.Contains(row)) continue;
            vertices += row.vertices;
            triangles += row.triangles;
        }
        EditorGUILayout.LabelField(Tr(rows.Count + " rows | " + recommended + " recommended | " + checkedRows.Count + " checked | checked original geometry: " + Count(vertices) + " vertices / " + Count(triangles) + " triangles"), EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField(Tr("Sort by a column header. Checkboxes choose the batch; clicking a row shows its sources and safety details. Read-only rows cannot be checked."), EditorStyles.wordWrappedMiniLabel);
    }

    private void DrawDetails()
    {
        showDetails = EditorGUILayout.Foldout(showDetails, Tr("Inspected row / existing LODs"), true);
        if (!showDetails) return;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            var row = inspectedRow;
            if (row == null)
            {
                EditorGUILayout.LabelField(Tr("Click a result row to inspect its source meshes and existing LOD coverage."), EditorStyles.wordWrappedMiniLabel);
                return;
            }
            EditorGUILayout.LabelField(row.name ?? Tr("Object"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(row.path ?? "", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField(Tr("Scene root"), row.root, typeof(GameObject), true);
                if (GUILayout.Button(Tr("Ping"), GUILayout.Width(50f)) && row.root != null) EditorGUIUtility.PingObject(row.root);
            }
            EditorGUILayout.LabelField(Tr("Original cost"), Tr(Count(row.vertices) + " vertices / " + Count(row.triangles) + " triangles"));
            EditorGUILayout.LabelField(Tr("Eligibility"), Tr(row.editable ? (row.recommended ? "Safe candidate — recommended by static geometry threshold" : "Safe manual candidate — not automatically recommended") : "Inspect only — excluded from automatic and manual edits"), EditorStyles.wordWrappedLabel);
            if (!string.IsNullOrEmpty(row.warning)) EditorGUILayout.HelpBox(Tr(row.warning), row.editable ? MessageType.Info : MessageType.Warning);
            if (row.existingLevels != null && row.existingLevels.Length > 0)
                foreach (string level in row.existingLevels) EditorGUILayout.LabelField(Tr(level), EditorStyles.wordWrappedMiniLabel);
            else EditorGUILayout.LabelField(Tr("No existing LOD coverage reported."), EditorStyles.miniLabel);
            if (row.group != null)
            {
                EditorGUILayout.ObjectField(Tr("Existing LODGroup"), row.group, typeof(LODGroup), true);
                LOD[] levels = row.group.GetLODs();
                for (int i = 0; i < levels.Length; i++)
                {
                    var renderers = levels[i].renderers;
                    EditorGUILayout.LabelField(Tr("Existing LOD" + i + " — " + (renderers == null ? 0 : renderers.Length) + " renderer slots; screen height " + (levels[i].screenRelativeTransitionHeight * 100f).ToString("0.##") + "%"), EditorStyles.wordWrappedMiniLabel);
                    if (renderers == null) continue;
                    foreach (var renderer in renderers) DrawSourceMesh(renderer, "LOD" + i);
                }
            }
            else if (row.sources != null)
            {
                foreach (var renderer in row.sources) DrawSourceMesh(renderer, "Original");
            }
            EditorGUILayout.LabelField(Tr("Populated existing levels are preserved, not regenerated. Builder-owned or conflicting groups remain inspect-only; missing slots are filled only when the backend confirms compatibility."), EditorStyles.wordWrappedMiniLabel);
        }
    }

    private static void DrawSourceMesh(Renderer renderer, string label)
    {
        if (renderer == null) return;
        Mesh mesh = null;
        var filter = renderer.GetComponent<MeshFilter>();
        if (filter != null) mesh = filter.sharedMesh;
        var skinned = renderer as SkinnedMeshRenderer;
        if (skinned != null) mesh = skinned.sharedMesh;
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.ObjectField(Tr(label + " mesh: " + renderer.name), mesh, typeof(Mesh), false);
            if (mesh != null)
            {
                GUILayout.Label(Tr(Count(mesh.vertexCount) + " vertices / " + Count(Triangles(mesh)) + " triangles"), EditorStyles.miniLabel, GUILayout.Width(205f));
                if (GUILayout.Button(Tr("Ping"), GUILayout.Width(45f))) EditorGUIUtility.PingObject(mesh);
                if (GUILayout.Button(Tr("Inspect"), GUILayout.Width(55f))) Selection.activeObject = mesh;
            }
        }
    }

    private void DrawOtherCosts()
    {
        showOtherCosts = EditorGUILayout.Foldout(showOtherCosts, Tr("Other possible costs — separate static findings (" + otherCosts.Count + ")"), true);
        if (!showOtherCosts) return;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(Tr("These flags are not addressed by mesh LOD generation. Confirm importance with Unity/VRChat profiling on the target hardware."), EditorStyles.wordWrappedMiniLabel);
            if (otherCosts.Count == 0) EditorGUILayout.LabelField(Tr(analyzed ? "No additional cost flags reported for this snapshot." : "Analyze to list other possible costs."), EditorStyles.wordWrappedMiniLabel);
            foreach (string finding in otherCosts) EditorGUILayout.LabelField(Tr("• " + finding), EditorStyles.wordWrappedLabel);
        }
    }

    private void DrawSettings()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(Tr("Lower-detail geometry"), EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        lowerLevels = EditorGUILayout.IntSlider(Tr("Generated lower levels"), lowerLevels, 1, 3);
        EditorGUILayout.LabelField(Tr("LOD0: original geometry (0% removed). Sliders below request the percentage of VERTICES REMOVED, not retained and not triangles."), EditorStyles.wordWrappedMiniLabel);
        for (int i = 1; i <= lowerLevels; i++)
        {
            float minimum = i == 1 ? 1f : reductions[i - 1];
            reductions[i] = EditorGUILayout.Slider(Tr("LOD" + i + " vertices removed (%)"), reductions[i], minimum, 95f);
        }
        EditorGUILayout.LabelField(Tr("Conservative edge collapse preserves material/UV seams, tests topology and local surface-plane error, and retains the original bounds envelope. It may retain MORE vertices than requested. Inspect actual counts and silhouette before applying."), EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField(Tr("When to switch detail"), EditorStyles.boldLabel);
        for (int i = 0; i <= lowerLevels; i++)
        {
            float max = i == 0 ? 1f : Mathf.Max(.001f, transitions[i - 1] - .001f);
            float min = .001f * (lowerLevels - i + 1);
            string label = i < lowerLevels ? "LOD" + i + " → LOD" + (i + 1) + " below (%)" : "Last LOD → culled below (%)";
            float value = EditorGUILayout.Slider(Tr(label), transitions[i] * 100f, min * 100f, Mathf.Max(min, max) * 100f);
            transitions[i] = value / 100f;
        }
        EditorGUILayout.LabelField(Tr("Screen height means the object's projected height as a fraction of the camera view, not distance in metres. Thresholds descend from near to far. Camera field of view, object scale and QualitySettings.lodBias change the apparent switch distance; VRChat quality settings can change it too. The last threshold culls the group entirely."), EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField(Tr("Current editor LOD bias: " + QualitySettings.lodBias.ToString("0.##") + ". Existing populated LOD levels and their thresholds are preserved; incompatible requested insertions are reported rather than silently changing them."), EditorStyles.wordWrappedMiniLabel);
        if (EditorGUI.EndChangeCheck())
        {
            DiscardPlan();
            SetStatus("LOD settings changed. Generate a new preview before applying.", MessageType.Info);
        }
    }

    private void DrawOcclusion()
    {
        showOcclusion = EditorGUILayout.Foldout(showOcclusion, Tr("Occlusion — let a static opaque object block what is behind it"), true);
        if (!showOcclusion) return;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.HelpBox(Tr("This configures Unity BAKED STATIC occlusion, not an arbitrary runtime blocker. Marking an object does not bake anything or immediately hide objects behind it. Unity's visibility cells, bake settings and camera positions determine the result. Moving, transparent, thin, one-sided or holed tarps are not reliable full opaque occluders."), MessageType.Info);
            GameObject target = Selection.activeGameObject;
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(Tr("Selected scene object"), target, typeof(GameObject), true);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(target == null || !target.scene.IsValid() || !target.scene.isLoaded))
                {
                    if (GUILayout.Button(Tr("Analyze Occluder"))) AnalyzeOccluder(target);
                    if (GUILayout.Button(Tr("Mark as Occluder…"))) SetOccluder(target, true);
                    if (GUILayout.Button(Tr("Unmark Occluder…"))) SetOccluder(target, false);
                }
                if (GUILayout.Button(Tr("Restore Last Occlusion / Floor Setup…"))) RestoreOccluder();
            }
            if (GUILayout.Button(Tr("Open Unity Occlusion / Bake")))
            {
                try { WorldOcclusionOptimizerBackend.OpenOcclusionWindow(); }
                catch (Exception ex) { occlusionStatus = "Could not open Unity Occlusion: " + ex.Message; }
            }
            EditorGUILayout.LabelField(Tr("Open Unity Occlusion / Bake only opens Unity's native window. Review the object and bake occlusion manually when appropriate; these occlusion controls never start a bake or save scenes automatically."), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.HelpBox(Tr(occlusionStatus), MessageType.None);
        }
    }

    private void AnalyzeOccluder(GameObject target)
    {
        try { occlusionStatus = WorldOcclusionOptimizerBackend.Analyze(target); }
        catch (Exception ex) { occlusionStatus = "Occluder analysis failed: " + ex.Message; }
    }

    private void SetOccluder(GameObject target, bool enabled)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || target == null) return;
        string action = enabled ? "Mark as Occluder" : "Unmark Occluder";
        if (!EditorUtility.DisplayDialog(Tr(action + "?"), Tr(action + " for " + target.name + "?\n\nThis changes the selected object's static occlusion setup only. It is not a runtime hide-behind-object switch, and it does not start a bake. Only suitable stationary opaque geometry should be used as an occluder.\n\nUndo and Restore Last Occlusion Change are available. No scene will be automatically saved."), Tr(action), Tr("Cancel")))
        {
            occlusionStatus = "Occlusion change canceled. Nothing changed.";
            return;
        }
        try
        {
            occlusionStatus = WorldOcclusionOptimizerBackend.SetOccluder(target, enabled);
            InvalidateAnalysis("Occlusion setup changed. Analyze again before another LOD batch.");
        }
        catch (Exception ex) { occlusionStatus = "Occlusion change refused or failed: " + ex.Message; }
    }

    private void RestoreOccluder()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorUtility.DisplayDialog(Tr("Restore last occlusion / floor setup?"), Tr("Restore the last occlusion or floor-preparation change made by this helper? Conflicting edits are checked by the backend. This does not restore unrelated scene work, undo a completed lighting bake, run a bake, or automatically save a scene."), Tr("Restore last setup change"), Tr("Cancel")))
        {
            occlusionStatus = "Occlusion restore canceled. Nothing changed.";
            return;
        }
        try
        {
            occlusionStatus = WorldOcclusionOptimizerBackend.RestoreLastChange();
            InvalidateAnalysis("Occlusion setup restored. Analyze again before another LOD batch.");
        }
        catch (Exception ex) { occlusionStatus = "Occlusion restore refused or failed: " + ex.Message; }
    }

    private void DrawFloorShadows()
    {
        showFloorShadows = EditorGUILayout.Foldout(showFloorShadows, Tr("Floor shadows — prepare a receiver and explicitly bake lighting"), true);
        if (!showFloorShadows) return;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.HelpBox(Tr("Baked floor shadows need suitable stationary geometry, valid lightmap UVs, and existing Baked or Mixed lights with appropriate shadow settings. Preparing a floor is separate from baking. Lighting baking is SCENE-WIDE, not floor-only; it can take a long time and does not bake shadows from every moving object."), MessageType.Info);
            GameObject target = Selection.activeGameObject;
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(Tr("Selected floor object"), target, typeof(GameObject), true);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(target == null || !target.scene.IsValid() || !target.scene.isLoaded))
                {
                    if (GUILayout.Button(Tr("Analyze Floor")))
                    {
                        try { floorShadowStatus = WorldOcclusionOptimizerBackend.AnalyzeFloor(target); }
                        catch (Exception ex) { floorShadowStatus = "Floor analysis failed: " + ex.Message; }
                    }
                    if (GUILayout.Button(Tr("Prepare selected floor…"))) PrepareFloor(target);
                }
                if (GUILayout.Button(Tr("Restore Last Occlusion / Floor Setup…"))) RestoreFloorSetup();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(Tr("Bake scene lighting / floor shadows…"))) StartFloorLightingBake();
                if (GUILayout.Button(Tr("Cancel Lighting Bake")))
                {
                    try { floorShadowStatus = WorldOcclusionOptimizerBackend.CancelLightingBake(); }
                    catch (Exception ex) { floorShadowStatus = "Cancel lighting bake failed: " + ex.Message; }
                }
            }
            EditorGUILayout.LabelField(Tr("Preparation sets Contribute GI and Receive GI = Lightmaps only after safety checks; it does not change light modes. Restore reverts the last setup change, not baked lighting data. Analysis and Auto LOD never trigger a lighting bake."), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.HelpBox(Tr(floorShadowStatus), MessageType.None);
        }
    }

    private void PrepareFloor(GameObject target)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || target == null) return;
        if (!EditorUtility.DisplayDialog(Tr("Prepare selected floor for baked shadows?"), Tr("Prepare " + target.name + " to receive baked lighting?\n\nThe backend validates the static floor and sets Contribute GI plus Receive GI = Lightmaps. This changes setup only; it does not start a bake, alter light modes, or automatically save the scene.\n\nUndo and persistent Restore Last Occlusion / Floor Setup are available."), Tr("Prepare selected floor"), Tr("Cancel")))
        {
            floorShadowStatus = "Floor preparation canceled. Nothing changed.";
            return;
        }
        try
        {
            floorShadowStatus = WorldOcclusionOptimizerBackend.PrepareFloor(target);
            InvalidateAnalysis("Floor lighting setup changed. Analyze again before another LOD batch.");
        }
        catch (Exception ex) { floorShadowStatus = "Floor preparation refused or failed: " + ex.Message; }
    }

    private void RestoreFloorSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorUtility.DisplayDialog(Tr("Restore last occlusion / floor setup?"), Tr("Restore the last occlusion or floor-preparation change made by this helper? Conflicting edits are checked by the backend. This does not restore unrelated scene work or undo a completed lighting bake, and does not automatically save a scene."), Tr("Restore last setup change"), Tr("Cancel")))
        {
            floorShadowStatus = "Setup restoration canceled. Nothing changed.";
            return;
        }
        try
        {
            floorShadowStatus = WorldOcclusionOptimizerBackend.RestoreLastChange();
            InvalidateAnalysis("Static occlusion / floor setup restored. Analyze again before another LOD batch.");
        }
        catch (Exception ex) { floorShadowStatus = "Setup restoration refused or failed: " + ex.Message; }
    }

    private void StartFloorLightingBake()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorUtility.DisplayDialog(Tr("Start scene-wide lighting bake?"), Tr("Start Unity's lighting bake for the loaded scene configuration using the CURRENT Baked / Mixed lights?\n\nThis is a SCENE-WIDE bake, NOT a floor-only operation. It may take a long time and will update baked lighting data. No light modes are changed. Verify your lighting setup and save any work you want preserved before proceeding.\n\nThis does not bake occlusion data. Cancel Lighting Bake requests cancellation; restoring floor setup does not undo completed baked lighting data."), Tr("Start scene-wide lighting bake"), Tr("Cancel")))
        {
            floorShadowStatus = "Lighting bake canceled before starting. Nothing baked.";
            return;
        }
        try
        {
            floorShadowStatus = WorldOcclusionOptimizerBackend.StartLightingBake();
            InvalidateAnalysis("Lighting bake requested. Analyze again after lighting changes before another LOD batch.");
        }
        catch (Exception ex) { floorShadowStatus = "Lighting bake refused or failed: " + ex.Message; }
    }

    private void DrawActions()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!analyzed || stale || rows.Count == 0))
            {
                if (GUILayout.Button(Tr("Auto LOD recommended → Preview"), GUILayout.Height(30f)))
                {
                    SelectRecommended();
                    GeneratePreview(true);
                }
                using (new EditorGUI.DisabledScope(checkedRows.Count == 0))
                    if (GUILayout.Button(Tr("Generate Preview for checked"), GUILayout.Height(30f))) GeneratePreview(false);
            }
            if (GUILayout.Button(Tr("Restore Last Batch…"), GUILayout.Width(155f), GUILayout.Height(30f))) Restore();
        }
        EditorGUILayout.LabelField(Tr("Auto selects all recommended safe rows in the analyzed scope (including rows hidden by search), generates a read-only preview, then waits for your explicit Apply confirmation below."), EditorStyles.wordWrappedMiniLabel);
    }

    private void Analyze()
    {
        DiscardPlan();
        busy = true;
        try
        {
            var roots = scope == Backend.Scope.SelectedObjects ? Selection.gameObjects : null;
            var options = new Backend.ScanOptions { scope = scope, includeInactive = includeInactive, roots = roots };
            var inventory = Backend.Scan(options);
            var findings = Backend.AnalyzeOtherCosts(options);
            rows.Clear();
            if (inventory != null) rows.AddRange(inventory);
            otherCosts = findings ?? new List<string>();
            checkedRows.Clear();
            inspectedRow = null;
            table.Reload();
            scannedRoots = roots == null ? null : Array.ConvertAll(roots, root => root.GetInstanceID());
            stale = false;
            analyzed = true;
            SetStatus("Analysis complete: " + rows.Count + " geometry rows and " + otherCosts.Count + " other-cost findings. Nothing changed. Review candidates or use Auto LOD recommended.", MessageType.Info);
        }
        catch (OperationCanceledException)
        {
            stale = analyzed;
            SetStatus("Analysis canceled. No project changes; the previous snapshot, if any, is now stale.", MessageType.Info);
        }
        catch (Exception ex)
        {
            stale = analyzed;
            SetStatus("Analysis failed: " + ex.Message, MessageType.Error);
        }
        finally
        {
            busy = false;
            EditorUtility.ClearProgressBar();
        }
    }

    private void SelectRecommended()
    {
        DiscardPlan();
        checkedRows.Clear();
        foreach (var row in rows) if (row.editable && row.recommended) checkedRows.Add(row);
        SetStatus(checkedRows.Count + " recommended safe rows checked. Selection covers the full snapshot, not just the search results.", MessageType.Info);
        RequestRepaint();
    }

    private void GeneratePreview(bool automatic)
    {
        DiscardPlan();
        var chosen = new List<Backend.Row>();
        foreach (var row in rows) if (row.editable && checkedRows.Contains(row)) chosen.Add(row);
        if (chosen.Count == 0)
        {
            SetStatus(automatic ? "No recommended safe candidates in this snapshot. No meshes or project changes were created." : "Check at least one safe editable row first.", MessageType.Info);
            return;
        }
        busy = true;
        try
        {
            var settings = new Backend.Settings { reductions = new float[lowerLevels + 1], transitions = new float[lowerLevels + 1] };
            Array.Copy(reductions, settings.reductions, lowerLevels + 1);
            Array.Copy(transitions, settings.transitions, lowerLevels + 1);
            settings.reductions[0] = 0f;
            plan = Backend.Preview(chosen, settings);
            if (plan == null) throw new InvalidOperationException("The backend did not return a preview plan.");
            previewLabels = new string[plan.meshes.Count];
            for (int i = 0; i < previewLabels.Length; i++)
            {
                var item = plan.meshes[i];
                previewLabels[i] = (item.renderer == null ? "Mesh" : item.renderer.name) + " / LOD" + item.level + " / " + Count(item.mesh == null ? 0 : item.mesh.vertexCount) + " vertices";
            }
            previewIndex = 0;
            SetStatus(plan.meshes.Count == 0 ? "Preview produced no applicable meshes. Read the notices below; nothing changed." : (automatic ? "Auto LOD preview ready. " : "Preview ready. ") + plan.meshes.Count + " generated mesh entries. Inspect the original/generated views and actual counts, then explicitly Apply checked preview. Nothing has changed in the project.", MessageType.Info);
        }
        catch (OperationCanceledException)
        {
            DiscardPlan();
            SetStatus("Preview generation canceled. Temporary meshes discarded; nothing applied.", MessageType.Info);
        }
        catch (Exception ex)
        {
            DiscardPlan();
            SetStatus("Preview failed: " + ex.Message, MessageType.Error);
        }
        finally
        {
            busy = false;
            EditorUtility.ClearProgressBar();
        }
    }

    private void DrawPreview()
    {
        if (plan == null) return;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(Tr("Read-only generated preview"), EditorStyles.boldLabel);
        bool canApply = !busy && !stale && !EditorApplication.isPlayingOrWillChangePlaymode && plan.meshes.Count > 0;
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!canApply))
                if (GUILayout.Button(Tr("Apply checked preview…"), GUILayout.Height(30f))) Apply();
            if (GUILayout.Button(Tr("Discard preview"), GUILayout.Height(30f)))
            {
                DiscardPlan();
                SetStatus("Preview discarded. Nothing applied.", MessageType.Info);
            }
        }
        if (plan == null) return;
        EditorGUILayout.LabelField(Tr(plan.rows.Count + " planned rows / " + plan.meshes.Count + " generated mesh entries. Existing populated levels are not included in these generated counts."), EditorStyles.wordWrappedMiniLabel);
        if (plan.meshes.Count > 0)
        {
            previewIndex = Mathf.Clamp(EditorGUILayout.Popup(Tr("Preview mesh / level"), previewIndex, Tr(previewLabels)), 0, plan.meshes.Count - 1);
            var item = plan.meshes[previewIndex];
            DrawCounts(item);
            using (new EditorGUILayout.HorizontalScope())
            {
                zoom = EditorGUILayout.Slider(Tr("Preview zoom"), zoom, .5f, 3f);
                if (GUILayout.Button(Tr("Reset view"), GUILayout.Width(90f))) { orbit = new Vector2(-20f, -25f); zoom = 1f; }
            }
            EditorGUILayout.LabelField(Tr("Drag either view to orbit both meshes. Scroll to zoom. Neutral studio lighting; source materials where available. This is a mesh comparison, not a rendered-world or performance prediction."), EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(Tr("Original — LOD0"), EditorStyles.boldLabel);
                    DrawMeshPreview(GUILayoutUtility.GetRect(150f, 100000f, 240f, 240f), item.source, item);
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(Tr("Generated — LOD" + item.level), EditorStyles.boldLabel);
                    DrawMeshPreview(GUILayoutUtility.GetRect(150f, 100000f, 240f, 240f), item.mesh, item);
                }
            }
            if (!string.IsNullOrEmpty(item.notice)) EditorGUILayout.HelpBox(Tr(item.notice), MessageType.Info);
            EditorGUILayout.LabelField(Tr("All generated levels: requested versus actual"), EditorStyles.boldLabel);
            using (var view = new EditorGUILayout.ScrollViewScope(noticeScroll, GUILayout.Height(Mathf.Min(160f, 30f + plan.meshes.Count * 38f))))
            {
                noticeScroll = view.scrollPosition;
                foreach (var mesh in plan.meshes) DrawCountLine(mesh);
            }
        }
        if (plan.notices.Count > 0)
        {
            EditorGUILayout.LabelField(Tr("Preview notices / skipped candidates"), EditorStyles.boldLabel);
            foreach (string notice in plan.notices) EditorGUILayout.HelpBox(Tr(notice), MessageType.Info);
        }
    }

    private static long TargetVertices(Backend.PreviewMesh item)
    {
        return item.source == null ? 0 : Math.Max(1L, (long)Math.Ceiling(item.source.vertexCount * (1d - item.reduction / 100d)));
    }

    private static void DrawCounts(Backend.PreviewMesh item)
    {
        long original = item.source == null ? 0 : item.source.vertexCount;
        long actual = item.mesh == null ? 0 : item.mesh.vertexCount;
        double removed = original == 0 ? 0 : 100d * (1d - (double)actual / original);
        EditorGUILayout.LabelField(Tr("Requested: " + item.reduction.ToString("0.#") + "% vertices removed → approximately " + Count(TargetVertices(item)) + " remaining."), EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField(Tr("Actual: " + Count(original) + " → " + Count(actual) + " vertices (" + removed.ToString("0.#") + "% removed); triangles " + Count(Triangles(item.source)) + " → " + Count(Triangles(item.mesh)) + "."), EditorStyles.wordWrappedLabel);
        if (actual > TargetVertices(item)) EditorGUILayout.HelpBox(Tr("Target limited: the conservative reducer retained more vertices than requested to protect shape, seams or topology. This is not the requested reduction achieved; inspect the actual mesh."), MessageType.Warning);
    }

    private static void DrawCountLine(Backend.PreviewMesh item)
    {
        long actual = item.mesh == null ? 0 : item.mesh.vertexCount;
        EditorGUILayout.LabelField(Tr((item.renderer == null ? "Mesh" : item.renderer.name) + " / LOD" + item.level + ": requested " + item.reduction.ToString("0.#") + "% removed, ~" + Count(TargetVertices(item)) + " vertices; actual " + Count(actual) + " vertices / " + Count(Triangles(item.mesh)) + " triangles" + (actual > TargetVertices(item) ? " — TARGET LIMITED" : "")), EditorStyles.wordWrappedMiniLabel);
    }

    private void DrawMeshPreview(Rect rect, Mesh mesh, Backend.PreviewMesh item)
    {
        if (mesh == null) { GUI.Box(rect, Tr("No mesh available")); return; }
        int control = GUIUtility.GetControlID(FocusType.Passive);
        Event current = Event.current;
        if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition))
        {
            GUIUtility.hotControl = control;
            current.Use();
        }
        if (current.type == EventType.MouseDrag && GUIUtility.hotControl == control)
        {
            orbit += new Vector2(-current.delta.y, current.delta.x) * .6f;
            orbit.x = Mathf.Clamp(orbit.x, -89f, 89f);
            current.Use();
            RequestRepaint();
        }
        if (current.type == EventType.MouseUp && GUIUtility.hotControl == control)
        {
            GUIUtility.hotControl = 0;
            current.Use();
        }
        if (current.type == EventType.ScrollWheel && rect.Contains(current.mousePosition))
        {
            zoom = Mathf.Clamp(zoom * (1f - current.delta.y * .05f), .5f, 3f);
            current.Use();
            RequestRepaint();
        }
        if (current.type != EventType.Repaint || rect.width < 2f || rect.height < 2f) return;
        try
        {
            EnsureVisualPreview();
            Bounds bounds = item.source == null ? mesh.bounds : item.source.bounds;
            float radius = Mathf.Max(bounds.extents.magnitude, .001f);
            Quaternion rotation = Quaternion.Euler(orbit.x, orbit.y, 0f);
            Matrix4x4 matrix = Matrix4x4.Rotate(rotation) * Matrix4x4.Translate(-bounds.center);
            meshPreview.BeginPreview(rect, GUIStyle.none);
            var camera = meshPreview.camera;
            camera.fieldOfView = 30f;
            camera.nearClipPlane = Mathf.Max(radius * .001f, .00001f);
            camera.farClipPlane = radius * 40f;
            float framing = Mathf.Max(1f, rect.height / rect.width);
            camera.transform.position = new Vector3(0f, 0f, -radius * 4.2f * framing / zoom);
            camera.transform.rotation = Quaternion.identity;
            Material[] materials = item.renderer == null ? null : item.renderer.sharedMaterials;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                Material material = materials != null && submesh < materials.Length ? materials[submesh] : null;
                meshPreview.DrawMesh(mesh, matrix, material == null ? previewMaterial : material, submesh);
            }
            meshPreview.Render();
            GUI.DrawTexture(rect, meshPreview.EndPreview(), ScaleMode.StretchToFill, false);
        }
        catch (Exception ex)
        {
            ReleaseVisualPreview();
            GUI.Box(rect, Tr("Visual preview unavailable: " + ex.Message));
        }
    }

    private void EnsureVisualPreview()
    {
        if (meshPreview != null) return;
        meshPreview = new PreviewRenderUtility();
        meshPreview.camera.clearFlags = CameraClearFlags.SolidColor;
        meshPreview.camera.backgroundColor = new Color(.16f, .17f, .19f, 1f);
        meshPreview.lights[0].intensity = 1.2f;
        meshPreview.lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0f);
        meshPreview.lights[1].intensity = .7f;
        meshPreview.lights[1].transform.rotation = Quaternion.Euler(340f, 215f, 0f);
        meshPreview.ambientColor = new Color(.35f, .35f, .35f, 1f);
        Shader shader = Shader.Find("Standard") ?? Shader.Find("Hidden/InternalErrorShader");
        if (shader == null) throw new InvalidOperationException("No preview shader found.");
        previewMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, color = new Color(.72f, .74f, .78f, 1f) };
    }

    private void Apply()
    {
        if (plan == null || stale || busy || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorUtility.DisplayDialog(Tr("Apply checked LOD preview?"), Tr("Apply the displayed plan for " + plan.rows.Count + " rows / " + plan.meshes.Count + " generated mesh entries?\n\nThis creates generated mesh assets and renderer-only LOD proxies, and changes eligible LODGroups. Original mesh assets and gameplay objects remain intact. Populated existing levels are preserved.\n\nUndo and persistent Restore Last Batch are provided with conflict checks. Scenes are marked dirty but are NOT automatically saved. Review scene changes before saving."), Tr("Apply checked preview"), Tr("Cancel")))
        {
            SetStatus("Apply canceled. The read-only preview is still available; nothing applied.", MessageType.Info);
            return;
        }
        busy = applying = true;
        try
        {
            string report = Backend.Apply(plan);
            DiscardPlan();
            stale = true;
            SetStatus(report + "\nAnalyze again before another batch. Scene saves remain your choice.", MessageType.Info);
        }
        catch (OperationCanceledException)
        {
            DiscardPlan();
            stale = true;
            SetStatus("Apply canceled. Analyze again before proceeding; inspect the scene and backend restoration record if any changes had already begun.", MessageType.Warning);
        }
        catch (Exception ex)
        {
            DiscardPlan();
            stale = true;
            SetStatus("Apply failed: " + ex.Message + "\nAnalyze again before another batch. No scene was automatically saved.", MessageType.Error);
        }
        finally
        {
            busy = applying = false;
            EditorUtility.ClearProgressBar();
        }
    }

    private void Restore()
    {
        if (busy || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorUtility.DisplayDialog(Tr("Restore last LOD optimizer batch?"), Tr("Restore the last saved LOD optimizer batch, including its original LODGroup configuration and generated proxies/assets?\n\nThe backend checks for conflicting edits and refuses unsafe restoration. This does not restore unrelated scene work. No scene is automatically saved."), Tr("Restore last batch"), Tr("Cancel")))
        {
            SetStatus("Restore canceled. Nothing changed.", MessageType.Info);
            return;
        }
        DiscardPlan();
        busy = applying = true;
        try
        {
            string report = Backend.RestoreLastBatch();
            stale = analyzed;
            SetStatus(report + "\nAnalyze again before generating another batch.", MessageType.Info);
        }
        catch (OperationCanceledException)
        {
            stale = analyzed;
            SetStatus("Restore canceled. Analyze again before proceeding.", MessageType.Warning);
        }
        catch (Exception ex)
        {
            stale = analyzed;
            SetStatus("Restore refused or failed: " + ex.Message, MessageType.Error);
        }
        finally
        {
            busy = applying = false;
            EditorUtility.ClearProgressBar();
        }
    }

    private void DiscardPlan()
    {
        if (plan != null)
        {
            plan.Dispose();
            plan = null;
        }
        previewLabels = null;
        previewIndex = 0;
        ReleaseVisualPreview();
    }

    private void ReleaseVisualPreview()
    {
        if (meshPreview != null) { meshPreview.Cleanup(); meshPreview = null; }
        if (previewMaterial != null) { DestroyImmediate(previewMaterial); previewMaterial = null; }
    }

    private void SetStatus(string message, MessageType type)
    {
        status = message;
        statusType = type;
        RequestRepaint();
    }

    private static string Count(long count) { return count.ToString("N0"); }
    private static long Triangles(Mesh mesh)
    {
        if (mesh == null) return 0;
        long count = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
            if (mesh.GetTopology(i) == MeshTopology.Triangles) count += (long)mesh.GetIndexCount(i) / 3;
        return count;
    }

    private sealed class CostTable : TreeView
    {
        private readonly WorldLodOptimizerWindow owner;
        private GUIStyle wrapped;

        public CostTable(TreeViewState state, WorldLodOptimizerWindow owner) : base(state, CreateHeader())
        {
            this.owner = owner;
            rowHeight = 42f;
            showAlternatingRowBackgrounds = true;
            showBorder = true;
            multiColumnHeader.sortingChanged += header => Reload();
            Reload();
        }

        private static MultiColumnHeader CreateHeader()
        {
            string[] titles = { "Use", "Object", "Vertices", "Triangles", "LOD coverage", "Safety / recommendation", "Hierarchy path" };
            float[] widths = { 42f, 185f, 95f, 95f, 190f, 290f, 320f };
            var columns = new MultiColumnHeaderState.Column[titles.Length];
            for (int i = 0; i < columns.Length; i++)
                columns[i] = new MultiColumnHeaderState.Column { headerContent = new GUIContent(Tr(titles[i])), width = widths[i], minWidth = i == 0 ? 42f : 75f, autoResize = false, canSort = i != 0, allowToggleVisibility = i != 0 };
            var header = new MultiColumnHeader(new MultiColumnHeaderState(columns));
            header.SetSorting(3, false);
            return header;
        }
        public void RefreshLanguage()
        {
            var translated = CreateHeader().state.columns;
            var current = multiColumnHeader.state.columns;
            for (int i = 0; i < current.Length; i++) current[i].headerContent = translated[i].headerContent;
        }

        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem(0, -1, "Geometry");
            root.children = new List<TreeViewItem>();
            for (int i = 0; i < owner.rows.Count; i++) root.children.Add(new TreeViewItem(i + 1, 0, owner.rows[i].name ?? "Object"));
            int column = multiColumnHeader.sortedColumnIndex;
            if (column >= 0)
                root.children.Sort((a, b) =>
                {
                    int comparison = Compare(owner.rows[a.id - 1], owner.rows[b.id - 1], column);
                    if (comparison == 0) comparison = a.id.CompareTo(b.id);
                    return comparison * (multiColumnHeader.IsSortedAscending(column) ? 1 : -1);
                });
            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        private static int Compare(Backend.Row a, Backend.Row b, int column)
        {
            switch (column)
            {
                case 1: return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
                case 2: return a.vertices.CompareTo(b.vertices);
                case 3: return a.triangles.CompareTo(b.triangles);
                case 4: return (a.existingLevels == null ? 0 : a.existingLevels.Length).CompareTo(b.existingLevels == null ? 0 : b.existingLevels.Length);
                case 5:
                    int eligibility = a.editable.CompareTo(b.editable);
                    return eligibility == 0 ? a.recommended.CompareTo(b.recommended) : eligibility;
                case 6: return string.Compare(a.path, b.path, StringComparison.OrdinalIgnoreCase);
                default: return 0;
            }
        }

        protected override bool DoesItemMatchSearch(TreeViewItem item, string query)
        {
            var row = owner.rows[item.id - 1];
            return Contains(row.name, query) || Contains(row.path, query) || Contains(row.warning, query);
        }

        private static bool Contains(string text, string query)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            owner.inspectedRow = selectedIds.Count > 0 && selectedIds[0] > 0 && selectedIds[0] <= owner.rows.Count ? owner.rows[selectedIds[0] - 1] : null;
            owner.RequestRepaint();
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var row = owner.rows[args.item.id - 1];
            if (wrapped == null) wrapped = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            for (int i = 0; i < args.GetNumVisibleColumns(); i++)
            {
                Rect rect = args.GetCellRect(i);
                rect.x += 4f;
                rect.width -= 8f;
                rect.y += 3f;
                rect.height -= 6f;
                switch (args.GetColumn(i))
                {
                    case 0:
                        using (new EditorGUI.DisabledScope(!row.editable || owner.stale))
                        {
                            bool value = owner.checkedRows.Contains(row);
                            bool next = EditorGUI.Toggle(rect, value);
                            if (next != value)
                            {
                                if (next) owner.checkedRows.Add(row); else owner.checkedRows.Remove(row);
                                owner.DiscardPlan();
                            }
                        }
                        break;
                    case 1: GUI.Label(rect, new GUIContent(row.name, row.path), wrapped); break;
                    case 2: GUI.Label(rect, Count(row.vertices), wrapped); break;
                    case 3: GUI.Label(rect, Count(row.triangles), wrapped); break;
                    case 4:
                        string coverage = row.existingLevels == null || row.existingLevels.Length == 0 ? Tr("No existing LOD levels") : string.Join(" | ", Tr(row.existingLevels));
                        GUI.Label(rect, new GUIContent(coverage, coverage), wrapped);
                        break;
                    case 5:
                        string safety = Tr(row.editable ? (row.recommended ? "RECOMMENDED" : "Manual candidate") : "READ ONLY");
                        if (!string.IsNullOrEmpty(row.warning)) safety += " — " + Tr(row.warning);
                        GUI.Label(rect, new GUIContent(safety, safety), wrapped);
                        break;
                    case 6: GUI.Label(rect, new GUIContent(row.path, row.path), wrapped); break;
                }
            }
        }
    }
}

}
