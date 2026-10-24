#!/usr/bin/env python3
"""Build the allowlisted Unity/VPM release with Python 3.10+ (standard library only).

Run from any directory:
  python Tools/build_release.py --repo-url https://github.com/prokjs/texture-optimizer --version 1.0.0

The script never executes Unity, modifies source assets, or publishes anything.
VPM schema references: https://vcc.docs.vrchat.com/vpm/repos/ and
https://vcc.docs.vrchat.com/vpm/packages/#vpm-manifest-additions
"""

import argparse
import copy
import gzip
import hashlib
import html
import io
import json
from pathlib import Path
import re
import tarfile
from urllib.parse import quote, urlencode, urlsplit
import uuid
import zipfile


PACKAGE_NAME = "com.prokjs.texture-optimizer"
LEGACY_ROOT = "Assets/prokjs/TextureOptimizer"
GUID_NAMESPACE = uuid.UUID("472894ec-67ba-5b39-91dc-f8cb4120c140")
# This is deliberately a file allowlist, not a recursive copy of the repository.
REQUIRED_FILES = (
    "package.json",
    "Editor/Prokjs.TextureOptimizer.Editor.asmdef",
    "Editor/WorldTextureOptimizerBackend.cs",
    "Editor/WorldTextureOptimizerWindow.cs",
    "README.md",
    "README.ko.md",
    "README.ja.md",
    "CHANGELOG.md",
    "LICENSE",
    "THIRD_PARTY_NOTICES.md",
    "Documentation~/user-guide.md",
    "Documentation~/user-guide.ko.md",
    "Documentation~/user-guide.ja.md",
)
SEMVER = re.compile(
    r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
    r"(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?"
    r"(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?"
)


def json_bytes(value):
    return (json.dumps(value, indent=2, ensure_ascii=False) + "\n").encode("utf-8")


def safe_source(root, relative):
    path = root / relative
    current = root
    for part in Path(relative).parts:
        current = current / part
        if current.is_symlink():
            raise ValueError(f"Symlinks are not permitted in release inputs: {relative}")
    if not path.resolve().is_relative_to(root):
        raise ValueError(f"Release input escapes repository: {relative}")
    return path


def stable_guid(relative):
    return uuid.uuid5(GUID_NAMESPACE, PACKAGE_NAME + "/" + relative).hex


def metadata(root, relative, folder=False):
    source = safe_source(root, relative + ".meta")
    if source.exists():
        data = source.read_bytes().replace(b"\r\n", b"\n")
        matches = re.findall(rb"(?m)^guid: ([0-9a-fA-F]{32})\s*$", data)
        if len(matches) != 1:
            raise ValueError(f"Invalid Unity GUID in {relative}.meta")
        return matches[0].decode("ascii").lower(), data
    guid = stable_guid(relative)
    lines = ["fileFormatVersion: 2", "guid: " + guid]
    if folder:
        lines.append("folderAsset: yes")
    if relative.endswith(".cs"):
        lines += ["MonoImporter:", "  externalObjects: {}", "  serializedVersion: 2",
                  "  defaultReferences: []", "  executionOrder: 0", "  icon: {instanceID: 0}"]
    elif relative.endswith(".asmdef"):
        lines += ["AssemblyDefinitionImporter:", "  externalObjects: {}"]
    else:
        lines += ["DefaultImporter:", "  externalObjects: {}"]
    lines += ["  userData:", "  assetBundleName:", "  assetBundleVariant:"]
    return guid, ("\n".join(lines) + "\n").encode("utf-8")


def collect_assets(root, manifest):
    assets = {}
    for relative in REQUIRED_FILES:
        path = safe_source(root, relative)
        if not path.is_file():
            raise ValueError(f"Missing required public release input: {relative}")
        # Every allowlisted input is UTF-8 text. Match the Git LF checkout so
        # Windows-built artifacts are identical to future Linux release builds.
        assets[relative] = (False, path.read_bytes().replace(b"\r\n", b"\n"))
        for parent in Path(relative).parents:
            if parent.as_posix() != ".":
                assets[parent.as_posix()] = (True, b"")
    assets["package.json"] = (False, json_bytes(manifest))
    result = {}
    seen_guids = set()
    for relative, (folder, content) in sorted(assets.items()):
        guid, meta = metadata(root, relative, folder)
        if guid in seen_guids:
            raise ValueError(f"Duplicate Unity GUID at {relative}")
        seen_guids.add(guid)
        result[relative] = (folder, content, guid, meta)
    return result


