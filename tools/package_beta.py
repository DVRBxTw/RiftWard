"""Package the player download: launcher + bundled Unity client. Never includes accounts, keys or logs."""
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[1]
FILES = ["RiftWard.exe", "RiftWard.pck", "game/RiftWard.zip", "game/manifest.json", "server.cfg", "README.md",
         "FONT-LICENSE.txt", "GODOT-LICENSE.txt", "GODOT-COPYRIGHT.txt"]


def main():
    destination = ROOT / "RiftWard-Windows.zip"
    with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for filename in FILES:
            archive.write(ROOT / "build" / filename, "RiftWard/" + filename)
    with zipfile.ZipFile(destination) as archive:
        assert archive.testzip() is None
        assert not any("server-data" in name or name.endswith(".key") or name.endswith(".log") for name in archive.namelist())
    print(f"PACKAGE_READY {destination.name} {destination.stat().st_size} bytes")


if __name__ == "__main__":
    main()
