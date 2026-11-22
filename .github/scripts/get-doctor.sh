#!/usr/bin/env bash
# Prints the path of the Vintage Doctor zip a test should install.
# With the secret DOCTOR_TOKEN (a read-only token for the private Comp-any/Vintage-Doctor repo, kept as an organisation
# secret) it downloads the newest release, so every run tests against the doctor players actually get. Without the
# token, or if the download fails, it falls back to the copy kept in tools/vendor/vintagedoctor/, so a build never
# fails because of it. Which one was used is written to stderr and to $DOCTOR_NOTE (default ci/doctor-version.txt).
set -u
note=${DOCTOR_NOTE:-ci/doctor-version.txt}
mkdir -p "$(dirname "$note")"
saved=$(ls tools/vendor/vintagedoctor/*.zip 2>/dev/null | sort -V | tail -1)
if [ -n "${DOCTOR_TOKEN:-}" ]; then
  dir=${RUNNER_TEMP:-/tmp}/vintagedoctor-latest
  mkdir -p "$dir"
  rel=$(curl -fsSL -H "Authorization: Bearer $DOCTOR_TOKEN" -H "Accept: application/vnd.github+json" \
    https://api.github.com/repos/Comp-any/Vintage-Doctor/releases/latest 2>/dev/null)
  tag=$(printf '%s' "$rel" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("tag_name",""))' 2>/dev/null)
  read -r id name < <(printf '%s' "$rel" | python3 -c 'import json,sys
r=json.load(sys.stdin)
for a in r.get("assets",[]):
    if a["name"].endswith(".zip"): print(a["id"], a["name"]); break' 2>/dev/null)
  if [ -n "${id:-}" ] && curl -fsSL -H "Authorization: Bearer $DOCTOR_TOKEN" -H "Accept: application/octet-stream" \
       -o "$dir/$name" "https://api.github.com/repos/Comp-any/Vintage-Doctor/releases/assets/$id" && [ -s "$dir/$name" ]; then
    echo "Vintage Doctor: newest release $tag ($name), downloaded with DOCTOR_TOKEN" | tee "$note" >&2
    echo "$dir/$name"
    exit 0
  fi
  echo "Vintage Doctor: DOCTOR_TOKEN is set but the newest release could not be downloaded; using the saved copy" >&2
fi
if [ -n "$saved" ]; then
  echo "Vintage Doctor: saved copy $(basename "$saved") (no DOCTOR_TOKEN, or its download failed)" | tee "$note" >&2
  echo "$saved"
else
  echo "Vintage Doctor: none (no DOCTOR_TOKEN and no saved copy)" | tee "$note" >&2
fi
