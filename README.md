# Texture Optimizer

**Languages:** English · [한국어](README.ko.md) · [日本語](README.ja.md)

> **Made for VRChat projects using Unity 2022.3.22f1. Other Unity versions have not been tested and may be incompatible.**

**Review texture resolution caps before you change them.** A standalone Unity Editor tool by prokjs for finding imported textures, comparing estimated GPU allocations, and applying deliberate, reversible importer-cap reductions.

Open **Tools → prokjs → Texture Optimizer**. Nothing is scanned or changed just by opening the window. There are no runtime components or network hooks.

- Scan loaded scenes, selected objects, an Assets folder, or all project texture assets under Assets.
- Review source dimensions, loaded dimensions and format, Default / Standalone / Android caps, and safety restrictions in one table.
- Check textures, propose smaller caps, and review a complete named preview before applying.
- Keep per-project backup records and restore safe matching entries from the latest batch.

## Requirements

- **Unity 2022.3.22f1**. Tested in a clean, separate project using a local UPM installation.
- Unity UI (`com.unity.ugui`), declared as a package dependency for UPM/VCC installs. For a `.unitypackage` install, ensure Unity UI is available in Package Manager.
- Editable texture assets and their `.meta` files. Check them out first if your version-control system locks files.

VRChat projects are the intended audience, but the package has no VRChat SDK dependency. No VRChat SDK, Udon, or other avatar/world tool is required to install or use it.

### Release verification

In that clean Unity 2022.3.22f1 project, 17 smoke checks passed, covering package assembly compilation, the public namespace, Default cap apply/reimport and exact restore, Android override apply and exact restore, stale-state/no-upscale guards, protection against restoring over later edits, sprite locking, and unchanged source hashes. The actual Editor window was also visually inspected. These checks did not modify the development world project.

This is **not** an end-to-end VCC application test or an Android-device memory/performance test. A local UPM installation does not establish that every installation method or Unity version has been exercised.

## Install — choose ONE method

Do not combine a `.unitypackage` import with a UPM or VCC installation. If you previously copied standalone optimizer scripts into your project, remove those old copies before installing this package; otherwise duplicate types or menu items can prevent compilation. Preserve any backups you need before migrating.

### Option 1: GitHub release `.unitypackage`

1. Open the [v1.0.0 release](https://github.com/prokjs/texture-optimizer/releases/tag/v1.0.0).
2. Download **`prokjs-texture-optimizer-1.0.0.unitypackage`** from the release's **Assets**. GitHub's “Source code” ZIP/TAR downloads are not the Unity import package.
3. In Unity, choose **Assets → Import Package → Custom Package…**, select the downloaded `.unitypackage`, and import its contents.
4. Let Unity compile, then open **Tools → prokjs → Texture Optimizer**.

### Option 2: Unity Package Manager (Git)

In Unity, open **Window → Package Manager → + → Add package from git URL…** and enter:

```text
https://github.com/prokjs/texture-optimizer.git#v1.0.0
```

This installs `com.prokjs.texture-optimizer` pinned to v1.0.0. A working Git installation is required by Unity's Git package workflow.

### Option 3: VRChat Creator Companion (VCC)

1. Open VCC **Settings → Packages → Add Repository**.
2. Add this listing URL:

   ```text
   https://prokjs.github.io/texture-optimizer/index.json
   ```

3. Enable the repository. Open **Manage Project** for your project, find **Texture Optimizer**, and add version **1.0.0**.
4. Open Unity and use **Tools → prokjs → Texture Optimizer**.

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

## Important limits and safety

> **These are shared-asset edits.** A cap change affects every scene, material, prefab, or other consumer using that texture, not just the objects you scanned.

- **Original source pixels are untouched.** The tool changes supported texture import resolution caps and, when needed for Standalone/Android, the selected platform's override state. Unity reimports the affected texture. It is not a source-image resizer or a compression-format optimizer.
- **No FPS promise.** Lower resolution can trade detail for a smaller imported allocation, but it does not guarantee better frame rate or solve every performance bottleneck.
- **Estimates are not measured residency.** Current allocation estimates use the texture's loaded Editor dimensions and graphics format. Chosen-target/proposed figures use that same format as a proxy. Streaming, platform formats, device fallback, atlas packing, and importer rounding can differ. **Android estimates are a proxy, not an Android-device measurement.**
- **Some assets are locked by design:** sprites/atlas inputs; generated lighting/reflection assets; package, built-in, transient, embedded, and unsupported non-2D textures; and assets whose importer cannot be safely edited. A locked row is informational, not an invitation to bypass its owning workflow.
- **Scene/selection scans are not exhaustive dependency audits.** They inspect supported component/material references. In particular, detection of textures or materials referenced only by avatar animation clips is **not supported or claimed**. Folder/project scopes inventory texture assets rather than proving they are used.
- **Do not rely on Unity Undo.** There is no Unity Undo guarantee. Use the optimizer's backups and your own source control or project backup.
- **Backups are local to each project:** `UserSettings/ProkjsTextureOptimizerBackups/`. They are not source-image backups and may not be included in your version-control setup. Keep them if you need restoration. The window exposes the latest batch only, not a multi-batch history browser.
- **Uninstalling does not restore caps.** Restore or revert the desired importer changes before removing the tool. Removing package files also does not remove your local backup records automatically.

## Attribution and license

Independently implemented by **prokjs**, inspired by **Thry's VRAM Checker** in [Thry's Avatar Performance Tools](https://github.com/Thryrallo/VRC-Avatar-Performance-Tools). This project was not reverse engineered from or copied from that implementation. It is not affiliated with, maintained by, or endorsed by Thryrallo or the upstream project.

The independently authored code is licensed under the [MIT License](LICENSE), copyright © 2026 prokjs. See [Third-party notices](THIRD_PARTY_NOTICES.md).

## Support

[Source repository](https://github.com/prokjs/texture-optimizer) · [Report an issue](https://github.com/prokjs/texture-optimizer/issues) · [Changelog](CHANGELOG.md)

For a useful report, include the package version, exact Unity version, install method, selected scope/target, error text, and reproduction steps. Do not attach private project assets or backup JSON without reviewing them; paths in those files may identify your project.
