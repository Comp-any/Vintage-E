# Changelog

All notable changes to Vintage-E. Each GitHub release uses its section from this file as its release notes.

## 1.17.3 — 2026-11-03

### Fixed
- **Table and tablet window:** the EMC balance has its own line, with the known count under it, so a long number no longer runs into the learn and burn slots. Past what fits (billions), it shows a short form (1.23 billion).

## 1.17.2 — 2026-11-03

### Fixed
- **Doctor window:** lines that show an item icon are given the icon's height, so they no longer run into the buttons under them (seen under the blacklisted-ingredients check).

### Changed
- **The doctor no longer pauses the game.** Reports are built on a worker thread and the remapping a fix triggers runs there too; the world keeps ticking and the window shows "Scanning…" until the answer arrives.
- **Free-EMC check:** one line per item instead of one per recipe (18 kinds of fish all filleting into the same raw fillet were 18 lines); the line shows the recipe with the biggest gain and says how many more there are.
- **Meteorite stones are worth half a meteoric iron ingot (192):** two smelt into one ingot (384), and at the generic stone value of 1 that made 382 EMC out of nothing per ingot.
- **Carburising charges its fuel:** the stone coffin packs 40 pieces of fuel around 16 iron ingots, so blister steel now costs an iron ingot plus 2.5 of the cheapest coffin fuel (charcoal, coal or coke), not an iron ingot alone.

### Added
- **Doctor:** "Remove all N entries that match nothing and remap" under the settings-file check, next to the per-entry links.
- **Doctor window:** a Check-up button left of Back that returns to the doctor's first page; the Copy button says what it copies ("Copy this report").
- **Wording:** 269 of the doctor's texts rewritten in plainer words: one idea per sentence, "priced from a recipe that uses it" instead of "worked backwards", "fallback value" instead of "last-resort value", "copied from a look-alike variant" instead of "copied from variants", and every check header as "Topic: result".

## 1.17.1 — 2026-11-03

### Fixed
- **Doctor:** the check-up crashed with a player online (the players check walked the creative inventory, which has no slots on the server). Every health check now runs on its own: one failing is reported as a line, not as a failed diagnosis.

## 1.17.0 — 2026-11-03

### Added
- **Doctor: health checks** (`/emc diagnose health`, and one line each in the check-up): the game version against the one this build was tested on; whether the mod can write its data, cache and settings folders; what a join costs (table size and send time); the online players' data (EMC a real number, learned items that still exist, the exact-knowledge cap, tablet filters naming missing items); values that moved tenfold since the last mapping with the same mods; recipes whose ingredients all have values but whose output got none (the solver's loop guard or floor); items priced from a trader although a recipe exists; look-only variant groups whose members came out worth different amounts.
- **Doctor, your game:** hotkeys of other mods or the game bound to the same keys as Vintage-E's; other mods' Harmony patches on the game methods Vintage-E relies on (shape loaders, tooltips, inventories, damage), with their owners; and when client and server versions differ, what that difference breaks.

## 1.16.7 — 2026-11-03

### Fixed
- **Doctor window:** Run checks again, Back and Copy are now the same width (the game's button grows past a narrower fixed width, so they differed), and the question and next-step buttons under one heading are all the width of the widest, so they line up.

## 1.16.6 — 2026-11-03

### Changed
- **Doctor:** the "N things worth fixing, marked ⚠ above" line is gone; the chips at the top count the checks and each card says what to do.

## 1.16.5 — 2026-11-03

### Fixed
- **Doctor, start-up check:** mods the game skipped for a missing dependency were missed, because the game reports them before the line the check started reading from. Seen in the 50-mod test: 14 skipped mods went unreported.

## 1.16.4 — 2026-11-03

### Added
- **Doctor window:** an Info chip next to Passed, Warnings and Failed; click it to hide or show the informational checks. A chip whose checks are shown has a tint of its colour behind the words; a hidden one is plain. The Back and Copy buttons are the same width.

### Changed
- **Bug report:** the same facts in about half the lines: values out of line are one short line each (code, value, family middle, source), counts and families sit on one line, settings are `Name=value` on one line, and the double blank lines are gone.

## 1.16.3 — 2026-11-03

### Added
- **Doctor: what other mods did at start-up.** A new check reads the game's own start-up messages and reports, in plain words: mods the game skipped because something they need isn't installed (and what that is), other mods' errors while starting (which can stop the game from reaching Vintage-E's start-up step, so no values get mapped), failed JSON patches and recipes naming items that don't exist, each with what it means for the values and what to do. It is the first thing shown when there are no values at all.
- **Doctor:** reports when Vintage-E's own start-up steps crashed, when the mapping had to start late, and when another mod has taken the /emc command.
- **Doctor:** if the server doesn't answer in 12 seconds, the window says so (Vintage-E probably isn't running on the server) instead of waiting forever.

### Fixed
- **Start-up is harder to break from outside.** Each of Vintage-E's start-up steps runs on its own, so one failing no longer takes the rest down. If another mod crashes in the game's start-up step before the game reaches Vintage-E, the mapping now runs on the first game tick or when the first player joins instead of never, and the log and the doctor say why. The whole mapping is caught (not just the solver), so a crash in the value files, the cache, the stack data or the snapshot is reported as a mapping error rather than escaping into the game's start-up.

## 1.16.2 — 2026-11-03

### Fixed
- **Doctor window:** cards really open and close now, and the Passed/Warnings/Failed chips really filter. The drawn card background was marking every click on it as handled, so the click never reached the card header underneath.

## 1.16.1 — 2026-11-03

### Fixed
- **Doctor window:** cards open and close again (the window now rebuilds itself a frame after the click, not during it); the check's text no longer runs into its status dot; the header is plain text that opens the card when clicked anywhere on it.

### Added
- **Doctor window:** click the Passed, Warnings or Failed chip at the top to hide or show the checks of that kind.

### Changed
- **Doctor window:** no Close button at the bottom; the title bar's X (or Ctrl+H) closes it.

## 1.16.0 — 2026-11-03

### Changed
- **The EMC doctor has its own window.** A checklist instead of a wall of text: how many checks passed, need a look or failed, "Run checks again" and a progress bar at the top; one card per check with a coloured bar and dot, the check in words and an OK / Look / Problem badge; a card opens to show what was found, what to do, and buttons for the fixes and the reports it links to (green ⚒ for things that change the server). Sections and the questions sit between the cards; clicking an item opens its value breakdown in the text window. Back, Copy and Close as before. Ctrl+H opens and closes it.

## 1.15.9 — 2026-11-03

### Fixed
- **The doctor's check lines showed orange whatever they said.** Lines that can be expanded are links, and the game paints every link in its own orange; the meant colour is now kept, so a passed check (✓) is green, a problem (⚠) red-orange, item links and the questions at the bottom blue, and fixes green.

## 1.15.8 — 2026-11-03

### Added
- **Ctrl+H opens the EMC doctor** (`/emc diagnose`; admins), and closes it again when its window is on top. The key can be changed in the game's Controls under "Vintage-E: open the EMC doctor". Ctrl is part of the binding, so H on its own still opens the handbook. (Written for 1.15.5, but that release went out from another branch without it.)

## 1.15.7 — 2026-11-03

### Changed
- `modinfo.json` gives the mod's current GitHub address (github.com/Comp-any/Vintage-E) as its website instead of the old demonic69 one, so tools that read it (such as Vintage Auto Update) go straight to its releases. Nothing else changes.

