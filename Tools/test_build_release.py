"""Regression checks for consumer-visible legacy Unity import contracts."""
import io
import tarfile
import unittest

import build_release as release


class LegacyPackageImportTests(unittest.TestCase):
    def archive(self):
        folder_meta = b"fileFormatVersion: 2\nguid: 11111111111111111111111111111111\nfolderAsset: yes\n"
        file_meta = b"fileFormatVersion: 2\nguid: 22222222222222222222222222222222\n"
        assets = {
            "Documentation~": (True, b"", "1" * 32, folder_meta),
            "README.md": (False, b"[Guide](Documentation~/user-guide.md)", "2" * 32, file_meta),
        }
        return tarfile.open(fileobj=io.BytesIO(release.create_unitypackage(assets)), mode="r:gz")

    def test_folders_never_become_empty_asset_files(self):
        # Unity otherwise attempts to copy a file onto a destination folder and
        # refuses the package before any scripts are installed.
        with self.archive() as archive:
            members = set(archive.getnames())
            folder_guids = [release.stable_guid("Assets/prokjs"), release.stable_guid(release.LEGACY_ROOT), "1" * 32]
            for guid in folder_guids:
                self.assertIn(guid + "/asset.meta", members)
                self.assertIn(guid + "/pathname", members)
                self.assertNotIn(guid + "/asset", members)
            self.assertIn("2" * 32 + "/asset", members)


if __name__ == "__main__":
    unittest.main()
