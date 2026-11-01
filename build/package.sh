#!/usr/bin/env bash
# Builds Vintage-E and zips it into release/vintagee_<version>.zip, ready to drop into your Mods folder.
# Requires the .NET 10 SDK and VINTAGE_STORY pointing at a Vintage Story 1.22 install.
set -euo pipefail
cd "$(dirname "$0")/.."

if [ -z "${VINTAGE_STORY:-}" ]; then
  echo "Set VINTAGE_STORY to your Vintage Story install folder (the one with VintagestoryAPI.dll)." >&2
  exit 1
fi

python3 tools/validate_assets.py
dotnet build -c Release

version=$(python3 -c "import json;print(json.load(open('modinfo.json'))['version'])")
out="release/vintagee_${version}.zip"
mkdir -p release
rm -f "$out"
(cd bin/Release/Mods/vintagee && zip -qr "../../../../$out" modinfo.json moddb.json modicon.png VintageE.dll THIRD-PARTY-NOTICES.md assets)
echo "Built $out"
