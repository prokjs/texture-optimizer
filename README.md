# prokjs's VRChat World Optimizer — v1.2.0

**Languages:** English · [한국어](README.ko.md) · [日本語](README.ja.md)

> **Made for VRChat projects using Unity 2022.3.22f1. Other Unity versions have not been tested and may be incompatible.**

**Review texture resolution caps before you change them.** A standalone Unity Editor tool by prokjs for finding imported textures, comparing estimated GPU allocations, and applying deliberate, reversible importer-cap reductions.

Open **Tools → prokjs → VRChat World Optimizer**. The unified window has **Textures**, **LOD & Analysis**, and **Occlusion & Shadows** tabs. Opening it does not scan, apply changes, or bake automatically.

Choose **English**, **日本語**, or **한국어** from the language dropdown beside **User guide**. English is the default. The choice persists in EditorPrefs and refreshes all tabs, tables, previews and confirmations immediately without rescanning or clearing checked rows, proposed caps, LOD settings or the current preview plan. The guide button opens the matching language. Object names, paths and source evidence stay unchanged; unrecognized external exception text remains in English. This guide uses the English control labels.

- Scan loaded scenes, selected objects, an Assets folder, or all project texture assets under Assets.
- Review source dimensions, loaded dimensions and format, Default / Standalone / Android caps, and safety restrictions in one table.
- Check textures, propose smaller caps, and review a complete named preview before applying.
- Keep per-project backup records and restore safe matching entries from the latest batch.

## Requirements

- **Unity 2022.3.22f1**. Tested in a clean, separate project using a local UPM installation.
- Unity UI (`com.unity.ugui`), declared as a package dependency for UPM/VCC installs. For a `.unitypackage` install, ensure Unity UI is available in Package Manager.
- Editable texture assets and their `.meta` files. Check them out first if your version-control system locks files.

VRChat projects are the intended audience, but the package has no VRChat SDK dependency. No VRChat SDK, Udon, or other avatar/world tool is required to install or use it.

### Version and upgrade identity

Version **1.2.0** adds the three-language interface to the existing World Optimizer; it is not a new package. The repository remains `prokjs/texture-optimizer`, the package remains `com.prokjs.texture-optimizer`, and the assembly namespace remains `Prokjs.TextureOptimizer`. Existing texture backup paths and optimization safety rules are preserved. Historical v1.0.0 texture checks are not verification of the newer LOD, lighting or language workflows.

## Install — choose ONE method

Do not combine a `.unitypackage` import with a UPM or VCC installation. If you previously copied standalone optimizer scripts into your project, remove those old copies before installing this package; otherwise duplicate types or menu items can prevent compilation. Preserve any backups you need before migrating.

### Option 1: GitHub release `.unitypackage`