def create_zip(assets):
    output = io.BytesIO()
    entries = {}
    for relative, (folder, content, _guid, meta) in assets.items():
        if not folder:
            entries[relative] = content
        entries[relative + ".meta"] = meta
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_STORED) as archive:
        for name, content in sorted(entries.items()):
            entry = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            entry.create_system = 3
            entry.external_attr = 0o100644 << 16
            archive.writestr(entry, content)
    return output.getvalue()


def create_unitypackage(assets):
    # The legacy asset root and its parent folders receive stable folder GUIDs.
    entries = []
    for folder in ("Assets/prokjs", LEGACY_ROOT):
        guid = stable_guid(folder)
        meta = (f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\n"
                "DefaultImporter:\n  externalObjects: {}\n  userData:\n"
                "  assetBundleName:\n  assetBundleVariant:\n").encode("utf-8")
        entries.append((guid, folder, b"", meta, True))
    for relative, (_folder, content, guid, meta) in assets.items():
        # Tilde directories are hidden from Unity's AssetDatabase in UPM packages.
        # Expose the same guide as an ordinary folder in the legacy import.
        legacy_relative = relative.replace("Documentation~/", "Documentation/")
        if legacy_relative == "Documentation~":
            legacy_relative = "Documentation"
        if relative.endswith(".md"):
            content = content.replace(b"Documentation~/", b"Documentation/")
        entries.append((guid, LEGACY_ROOT + "/" + legacy_relative, content, meta, _folder))
    if len({entry[0] for entry in entries}) != len(entries):
        raise ValueError("Duplicate GUID in legacy Unity package")
    output = io.BytesIO()
    with gzip.GzipFile(fileobj=output, mode="wb", filename="", mtime=0, compresslevel=9) as compressed:
        with tarfile.open(fileobj=compressed, mode="w", format=tarfile.USTAR_FORMAT) as archive:
            for guid, pathname, content, meta, folder in sorted(entries):
                files = [("asset.meta", meta), ("pathname", pathname.encode("utf-8"))]
                if not folder:
                    files.insert(0, ("asset", content))
                for name, data in files:
                    entry = tarfile.TarInfo(guid + "/" + name)
                    entry.size = len(data)
                    entry.mode = 0o644
                    entry.mtime = 0
                    entry.uid = entry.gid = 0
                    entry.uname = entry.gname = ""
                    archive.addfile(entry, io.BytesIO(data))
    return output.getvalue()


def landing_page(repo_url, version, listing_url, zip_url, unity_url):
    esc = lambda value: html.escape(value, quote=True)
    add_url = "vcc://vpm/addRepo?" + urlencode({"url": listing_url})
    return f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="description" content="Standalone Unity editor texture inspection and reversible import optimization by prokjs.">
