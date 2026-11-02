"""Picks the most-downloaded Vintage Story mods for the server test, leaving out libraries and APIs.

Usage: python3 ci_top_mods.py <how many> <game version> <output file>

Asks the Mod DB (https://mods.vintagestory.at/api/mods) for every mod, sorts by downloads, drops anything that is a
library, an API, a framework, a tool or a pack rather than a content mod, and keeps the first N whose latest release
fits the game version (the install-information endpoint says so). Libraries that other mods may depend on are added
after the N, so they don't count but are present. Writes the chosen mod ids, comma-separated, to the output file and
a human-readable list (name, downloads, why skipped) next to it as <output file>.txt.
"""
import json
import re
import sys
import urllib.request

LIBRARY_WORDS = re.compile(r"\b(library|libraries|lib|api|framework|core mod|dependency|modding tool|toolkit|utility mod)\b", re.IGNORECASE)
KNOWN_LIBS = {"commonlib", "configlib", "vsimgui", "harmony", "exlib", "cairosharp", "lmlib", "simplelibs", "zlib", "combatoverhaullib", "xlib", "xskills_lib"}


def get(url):
    req = urllib.request.Request(url, headers={"User-Agent": "Vintage-E server test"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.loads(r.read().decode("utf-8"))


def is_library(mod):
    tags = [t.lower() for t in (mod.get("tags") or [])]
    ids = [i.lower() for i in (mod.get("modidstrs") or [])]
    if mod.get("type") != "mod":
        return "not a mod (" + str(mod.get("type")) + ")"
    if any(t in ("library", "api", "utility", "tool") for t in tags):
        return "tagged " + ", ".join(t for t in tags if t in ("library", "api", "utility", "tool"))
    if any(i in KNOWN_LIBS for i in ids) or any(i.endswith("lib") or i.endswith("api") for i in ids):
        return "a known library id"
    text = (mod.get("name") or "") + " " + (mod.get("summary") or "")
    if LIBRARY_WORDS.search(text):
        return "sounds like a library: " + LIBRARY_WORDS.search(text).group(0)
    return None


def main():
    n, gv, out = int(sys.argv[1]), sys.argv[2], sys.argv[3]
    mods = get("https://mods.vintagestory.at/api/mods").get("mods", [])
    mods.sort(key=lambda m: -(m.get("downloads") or 0))
    chosen, libs, log = [], [], []
    for mod in mods:
        if len(chosen) >= n:
            break
        ids = mod.get("modidstrs") or []
        if not ids:
            continue
        modid = ids[0]
        if modid in ("vintagee", "game", "survival", "creative"):
            continue
        why = is_library(mod)
        if why:
            if mod.get("type") == "mod" and len(libs) < 30:
                libs.append(modid)
            log.append("skip  %-28s %8s  %s" % (modid, mod.get("downloads"), why))
            continue
        # Does its latest release fit this game version?
        try:
            info = get("https://mods.vintagestory.at/api/v2/mods/install-information?ids=%s&gv=%s" % (modid, gv)).get("data", {})
            rel = info.get(modid) or {}
            if "fileUrl" not in rel:
                log.append("skip  %-28s %8s  no release for %s" % (modid, mod.get("downloads"), gv))
                continue
        except Exception as e:  # noqa: BLE001
            log.append("skip  %-28s %8s  install-information failed: %s" % (modid, mod.get("downloads"), e))
            continue
        chosen.append(modid)
        log.append("take  %-28s %8s  %s" % (modid, mod.get("downloads"), mod.get("name")))
    # Libraries after the content mods: they load if some mod needs them and are harmless otherwise.
    usable_libs = []
    for lib in libs:
        try:
            info = get("https://mods.vintagestory.at/api/v2/mods/install-information?ids=%s&gv=%s" % (lib, gv)).get("data", {})
            if "fileUrl" in (info.get(lib) or {}):
                usable_libs.append(lib)
        except Exception:  # noqa: BLE001
            pass
    with open(out, "w", encoding="utf-8") as f:
        f.write(",".join(chosen + usable_libs))
    with open(out + ".txt", "w", encoding="utf-8") as f:
        f.write("Top %d content mods by downloads for game %s (plus %d libraries after them)\n\n" % (len(chosen), gv, len(usable_libs)))
        f.write("\n".join(log) + "\n\nlibraries added: " + ", ".join(usable_libs) + "\n")
    print("chose", len(chosen), "mods and", len(usable_libs), "libraries")


if __name__ == "__main__":
    main()
