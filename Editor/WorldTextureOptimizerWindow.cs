using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Backend = Prokjs.TextureOptimizer.WorldTextureOptimizerBackend;
using static Prokjs.TextureOptimizer.WorldOptimizerLocalization;

namespace Prokjs.TextureOptimizer
{
/// <summary>Original world-oriented UI, inspired by Thry's texture inventory workflow; no copied implementation or dependency.</summary>
public sealed class WorldTextureOptimizerWindow : EditorWindow
{
    [SerializeField] private Backend.Scope scope = Backend.Scope.LoadedScenes;
    [SerializeField] private Backend.Target target = Backend.Target.Default;
    [SerializeField] private bool includeInactive;
    [SerializeField] private DefaultAsset folder;
    [SerializeField] private int batchSize = 1024;
    [SerializeField] private TreeViewState treeState;
    [SerializeField] private int activeTab;
    private WorldLodOptimizerWindow lodController;
    private static readonly string[] TabLabels = { "Textures", "LOD & Analysis", "Occlusion & Shadows" };
    private TextureTable table;
    private Backend.ScanResult inventory;
    private SearchField search;
    private string status = "Choose a scope and target, then Scan. Opening this window never scans or changes assets.";
    private bool stale;
    private Vector2 reportScroll;
    [SerializeField] private bool showGettingStarted = true;
    [SerializeField] private bool showAbout;
    private const string RepositoryUrl = "https://github.com/prokjs/texture-optimizer";
    private static readonly string[] SizeLabels = Array.ConvertAll(Backend.Sizes, value => value.ToString());
    private static readonly string[] ScopeLabels = Enum.GetNames(typeof(Backend.Scope));
    private static readonly string[] TargetLabels = Enum.GetNames(typeof(Backend.Target));