## 1.15.6 — 2026-11-03

### Added
- **`/emc diagnose troubleshoot`** (also `novalue`, `help`, and the first question in the window): the seven-step walkthrough for "an item shows no EMC value", using what's known already (your game's table, the item you hold) and linking each step to the report or fix that does it. The README has the same list.

## 1.15.5 — 2026-11-03

### Changed
- **The check-up is a list of checks.** Each test is one line in the window (✓ passed, ⚠ found something, • a fact), and clicking it shows what it found and the fix; **Expand all** opens them all. The headers say the result in a few words ("Mod coverage: 4 mods have a value for less than three quarters of their content") and the explanations sit behind them. The file and the console still show everything.
- **A shorter bug report.** Results and facts only: no advice lines, no explanations, the mod list wrapped instead of one per line, one line per patching mod and per value out of line, the log without its plain-words meanings, and only the settings that differ from the defaults (a fresh install's report is a third of the length).

## 1.15.4 — 2026-11-03

### Fixed
- **Values driven to nothing by a loop that makes more than it takes.** With a mod that lets a copper shingle block be taken apart into 15 plates (the base game makes 4 blocks from 3 plates), every round of the mapping found copper plates cheaper, past the point where the loop guard looks, until they settled at a millionth - shown as 0 - and copper bits, sheets and blocks followed. A recipe that would push a value below the floor now counts as a loop, as does a value falling a thousandfold from its first derivation, so plates keep the value their ingot gives them. Found by the 50-mod server test.

### Changed
- The server test can run with the Mod DB's most-downloaded content mods (`topMods` input; `tools/ci_top_mods.py` leaves out libraries and APIs and adds the libraries those mods may need).

## 1.15.3 — 2026-11-02

### Added
- **`/emc diagnose trace <code>` (or hold it):** a value's whole chain, each item with where its value came from and, under it, the ingredients of the recipe that made it, down to the raw materials it rests on; the weak links (values worked out backwards from a recipe with several ingredients) are marked, with a suggested pin for each.
- **`/emc diagnose short`:** only the ⚠ lines and their fixes, for the console.
- **Apply all safe fixes** in one click: dead settings entries removed, disabled compatibility files switched on, the EMC table resent.
- **The check-up runs after every mapping** (`CheckupOnStart`, on by default): the server log says how many things are worth fixing, and admins who join are told too, when there are any.
- **Your game's log** travels with each click in the window: Vintage-E errors and warnings from `client-main.log` show in the "Your game" section.
- **What changed** now also says which mods were added, removed or updated between the two mappings (the mod list is kept with each snapshot).
- **Deeper checks** (`/emc diagnose checks`, summarised in the check-up): recipes that give more EMC than they cost (craft-and-burn for free; `profit` lists them all with the ingredient whose value is a guess or trader price), blacklisted ingredients that are the only thing a missing recipe waits for, containers in item-price mode that give their contents away (one click switches them), tool and armour families whose metal tiers are out of order, mapping time and frequency (a slow or repeated remap), the EMC table's size on the wire, tooltips that threw in your game (counted by item family), every skipped recipe with its reason (`skipped`), compatibility files that drifted from their mods (dead entries, new raw materials, with a draft to update), and values defined twice (two files, pin and base value, blacklist and allow). `/emc diagnose baseline` maps twice more to tell what your settings change from what your mods change against a fresh install.
- **Pins teach:** when a pin replaces a value that had been worked out backwards from a recipe, copied from a variant or taken from a compatibility file, the outcome says so, how many other items are valued the same way, and where the general fix belongs (the mapper, `neverAcross`, or the compatibility file), so the same problem isn't pinned item by item.

## 1.15.2 — 2026-11-02

### Added
- **`/emc diagnose watch`:** every refusal the transmutation table or tablet makes is kept (the last 300, always on), and this lists the recent ones grouped by reason - no value, no-transmute, contents, not learned, not enough EMC, too many exact versions... - each with what it means, which items and players, and the fix (pin, allow, learn, change the setting). Linked from the "won't burn or create" symptoms.
- **Suggested pins:** where the diagnosis says "give it a value", it now suggests one - the middle value of the item's valued variants, or of related items of the same mod - as a one-click pin, saying where the number comes from. A mod's report can pin every missing item that has a suggestion at once.
- **`/emc diagnose draftcompat <mod id>`:** writes a compatibility file for a mod whose raw materials have no value, `ModData/vintagee/vintagee-emc-compat-<modid>.json`: `requiresMod`, every raw material (a wildcard where a whole family is missing) with a suggested value and a `//` line saying where it came from, ready to adjust and post on the Mod DB page so it can ship with Vintage-E. The same values can be pinned on the server right away.

## 1.15.1 — 2026-11-02

### Added
- **`/emc diagnose` sees much more, and can fix what it finds.**
  - **Your game:** every click in the window carries your game's Vintage-E version, its copy of the EMC table (count and fingerprint), whether the look patches applied and whether the held item has the value tooltip. A version mismatch, a missing or stale table (with a one-click **Resend**) and a missing tooltip are each reported with their fix.
  - **Server log:** the Vintage-E errors and warnings in `server-main.log`, each with what it means in plain words.
  - **Other mods' patches:** which mods patch recipes, item processing (ground storage, squeezing, burning, drying), `emcValue` or Vintage-E's own files, and which ship their own value files.
  - **Values out of line:** items worked out backwards or guessed that sit more than 8× from the middle of their family, with the recipe that pulled them there.
  - **A mapping without other mods** (`/emc diagnose clean`): maps once more with only the base game's and Vintage-E's recipes and lists every base-game value that differs, grouped by the mod it comes through.
  - **Players** (`/emc diagnose players`, `player <name>`): EMC, items learned against the exact-knowledge limit, game mode, privilege, and whether they've learned what you hold.
  - **One-click fixes** (green ⚒ links): allow, unpin, remove a dead settings entry, take off the gray or no-transmute list, switch a compatibility file on, change a setting, map afresh, resend the table, learn the held item, or open the value editor. Each does its work and shows the report you were on again, with what happened at the top.
  - **Bug report** (`/emc diagnose bugreport`): versions, the mod list, the check-up, other mods' patches, outliers, the log lines and your settings in one text, saved to `ModData/vintagee/bug-report.txt` and ready to copy, with addresses, player ids and user folders taken out.
  - **Symptoms** (`/emc diagnose symptom`): ten sentences ("the tooltip shows a value but the table won't burn it", "it worked, then after a restart it didn't", "it works for another player but not me"...), each running just the checks for that situation.
  - **What changed** (`/emc diagnose changes`): the previous mapping's values are kept (`emc-snapshot.json`, also across restarts) and compared with the current ones: what gained, lost or changed a value, by family, with each value's origin.
  - **Next steps:** when a mod's items are missing because nothing the mapper reads makes them, the report says to post a comment on Vintage-E's Mod DB page asking for them to be covered, with the text and item codes to paste, how the other mod's author can help (`emcValue` attributes), and what to do meanwhile.

## 1.15.0 — 2026-11-02

### Added
- **`/emc diagnose`: finds out why EMC isn't working and says what to do.** It opens a window with a check-up of the whole mapping (is it mapped at all, did the last mapping fail and why, is the settings file readable, settings that switch most of the mapping off, settings entries that match no block or item - with the code they probably meant - unreadable custom recipes, compatibility files switched off, mods with poor coverage, value loops, the missing items by family and by reason) and then asks what you're seeing. Each answer is a link that opens a report for just that: **the block or item you hold** (blacklisted and by what, pinned to 0, gray-listed, nothing makes it, waiting on which ingredients - each with its own 'why?' - in a loop, other variants that do have values, settings entries that nearly match its code, and for an item that does have a value, why the exact stack you hold is worth something else: wear, contents without a value, a material without a value, Klein star contents, no-transmute, not yet learned), **everything from one mod** (its coverage, the compatibility file for it and its state, how many items nothing makes, how many wait on an ingredient and which ingredients hold back the most items, the loops, and every missing item with a 'why?' link), **values that come and go** (every reason a value can differ between stacks or players, how this server is set for each, and a comparison of the stack you hold with the plain item), or **every mapping check**. Every problem is marked ⚠ and followed by a → line saying how to fix it, with the exact code or command to use. Back returns to the previous report, Refresh asks again, Copy puts the text on the clipboard; the server also writes it to `ModData/vintagee/emc-diagnose.txt`. From the console: `/emc diagnose [hand|<code>|<mod id>|mods|sporadic|mapping]`, with the answers written as the commands that open them. Admins only. `/emc diag`, `/emc doctor` and `/emc selftest` are the same command.

## 1.14.1 — 2026-11-02

### Fixed
- **The New look's blocks and items were invisible** (only their outline showed). A player's game loads block and item shapes as soon as it receives them from the server, before mods get their usual chance to change things, so the New models were never loaded. The look is now applied right as the game loads those shapes. Middle and Original (textures only) weren't affected.

## 1.14.0 — 2026-11-02

### Changed
- **Each player picks their own look.** Looks are now applied on each player's own computer: open **`.vintagee`** and pick **New**, **Middle**, **Original** or **the server's default** from the Look drop-down, then leave the world and come back (single player and multiplayer alike; no server restart). The server's `Look` setting is the default for players who haven't picked one, and changing it reaches players the next time they join.
- **New is the default look everywhere**, including worlds that already had Vintage-E, unless the server's `Look` setting or a player's own choice says otherwise. (1.13.0's per-world `Auto` is gone; a config that says `Auto` means New.)
- **Outlines fit the models**: in the New look, a block's selection outline is sized to its model (the torch, chest, pedestals, table and fire pits no longer show a box that doesn't match). Collision stays the same in every look, so everyone bumps into the same thing whatever they see.
- **`Look` is a drop-down in the `/emc configure` window**: New, Middle or Original, each with a short explanation. Typed (`/emc configure Look ...`), it now refuses anything that isn't one of them.
- The release zip carries `moddb.json` with the mod's Mod DB page number.

## 1.13.0 — 2026-11-01

### Added
- **A new look, `New`: models that blend in with Vintage Story.** Rings, stones, amulets, gadgets, Klein Stars, the Philosopher's Stone, matter, fuels, covalence dust and the tablet become 3D models, and the transmutation table, pedestals, interdiction torch, matter furnaces, alchemical chest, collectors, relays, condensers and nova catalysts get models of their own, built mostly from the game's own planks, metal plates, polished stone, gems, linen, leather, paper and candles. Crystals glow; alchemical bags are the game's linen; dark and red matter (on tools, armour, blocks, pedestals and furnaces), fuel blocks and covalence dust get new textures in the game's style. See the README for what each looks like.

### Changed
- **New worlds get the New look; worlds that already had Vintage-E keep the flat look (now called Middle).** Each world remembers its look the first time it runs 1.13.0, so nothing changes in a world you're already playing.
- **The `ProjectETextures` switch is now the `Look` setting**, whose default, **`Auto`**, uses each world's own look; **`New`**, **`Middle`** (Vintage-E's own flat textures, the look until now) or **`Original`** (ProjectE's original textures, what `ProjectETextures` did) use that look in every world. A server that had `ProjectETextures` on gets `Original`. Change it with `/emc configure Look New` (or `Middle`, `Original`, `Auto`); it takes effect the next time the server starts.

## 1.12.1 — 2026-11-01

### Fixed
- **Fish with Primitive Survival.** Primitive Survival adds fish eggs to what adult fish are processed into, and the eggs only get a value from its compatibility file, so adult fish (and their taxidermy) were copied from the juvenile's value (a largemouth bass 24 instead of about 118) and 25 live fish had no value at all. Raw and taxidermied fish no longer copy values across ages (`fishraw:age`, `taxidermy:age` in `neverAcross`), and ratio rules run once more after `defaultValues`, so live fish follow their raw fish again.

## 1.12.0 — 2026-11-01

### Added
- **Compatibility values for other mods.** Vintage-E now ships `assets/vintagee/config/vintagee-emc-compat/<modid>.json` files for Primitive Survival (fish, crabs, snakes, worms, trophies, relics), Wildcraft: Trees and Shrubs, Wildcraft: Fruits and Nuts, Butchering (carcasses, blood, offal, sinew, prime meat, ivory), From Golden Combs and Expanded Matter. Each loads only when its mod is loaded, and its values are a **last resort**: a recipe, trader price, ratio rule or variant that values an item always wins, so the files only fill in raw materials nothing makes (anchored on the base game's values: a fish 24, a hide 16–80 by size, a crushed ore one nugget...). Any value file with a `"requiresMod"` key works the same way.
- **The server says when compatibility values are in use**: in its log, and in the chat summary players get when EMC is mapped (admins are pointed at `/emc compat`).
- **`/emc compat`** opens a window of every compatibility file: whether its mod is loaded, whether it's disabled, how many items it valued, and a Disable/Enable switch that remaps. `/emc compat disable|enable <name>` does the same from the console, and the files to leave out are the new `DisabledCompat` setting (`/emc configure`).

### Changed
- `defaultValues` and `relativeValues` now record which file valued each item, and `litreValues` work in last-resort files too.

## 1.11.0 — 2026-10-23

### Added
- **A Learn slot in the transmutation table and tablet**, next to Burn: drop an item on it to learn it and keep it, like the slots around ProjectE's circle. (Burning gives an item's full value back, so learning without burning costs nothing extra either way.)
- **`/emc learn hand`** (or just `/emc learn`) learns the held item, for everyone now: anything the table could burn. Admins can still learn anything.
- **Hoppers and chutes feed the transmutation table.** Point one into a table and it burns what comes in into the table owner's EMC and teaches it, up to 16 items a second while the owner is online (otherwise the items wait in the table). The owner is whoever placed it; a table placed before this version belongs to the next person to open it. Looking at the table says whose it is and what's waiting. It only takes things it can burn, and nothing comes back out of it.

### Changed
- **Clothes are worth their condition**: a coat at 40% condition is worth 40% of a new one (with `DurabilityScalesEmc`, like tools' durability). Clothes made in the table come out new (100%) instead of getting the game's random worn condition. `/emc dump hand` shows the condition step.
- **With `ProtectContainersWithContents` off, burning a container also gives what's inside** (it used to give only the empty container, and the contents were lost). The table, condensers and the hopper feed all count it.
- **EMC numbers show up to two decimals** (`1,234.56`), cut off rather than rounded, so a total never shows more than you have. Fractions were always kept; numbers of 100 and up just didn't show them.
- **The transmutation table is a thin slab on the floor**, like ProjectE's, instead of a table with legs.
- **Thatch has a value** of its own (0.5, like dry grass), so it's valued whatever else a server's mods do to thatch roofing.

### Fixed
- **The philosopher's stone is no longer used up making a transmutation table** (it already stayed in every other recipe).
- When the table says something has no EMC value but your game showed one, the server now writes what it has for that item to its log and sends you its values again, in case yours were out of date. The message names the item's code too.

## 1.10.1 — 2026-10-22

### Fixed
- **The flying wind no longer plays when another mod makes you fly** (an angel belt, say). With `ProjectESounds` on, ProjectE's wind loop now only plays while Swiftwolf's Rending Gale is what's keeping you in the air.

## 1.10.0 — 2026-10-20

### Added
- **`/emc dump hand` opens a window** that explains everything that makes up the held item's value, step by step with the numbers:
  - what the one you hold is worth: a Klein Star's stored EMC, what a container holds or what an item is made of (and its contents mode), its **durability** (e.g. `Durability 120/240: worth 50% of new: 64 x 50% = 32`, or that `DurabilityScalesEmc` is off), how many are in the stack, and whether it's a liquid;
  - where the plain item's value comes from: the value file and line, the server config's `BaseValues`/`FixedValues` (set by hand), the item's own mod, a trader price, a guess, or the recipe, with each ingredient's value and the sum (the cheapest of several that fit, free ingredients, the drying-time bonus from `TransitionEmcPerDay`, what's given back, how many it makes). Values worked out backwards or shared out between several products say so, with their sums;
  - the other ways of making it and what each would cost;
  - for something with no value, every way of making it and which ingredients have none (two levels down), and the value loop it's stuck in;
  - whether it's on the blacklist, the gray list or the no-transmute list, and whether you know it.
- Click any block or item in the window to see its own value explained (**Back** returns). **Find** keeps the sections that mention what you type. **Copy** puts the text on the clipboard, **Set value** opens the Ctrl + K box for it, **Handbook** opens its page and **Refresh** asks again after you change something. The same text is still written to `ModData/vintagee/emc-dump-<item>.txt`.
- **`/emc dump <code>`** explains any block or item without holding it (`/emc dump game:ingot-copper`).
- **The Ctrl + K box has a "Why this value?" button**, which opens the window for exactly what's under the mouse (its wear and contents count).
- **`/emc loop` opens a window too**: each value loop with its items as icons you can click, which recipe makes each from the next for short loops, and the settled loops with the values they kept. Find shows only the loops with the item you type. If the values came from the cache, they're worked out again first (the cache doesn't keep the recipes the loops are found from).

## 1.9.3 — 2026-10-20

### Fixed
- **`/emc loop` showed nothing in chat.** Chat reads messages as the game's text markup, where the `->` between items (and the `<value>`-style placeholders in `/emc help` and `/emc configure`) look like broken tags, so the whole answer was dropped. Every `/emc` answer is now escaped for chat. `/emc loop` and `/emc dump hand` also send their answer a line at a time (a long loop is shortened to its first items, `... (12 more)`), and the full text is still written to the file named at the end.

## 1.9.2 — 2026-10-18

### Fixed
- **Rings really go in the hand slot now** (and Klein Stars in the emblem slot). Clothing slots only take items marked as wearable, which they weren't, so the slot turned them away even though they were meant for it.
- **Swiftwolf's Rending Gale in shield-only mode no longer speeds you up.** The speed you pick is for flying (and running) with flight on.
- **The shield keeps creatures further away**: 8 blocks instead of 5, and it pushes ten times a second instead of twice, cancelling any step towards you, so melee creatures can't reach you between pushes. The Interdiction Torch pushes the same way.
- **The Ignition Ring's fireball sets things alight.** Creatures it hits burn their full time, things that can be lit (firepits, torches, bombs, nova explosives) are lit as if you held a torch to them, and wood and anything else that burns catches fire, as with the game's own fire. Land claims are respected.
- **Rings and other items on a pedestal stand upright and face you**, swaying a little, instead of spinning at an odd angle.

### Changed
- **Divining Rods look deeper**: 16, 32 and 128 blocks (were 3, 16 and 64). **They wear out**, after as many uses as 10x their EMC value (about 90, 230 and 4,350 uses with the default values).
- **The Mercurial Eye has 5x5, 7x7 and 9x9 modes** as well as 1x1 and 3x3 (tool-mode key). The bigger squares only change blocks like the one you clicked.

## 1.9.1 — 2026-10-18

### Added
- **The Ctrl + K box has switches for the blacklist, the gray list and free ingredients**, set to how the item is now. Tick or untick any of them, change the value or not, and press **Save**: the server applies it all and works the values out once. A value is only saved when you change it, so ticking a box doesn't also pin the item's current value. Where a value file (or a wildcard) puts an item on the gray list or makes it a free ingredient, the box says that unticking there can't undo it.

### Changed
- **Lore paper is blacklisted** again, with the rest of the lore.
- `/emc configure` shows long lists and tables 100 lines at a time, with **<** and **>** to page through them and a **Find** box that shows only the lines containing what you type. Edits on each page are kept until you save.

### Fixed
- **The `FixedValues` page of `/emc configure` was a black box** once it held a few hundred values: the editor drew the whole table into one picture, taller than the graphics card allows. It now only holds a page at a time.
- The note on free ingredients in the value file said the illuminal divergence manifold only comes from ruins. It's a story reward: Tobias gives it to you after you bring him the lens from the Devastation, along with the blueprint.

## 1.9.0 — 2026-10-17

### Added
- **Set a value from anywhere (admins).** Hover a block or item and press **Ctrl + K**: a small box shows its value and lets you set one (saved like `/emc set`) or **Unpin** it again. It works on inventory and container slots, the handbook (item pictures on a page, or the page's own item), any other window, the block you're looking at, and the held item. Change the key in the game's controls.
- **Hand-written recipes.** `/emc recipe` opens a window to tell the mapper about things it can't see being made, like other mods' boats: put the result in the first slot and its ingredients in the others by clicking them with an item held on the mouse (pick a slot to change its amount), or edit the recipe line by hand, wildcards included (`game:plank-*`). The server checks each recipe against the mapper, saves it to the new `CustomRecipes` setting and works out the values again; recipes it can't use are refused with the reason. Saved recipes can be edited or removed there, or with `/emc recipe add|remove|list` from chat. Value files can carry them too (`recipes`).
- **Constructions are found by themselves.** Things built in stages (like sailboats on rollers) from other mods, such as Shipwright's, are matched to the item they make by name, so they get a value without a `constructions` rule.
- **Gray list.** For blocks/items nothing makes yet, but that a mod might later (uncraftable blocks, say): gray-listed ones are left out of `/emc missing` and never given a guessed value, but get one as soon as a recipe makes them. `/emc graylist` opens its window (select, take off, or untick **Hide missing** to add missing ones), `/emc graylist add|remove` does the held item, and the missing window has a **Gray-list it** button. Saved as `GrayList`; value files can have `grayList`.
- **Blacklist only a mod's missing items.** With a mod picked in the `/emc missing` filter, **Blacklist its missing** blacklists just that mod's missing blocks/items by their own codes, instead of the whole mod with a wildcard, so the values its other items already have stay.
- **Watch of Flowing Time indicator.** While the watch is on, an icon on screen shows how fast time runs (`Time x10`), and the watch glows. Type `.vintagee` in chat for this computer's settings: turn either off, and set the icon's size and position and the glow's colour.
- **Nutrition from the Body and Life Stones.** A new tool mode feeds you through the game's hunger, so your food groups fill up too (the emptiest first), for 1 EMC per saturation instead of 0.5. A stone on a pedestal follows its mode.
- **Bigger Alchemical Bags.** Upgrade an Alchemical Bag with 4 dark matter for a 48-slot Dark Matter Alchemical Bag, and that with 4 red matter for a 64-slot Red Matter Alchemical Bag. Upgrading keeps what's in the bag.
- Pedestals show the item on them floating and turning above them.
- Energy Condensers show how much EMC a second their collectors and relays are giving them.

### Changed
- **Shorter tooltips.** Vintage-E's items keep a one-line description in their tooltip; the full details are on their handbook page, under **How it works**.
- **Rings go in the hand slot**, with gloves and other mods' rings, instead of the arm slot. Rings already worn on the arm keep working there.
- **Nova Catalyst and Cataclysm are lit like the game's bombs**: hold right-click on them with a torch or a firestarter. Sneak + right-click with an empty hand no longer lights them.
- The **illuminal divergence manifold** (translocator part) is a free ingredient, so the rickety translocator gets a value.
- Tooltips of lenses, amulets and the Ignition Ring name the key the fire-projectile control is bound to now, instead of always saying R.
- The Destruction Catalyst's and Catalytic Lens's charge labels show what the tunnel costs at most, so the Catalytic Lens's charges 3–7 no longer look the same. (Its shot stops growing at charge 3, the Hyperkinetic Lens's biggest; the tunnel keeps getting deeper.)
- In the `/emc missing`, `/emc blacklist`, `/emc notransmute`, `/emc graylist` and `/emc contents` windows, picks are kept when you change the filters; the selection line now says how many of them the filters are hiding, and the button that drops them all is labelled **Clear selection** (it was **Clear**).
- The `/emc configure` editor scrolls to keep the line you're typing on in view.

### Fixed
- **Full gem armour (and Swiftwolf's Rending Gale) stops fall damage again**: Vintage-E's protection now gets its turn before the game takes the damage off. The same goes for fire protection.
- **Evertide and Volcanite Amulets on a pedestal change the weather.** The game's weather command needs a player to run it, so it did nothing; the pedestal now sets the rain directly. With several pedestals, rain wins; taking the amulet off (or the pedestal unloading) gives the weather back to the game.
- **Energy Condensers fed by collectors and relays** show and keep the EMC they're given; before, their window and info only caught up when they made something.
- Archangel's Smite arrows look like arrows and point where they fly; lens, fire, water and lava shots show their whole picture instead of one corner of it.
- The cooked item on a Dark/Red Matter Furnace no longer jumps up for a moment when cooking finishes.
- The tablet's magnet filter text no longer runs into the filter slots.

## 1.8.0 — 2026-10-17

### Added
- **Magnet for the Transmutation Tablet.** The tablet window has a magnet slot: put a Black Hole Band or Void Ring in it and, while you carry the tablet, items on the ground within 7 blocks that pass its filter are pulled in and transmuted into your EMC (and learned), like burning them at the table. The filter is a row of 9 slots: **Pull: only these** (an empty filter pulls nothing) or **Pull: all except these**. Click a filter slot while holding an item to add it; click with an empty hand to clear it. Things you just threw away are left alone for a few seconds. Everything is stored on the tablet.
- **Sorting** in the transmutation window: most EMC first, least EMC first, name A to Z, name Z to A.
- **Forgetting asks first.** Ctrl + right-click on a known item opens a confirmation before it's forgotten.
- **Value loops.** The mapper finds rings of unvalued items that are each only made from the next (A needs B, B needs C, C needs A), which is why none of them can get a value. The login message says how many there are, **`/emc loop`** lists each one from its first item (`a -> b -> c -> a`) and writes them to `emc-loops.txt`, and the missing window says when an item is stuck in one. Recipe loops that kept making each other cheaper (and were settled by keeping their first value) are listed too.
- **`/emc dump hand` explains missing values**: every way the mod knows of making the item, which ingredients have no value and why (two levels deep), whether it's blacklisted or a free ingredient, and the value loop it's in. If the values came from the cache, it works them out again first so it can explain them.
- **`/emc notransmute`**: a list of blocks/items that keep their EMC value (recipes still use it) but can't be burned for EMC, learned or created, at the table, in condensers, by the Gem of Eternal Density, the Mercurial Eye or the tablet's magnet. The window works like `/emc blacklist`: tick **Show valued items** to pick some and press **Stop transmuting**; select listed ones and press **Allow transmuting**. `/emc notransmute add|remove` does the held item. Saved as the `NoTransmute` setting; such items say so in their tooltip.
- **Free ingredients**: blacklisted (no value, can't be burned or created) but free in recipes that use them. The customized translocator schematic is one by default. Add more with the `FreeIngredients` setting or a value file's `freeIngredients`.
- **Red Matter Pedestal**: twice the reach and twice the effect of the Dark Matter Pedestal (crafted from one, with red matter and red matter blocks).
- **Reset settings to their defaults**: in the `/emc configure` window, every changed setting has a **reset** link on its row and the selected one has **Reset to default**; **Reset all** puts every setting back (both ask first). From chat: `/emc configure <setting> default` and `/emc configure resetall`.
- **Shift-click** in the `/emc missing`, `/emc blacklist`, `/emc notransmute` and `/emc contents` windows selects everything between the last name you clicked and this one.

### Changed
- The bone arrow is worth the same as the flint arrow, whatever that works out to.
- The rickety translocator and the illuminal divergence manifold are off the built-in blacklist.
- The **Tome of Knowledge** is creative-only: it has no recipe and no EMC value, only works in creative mode, and only teaches what's in the creative inventory and can be transmuted.
- **The transmutation window is much faster** with many known items: each known item is looked up once and kept, so opening the window and typing in the search box only filter and sort. The server also checks knowledge with a lookup set instead of searching the whole list.
- `/emc dump`, `/emc dump hand` and `/emc loop` say in chat where their file is. All files (CSV lists, dumps, loops, the self-test and the cache) now go to `VintagestoryData/ModData/vintagee/` as documented; before, they went to `VintagestoryData/vintagee/`, which you can delete.

### Fixed
- The `/emc missing` and `/emc blacklist` windows no longer crowd or overlap: the summary is shorter and every text is measured, so the filters, buttons and hints each get their own row whatever the text length or GUI scale. The same for the `/emc contents` and `/emc configure` windows.
- `/emc configure` no longer jumps back to the top of the list when you click a setting or save.
- Items cooked on a spit no longer float above the Dark/Red Matter Furnaces: they lie on the furnace (it has no spit).

## 1.7.0 — 2026-10-16

### Added — more ways to work out EMC values
- **Placement states are worth exactly what they turn back into.** A block that only exists placed (facing another way, snow-covered, a fence piece joined to its neighbours, an open gate) gets the value of the item you get when you pick or break it, instead of a guess.
- **Smarter variant guesses.** Values are now copied using the variant groups each block/item type defines. They are copied freely across groups that only change the look or placement (rock, colour, wood, facing, snow cover...). They are never copied across groups that change what something is: ore, metal, mold type, and families like ores, crystals, clutter, boats and gadgets. Before, cinnabar was priced like gold-bearing quartz, platinum ingots like gold, and a black axe mold like an anvil mold. Things that can't be valued honestly now show in `/emc missing` instead. Groups in neither list (clothes types, for example) still get a guess, now the middle value of the variants rather than the highest.
- **Other mods' variants of base-game families** (their own wood types for the game's logs, say) borrow the base game's value when nothing else values them.
- **Fuel and food estimates** for anything nothing else can value. Fuels are valued by how hot and how long they burn, at the rate charcoal and coal are valued. Food is valued by how filling it is, at the rate of valued food of the same kind. Turn it off with `EstimateFuelAndFood`.
- **Values are worked out in stages.** The recipe graph is solved again after each step (ratios and trader prices, look/placement variants, family defaults, other variants, fuel/food), so anything made from newly valued things gets its real recipe value before a rougher step guesses at it. This fixed the Destruction Catalyst and the Hyperkinetic and Catalytic Lenses, which were guessed from the Tome of Knowledge (1,458,679 EMC) instead of costed from their recipes.
- **Beehive kiln firing** is read. Brown, gray, cream, orange and tan shingles, bricks and pottery come out of the kiln. Before, they were priced from trader sales or copied from another colour (a brown clay roof was 6x a black one, tiles 4x what their clay costs). Now they all cost the clay they're made of.
- **Things built in stages**: sailboats are built on rollers from logs, firewood, about 300 planks, support beams, rope and linen. They're now worth what those stages take (an oak sailboat is about 12,700 EMC). Other mods' stage-built things can be added with a `constructions` line in the value file.
- **Carburizing** (iron to blister steel in the stone coffin) is read, for mods that add carburizable items.
- **Crucibles of molten metal** count their metal (100 units = one ingot) in item + contents mode.
- **Cheese slices** are worth their share of the wheel. Before, 2- and 3-slice cheeses were worth a whole wheel.
- **Mineral defaults** for fluorite, kernite, phosphorite, corundum and graphite, and thatch's value for aged thatch roofs. Before, those ores were copied from anthracite and the roofs had none.
- Ratio rules are applied again after trader prices, and fuel/food estimates only apply to real inventory items (not a growing crop or a pit kiln).
- **Why something has no value**: `/emc missing` shows a reason under each entry (nothing makes it, or which ingredient has no value), and `emc-missing.csv` has a `why` column. In the base game only 19 items are left, and none of them can be crafted. They are the Erel and bone arrows, the Erel spear, four boss-themed figureheads, platinum, uranium and stainless steel items, cupronickel nails, crushed peridotite and the translocator schematic.
- `/emc dump` says which step valued each item, and why a guess was made (`differs in: rock`).
- **Show blacklisted / Hide missing.** The `/emc missing` window has a **Show blacklisted** switch that mixes everything on the blacklist into the list, tagged in red, with a **Remove from blacklist** button for them. The `/emc blacklist` window has the mirror: untick **Hide missing** to mix in everything that has no value, tagged in amber with the reason, and blacklist them from there. Both switches are remembered until you quit.

### Added — items that hold things or keep their material
- **Three modes** for every block/item that holds things (buckets, jugs, pots, bowls, crocks, barrels, bags) or keeps what it's made of on the item (round shields, lanterns, chests, clutter, chiseled blocks):
  - **Item only:** burns and is created as the plain item. A container that still holds something won't burn.
  - **Item + contents:** worth the item plus what it holds or is made of, and you learn and create exactly that version.
  - **Item price only:** exactly that version, but it costs and burns for the item alone; its contents are free.
- **Built-in defaults** only where they make sense. Containers are item only. Items whose material or type lives on the item, and chiseled blocks, are item + contents, because a plain copy of those would be the wrong or default version.
- **Each material is its own value.** A round shield ranges from 289 (tin bronze) to 4,104 (gold). Chiseled blocks are worth their voxels of each material. Containers in item + contents mode add what they hold (meals and pies times their servings).
- **Copies come out new:** no wear, freshness or clothing condition is copied. Condensers follow the same modes for their target.
- **`/emc contents`** opens a window listing every such block/item with its kind and mode, where it came from (default or set by you), search and filters by mod, kind and mode. Select several and switch them all to item only, item + contents, item price only, or back to the default. `/emc contents <item|full|itemprice|default>` sets the held item. Saved to `StackDataModes`; applies at once, no remap.
- The transmutation grid shows each exact version as its own entry. **Ctrl + right-click forgets an entry.** Each player can know up to `MaxExactKnowledge` (500) exact versions.
- `/emc dump` also writes `emc-contents.csv`: every such item, its mode and its material values.

### Added — ProjectE's original textures and sounds
- **`ProjectETextures`** (off by default) swaps Vintage-E's textures for ProjectE's wherever ProjectE has one: rings, stones, amulets, gadgets, matter, fuels, covalence dust, alchemical bags, blocks, machines, the transmutation table, nova catalysts, the interdiction torch, projectiles, and the matter look of tools, armour, the pedestal and the furnaces. Rings and stones light up while switched on, and the Ring of Arcana and Swiftwolf's Rending Gale show their mode, like in ProjectE. Condensers and the alchemical chest use faces cut from ProjectE's chest textures. It's applied by the server at start-up, so players see it without setting anything.
- **`ProjectESounds`** (off by default) plays ProjectE's eight sounds for what ProjectE plays them for: switching rings on (heal) and off (uncharge), lens and ring shots (power), the Destruction Catalyst and area mining (destruct), the Philosopher's Stone and lava (transmute), water (water magic), stones and pedestals healing (heal), putting a ring on a pedestal (charge) and taking it off (uncharge), and a looping wind while flying with Swiftwolf's Rending Gale. It applies straight away.
- Both are in `/emc configure`. The files are ProjectE's, under its MIT licence (see THIRD-PARTY-NOTICES.md), copied by `tools/import_projecte_assets.py`.

### Added — settings in game
- **`/emc configure`** opens a settings window: every setting, what it does, and an editor that fits it (a switch, a number box, or one entry per line for lists and tables). Save checks the value, writes `ModConfig/vintagee.json` and applies it straight away; settings that change values remap them.
- From chat: `/emc configure list`, `/emc configure <setting>`, `/emc configure <setting> <value>`, list `add`/`remove`/`clear`, table `set <key> <value>`/`remove <key>`/`clear`, and `/emc configure reload` after editing the file by hand. `/emc config` works too.
- Admins are told about `/emc configure` when they join.

### Changed
- **The Repair Talisman also restores clothing condition**, on clothes you wear or carry and for players near it on a pedestal: 1% a minute by default. `RepairTalismanRepairsClothing`, `RepairTalismanClothingPercentPerMinute`, `RepairTalismanClothingMinCondition` and `RepairTalismanClothingMaxCondition` turn it off, set the speed, leave badly worn clothes for a sewing kit, and set where it stops.

## 1.6.0 — 2026-10-14

### Added
- **Wear your Klein Star and rings**: a Klein Star goes in the **emblem** slot and shows on your chest; the Black Hole Band, Swiftwolf's Rending Gale, Harvest Goddess Band, Ignition Ring, Zero Ring, Ring of Arcana and Void Ring go in the **arm** slot and show as a band with its gem on your wrist. Worn, they keep working: rings keep their effects and a worn star's EMC still pays for rings and gadgets. Switch a ring on or off before you put it on (right-click still toggles it in your hand).
- **Swiftwolf's Rending Gale speed**: the tool-mode key now also picks 1x, 1.5x, 2x or 3x speed, for running and flying. Flying costs 6.4 EMC a second x your speed; running faster than normal costs 6.4 EMC a second x (speed - 1) while you move.
- **Select several entries** in the `/emc missing` and `/emc blacklist` windows: click names to tick them, or **Select shown** for everything the search and filters show, then give them all one value, blacklist them or take them off the blacklist in one go.
- **Blacklist a whole mod** from those windows: pick the mod in the mod filter and press **Blacklist whole mod** (saved as `modid:*`; **Unblacklist whole mod** undoes it). The base game can't be done as a whole.
- **`/emc dump hand`**: the held item's value, how it was worked out, and what each of its ingredients is worth, in chat and in `ModData/vintagee/emc-dump-<item>.txt`.
- `emc-values.csv` has a name column, and `/emc dump` also writes `emc-blacklisted.csv`.
- **Recipes**: 4 emeralds into 1 diamond and 1 diamond into 4 emeralds with the philosopher's stone, like iron and gold.
- **Values**: live fish (the same as the raw fish), Jonas parts 1,024, Jonas sub-assemblies 2,048, the night vision mask 4,096 and its upgrades 1,024, loose paper found in ruins (the same as parchment), metal parts and metal scraps 64 if nothing else values them, and shields from any mod 256 if nothing else values them.

### Changed
- **Matter and gem armour are silent**: no more metal clanking when you walk or run in them.
- **Creatures and NPCs from every mod, and maps from every mod**, are blacklisted (before, only the base game's). They're creative-only. Live butterflies and fish stay valued, since you can catch them.

## 1.5.0 — 2026-10-14

### Changed — closer to ProjectE
- **Matter furnaces now work like the game's firepit**: same window, fuel slot, cooking pots, crucibles and spits, lit with a firestarter or torch. The Dark Matter Furnace burns fuel 1.4x hotter, makes it last 3x as long and cooks 4x faster; the Red Matter Furnace 2x hotter, 4x as long, 12x faster. Collectors and relays next to one can keep it burning on EMC (2 or 4 EMC a second). Furnaces placed in 1.4.0 turn into the new ones by themselves; anything that was in them drops on top.
- **Hyperkinetic and Catalytic Lenses** shoot an explosive charge that flies straight and blows up where it hits, like ProjectE's. Pick the charge with the tool-mode key: radius 4, 6, 8 or 10 for 384, 768, 1,536 or 2,304 EMC. The Hyperkinetic Lens shoots on right-click; both shoot with the new **fire-projectile key (R)**, which you can rebind in the controls.
- The **Catalytic Lens** and **Destruction Catalyst** dig ProjectE's 3x3 tunnels: 1, 4, 8 or 16 deep for the catalyst, 1 and then 16 to 64 for the lens, 8 EMC a block. What they break drops where you clicked.
- **Klein Stars** use ProjectE's own artwork.
- **Swiftwolf's Rending Gale lets you fly** (jump to rise, sneak to sink) and has ProjectE's shield, which pushes hostile creatures and arrows away within 5 blocks. Pick flight + shield, flight or shield with the tool-mode key. Flying and the shield each cost 6.4 EMC a second, the ring switches off when you run out, and falling doesn't hurt while it's on or for 15 seconds after you stop flying. The 40% run speed bonus is gone.
- **Ring of Arcana**: switch each of its four effects on or off on its own with the tool-mode key, instead of only one at a time. It's free, like ProjectE's, and fire can't hurt you while you carry it.
- **Ignition Ring** and **Volcanite Amulet**: fire can't hurt you while you carry them, on or off.
- **Harvest Goddess Band** also harvests ripe crops, still grows crops slowly (for free) when switched off, and right-clicking farmland plants the seeds you carry up to 8 blocks around.
- **Black Hole Band**: right-click a water or lava source to remove it; pulls items within 7 blocks.
- **Repair Talisman** works whenever you carry it.
- **Archangel's Smite** fires homing arrows (3 damage each, costing what an arrow is worth): one on right-click, a volley of 10 on left-click.
- **Evertide and Volcanite Amulets** shoot water and lava with the fire-projectile key. The Ignition Ring (and the Ring of Arcana) shoot a fireball.
- **Pedestal**: an Evertide Amulet on it makes it rain, a Volcanite Amulet clears the sky; the Zero Ring also puts out burning creatures, the Harvest Goddess Band also harvests, the Watch also slows hostile creatures, and the Black Hole Band fills containers on any side. It now takes every ring, stone, amulet, talisman and watch.
- The **Watch of Flowing Time** now actually changes the flow of time. While it's on, time runs 10x faster for the whole world (crops, cooking, curing, drying, day and night), as when everyone sleeps, and crops near you grow faster still. The tool-mode key switches it to slow time: days last 3x longer and hostile creatures near you are slowed. It costs EMC every second it's on and stops when you run out. Before, it only grew nearby crops.
- **Interdiction Torch** is brighter: light level 20 (a torch is 14).
- Every block and item description now says what it does with the real numbers: rates, capacities, costs, ranges, temperatures.
- Emeralds are worth a quarter of a diamond (2,048 EMC).

### Added
- **Void Ring** (Black Hole Band + Gem of Eternal Density + 2 red matter): pulls in items, condenses junk, and sneak + right-click teleports you up to 64 blocks.
- **`/emc add <amount> [player]`** and **`/emc remove <amount> [player]`**: change a player's personal EMC (yours if no name is given). `/emc give` still works.
- **`/emc blacklist`**: the same window as `/emc missing`, listing everything on the blacklist. Select something to give it a value or take it off the blacklist. The `/emc missing` window gets a **Blacklist it** button. Changes are saved to the server config (`Blacklist`, and the new `Allow` list for exceptions to the built-in blacklist).
- Settings: `WatchChangesWorldTime` (turn off to keep the watch's effect local, e.g. on busy servers), `WatchSpeedUp` (default 10) and `WatchSlowDown` (default 3).

### Fixed
- **Rendered fat** and anything else cooked into an item in a cooking pot (linseed oil...) now gets its value from the pot recipe. Before it could be valued backwards from something else it's used in (one world had rendered fat at 2,494 EMC).
- The **altar** had no value and didn't show up in `/emc missing`, because it was blacklisted with the story blocks. It now has 64 EMC.

### Credits
- The Klein Star textures are ProjectE's (MIT licence); see THIRD-PARTY-NOTICES.md.

## 1.4.0 — 2026-10-13

### Added — the rest of ProjectE
- **Klein Stars** Ein, Zwei, Drei, Vier, Sphere and Omega (50,000 to 51,200,000 EMC): portable EMC. Right-click to empty one into your EMC, sneak + right-click to fill it. Rings and gadgets draw from stars you carry once your own EMC runs out, combining four stars into the next size keeps their EMC, and sneak + right-clicking a collector or relay with a star charges it.
- **Covalence Dust** (low, medium, high) and the **Iron Band**, used in the recipes below.
- **Rings, stones and amulets**: they work from anywhere in your hotbar or backpack (Vintage Story has no ring slots); right-click switches one on or off.
  - Black Hole Band: pulls loose items to you.
  - Swiftwolf's Rending Gale: run 40% faster, no fall damage.
  - Harvest Goddess Band: crops around you grow faster.
  - Ignition Ring: sets nearby hostile creatures on fire; fire can't hurt you.
  - Zero Ring: freezes water around you.
  - Ring of Arcana: all four of the above, picked with the tool-mode key.
  - Body Stone, Soul Stone and Life Stone: keep you fed and/or heal you using EMC.
  - Repair Talisman: slowly repairs everything you carry and wear.
  - Gem of Eternal Density: turns cheap junk you carry into iron, gold, diamond, dark or red matter.
  - Evertide Amulet and Volcanite Amulet: place water or lava.
  - Watch of Flowing Time: crops in a wide area grow much faster.
- **Gadgets**: Destruction Catalyst (3×3 tunnels), Hyperkinetic Lens and Catalytic Lens (explosions at range), Mercurial Eye (turn blocks into a chosen block), Archangel's Smite (strike a creature up to 48 blocks away), Tome of Knowledge (learn everything), Divining Rods (EMC in the ground below you).
- **Dark Matter Pedestal**: set a ring, stone, amulet or talisman on it and it works on the area around the pedestal for free: grows crops, burns or strikes hostile creatures, freezes water, pulls loose items into a container under it, heals and feeds nearby players, or repairs their gear.
- **Armour**: Dark Matter, Red Matter and Gem helmets, chestplates and leggings, on the game's plate armour models. They never wear out; a full gem set makes you immune to fall and fire damage.
- **Tools**: Dark and Red Matter Shears, the Red Matter Katar (sword, axe and shears) and the Red Matter Morning Star (pickaxe, shovel and sledgehammer).
- **Blocks**:
  - Alchemical Chest (104 slots) and Alchemical Bags (12 colours, 32-slot backpack bags).
  - Alchemical Coal, Mobius Fuel and Aeternalis Fuel blocks.
  - Dark and Red Matter Furnaces: smelt, fire and cook anything (ore straight to ingots), on fuel or on EMC from collectors.
  - Anti-Matter Relays MK1–MK3: store and boost collector EMC and pass it on.
  - Nova Catalyst and Nova Cataclysm explosives.
  - Interdiction Torch: keeps hostile creatures away.
- `/emc help` lists every command with a short description.
- In the `/emc missing` window, admins can click a name, type a value and press **Set value**. The value is saved like `/emc set` and the list refreshes.
- Settings: `HostileCreatures` (what the ignition ring, smite and interdiction torch affect) and `TrinketCostMultiplier`.

### Changed
- Items caught in a recipe loop that never settles now keep the first value they got instead of losing it.

### Not included
- The Mind Stone: Vintage Story has no experience points.
- Separate boots: Vintage Story armour has head, body and legs slots, so the leggings cover the feet.
- ProjectE's furnaces are adapted to how smelting works in Vintage Story, which has no furnaces.

## 1.3.1 — 2026-10-13

### Fixed
- `/emc missing` no longer crashes the game with a null pointer exception. The list now shows item icons inline (the same way the handbook does) and pages through long lists 40 entries at a time.
- The scrollbar in that window was drawn in the wrong place.

## 1.3.0 — 2026-10-13

### Added
- **Chat summary**: when you join (and after `/emc recalc`, `set` or `clear`) chat shows e.g. `Vintage-E version 1.3.0: calculated 13983 EMC values, 0 missing.` Turn it off with `AnnounceInChat`.
- **`/emc missing` window**: a scrollable list of everything without an EMC value, with item icons, a search bar, a mod filter, a blocks/items filter and a button that copies the codes ready to paste into `BaseValues`. Clicking an icon opens its handbook page.
- **3D matter tools**: the dark and red matter pickaxe, shovel, axe, sword and hoe now use the game's own 3D tool models with matter textures, so they're held, swung and displayed like vanilla metal tools (pickaxe swing, axe chop, shovel dig, falx sword swing) and fit on tool racks and the ground.
- **Sledgehammers**: the dark and red matter hammers are now sledgehammers with their own model. They smash stone, ore and other hard blocks 3×3 from the start (the red one can switch to 5×5 with the tool-mode key; sneak for a single block).
- **Trader prices**: things no recipe makes (clothes, loot armour, decorations…) are valued from what traders buy and sell them for. What a rusty gear is worth is measured each start from traded items that already have a value (about 57 EMC in vanilla), and the rusty gear itself is set to that.
- **Working backwards from crafting**: if a recipe's result has a value and only one ingredient doesn't (loot parts, gold jewellery…), that ingredient gets what's left over.
- **Value file sections**: `defaultValues` (only used when nothing else can value an item), `litreValues` (liquids per litre), `traderCurrency` and `allow` (exceptions to the blacklist).
- `/emc dump` (and the new `DumpValuesOnStart` setting) writes what every value was worked out from, e.g. `grid: 1 game:log-placed-oak-ud -> 12 game:plank-oak`.
- Soil and farmland are worth more the more fertile they are (very low 1 → terra preta 16).
- Butterflies (caught and dead) have values.
- Tooltips show liquids per litre, and buckets, bags and pots show what their contents are worth.
- New settings: `AnnounceInChat`, `InferValuesFromCrafting`, `UseTraderPrices`, `DumpValuesOnStart`.
- A "Test on a real server" GitHub workflow that compiles the mod against the real game and checks every value on a fresh 1.22.7 world.

### Changed
- Water is set per litre: 1 L of water = 1 EMC (ale and other drinks follow from their recipes: spelt ale is 45 EMC per litre).
- Coverage on vanilla 1.22.7 is now **100%** of the 8,096 blocks and items in the creative inventory (was about 80%).

### Fixed
- The mod's creative inventory tab had no name.
- Tin bronze, bismuth bronze and black bronze, and everything smithed from them, were worth almost nothing. Smithing now costs the whole ingots you have to put on the anvil, and value loops that creep down are detected and stopped.
- Rusty gears were worth far more than they buy, so selling things for gears and burning the gears made EMC from nothing.
- A few items (bear hide helmets) could lose their value on the second pass of the calculation.

## 1.2.0 — 2026-10-13

### Added
- **Drying time bonus**: drying adds 64 EMC per in-game day an item takes to dry. Other transitions (curing, ripening…) can be given a bonus too with `TransitionEmcPerDay`.
- **Casting**: items cast in tool molds cost the metal poured into them (`requiredUnits`, 100 units = 1 ingot); molds that break when filled add their own cost.
- **Processes with several outputs** share their value fairly: squeezing honeycomb (honey + beeswax), ground-storage processing such as sawing (including anything left behind) and the fruit press (juice + pressed mash).
- `relativeValues` rules in value files. Metal bits are worth 1/20 of the matching ingot, even for metals that can't be smelted.

### Changed
- Flax fibers are worth 4× as much (8 → 32).
- Beeswax and honey now get their values from squeezing honeycomb instead of fixed guesses.
- Smelting data is also read from items that work it out in code.

## 1.1.1 — 2026-10-13

### Fixed
- Barrel liquids (brine, diluted solutions, tannin) and everything made from them, such as leather, were hugely overvalued. Barrel outputs now count the portions the game actually gives.
- Crafting recipes that use a bucket or bowl of liquid now cost the liquid, not the container you get back.

### Changed
- Raw hides follow their leather yield: small 16, medium 32, large 48, huge 80.

## 1.1.0 — 2026-10-13

### Added
- **EMC cache**: values are saved per world and reused on the next start when nothing that affects them has changed (game version, mods, config, value files, recipes, item data). `/emc recalc` always recalculates; `CacheEmcValues: false` turns the cache off.

## 1.0.0 — 2026-10-13

First version (not published as a GitHub release).

- EMC values worked out automatically at server start from every loaded recipe and process, including other mods'.
- Philosopher's Stone, Transmutation Table and Tablet, alchemical fuels, dark and red matter (and blocks), unbreakable matter tools with area mining, Energy Collectors MK1–3 and Energy Condensers MK1–2.
- Base values, blacklist and philosopher's stone rules in data files any mod can extend; server config and `/emc` commands for admins.