1. Open the [v1.2.0 release](https://github.com/prokjs/texture-optimizer/releases/tag/v1.2.0).
2. Download **`prokjs-texture-optimizer-1.2.0.unitypackage`** from the release's **Assets**. GitHub's “Source code” ZIP/TAR downloads are not the Unity import package.
3. In Unity, choose **Assets → Import Package → Custom Package…**, select the downloaded `.unitypackage`, and import its contents.
4. Let Unity compile, then open **Tools → prokjs → VRChat World Optimizer**.

### Option 2: Unity Package Manager (Git)

In Unity, open **Window → Package Manager → + → Add package from git URL…** and enter:

```text
https://github.com/prokjs/texture-optimizer.git#v1.2.0
```

This installs `com.prokjs.texture-optimizer` pinned to v1.2.0. A working Git installation is required by Unity's Git package workflow.

### Option 3: VRChat Creator Companion (VCC)

1. Open VCC **Settings → Packages → Add Repository**.
2. Add this listing URL:

   ```text
   https://prokjs.github.io/texture-optimizer/index.json
   ```

3. Enable the repository. Open **Manage Project**, find **prokjs's VRChat World Optimizer** (`com.prokjs.texture-optimizer`), and add version **1.2.0**.
4. Open Unity and use **Tools → prokjs → VRChat World Optimizer**.

VCC is an optional installation route, not a runtime dependency.

## First safe batch

1. **Commit or back up your project**, including texture `.meta` files.
2. Choose a **Scope**, **Edit cap for** target, and whether to include inactive objects. Click **Scan / Refresh**.
3. Review the table. Check individual editable rows, or use **Select visible editable** after filtering. A check mark selects an asset; it does not change its importer.
4. Choose a smaller **Proposed cap** per row, or choose a batch size and click **Propose for checked**. Proposals are read-only until applied.
5. Click **Preview Apply checked...**. Read every named asset, cap change, platform-override notice, and backup location.
6. If correct, click **Apply these named resolution caps**. Review the result, inspect texture quality in your project, and commit only the changes you intend to keep.
7. To undo the latest optimizer batch, use **Restore Last Batch...**, inspect the preview, then **Restore safe matching entries**. Conflicting later edits are not overwritten.

[Read the full user guide](Documentation~/user-guide.md) for scope coverage, estimate interpretation, backup handling, and troubleshooting.

## LOD and scene preparation

**LOD & Analysis:** scan and review static recommendations, not measured profiler results or FPS predictions. Inspect existing LOD levels in the Inspector. For automatic LODs, choose eligible fixed opaque meshes, review the explicit preview, then apply. LOD0 stays unchanged; generated reduced meshes are cached as new assets. Reduction percentages target **vertices removed**, not triangles or an exact result: topology preservation can prevent reaching the target. Inspect every level and transition visually.

Original meshes, UV data, materials, and colliders are not rewritten. Recommended automation excludes pickups, Udon-controlled objects, skinned meshes, material overrides, and baked-lightmapped substitutions. Eligibility is intentionally conservative, not a guarantee that every object passing a scan is suitable.

**Occlusion & Shadows:** preview and apply static-flag preparation to suitable objects. Occlusion flags alone do not perform runtime culling: use Unity's separate occlusion bake. Floor preparation requires existing suitable UV2/GI; it does not generate UV2 or convert lights. Lighting bake is a separate, explicit **scene-wide** action that can take a long time; read its confirmation and use cancel when needed. Restoring flags does **not** restore baked lighting data. Existing runtime tarp controllers are not modified.

Use Undo or the relevant batch restore for scene changes. Generated LOD `.asset` files are deliberately retained after restore to protect Undo and saved references; restore is not asset deletion. Save and back up scenes and lighting data before applying or baking. Nothing is automatically applied or baked by a scan.

## Important limits and safety

> **These are shared-asset edits.** A cap change affects every scene, material, prefab, or other consumer using that texture, not just the objects you scanned.

- **Imported source images retain their pixels.** Their workflow changes supported importer caps and selected-platform overrides, then reimports. **Native Texture2D `.asset` files are different:** eligible standalone RGBA32/DXT1/DXT5 assets support Default-only pixel resampling in place, shared by all platforms. Original bytes are saved before writing; GUID/local ID, format, mipmap presence and sampler settings are preserved, with mipmaps regenerated. Review the [guide's eligibility and restore guards](Documentation~/user-guide.md#native-texture2d-asset-exception) before using this destructive-to-pixels operation.
- **No FPS promise.** Lower resolution can trade detail for a smaller imported allocation, but it does not guarantee better frame rate or solve every performance bottleneck.
- **Estimates are not measured residency.** Current allocation estimates use the texture's loaded Editor dimensions and graphics format. Chosen-target/proposed figures use that same format as a proxy. Streaming, platform formats, device fallback, atlas packing, and importer rounding can differ. **Android estimates are a proxy, not an Android-device measurement.**
- **Some assets are locked by design:** sprites/atlas inputs; generated lighting/reflection assets; package, built-in, transient, embedded, and unsupported non-2D textures; and assets whose importer cannot be safely edited. A locked row is informational, not an invitation to bypass its owning workflow.
- **Scene/selection scans are not exhaustive dependency audits.** They inspect supported component/material references. In particular, detection of textures or materials referenced only by avatar animation clips is **not supported or claimed**. Folder/project scopes inventory texture assets rather than proving they are used.
- **Texture importer changes do not have a Unity Undo guarantee.** Use texture batch backups and source control. LOD and static-flag changes have their own Undo/restore workflow, described in the guide.
- **Texture backups are local to each project:** `UserSettings/ProkjsTextureOptimizerBackups/`. Imported images store importer restoration data, not image copies; supported native textures also store original `.asset` bytes. Retain all backup files. Native restore requires matching path/GUID/metadata/applied-content hashes and refuses moved, replaced or later-edited assets. The texture tab exposes the latest batch only.
- **Uninstalling does not restore caps.** Restore or revert the desired importer changes before removing the tool. Removing package files also does not remove your local backup records automatically.

## Attribution and license

Independently implemented by **prokjs**, inspired by **Thry's VRAM Checker** in [Thry's Avatar Performance Tools](https://github.com/Thryrallo/VRC-Avatar-Performance-Tools). This project was not reverse engineered from or copied from that implementation. It is not affiliated with, maintained by, or endorsed by Thryrallo or the upstream project.

The independently authored code is licensed under the [MIT License](LICENSE), copyright © 2026 prokjs. See [Third-party notices](THIRD_PARTY_NOTICES.md).

## Support

[Source repository](https://github.com/prokjs/texture-optimizer) · [Report an issue](https://github.com/prokjs/texture-optimizer/issues) · [Changelog](CHANGELOG.md)

For a useful report, include the package version, exact Unity version, install method, selected scope/target, error text, and reproduction steps. Do not attach private project assets or backup JSON without reviewing them; paths in those files may identify your project.