    [MenuItem("Tools/prokjs/VRChat World Optimizer")]
    public static void Open()
    {
        var window = GetWindow<WorldTextureOptimizerWindow>(Tr("World Optimizer"));
        window.minSize = new Vector2(1100, 700);
        window.Show();
    }
    public static void OpenLODTab()
    {
        Open();
        var window = GetWindow<WorldTextureOptimizerWindow>();
        window.activeTab = 1;
        window.Repaint();
    }
    private void OnEnable()
    {
        titleContent = new GUIContent(Tr("World Optimizer"));
        minSize = new Vector2(1100, 700);
        if (treeState == null) treeState = new TreeViewState();
        search = new SearchField();
        table = new TextureTable(treeState, this);
        LanguageChanged += RefreshLanguage;
    }
    private void OnDisable()
    {
        LanguageChanged -= RefreshLanguage;
        if (lodController != null)
        {
            DestroyImmediate(lodController);
            lodController = null;
        }
    }
    private void OnSelectionChange()
    {
        if (lodController != null) lodController.NotifySelectionChanged();
        Repaint();
    }
    private void RefreshLanguage()
    {
        titleContent = new GUIContent(Tr("World Optimizer"));
        if (table != null) table.RefreshLanguage();
        Repaint();
    }
    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(Tr("prokjs's VRChat World Optimizer"), EditorStyles.boldLabel);
            GUILayout.Label(Tr("v1.2.0  |  Editor only"), EditorStyles.miniLabel, GUILayout.Width(165));
            if (GUILayout.Button(Tr("User guide"), GUILayout.MinWidth(90))) Application.OpenURL(RepositoryUrl + "/blob/v1.2.0/Documentation~/" + GuideFile);
            Language = EditorGUILayout.Popup(Language, LanguageNames, GUILayout.Width(100));
            if (GUILayout.Button(Tr("Report issue"), GUILayout.MinWidth(90))) Application.OpenURL(RepositoryUrl + "/issues");
        }
        EditorGUILayout.HelpBox(Tr("Made for VRChat projects using Unity 2022.3.22f1. Other Unity versions have not been tested and may be incompatible."), MessageType.Warning);
        showGettingStarted = EditorGUILayout.Foldout(showGettingStarted, Tr("Getting started"), true);
        if (showGettingStarted)
            EditorGUILayout.HelpBox(Tr("Back up or commit your project first. Textures: Scan, propose lower caps, then review Preview Apply checked. LOD & Analysis: Analyze, inspect safe candidates, generate a read-only mesh preview, then explicitly Apply. Occlusion & Shadows: inspect selected geometry before changing scene flags. Bakes are separate, deliberate scene-wide operations. Opening this tool never scans, saves or starts a bake."), MessageType.Info);
        showAbout = EditorGUILayout.Foldout(showAbout, Tr("About, attribution & safety"), true);
        if (showAbout)
        {
            EditorGUILayout.HelpBox(Tr("Developed by prokjs. Texture inventory independently implemented, inspired by Thry's VRAM Checker in Avatar Performance Tools. Not a reverse-engineered copy; not affiliated with or endorsed by Thry or VRChat. MIT licensed, provided as-is without warranty. Static estimates are not measured profiling or FPS predictions. Keep project backups; local recovery journals are not a substitute."), MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(Tr("GitHub / license"))) Application.OpenURL(RepositoryUrl);
                if (GUILayout.Button(Tr("Thry's original VRAM Checker"))) Application.OpenURL("https://github.com/Thryrallo/VRC-Avatar-Performance-Tools");
                if (GUILayout.Button(Tr("Open texture backup folder")))
                {
                    System.IO.Directory.CreateDirectory(Backend.BackupDirectory);
                    EditorUtility.RevealInFinder(System.IO.Path.GetFullPath(Backend.BackupDirectory));
                }
            }
        }
        activeTab = GUILayout.Toolbar(Mathf.Clamp(activeTab, 0, TabLabels.Length - 1), Tr(TabLabels), GUILayout.Height(26));
        EditorGUILayout.Space(4);
        if (activeTab != 0)
        {
            if (lodController == null)
            {
                lodController = CreateInstance<WorldLodOptimizerWindow>();
                lodController.hideFlags = HideFlags.HideAndDontSave;
                lodController.SetHost(this);
            }
            lodController.DrawEmbedded(activeTab == 2);
            return;
        }
        EditorGUILayout.LabelField(Tr("Texture Resolution"), EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(Tr("Shared-asset edit: a cap affects every scene, prefab and material using that texture. Imported sources are untouched. Native RGBA32/DXT1/DXT5 .asset pixels are resampled in place under Default only, preserving their format with an exact original-byte backup. Only confirmed Apply/Restore writes assets or import settings. No FPS claim."), MessageType.Warning);
        EditorGUI.BeginChangeCheck();
        using (new EditorGUILayout.HorizontalScope())
        {
            scope = (Backend.Scope)EditorGUILayout.Popup(Tr("Scope"), (int)scope, Tr(ScopeLabels));
            target = (Backend.Target)EditorGUILayout.Popup(Tr("Edit cap for"), (int)target, Tr(TargetLabels));
            includeInactive = EditorGUILayout.ToggleLeft(Tr("Include inactive objects"), includeInactive, GUILayout.Width(185));
        }
        if (scope == Backend.Scope.Folder) folder = (DefaultAsset)EditorGUILayout.ObjectField(Tr("Assets folder"), folder, typeof(DefaultAsset), false);
        if (EditorGUI.EndChangeCheck()) stale = inventory != null;
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(Tr("Scan / Refresh"), GUILayout.Width(130))) Scan();
            GUILayout.Label(Tr("Loaded scenes: renderer/skybox materials, terrain layers, UI and light cookies. Project/folder: imported and native texture assets."), EditorStyles.miniLabel);
        }
        EditorGUILayout.LabelField(Tr(inventory == null ? "No scan snapshot." : inventory.scopeDescription), EditorStyles.miniLabel);
        if (stale) EditorGUILayout.HelpBox(Tr("Scope/target changed or a batch completed. Scan again before previewing another batch. Table remains the last explicit snapshot."), MessageType.Info);
        EditorGUILayout.HelpBox(Tr("All memory numbers are full GPU allocation ESTIMATES, not measured residency. Current uses the loaded editor format/dimensions. Target/proposed use that SAME format as a proxy; Android formats/device fallback, streaming, atlas packing and importer rounding can differ. D/S/A = Default/Standalone/Android; * = explicit override (takes precedence)."), MessageType.Info);
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            table.searchString = search.OnToolbarGUI(table.searchString);
            if (GUILayout.Button(Tr("Select visible editable"), EditorStyles.toolbarButton, GUILayout.Width(155))) table.SetVisibleSelection(true);
            if (GUILayout.Button(Tr("Clear selection"), EditorStyles.toolbarButton, GUILayout.Width(100))) table.ClearChecks();
            GUILayout.Space(10);
            batchSize = EditorGUILayout.IntPopup(batchSize, SizeLabels, Backend.Sizes, GUILayout.Width(75));
            if (GUILayout.Button(Tr("Propose for checked"), EditorStyles.toolbarButton, GUILayout.Width(135))) table.BatchPropose(batchSize);
        }
        Rect tableRect = GUILayoutUtility.GetRect(0, 100000, 160, 100000, GUILayout.ExpandHeight(true));
        table.OnGUI(tableRect);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(Tr(table.Summary), EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(stale || inventory == null || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button(Tr("Preview Apply checked..."), GUILayout.Width(180))) PreviewApply();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button(Tr("Restore Last Batch..."), GUILayout.Width(155))) PreviewRestore();
        }
        using (var scroll = new EditorGUILayout.ScrollViewScope(reportScroll, GUILayout.Height(78)))
        {
            reportScroll = scroll.scrollPosition;
            EditorGUILayout.SelectableLabel(Tr(status), EditorStyles.wordWrappedLabel, GUILayout.ExpandHeight(true));
        }
    }
    private void Scan()
    {
        try
        {
            inventory = Backend.Scan(new Backend.ScanOptions { scope = scope, target = target, includeInactive = includeInactive, folder = scope == Backend.Scope.Folder ? (folder == null ? "" : AssetDatabase.GetAssetPath(folder)) : "Assets", roots = scope == Backend.Scope.SelectedObjects ? Selection.gameObjects : null }, true);
            table.SetInventory(inventory.rows);
            stale = false;
            status = (inventory.canceled ? "PARTIAL/CANCELED inventory: " : "Inventory: ") + inventory.rows.Count + " deduplicated textures. " + inventory.scopeDescription + "\n" + string.Join("\n", inventory.errors.ToArray());
        }
        catch (Exception ex) { status = "Scan failed: " + ex.Message; }
    }
    private void PreviewApply()
    {
        try
        {
            var changes = table.Changes();
            var plan = Backend.CreatePlan(target, changes);
            if (plan.entries.Count == 0) { status = "No changed caps selected. No import or backup created.\n" + string.Join("\n", plan.notices.ToArray()); return; }
            WorldTextureOptimizerPreview.ShowApply(plan, BatchFinished);
        }
        catch (Exception ex) { status = "Preview refused: " + ex.Message; }
    }
    private void PreviewRestore()
    {
        try
        {
            var batch = Backend.ReadLastBatch();
            if (batch == null) { status = "No saved optimizer batch exists."; return; }
            WorldTextureOptimizerPreview.ShowRestore(batch, BatchFinished);
        }
        catch (Exception ex) { status = "Restore preview failed: " + ex.Message; }
    }
    private void BatchFinished(string report)
    {
        status = report;
        stale = inventory != null;
        Repaint();
    }

    private sealed class TextureTable : TreeView
    {
        private readonly WorldTextureOptimizerWindow owner;
        private List<Backend.Row> rows = new List<Backend.Row>();
        private readonly Dictionary<int, long> estimates = new Dictionary<int, long>();
        private readonly Dictionary<int, int[]> allowedSizes = new Dictionary<int, int[]>();
        private readonly Dictionary<int, string[]> allowedLabels = new Dictionary<int, string[]>();
        private GUIStyle twoLines;
        private int checkedCount, changedCount;
        private long estimatedSaving;
        public string Summary { get { return rows.Count + " textures | " + checkedCount + " checked | " + changedCount + " changed caps | estimated target saving " + Bytes(estimatedSaving); } }
        public TextureTable(TreeViewState state, WorldTextureOptimizerWindow owner) : base(state, Header())
        {
            this.owner = owner;
            rowHeight = 46;
            showAlternatingRowBackgrounds = true;
            showBorder = true;
            multiColumnHeader.sortingChanged += header => Reload();
            Reload();
        }
        private static MultiColumnHeader Header()
        {
            string[] names = { "Apply", "Texture / source dimensions", "Asset path", "Loaded now", "Caps: D / S / A", "Chosen target now", "Current GPU est.", "Proposed cap", "Proposed GPU est.", "Savings est.", "Safety / precedence" };
            float[] widths = { 48, 235, 280, 125, 280, 145, 125, 110, 145, 120, 330 };
            var columns = new MultiColumnHeaderState.Column[names.Length];
            for (int i = 0; i < columns.Length; i++) columns[i] = new MultiColumnHeaderState.Column { headerContent = new GUIContent(Tr(names[i])), width = widths[i], minWidth = i == 0 ? 48 : 90, autoResize = false, canSort = i != 0, allowToggleVisibility = i != 0 };
            var header = new MultiColumnHeader(new MultiColumnHeaderState(columns));
            header.SetSorting(6, false);
            return header;
        }
        public void RefreshLanguage()
        {
            var translated = Header().state.columns;
            var current = multiColumnHeader.state.columns;
            for (int i = 0; i < current.Length; i++) current[i].headerContent = translated[i].headerContent;
        }
        public void SetInventory(List<Backend.Row> inventory)
        {
            rows = inventory;
            estimates.Clear(); allowedSizes.Clear(); allowedLabels.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var options = new List<int>();
                foreach (int size in Backend.Sizes) if (size <= row.effectiveCap && (!row.nativeAsset || size <= Backend.LargestAllowedCap(row.sourceWidth, row.sourceHeight))) options.Add(size);
                allowedSizes[i] = options.ToArray();
                allowedLabels[i] = options.ConvertAll(size => size.ToString()).ToArray();
                estimates[i] = Backend.ProposedBytes(row, row.proposedMaxSize);
            }
            UpdateSummary(); Reload();
        }
        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem(0, -1, "Textures");
            var children = new List<TreeViewItem>();
            for (int i = 0; i < rows.Count; i++) children.Add(new TreeViewItem(i + 1, 0, rows[i].name ?? "Texture"));
            int column = multiColumnHeader.sortedColumnIndex;
            if (column >= 0) children.Sort((a, b) => Compare(rows[a.id - 1], rows[b.id - 1], a.id - 1, b.id - 1, column) * (multiColumnHeader.IsSortedAscending(column) ? 1 : -1));
            root.children = children;
            SetupDepthsFromParentsAndChildren(root);
            return root;
        }
        private int Compare(Backend.Row a, Backend.Row b, int ai, int bi, int column)
        {
            switch (column)
            {
                case 1: return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
                case 2: return string.Compare(a.assetPath, b.assetPath, StringComparison.OrdinalIgnoreCase);
                case 3: return ((long)a.width * a.height).CompareTo((long)b.width * b.height);
                case 4: return a.defaultCap.CompareTo(b.defaultCap);
                case 5: return a.effectiveCap.CompareTo(b.effectiveCap);
                case 6: return a.currentBytes.CompareTo(b.currentBytes);
                case 7: return a.proposedMaxSize.CompareTo(b.proposedMaxSize);
                case 8: return Estimate(ai).CompareTo(Estimate(bi));
                case 9: return Saving(ai).CompareTo(Saving(bi));
                case 10: return string.Compare(a.lockedReason ?? a.warning, b.lockedReason ?? b.warning, StringComparison.OrdinalIgnoreCase);
                default: return 0;
            }
        }
        protected override bool DoesItemMatchSearch(TreeViewItem item, string query)
        {
            var row = rows[item.id - 1];
            return (row.name ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || (row.assetPath ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private long Estimate(int index) { long value; return estimates.TryGetValue(index, out value) ? value : -1; }
        private long Saving(int index) { long proposed = Estimate(index); return proposed < 0 || rows[index].targetBytes < 0 ? 0 : Math.Max(0, rows[index].targetBytes - proposed); }
        protected override void RowGUI(RowGUIArgs args)
        {
            int index = args.item.id - 1;
            var row = rows[index];
            if (twoLines == null) twoLines = new GUIStyle(EditorStyles.label) { wordWrap = true, fontSize = 11 };
            for (int c = 0; c < args.GetNumVisibleColumns(); c++)
            {
                int column = args.GetColumn(c);
                var rect = args.GetCellRect(c); rect.x += 4; rect.width -= 8; rect.y += 3; rect.height -= 6;
                switch (column)
                {
                    case 0:
                        using (new EditorGUI.DisabledScope(!row.Editable))
                        {
                            bool selected = EditorGUI.Toggle(rect, row.selected);
                            if (selected != row.selected) { row.selected = selected; UpdateSummary(); }
                        }
                        break;
                    case 1:
                        var iconRect = new Rect(rect.x, rect.y, 36, 36);
                        var thumbnail = AssetPreview.GetMiniThumbnail(row.texture);
                        if (thumbnail != null) GUI.DrawTexture(iconRect, thumbnail, ScaleMode.ScaleToFit);
                        if (GUI.Button(iconRect, new GUIContent("", Tr("Ping texture in Project")), GUIStyle.none)) EditorGUIUtility.PingObject(row.texture);
                        var nameRect = new Rect(rect.x + 42, rect.y, rect.width - 42, rect.height);
                        GUI.Label(nameRect, new GUIContent(row.name + "\n" + Tr("Source {0} x {1}", row.sourceWidth, row.sourceHeight), row.loadedFormat), twoLines);
                        break;
                    case 2: GUI.Label(rect, new GUIContent(row.assetPath, row.assetPath), twoLines); break;
                    case 3: GUI.Label(rect, new GUIContent(row.width + " x " + row.height + "\n" + row.loadedFormat, Tr("Actual loaded editor dimensions and graphics format, not the chosen target's imported output.")), twoLines); break;
                    case 4: GUI.Label(rect, new GUIContent(Tr(row.caps), Tr(row.caps)), twoLines); break;
                    case 5: GUI.Label(rect, new GUIContent(Tr("{0} x {1} est.\nEffective cap {2}", row.targetWidth, row.targetHeight, row.effectiveCap), Tr("Chosen target's effective cap. Default is masked on platforms with an explicit override.")), twoLines); break;
                    case 6: GUI.Label(rect, Bytes(row.currentBytes), twoLines); break;
                    case 7:
                        using (new EditorGUI.DisabledScope(!row.Editable || allowedSizes[index].Length == 0))
                        {
                            int proposed = EditorGUI.IntPopup(new Rect(rect.x, rect.y + 8, rect.width, 20), row.proposedMaxSize, allowedLabels[index], allowedSizes[index]);
                            if (proposed != row.proposedMaxSize) { row.proposedMaxSize = proposed; estimates[index] = Backend.ProposedBytes(row, proposed); UpdateSummary(); }
                        }
                        break;
                    case 8: GUI.Label(rect, Bytes(Estimate(index)) + "\n" + Tr("Current-format proxy"), twoLines); break;
                    case 9: GUI.Label(rect, Bytes(Saving(index)), twoLines); break;
                    case 10: GUI.Label(rect, new GUIContent(Tr(row.Editable ? (string.IsNullOrEmpty(row.warning) ? "Editable; explicit confirmation required" : row.warning) : "LOCKED: " + row.lockedReason), Tr(row.lockedReason ?? row.warning)), twoLines); break;
                }
            }
        }
        protected override void DoubleClickedItem(int id) { Selection.activeObject = rows[id - 1].texture; EditorGUIUtility.PingObject(rows[id - 1].texture); }
        protected override void ContextClickedItem(int id)
        {
            var row = rows[id - 1];
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(Tr("Ping asset")), false, () => EditorGUIUtility.PingObject(row.texture));
            menu.AddItem(new GUIContent(Tr("Select in Inspector")), false, () => Selection.activeObject = row.texture);
            menu.AddItem(new GUIContent(Tr("Copy asset path")), false, () => EditorGUIUtility.systemCopyBuffer = row.assetPath);
            menu.ShowAsContext();
        }
        public void SetVisibleSelection(bool selected)
        {
            foreach (var item in GetRows()) if (rows[item.id - 1].Editable) rows[item.id - 1].selected = selected;
            UpdateSummary(); owner.Repaint();
        }
        public void ClearChecks() { foreach (var row in rows) row.selected = false; UpdateSummary(); }
        public void BatchPropose(int size)
        {
            int changed = 0, skipped = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (!row.selected || !row.Editable) continue;
                if (Array.IndexOf(allowedSizes[i], size) < 0) { skipped++; continue; }
                row.proposedMaxSize = size; estimates[i] = Backend.ProposedBytes(row, size); changed++;
            }
            owner.status = "Proposed " + size + " for " + changed + " checked rows. Skipped " + skipped + " rows where it would exceed the source/effective cap. NO importer changed. Review rows and Preview Apply.";
            UpdateSummary(); Reload();
        }
        private void UpdateSummary()
        {
            checkedCount = 0; changedCount = 0; estimatedSaving = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (!row.selected || !row.Editable) continue;
                checkedCount++;
                if (row.proposedMaxSize == row.effectiveCap) continue;
                changedCount++; estimatedSaving += Saving(i);
            }
        }
        public List<Backend.Change> Changes()
        {
            var changes = new List<Backend.Change>();
            foreach (var row in rows) if (row.selected && row.Editable) changes.Add(new Backend.Change(row.assetPath, row.proposedMaxSize, row.importerHash));
            return changes;
        }
    }
    public static string Bytes(long bytes)
    {
        if (bytes < 0) return Tr("Unknown");
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024d).ToString("F1") + " KiB";
        return (bytes / (1024d * 1024d)).ToString("F2") + " MiB";
    }
}