<title>prokjs Texture Optimizer</title>
<style>
:root {{ color-scheme: dark; font: 17px/1.6 system-ui,sans-serif; background:#11151c; color:#e8edf4; }}
body {{ max-width:800px; margin:4rem auto; padding:0 1.4rem; }}
h1 {{ line-height:1.15; }} h2 {{ margin-top:2rem; }} a {{ color:#8cd9ff; }}
.button {{ display:inline-block; background:#8cd9ff; color:#10212b; padding:.7rem 1rem; border-radius:.5rem; font-weight:700; text-decoration:none; }}
code {{ overflow-wrap:anywhere; }} .muted {{ color:#aebcca; }}
</style>
</head>
<body>
<p class="muted">prokjs · Unity editor tool · MIT</p>
<h1>Texture Optimizer</h1>
<nav aria-label="Installation guide languages"><a href="{esc(repo_url)}/blob/main/README.md">English guide</a> · <a href="{esc(repo_url)}/blob/main/README.ko.md" lang="ko">한국어 설치 안내</a> · <a href="{esc(repo_url)}/blob/main/README.ja.md" lang="ja">日本語インストールガイド</a></nav>
<p>Inspect texture memory and apply reversible resolution-cap reductions. Unity UI (com.unity.ugui) required. Editor-only: no runtime or network hooks.</p>
<p><strong>Made for VRChat projects using Unity 2022.3.22f1. Other Unity versions have not been tested and may be incompatible.</strong></p>
<h2>Install with VRChat Creator Companion</h2>
<p><a class="button" href="{esc(add_url)}">Add repository to VCC</a></p>
<p>Then manage your project in VCC and add <strong>prokjs Texture Optimizer</strong>.</p>
<p>Or add this repository URL manually in VCC Settings → Packages → Add Repository:</p>
<p><code>{esc(listing_url)}</code></p>
<h2>Other installation options</h2>
<ul>
<li><a href="{esc(unity_url)}">Download v{esc(version)} .unitypackage</a>. Ensure Unity UI is installed, then import into your project.</li>
<li>Unity Package Manager → Add package from git URL: <code>{esc(repo_url)}.git#v{esc(version)}</code>.</li>
<li><a href="{esc(zip_url)}">Download the VPM package ZIP</a> (package contents at archive root).</li>
</ul>
<p>Use only one installation method per project. Back up your project before changing texture settings.</p>
<p>Open <strong>Tools → prokjs → Texture Optimizer</strong> in Unity.</p>
<h2>Documentation and attribution</h2>
<p><a href="{esc(repo_url)}#readme">User guide and source</a> · <a href="{esc(repo_url)}/releases/tag/v{esc(version)}">Release v{esc(version)}</a> · <a href="{esc(repo_url)}/blob/main/LICENSE">MIT license</a></p>
<p class="muted">Independently implemented by prokjs, inspired by <a href="https://github.com/Thryrallo/VRC-Avatar-Performance-Tools">Thry's VRAM Checker in Avatar Performance Tools</a>. Not copied or reverse engineered, and not affiliated with or endorsed by Thry or VRChat.</p>
</body>
</html>
"""


def build(args):
    root = Path(__file__).resolve().parents[1]
    repo_url = args.repo_url.rstrip("/")
    if repo_url.endswith(".git"):
        repo_url = repo_url[:-4]
    parsed = urlsplit(repo_url)
    match = re.fullmatch(r"/([A-Za-z0-9-]+)/([A-Za-z0-9_.-]+)", parsed.path)
    if parsed.scheme != "https" or parsed.netloc != "github.com" or parsed.query or parsed.fragment or not match:
        raise ValueError("--repo-url must be https://github.com/OWNER/REPOSITORY")
    if not SEMVER.fullmatch(args.version):
        raise ValueError("--version must be a SemVer value without a v prefix")
    owner, repo = match.groups()
    listing_url = args.listing_url or f"https://{owner.lower()}.github.io/{repo}/index.json"
    listing_parts = urlsplit(listing_url)
    if listing_parts.scheme != "https" or not listing_parts.netloc or listing_parts.fragment:
        raise ValueError("--listing-url must be an absolute HTTPS URL without a fragment")
    manifest = json.loads(safe_source(root, "package.json").read_text(encoding="utf-8"))
    if manifest.get("name") != PACKAGE_NAME or manifest.get("version") != args.version:
        raise ValueError("package.json name/version must match the requested release; update it before building")
    if manifest.get("author", {}).get("name") != "prokjs":
        raise ValueError("Release author must be prokjs")
    if manifest.get("unity") != "2022.3" or manifest.get("dependencies", {}).get("com.unity.ugui") != "1.0.0":
        raise ValueError("Expected Unity 2022.3 and com.unity.ugui 1.0.0")
    zip_name = f"{PACKAGE_NAME}-{args.version}.zip"
    unity_name = f"prokjs-texture-optimizer-{args.version}.unitypackage"
    download_root = repo_url + "/releases/download/" + quote("v" + args.version, safe="") + "/"
    zip_url = download_root + quote(zip_name)
    unity_url = download_root + quote(unity_name)
    manifest["url"] = zip_url
    manifest["documentationUrl"] = repo_url + "#readme"
    manifest["changelogUrl"] = repo_url + "/blob/main/CHANGELOG.md"
    manifest["licensesUrl"] = repo_url + "/blob/main/LICENSE"
    manifest.pop("zipSHA256", None)  # Only the listing contains the ZIP hash.
    assets = collect_assets(root, manifest)
    zip_data = create_zip(assets)
    unity_data = create_unitypackage(assets)
    zip_hash = hashlib.sha256(zip_data).hexdigest()
    unity_hash = hashlib.sha256(unity_data).hexdigest()
    website = (root / args.website_dir).resolve()
    output = (root / args.output).resolve()
    if website == root or output == root or website == output:
        raise ValueError("Output and website directories must be distinct non-root directories")
    listing_path = website / "index.json"
    prior_listing_path = Path(args.previous_listing).resolve() if args.previous_listing else listing_path
    if args.previous_listing and not prior_listing_path.is_file():
        raise ValueError("--previous-listing must identify an existing JSON file")
    if prior_listing_path.exists():
        listing = json.loads(prior_listing_path.read_text(encoding="utf-8"))
        if listing.get("id") != "com.prokjs.texture-optimizer.repository":
            raise ValueError("Existing website listing has an unexpected repository id")
    else:
        listing = {"packages": {}}
    listing.update({"name": "prokjs Texture Optimizer", "id": "com.prokjs.texture-optimizer.repository",
                    "url": listing_url, "author": "prokjs"})
    package_listing = listing.setdefault("packages", {}).setdefault(PACKAGE_NAME, {"versions": {}})
    versions = package_listing.setdefault("versions", {})
    version_manifest = copy.deepcopy(manifest)
    version_manifest["zipSHA256"] = zip_hash
    previous = versions.get(args.version)
    if previous is not None and previous != version_manifest:
        raise ValueError("This version already exists with different release contents; bump the version instead")
    versions[args.version] = version_manifest
    output.mkdir(parents=True, exist_ok=True)
    website.mkdir(parents=True, exist_ok=True)
    (output / zip_name).write_bytes(zip_data)
    (output / unity_name).write_bytes(unity_data)
    (output / "SHA256SUMS.txt").write_text(
        f"{zip_hash}  {zip_name}\n{unity_hash}  {unity_name}\n", encoding="ascii", newline="\n")
    listing_path.write_bytes(json_bytes(listing))
    (website / "index.html").write_text(
        landing_page(repo_url, args.version, listing_url, zip_url, unity_url), encoding="utf-8", newline="\n")
    (website / ".nojekyll").write_bytes(b"")
    print(f"Built {output / zip_name}\nSHA256 {zip_hash}\nBuilt {output / unity_name}\nSHA256 {unity_hash}")
    print(f"Generated {listing_path} and {website / 'index.html'}; no files published.")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--repo-url", required=True, help="Public HTTPS GitHub repository URL")
    parser.add_argument("--version", required=True, help="Must match package.json, e.g. 1.0.0")
    parser.add_argument("--listing-url", help="Custom Pages listing URL (default: OWNER.github.io/REPOSITORY/index.json)")
    parser.add_argument("--previous-listing", help="Existing deployed index.json to merge, preserving prior release versions")
    parser.add_argument("--output", default="dist", help="Artifact directory, relative to the repository root")
    parser.add_argument("--website-dir", default="Website", help="Pages output directory, relative to the repository root")
    args = parser.parse_args()
    try:
        build(args)
    except (OSError, ValueError, KeyError, TypeError, AttributeError, json.JSONDecodeError) as error:
        parser.exit(1, f"Release build failed: {error}\n")


if __name__ == "__main__":
    main()
