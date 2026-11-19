"""Package the player download: launcher + bundled Unity client. Never includes accounts, keys or logs.
--source build (測試服, default) or prod (正式服); the folder's server.cfg decides which server the download joins."""
from pathlib import Path
import argparse
import zipfile

ROOT = Path(__file__).resolve().parents[1]
FILES = ["RiftWard.exe", "game/RiftWard.zip", "game/manifest.json", "server.cfg", "README.md", "FONT-LICENSE.txt"]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", default="build")
    parser.add_argument("--out", default="RiftWard-Test-Windows.zip")
    args = parser.parse_args()
    destination = ROOT / args.out
    with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for filename in FILES:
            archive.write(ROOT / args.source / filename, "RiftWard/" + filename)
    with zipfile.ZipFile(destination) as archive:
        assert archive.testzip() is None
        assert not any("godot" in name.lower() or name.endswith(".pck") for name in archive.namelist())
        assert not any("server-data" in name or name.endswith(".key") or name.endswith(".log") for name in archive.namelist())
    print(f"PACKAGE_READY {destination.name} {destination.stat().st_size} bytes")


if __name__ == "__main__":
    main()
