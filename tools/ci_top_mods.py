"""Picks the most-downloaded Vintage Story mods for the server test, leaving out libraries and APIs.

Usage: python3 ci_top_mods.py "<how many> [tags=a,b] [exclude=id,id] [skip=K]" <game version> <output file>

  tags=     only mods with at least one of these Mod DB tags (case doesn't matter), e.g. crafting,technology
  exclude=  mod ids to leave out (ones already tested, say)
  skip=     leave out the first K mods that pass every other filter (for a second batch)

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
    if any(t in ("library", "api") for t in tags):
        return "tagged " + ", ".join(t for t in tags if t in ("library", "api"))
    if any(i in KNOWN_LIBS for i in ids) or any(i.endswith("lib") or i.endswith("api") for i in ids):
        return "a known library id"
    text = (mod.get("name") or "") + " " + (mod.get("summary") or "")
    if LIBRARY_WORDS.search(text):
        return "sounds like a library: " + LIBRARY_WORDS.search(text).group(0)
    return None


def main():
    spec, gv, out = sys.argv[1].split(), sys.argv[2], sys.argv[3]
    n = int(spec[0])
    opts = dict(o.split("=", 1) for o in spec[1:] if "=" in o)
    want_tags = {t.strip().lower() for t in opts.get("tags", "").split(",") if t.strip()}
    exclude = {t.strip().lower() for t in opts.get("exclude", "").split(",") if t.strip()}
    skip = int(opts.get("skip", "0") or 0)
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
        tags = [t.lower() for t in (mod.get("tags") or [])]
        if want_tags and not want_tags.intersection(tags):
            continue
        if modid.lower() in exclude:
            log.append("skip  %-28s %8s  excluded" % (modid, mod.get("downloads")))
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
        if skip > 0:
            skip -= 1
            log.append("skip  %-28s %8s  in an earlier batch" % (modid, mod.get("downloads")))
            continue
        chosen.append(modid)
        log.append("take  %-28s %8s  %s [%s]" % (modid, mod.get("downloads"), mod.get("name"), ", ".join(mod.get("tags") or [])))
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
        f.write("Top %d content mods by downloads for game %s (plus %d libraries after them); asked for: %s\n\n" % (len(chosen), gv, len(usable_libs), sys.argv[1]))
        f.write("\n".join(log) + "\n\nlibraries added: " + ", ".join(usable_libs) + "\n")
    print("chose", len(chosen), "mods and", len(usable_libs), "libraries")


if __name__ == "__main__":
    main()
