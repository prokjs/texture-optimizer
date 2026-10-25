# prokjs's VRChat World Optimizer v1.2.0 user guide

**Languages:** English · [한국어](user-guide.ko.md) · [日本語](user-guide.ja.md)

> **Made for VRChat projects using Unity 2022.3.22f1. Other Unity versions have not been tested and may be incompatible.**

VRChat projects are the intended audience. The package itself has no VRChat SDK dependency.

An Editor-only unified window for textures, LOD analysis/generation, and occlusion/floor-shadow preparation. Opening the window does not scan, apply, or bake automatically. The package identity remains `com.prokjs.texture-optimizer`.

For installation, requirements, and version-pinned package URLs, see the [README](../README.md#install--choose-one-method). Install only one copy using one installation method. Remove older standalone optimizer scripts before installing.

## Interface language

Use the **English / 日本語 / 한국어** dropdown beside **User guide** at the top of the window. English is the default; the choice is stored in EditorPrefs and survives closing the window or restarting Unity. Changing language immediately refreshes both controllers, including table headers, safety messages, dialogs and open previews. It does not rescan, clear checkboxes, discard the current LOD plan, or change proposed caps or configuration. **User guide** opens the matching translated guide. Names, asset/hierarchy paths and source evidence are preserved verbatim; unknown Unity, OS or third-party exception text keeps its original English wording. Instructions below use English labels so they can be matched by selecting **English**.

## Unified workflow: LOD & Analysis

1. Save and back up scenes/assets before working in Edit Mode. Open **LOD & Analysis** and scan. Recommendations are static estimates, not measured draw calls, GPU timing, residency, or proof of FPS improvement.
2. Review candidates and exclusions individually. Recommended automatic generation is for fixed opaque MeshRenderers using supported built-in shaders. Pickups, Udon-controlled objects, skinned meshes, material/property overrides, existing unsafe hierarchies, and baked-lightmapped substitutions are excluded. Do not bypass a lock to force automation.
3. For an existing LODGroup, inspect its levels and transitions using the Inspector preview; this is not regeneration of its meshes.
4. For an eligible candidate, choose reduction percentages and review the explicit named preview before applying automatic LODs. Percentages mean **vertices removed**, not triangles remaining. Topology preservation can prevent meeting a target exactly.
5. Apply only the reviewed proposal. LOD0 preserves the original mesh; reduced meshes are cached as new `.asset` files under `Assets/prokjs/WorldOptimizerGenerated`. The original mesh/UV data, material assets and collider are not rewritten. Generated levels still need visual checks for silhouettes, UV shading and transitions; no automatic quality guarantee is made.
6. Use Undo or the relevant batch restore to revert scene changes. Generated meshes are intentionally **retained after restore**, protecting Undo and saved references. Restore is not an asset cleanup operation. Scene batch records use `UserSettings/ProkjsWorldOptimizerBatches`; save your own scene/version-control backups too.

## Unified workflow: Occlusion & Shadows

Scan, review suitable fixed opaque candidates, preview the named changes, and explicitly apply static-flag preparation. Setting occluder/occludee flags is **not a generic runtime culler** and does not immediately hide objects. Use Unity's separate occlusion bake and inspect the result. Existing tarp runtime controllers are not modified.

Floor-shadow preparation requires suitable **existing UV2 and GI settings**. It does not unwrap UV2, convert realtime lights, or create a private per-floor lighting bake. Lighting bake is a separate, explicitly confirmed **scene-wide** Unity operation, potentially long-running. Review scene lighting settings and all affected content first; use the cancel control to request cancellation when needed. Neither scanning nor preparing flags starts a bake.

Undo/batch restore restores recorded scene flags, **not baked lighting data**. Back up scenes and lighting assets before baking; a later bake can replace shared scene lighting output. Inspect baked results and use your own backups to recover lighting data.

## Native Texture2D `.asset` exception

In **Textures**, a standalone editable main Texture2D that is the sole object in a `.asset` file may be reduced in **Default only** when its format is RGBA32, DXT1 or DXT5. This is **pixel resampling in place**, shared by all platforms, not a TextureImporter cap. Imported PNG/JPG and other source images retain their original pixels and use the cap workflow below.

Native reduction preserves GUID/local ID, format, color space, sampler/streaming settings and whether mipmaps exist; existing mip chains are regenerated. The original `.asset` bytes are saved before writing. Dirty, embedded, subasset, package, noneditable, missing-metadata, unsupported-format and generated lighting/reflection assets remain locked. The minimum offered cap is 32.

Native restore requires matching GUID/path, metadata and applied-content hashes plus the saved byte backup. Moved, replaced, later-edited or uncertain interrupted saves are refused rather than overwritten. Preserve the backup and use source control for conflicts. Uninstalling restores neither native pixels nor importer caps.

The remaining sections document the **Textures** workflow, with the native exception above taking precedence over importer-only descriptions.

## Before you start

1. Save your work and commit or back up the project, including texture source files and `.meta` files.
2. If your version-control system uses checkout or locks, make the texture and its `.meta` file editable first.
3. Work outside Play Mode. Applying and restoring importer changes requires Edit Mode.
4. Decide what quality you are willing to trade for a smaller imported texture. Start with a few unimportant textures, not a whole project.

**Shared texture means shared consequences:** selecting one object does not create a private copy of its textures. Every other scene, prefab, and material using an edited texture sees the new imported cap too.

Texture changes do not have a Unity Undo recovery guarantee. Use texture batch backups and source control. LOD/static-flag scene changes have separate Undo and batch restore.

## 1. Choose what to scan

Open **Tools → prokjs → VRChat World Optimizer**, choose **Textures**, then choose a **Scope**:

| Scope | What it inventories |
| --- | --- |
| **LoadedScenes** | Supported references on objects in currently loaded scenes, plus the scene skybox material. It does not open other scenes. |
| **SelectedObjects** | The selected scene objects or prefab assets and their descendants. Select at least one object before scanning. |
| **Folder** | Texture assets in the chosen folder under `Assets`, including its descendants. |
| **ProjectAssets** | Texture assets under `Assets`. This deliberately excludes `Packages`. |

Scene/selection traversal reads renderer materials, terrain material/layer textures, Unity UI graphic textures/materials, and light cookies. Referenced material texture properties are inspected. **Include inactive objects** controls whether inactive objects contribute to object-based scans.

This is not a complete build-dependency analysis. It does not claim to discover materials or textures referenced only by avatar animation clips, arbitrary scripts, addressable loading, or every custom component. Folder/project scans show asset inventory, not proof that an asset is used or unused.

Choose **Edit cap for**:

- **Default:** changes the default maximum texture size. Explicit platform overrides take precedence, so this may not change Standalone or Android output.
- **Standalone:** changes the Standalone cap. If there is no active override, applying a changed cap may enable one using inherited Default settings; read the preview notice.
- **Android:** the same cap/override workflow for Android. This does not measure an Android device or automatically select an Android compression format.

Click **Scan / Refresh**. This reads a snapshot; it does not modify import settings. A canceled scan can leave a partial inventory, which is identified in the report. Rescan if you need a complete inventory.

Changing scope, target, or inactive-object options makes the existing snapshot stale. Click **Scan / Refresh** again before building another apply preview. Rescan after external asset/importer edits too.

## 2. Read the table

The table deduplicates textures by asset path so multiple references do not create repeated importer operations. You can filter it with the search field, sort columns, and inspect asset paths before selecting anything. Click a thumbnail to ping its texture in the Project window; the row context menu can select the asset in the Inspector or copy its path.

| Column | Meaning |
| --- | --- |
| **Apply** | Whether the row is checked for a proposed batch. Checking alone writes nothing. |
| **Texture / source dimensions** | Original source dimensions used to restrict allowed caps. |
| **Asset path** | The shared texture asset that would be edited. |
| **Loaded now** | Current Editor-loaded dimensions and graphics format, not necessarily the chosen platform's output. |
| **Caps: D / S / A** | Default, Standalone, and Android settings. `*` marks an explicit platform override. |
| **Chosen target now** | Effective cap for the chosen target and estimated dimensions. |
| **Current GPU est.** | Full-allocation estimate for the loaded texture. It is not measured residency. |
| **Proposed cap** | Requested resolution cap; it is not applied yet. |
| **Proposed GPU est. / Savings est.** | Estimates using the current loaded format as a proxy for the proposal. |
| **Safety / precedence** | Locks, platform masking, and override notices. Read these before applying. |

### How to interpret memory estimates

The tool estimates a full GPU allocation, not a profiler's resident-memory reading. Source file size on disk is a different quantity. A smaller imported texture can also look worse; an estimate cannot judge acceptable quality for you.

The loaded Editor graphics format is reused for target and proposed estimates. Actual output can differ because of platform-specific compression, device format fallback, streaming, sprite-atlas packing, and importer rounding. **An Android proposal is a current-format proxy, not measured Android memory.** A platform cap is not a guarantee of exact imported dimensions.

There is no FPS promise. Profile your actual build and inspect it on your target hardware before attributing any frame-rate improvement to these changes.

### Why a texture may be locked

Locked rows are intentionally excluded from importer-cap changes. Common reasons include:

- Sprite/atlas input: changing its importer can affect packing, slicing, and UI layout.
- Generated lighting, reflection, lightmap, or similar assets: use the workflow that generates them.
- Package, built-in, or transient texture: not a normal editable asset under `Assets`.
- Embedded/sub-assets and unsupported native/generated assets. The supported standalone native Texture2D exception is described above.
- Render texture, array, cubemap, or other unsupported non-2D importer shape.
- Read-only asset/metadata, missing source dimensions, or dimensions below the offered minimum cap.
- An inherited explicit format unsupported by the selected platform. Choose a supported format deliberately in Unity's Inspector instead of relying on this cap tool to fix it.

Do not move package/generated assets or bypass a lock merely to make the tool edit them. Their owning system may regenerate them or require different settings.

## 3. Check and propose

1. Check the editable textures you intend to change. **Select visible editable** checks editable rows matching the current table filter; it is a convenience, not a recommendation that all visible textures should be reduced. **Clear selection** clears checks.
2. Change **Proposed cap** on individual rows, or select a batch size and click **Propose for checked**.
3. Review the status report for skipped rows and inspect each proposal.

Offered caps range from 32 to 16384 pixels, constrained by the source dimensions and effective cap. The optimizer does not increase an effective cap or upscale source pixels. A batch proposal is skipped for a row if the requested cap exceeds its allowed range. Choosing a cap equal to the effective cap does not create an unnecessary override or importer write.

Proposing does not modify assets. Check marks and lower estimate numbers are not confirmation to apply.

## 4. Preview, then apply

Click **Preview Apply checked...**. If nothing would change, the tool reports that without creating a backup or importing assets.

The preview lists the complete named set of texture assets, the selected target, old/new stored caps, platform-override notices, and backup path. Read it carefully, especially when editing Default with existing overrides or enabling a new platform override.

- **Cancel — no changes** closes the preview without applying.
- **Apply these named resolution caps** is the deliberate write step.

Before writing importer changes, the tool writes a durable batch record to:

```text
<your-project>/UserSettings/ProkjsTextureOptimizerBackups/
```

It rechecks asset/importer state before each change. Changes made since the scan or preview can invalidate a plan or produce conflicts rather than being overwritten. Unity reimports each affected texture; this can take time.

A batch is not an all-or-nothing transaction. Cancellation, an error, or an interruption can leave some entries applied and others untouched. The per-entry record and result report identify progress; do not assume that canceling a running batch reverts earlier entries. Keep its backup.

After applying, read the report, rescan, and inspect quality across all consumers. Imported image source pixels are untouched; importer metadata changes affect every consumer. Supported native `.asset` textures instead have their pixels resized in place.

## 5. Restore the latest batch

1. Keep the optimizer installed and keep its backup directory.
2. Click **Restore Last Batch...**.
3. Inspect the named entries and backup location.
4. Click **Restore safe matching entries** to restore entries that still safely match the optimizer-owned state.
5. Read the report, rescan, and review your project and source-control diff.

Imported textures restore recorded resolution/override settings for safe matching entries, not source images or unrelated edits. An importer already matching its original state is treated as restored. Native textures restore the exact saved original `.asset` bytes only when all identity/content guards match.

### Conflicts are protection, not permission to overwrite

A changed source image, later importer edit, missing/locked asset, or uncertain interrupted import can prevent restoration. Conflicting entries are skipped and retained in the backup for manual review. A later compression or platform-setting change can be a conflict even if it was unrelated to maximum size.

Keep the report and backup. Compare the asset's current Inspector settings and source-control history with what you intended; revert the relevant `.meta` change using your own version-control workflow or deliberately edit the settings in the Inspector. Do not edit hashes or remove safeguards in the backup to force a restore.

### Backup limitations

- Backups are **local, per project**, under `UserSettings/ProkjsTextureOptimizerBackups/`. Imported textures store importer restoration data, not source-image copies. Supported native textures also store original `.asset` bytes; retain those files.
- Your version-control or backup configuration may exclude `UserSettings`; do not assume teammates or another computer have these records.
- The window offers **only the latest saved batch**, not a history picker. Restoring it does not expose a previous batch as the next automatic undo step. Keep source-control history for multi-batch recovery.
- Records are tied to the original project location. Moving/cloning a project does not make its records portable recovery files; use source control or your project backup for that case.
- A domain reload can invalidate an open preview. Rebuild the preview rather than assuming anything was applied.

## Updating, migrating, and uninstalling

Use one installation method. To switch methods, remove the existing package/scripts using their original installation workflow before adding the other copy. Do not delete the texture assets or their metadata while removing the tool.

- **UPM:** remove `com.prokjs.texture-optimizer` in Unity Package Manager.
- **VCC:** remove the package through VCC's project package management.
- **`.unitypackage`:** remove the imported `Assets/prokjs/TextureOptimizer` tool folder.

**Uninstalling does not restore texture caps.** Restore the desired batch or revert importer metadata in source control first. Local backup records are separate from the installed tool and are not automatically removed when the package is removed. Keep them until you no longer need them.

When removing an old standalone version, locate the old optimizer scripts, not arbitrary texture assets. Duplicate backend/window/preview classes can cause compilation errors even if only one copy is inside a package.

## Troubleshooting

| Symptom | What to do |
| --- | --- |
| Duplicate type or duplicate menu errors | Check for a `.unitypackage`/UPM/VCC double install and older standalone script copies. Keep one installation only. |
| UI/`UnityEngine.UI` reference error | Ensure Unity UI (`com.unity.ugui`) is installed, especially with `.unitypackage` imports. |
| No expected texture in a scene scan | Check loaded scenes, selection, inactive-object settings, and supported reference coverage. Try Folder/ProjectAssets inventory; absence from a scene scan does not prove the texture is unused. |
| Default reduction does not affect a platform | Check the `*` override marker. An explicit platform override masks Default. Select the intended platform target and review its preview. |
| Proposal skipped or cannot select a larger cap | The tool only offers supported reductions within source/effective limits. Use Unity's Inspector for deliberate increases. |
| Preview refuses an importer changed since scan | Rescan and review a new proposal. Do not reuse a stale preview. |
| Missing backup or unexpected latest batch | Check this project's local backup directory and the path in the report. The UI is not a history browser; source-control history is the recovery route for older changes. |
| Partial/canceled apply or restore | Read entry counts/messages, retain the backup, and inspect current state. Already completed entries are not automatically rolled back. |
| Restore conflict | Preserve the later edit. Review the backup and source-control history manually; do not force the backup to match. |

For support, [open an issue](https://github.com/prokjs/texture-optimizer/issues) with package/Unity version, installation route, interface language, scope/target, messages, and reproduction steps. Review records for private paths before sharing. Historical texture checks do not verify all newer features; no device memory or performance measurements are claimed.

## Credits

Independently implemented by prokjs, inspired by Thry's VRAM Checker in [Thry's Avatar Performance Tools](https://github.com/Thryrallo/VRC-Avatar-Performance-Tools). No upstream implementation was reverse engineered or copied. This package is not affiliated with or endorsed by Thryrallo or that project. See the [MIT license](../LICENSE) and [third-party notices](../THIRD_PARTY_NOTICES.md).
