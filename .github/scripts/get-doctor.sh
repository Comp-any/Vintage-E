#!/usr/bin/env bash
# Prints the path of the Vintage Doctor zip a test should install: the newest release of Comp-any/Vintage-Doctor.
# The repository is public (since 2026-11-25), so no secret is needed: the release is found by following
# github.com/.../releases/latest to its tag (a web redirect, not the API, so GitHub's limit of 60 anonymous API
# requests an hour doesn't apply) and its zip downloaded by its public link. A token in DOCTOR_TOKEN, GH_TOKEN or
# GITHUB_TOKEN, if there is one, is used for the API instead. If the download fails, the copy kept in
# tools/vendor/vintagedoctor/ is used, so a build never fails because of it. Which one was used is written to stderr
# and to $DOCTOR_NOTE (default ci/doctor-version.txt): "saved copy" there means the download failed.
set -u
repo=Comp-any/Vintage-Doctor
note=${DOCTOR_NOTE:-ci/doctor-version.txt}
mkdir -p "$(dirname "$note")"
saved=$(ls tools/vendor/vintagedoctor/*.zip 2>/dev/null | sort -V | tail -1)
dir=${RUNNER_TEMP:-/tmp}/vintagedoctor-latest
mkdir -p "$dir"
token=${DOCTOR_TOKEN:-${GH_TOKEN:-${GITHUB_TOKEN:-}}}

# With a token: the API (the release's own list of files).
if [ -n "$token" ]; then
  rel=$(curl -fsSL -H "Authorization: Bearer $token" -H "Accept: application/vnd.github+json" \
    "https://api.github.com/repos/$repo/releases/latest" 2>/dev/null)
  tag=$(printf '%s' "$rel" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("tag_name",""))' 2>/dev/null)
  read -r id name < <(printf '%s' "$rel" | python3 -c 'import json,sys
r=json.load(sys.stdin)
for a in r.get("assets",[]):
    if a["name"].endswith(".zip"): print(a["id"], a["name"]); break' 2>/dev/null)
  if [ -n "${id:-}" ] && curl -fsSL -H "Authorization: Bearer $token" -H "Accept: application/octet-stream" \
       -o "$dir/$name" "https://api.github.com/repos/$repo/releases/assets/$id" && [ -s "$dir/$name" ]; then
    echo "Vintage Doctor: newest release $tag ($name)" | tee "$note" >&2
    echo "$dir/$name"
    exit 0
  fi
  echo "Vintage Doctor: the API didn't give the newest release; trying its public link" >&2
fi

# Without one: the public release page and download link (the zip is named vintagedoctor_<version>.zip).
url=$(curl -fsSIL -o /dev/null -w '%{url_effective}' "https://github.com/$repo/releases/latest" 2>/dev/null)
tag=${url##*/tag/}
if [ -n "$tag" ] && [ "$tag" != "$url" ]; then
  name="vintagedoctor_${tag#v}.zip"
  if curl -fsSL -o "$dir/$name" "https://github.com/$repo/releases/download/$tag/$name" && [ -s "$dir/$name" ]; then
    echo "Vintage Doctor: newest release $tag ($name)" | tee "$note" >&2
    echo "$dir/$name"
    exit 0
  fi
fi

if [ -n "$saved" ]; then
  echo "Vintage Doctor: saved copy $(basename "$saved") (the newest release could not be downloaded)" | tee "$note" >&2
  echo "$saved"
else
  echo "Vintage Doctor: none (the newest release could not be downloaded and there is no saved copy)" | tee "$note" >&2
fi
