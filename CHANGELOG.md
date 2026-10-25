# Changelog

## [1.2.0] — 2026-10-25

- Added **English / 日本語 / 한국어** beside **User guide**, with English as the default and a persistent EditorPrefs choice.
- Localized the combined texture, LOD/analysis and occlusion/shadow interface: controls, tabs, headers, dialogs, safety restrictions, progress, status reports and preview notices.
- Language changes refresh both controllers and open texture previews without rescanning, clearing checked rows, changing configuration or discarding the active LOD preview plan.
- Preserved object names, paths, source evidence and canonical backup/receipt values. Unrecognized external exception text falls back to its original English.
- Updated version-pinned downloads and all three installation/user guides. Package identity, optimization algorithms and safety thresholds are unchanged.

[1.2.0]: https://github.com/prokjs/texture-optimizer/releases/tag/v1.2.0

## [1.1.0] — 2026-10-25

- Rebranded as **prokjs's VRChat World Optimizer** with one **Tools → prokjs → VRChat World Optimizer** window: **Textures**, **LOD & Analysis**, **Occlusion & Shadows**.
- Preserved repository, `com.prokjs.texture-optimizer` identity, `Prokjs.TextureOptimizer` namespace and existing texture backup directory for upgrades.
- Added static scene recommendations, existing-LOD Inspector preview, and explicit preview/apply automatic LOD generation for conservative fixed opaque candidates. LOD0 is preserved; vertex-removal targets are approximate and topology-constrained. Original meshes/UV/material assets/colliders are not rewritten.
- Added static-flag preparation for occlusion and existing-UV2/GI floor shadows, with separate Unity bake controls. Occlusion requires a bake; lighting bake is explicitly confirmed, scene-wide, cancelable and potentially long-running. No UV2 generation, light conversion or runtime tarp-controller changes.
- Added Undo/batch restore for scene changes. Generated mesh assets remain after restore for Undo/reference safety; restored flags do not restore baked lighting data.
- Retained imported-texture cap workflow and added guarded Default-only pixel reduction for standalone RGBA32/DXT1/DXT5 Texture2D `.asset` files, with original-byte backup and conflict-aware restore.
- Updated English, Korean and Japanese guidance. Analysis and memory figures remain estimates, not profiler results or performance promises.

[1.1.0]: https://github.com/prokjs/texture-optimizer/releases/tag/v1.1.0

## [1.0.0] — 2026-10-24

Initial standalone release of **Texture Optimizer** by prokjs.

> **Made for VRChat projects using Unity 2022.3.22f1. Other Unity versions have not been tested and may be incompatible.**

VRChat projects are the intended audience; the package has no VRChat SDK dependency.

### Added

- Editor-only texture inventory for loaded scenes, selected objects/prefabs, an Assets folder, and project texture assets under Assets.
- Searchable, sortable texture table with source/loaded dimensions, Default / Standalone / Android cap visibility, explicit-override indicators, safety notices, and GPU allocation estimates.
- Checked-row resolution-cap proposals, with no importer writes during scanning, selection, or proposal.
- Complete named apply preview and explicit confirmation before shared importer changes.
- Reductions constrained by source dimensions and current effective caps; no source-image resampling or automatic resolution increases.
- Per-project durable batch records under `UserSettings/ProkjsTextureOptimizerBackups/`, with latest-batch restore and conflict-aware protection for later edits.
- Read-only handling for sprite/atlas inputs, generated lighting/reflection assets, package assets, unsupported texture types, and other unsafe importer states.
- Installation through a GitHub release `.unitypackage`, version-pinned UPM Git URL, or the optional VCC listing.
- Complete installation and usage guides in English, Korean and Japanese, with language navigation.
- MIT license for independently authored code and attribution to Thry's VRAM Checker, including the upstream MIT notice for reference.

### Compatibility and limits

- Requires **Unity 2022.3.22f1** and Unity UI (`com.unity.ugui`). Tested in a clean, separate project through a local UPM installation; an end-to-end VCC application installation has not been tested.
- GPU figures are full-allocation estimates, not measured residency. Target/proposed figures reuse the current loaded format as a proxy; Android-device measurements are not provided.
- No frame-rate improvement or Unity Undo guarantee. Backups are local to each project; uninstalling does not restore importer changes.
- Object-based scans do not claim detection of avatar animation-only references or exhaustive build dependency coverage.

### Verified

- Passed 17 smoke checks in the clean Unity 2022.3.22f1 project: package assembly/public namespace compilation, Default cap apply/reimport and exact restoration, Android override apply and exact restoration, stale-state and no-upscale guards, later-edit restore conflict protection, sprite locking, and unchanged source hashes.
- Imported the generated `.unitypackage` into a second clean project, compiled it, and passed the same 17 checks against the installed package.
- Visually inspected the actual Editor window.
- Kept the development world project untouched. These checks are not Android-device memory or frame-rate measurements.

[1.0.0]: https://github.com/prokjs/texture-optimizer/releases/tag/v1.0.0