/// <summary>Scrollable complete named preview; no writes until the final deliberate button.</summary>
public sealed class WorldTextureOptimizerPreview : EditorWindow
{
    private Backend.Batch batch;
    private bool restore, used;
    private Action<string> finished;
    private Vector2 scroll;
    public static void ShowApply(Backend.Batch plan, Action<string> callback) { Show(plan, false, callback); }
    public static void ShowRestore(Backend.Batch plan, Action<string> callback) { Show(plan, true, callback); }
    private static void Show(Backend.Batch plan, bool restore, Action<string> callback)
    {
        var window = CreateInstance<WorldTextureOptimizerPreview>();
        window.batch = plan; window.restore = restore; window.finished = callback;
        window.titleContent = new GUIContent(Tr(restore ? "Restore texture batch" : "Confirm texture caps"));
        window.minSize = new Vector2(780, 430); window.position = new Rect(120, 100, 900, 600);
        window.ShowUtility();
    }
    private void OnEnable() { LanguageChanged += RefreshLanguage; }
    private void OnDisable() { LanguageChanged -= RefreshLanguage; }
    private void RefreshLanguage()
    {
        titleContent = new GUIContent(Tr(restore ? "Restore texture batch" : "Confirm texture caps"));
        Repaint();
    }
    private void OnGUI()
    {
        if (batch == null) { EditorGUILayout.HelpBox(Tr("Preview lost after domain reload. Close and rebuild the preview; no implicit apply occurs."), MessageType.Info); return; }
        EditorGUILayout.LabelField(Tr((restore ? "Restore" : "Apply") + " — " + batch.target + " — " + batch.entries.Count + " named textures"), EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(Tr(restore ? "Restore only unchanged owned asset/importer states. Source, native pixel, metadata or importer edits since Apply are conflicts and are NEVER overwritten. Native restore recovers the exact original .asset bytes. Partial/canceled entries remain in the durable backup." : "This changes SHARED texture assets across all scenes/prefabs. Native RGBA32/DXT1/DXT5 .asset pixels are resampled under Default only, preserving their format after an exact original-byte backup. Imported source files are untouched; non-default importer targets may enable overrides, and Default does not override explicit platform settings. This is not a performance guarantee."), MessageType.Warning);
        EditorGUILayout.SelectableLabel(Tr("Backup: " + batch.backupPath), GUILayout.Height(20));
        using (var area = new EditorGUILayout.ScrollViewScope(scroll))
        {
            scroll = area.scrollPosition;
            foreach (var entry in batch.entries)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.SelectableLabel(entry.assetPath, GUILayout.Height(18));
                    EditorGUILayout.LabelField(Tr(entry.nativeAsset
                        ? (restore ? entry.state + " | Native: restore exact original .asset bytes" : "Native Default: resample longest edge " + entry.beforeCap + " → " + entry.targetCap + "; original bytes backed up")
                        : (restore ? (entry.state + " | " + batch.target + " cap " + entry.targetCap + " → previous cap " + entry.beforeCap + (batch.target == Backend.Target.Default ? "" : " (restore original override state)")) : (batch.target + ": cap " + entry.targetCap + " | previous stored cap " + entry.beforeCap))));
                    if (!string.IsNullOrEmpty(entry.message)) EditorGUILayout.LabelField(Tr(entry.message), EditorStyles.wordWrappedMiniLabel);
                }
            }
            foreach (string notice in batch.notices) EditorGUILayout.HelpBox(Tr(notice), MessageType.Info);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(Tr("Cancel — no changes"))) Close();
            using (new EditorGUI.DisabledScope(used || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating))
            {
                if (GUILayout.Button(Tr(restore ? "Restore safe matching entries" : "Apply these named resolution caps")))
                {
                    used = true;
                    string report;
                    try { report = (restore ? Backend.RestoreBatch(batch.backupPath, true) : Backend.ApplyPlan(batch, true)).ToString(); }
                    catch (Exception ex) { report = "Batch operation failed: " + ex.Message + "\nRetain/review backup: " + batch.backupPath; }
                    if (finished != null) finished(report);
                    Debug.Log("[World Texture Optimizer] " + report);
                    Close();
                }
            }
        }
    }
}

}
