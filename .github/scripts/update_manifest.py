"""Adds a release to the Jellyfin plugin repository manifest."""

import argparse
import json
from datetime import datetime, timezone

TARGET_ABI = "12.0.0.0"
IMAGE_URL = "https://raw.githubusercontent.com/andrey-lysikov/jellyfin-plugin-jelly-twiggy/main/assets/icon.png"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("manifest")
    parser.add_argument("--version", required=True)
    parser.add_argument("--url", required=True)
    parser.add_argument("--checksum", required=True)
    parser.add_argument("--changelog", required=True)
    args = parser.parse_args()

    with open(args.manifest, encoding="utf-8") as f:
        manifest = json.load(f)

    plugin = manifest[0]
    plugin["imageUrl"] = IMAGE_URL
    # Re-releasing the same version replaces its entry.
    versions = [v for v in plugin["versions"] if v["version"] != args.version]
    versions.insert(0, {
        "version": args.version,
        "changelog": args.changelog,
        "targetAbi": TARGET_ABI,
        "sourceUrl": args.url,
        "checksum": args.checksum,
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    })
    plugin["versions"] = versions

    with open(args.manifest, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")


if __name__ == "__main__":
    main()
