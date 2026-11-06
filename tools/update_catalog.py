"""Merge one verified release into the current catalog, retaining other releases."""
import json
import sys
from pathlib import Path

def update_catalog(current, candidate, version):
    plugin_id = "7bd9d031-4b68-47c2-a04b-fb2e1987ae71"
    source = next(p for p in candidate if p["guid"] == plugin_id)
    target = next(p for p in current if p["guid"] == plugin_id)
    release = next(v for v in source["versions"] if v["version"] == version)
    previous = next((v for v in target["versions"] if v["version"] == version), None)
    if previous is not None:
        if previous["checksum"].lower() != release["checksum"].lower() or previous["sourceUrl"] != release["sourceUrl"]:
            raise ValueError("Published version already refers to a different package")
        return current
    target["versions"].append(release)
    target["versions"].sort(key=lambda v: tuple(map(int, v["version"].split("."))), reverse=True)
    return current

if __name__ == "__main__":
    path = Path("manifest.json")
    current = json.loads(path.read_text(encoding="utf-8"))
    candidate = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    result = update_catalog(current, candidate, sys.argv[2])
    path.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
