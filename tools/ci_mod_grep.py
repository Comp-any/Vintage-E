"""Search other mods' asset files (in the server's Mods folder) for patterns, for the server test's results.

Usage: python3 ci_mod_grep.py <mods dir> <game assets dir or ""> <patterns file> <output file>

The patterns file has one regular expression per line ('#' starts a comment). For every .json file in every mod
zip or folder (and in the game's own assets, if given) that matches, the output lists the file and each matching
line with three lines either side, so patches and item changes made by other mods can be read without the mods.
"""
import os
import re
import sys
import zipfile


def patterns(path):
    out = []
    for line in open(path, encoding="utf-8"):
        line = line.strip()
        if line and not line.startswith("#"):
            out.append(re.compile(line, re.IGNORECASE))
    return out


def report(name, text, pats, out):
    lines = text.splitlines()
    hits = [i for i, l in enumerate(lines) if any(p.search(l) for p in pats)]
    if not hits:
        return
    out.write(f"===== {name} ({len(hits)} matching lines)\n")
    shown = set()
    for i in hits:
        lo, hi = max(0, i - 3), min(len(lines), i + 4)
        if lo > 0 and lo not in shown and (lo - 1) not in shown:
            out.write("  ...\n")
        for j in range(lo, hi):
            if j in shown:
                continue
            shown.add(j)
            out.write(f"{j + 1:6}{'>' if j == i or j in hits else ' '} {lines[j][:400]}\n")
    out.write("\n")


def main():
    mods_dir, game_assets, pat_file, out_file = sys.argv[1:5]
    pats = patterns(pat_file)
    with open(out_file, "w", encoding="utf-8") as out:
        for entry in sorted(os.listdir(mods_dir)):
            path = os.path.join(mods_dir, entry)
            if entry.endswith(".zip"):
                with zipfile.ZipFile(path) as z:
                    for n in z.namelist():
                        if n.endswith(".json"):
                            report(f"{entry}/{n}", z.read(n).decode("utf-8", "replace"), pats, out)
            elif os.path.isdir(path) and entry != "vintagee":
                for root, _, files in os.walk(path):
                    for f in files:
                        if f.endswith(".json"):
                            p = os.path.join(root, f)
                            report(os.path.relpath(p, mods_dir), open(p, encoding="utf-8", errors="replace").read(), pats, out)
        if game_assets and os.path.isdir(game_assets):
            for root, _, files in os.walk(game_assets):
                for f in files:
                    if f.endswith(".json"):
                        p = os.path.join(root, f)
                        report("GAME/" + os.path.relpath(p, game_assets), open(p, encoding="utf-8", errors="replace").read(), pats, out)


if __name__ == "__main__":
    main()
