"""Package only shipping artifacts; never include accounts, keys or logs."""
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[1]
FILES = ["RiftWard.exe", "RiftWard_console.exe", "RiftWard.pck", "game/RiftWardGame.pck", "game/manifest.json",
         "StartServer.cmd", "server.cfg", "README.md", "server/service.py", "server/run_server.py", "server/schema.sql",
         "FONT-LICENSE.txt", "GODOT-LICENSE.txt", "GODOT-COPYRIGHT.txt"]


def main():
    destination = ROOT / "RiftWard-Windows-Beta.zip"
    with zipfile.ZipFile(destination,"w",zipfile.ZIP_DEFLATED,compresslevel=9) as archive:
        for filename in FILES:
            archive.write(ROOT/"build"/filename,filename)
    with zipfile.ZipFile(destination) as archive:
        assert archive.testzip() is None
        assert not any("server-data" in name or name.endswith(".key") for name in archive.namelist())
    print(f"BETA_PACKAGE_READY {destination.name} {destination.stat().st_size} bytes")


if __name__ == "__main__":
    main()
