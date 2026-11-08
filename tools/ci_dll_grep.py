"""Search other mods' code (the .dll files in their zips or folders) for type and member names, for the server test.

Usage: python3 ci_dll_grep.py <mods dir> <output file> <name> [<name> ...]

A .NET assembly keeps the names of the types and members it uses (and defines) as plain UTF-8 in its metadata, so a
byte search shows which mods touch a given API, e.g. ICustomHandbookPageContent. For each hit it also lists the
type names defined or used near it that look like classes of the mod (best effort: names are read from the
assembly's string heap, not from its type tables).
"""
import os
import re
import sys
import zipfile

NAME = re.compile(rb"[A-Za-z_][A-Za-z0-9_`.]{2,80}")


def scan(label, data, names, out, hits):
    found = [n for n in names if n.encode() in data]
    if not found:
        return
    hits.append(label)
    out.write(f"===== {label}: {', '.join(found)}\n")
    # The #Strings heap holds every name; list the ones that look like the mod's own classes or behaviors.
    words = sorted({m.group().decode() for m in NAME.finditer(data)
                    if (m.group().startswith(b"Block") or m.group().startswith(b"Item") or b"Behavior" in m.group()
                        or m.group().startswith(b"Collectible")) and len(m.group()) < 60})
    out.write("  candidate classes/behaviors: " + ", ".join(words[:120]) + ("" if len(words) <= 120 else f" (+{len(words) - 120} more)") + "\n")


def main():
    mods, outpath, names = sys.argv[1], sys.argv[2], sys.argv[3:]
    hits = []
    count = 0
    with open(outpath, "w", encoding="utf-8") as out:
        for entry in sorted(os.listdir(mods)):
            path = os.path.join(mods, entry)
            try:
                if entry.lower().endswith(".zip"):
                    with zipfile.ZipFile(path) as z:
                        for info in z.infolist():
                            if info.filename.lower().endswith(".dll"):
                                count += 1
                                scan(f"{entry}/{info.filename}", z.read(info), names, out, hits)
                elif entry.lower().endswith(".dll"):
                    count += 1
                    scan(entry, open(path, "rb").read(), names, out, hits)
                elif os.path.isdir(path):
                    for root, _, files in os.walk(path):
                        for f in files:
                            if f.lower().endswith(".dll"):
                                count += 1
                                scan(os.path.relpath(os.path.join(root, f), mods), open(os.path.join(root, f), "rb").read(), names, out, hits)
            except Exception as e:
                out.write(f"!!!!! {entry}: {e}\n")
        out.write(f"\n{len(hits)} of {count} mod assemblies mention {', '.join(names)}\n")
    print(f"dll grep: {len(hits)} of {count} assemblies")


if __name__ == "__main__":
    main()
