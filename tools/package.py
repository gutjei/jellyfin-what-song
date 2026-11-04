"""Package the published DLL and update the Jellyfin plugin catalog."""
import hashlib
import json
from io import BytesIO
import sys
from datetime import datetime, timezone
from pathlib import Path
from xml.etree import ElementTree
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo

root = Path(__file__).resolve().parents[1]
project = ElementTree.parse(root / "Jellyfin.Plugin.SongPause.csproj")
version = project.findtext(".//Version")
abi = project.find(".//PackageReference[@Include='Jellyfin.Controller']").get("Version") + ".0"
guid = "7bd9d031-4b68-47c2-a04b-fb2e1987ae71"
dll = "Jellyfin.Plugin.SongPause.dll"
package = root / "packages" / f"SongPause_{version}.zip"
manifest = root / "manifest.json"
catalog = json.loads(manifest.read_text("utf-8")) if manifest.exists() else [{
    "guid": guid, "name": "Song Pause", "owner": "gutjei", "category": "General",
    "overview": "See which song is playing when you pause a movie or TV episode.",
    "description": "Shows songs from a .songs.json timeline in Jellyfin Web.",
    "versions": []
}]
plugin, = catalog
if plugin["guid"] != guid:
    sys.exit("Unexpected plugin ID in manifest")
data = (root / "dist" / dll).read_bytes()
package.parent.mkdir(exist_ok=True)
# The archive timestamp is fixed so packaging the same DLL produces the same checksum.
payload = BytesIO()
with ZipFile(payload, "w") as archive:
    archive.writestr(ZipInfo(dll), data, compress_type=ZIP_DEFLATED)
content = payload.getvalue()
checksum = hashlib.md5(content).hexdigest()  # Required by Jellyfin 10.11
previous = next((v for v in plugin["versions"] if v["version"] == version), None)
if previous and previous["checksum"].lower() != checksum:
    sys.exit("A different package already exists for this version. Increment Version first.")
if not previous:
    plugin["versions"].insert(0, {
        "version": version, "targetAbi": abi, "checksum": checksum,
        "sourceUrl": "https://raw.githubusercontent.com/gutjei/jellyfin-what-song/main/packages/" + package.name,
        "timestamp": datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z"),
        "changelog": sys.argv[1] if len(sys.argv) > 1 else "Plugin update."
    })
package.write_bytes(content)
manifest.write_text(json.dumps(catalog, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
print(f"{package.name}  {checksum}")
