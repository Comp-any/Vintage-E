# Vintage-E

**Equivalent exchange for [Vintage Story](https://www.vintagestory.at/) 1.22**, inspired by the Minecraft mod [ProjectE](https://github.com/sinkillerj/ProjectE).

Burn items into **EMC**, learn them, and transmute your EMC back into anything you've learned. Every block and item gets its EMC value **worked out automatically from the recipes loaded in your game** when the server starts — so items added by other mods get values too, without anyone writing a compatibility patch.

![The New look](docs/preview-new.png)

## What's in it

| | |
|---|---|
| **Philosopher's Stone** | Right-click a block to transmute it: rock types cycle (granite → andesite → basalt …), wood types cycle, sand ↔ gravel, bare soil ↔ grassy soil, water ↔ ice. Sneak to go backwards. Press the tool-mode key (**F**) for 1×1, 3×3 or 5×5. Also the catalyst for the alchemy recipes (never used up). |
| **Transmutation Table / Tablet** | Drop items on the **burn slot** to turn them into EMC and learn them, or on the **learn slot** to learn them and keep them (like the slots around ProjectE's circle). Then click anything you've learned to create it from your EMC (left-click: one, right-click: a stack, shift-click: a stack straight into your inventory, Ctrl + right-click: forget it). Searchable, sortable (most or least EMC first, name A–Z or Z–A), paged. Forgetting asks first. The tablet works anywhere, and has a **magnet slot**: put a Black Hole Band or Void Ring in it, choose items in its filter (only these, or all except these), and while you carry the tablet those items on the ground within 7 blocks are pulled in and transmuted into your EMC. Items that hold things or keep their material on the item can be learned and created as exactly that version (see below). The table is a thin slab, like ProjectE's. A **hopper or chute** pointing into a table feeds it items, which it burns into its owner's EMC (whoever placed it; an older table belongs to the next person to open it) and teaches them, up to 16 a second while the owner is online; it only takes things it can burn. |
| **Alchemical Coal → Mobius Fuel → Aeternalis Fuel** | Each tier is 4× the last. Hotter and much longer burning than charcoal; they work in forges and fire pits. |
| **Dark Matter / Red Matter** | Plus blocks of each. |
| **Matter tools** | Pickaxe, shovel, axe, sword, hoe and **sledgehammer** in dark and red matter, as 3D tools held, swung and displayed like the game's own (they use the vanilla tool models and animations with matter textures; the sledgehammer has its own model). They **never wear out**. The pickaxe/shovel/axe dig 1×1 by default and 3×3 (dark) or up to 5×5 (red) with the tool-mode key **F**; the sledgehammers smash stone and ore **3×3 from the start** (red: 5×5 with **F**). Sneak to dig a single block. The axe still fells trees and the hoe still tills. |
| **Energy Collectors MK1–MK3** | Turn sunlight into EMC (4 / 12 / 40 EMC per second in full sun). They push it into a condenser next to them, or sneak + right-click with an empty hand to take it into your personal EMC. |
| **Energy Condensers MK1–MK2** | Put an item in the target slot. The condenser burns everything else inside it (plus EMC from collectors) and makes copies of the target. Works with hoppers and chutes; automation only pulls out the finished copies. |
| **Klein Stars** | Ein → Omega (50k to 51.2M EMC). Portable EMC: right-click to empty into your EMC, sneak + right-click to fill. Rings and gadgets draw from carried stars. Can be worn in the **emblem** slot, where it shows on your chest and still counts as carried. |
| **Rings, stones, amulets** | Black Hole Band, Swiftwolf's Rending Gale (flight, shield and 1x–3x speed), Harvest Goddess Band, Ignition Ring, Zero Ring, Ring of Arcana (every effect switchable on its own), Void Ring, Body/Soul/Life Stones, Repair Talisman, Gem of Eternal Density, Evertide and Volcanite Amulets, Watch of Flowing Time. They work from your hotbar or backpack; right-click switches them on/off. The rings can also be worn in the **hand** slot, where they show on your hand and keep working. The Body and Life Stones have a mode that feeds nutrition too, and the Watch of Flowing Time glows and shows an icon on screen while it's on (`.vintagee` in chat to move, resize, recolour or hide them). |
| **Gadgets** | Destruction Catalyst, Hyperkinetic and Catalytic Lenses (explosive charges), Mercurial Eye (1x1 up to 9x9), Archangel's Smite (homing arrows), Divining Rods (16, 32 and 128 blocks deep; they wear out after as many uses as 10x their EMC value), and the creative-only Tome of Knowledge. Lenses, amulets and the Ignition Ring shoot with the **fire-projectile key** (**R** unless you change it in the controls; their tooltip names the key you set). |
| **Armour** | Dark Matter, Red Matter and Gem armour (head, body, legs) that never wears out and makes no clanking noise; full gem armour stops fall and fire damage. |
| **More tools** | Dark/Red Matter Shears, Red Matter Katar, Red Matter Morning Star. |
| **Storage and machines** | Alchemical Chest (104 slots), Alchemical Bags (32-slot backpack bags; upgrade with dark matter for 48 slots and red matter for 64, keeping what's inside), fuel blocks, Dark/Red Matter Furnaces (hotter, faster firepits that can run on EMC), Anti-Matter Relays MK1–3, Dark and Red Matter Pedestals (the item floats above them; red: twice the reach and effect), Nova Catalyst/Cataclysm (lit with a torch or firestarter, like the game's bombs), Interdiction Torch. |

Hover any item to see its EMC in the tooltip. Tooltips are kept to a line; the full details of each Vintage-E item are on its handbook page under **How it works**. Liquids show EMC **per litre** (1 L of water = 1 EMC), and buckets, bags and pots also show what their contents are worth. Worn tools are worth their remaining durability and clothes their condition (`DurabilityScalesEmc`); anything made from EMC comes out new. EMC numbers show up to two decimals, cut off rather than rounded.

When you join, chat shows a summary in the form `Vintage-E version <version>: calculated <n> EMC values, <n> missing, <n> value loops (/emc loop).` (turn off with `AnnounceInChat`).

## How EMC values are calculated

Nothing is hard-coded per item. When the server reaches the "run game" phase (after every mod has loaded its recipes) Vintage-E:

1. **Seeds a short list of base values** for raw materials — rocks, soil, sand, logs, clay, ore nuggets, ingots, coal, gems, food, hides (see [`assets/vintagee/config/vintagee-emc.json`](assets/vintagee/config/vintagee-emc.json)).
2. **Collects every way of making things** the loaded mods define:
   - crafting-grid recipes;
   - every recipe list held by **any loaded mod** whose recipe type implements the game's `IRecipeBase` interface — that covers vanilla smithing, knapping, clay forming and barrel recipes *and* custom recipe types from other mods, found automatically by reflection;
   - alloy recipes (copper + tin → bronze, by ratio);
   - item properties: smelting/firing/cooking, quern grinding, pulverizer crushing, and drying, curing, ripening, hardening, melting and other transitions;
   - clay-oven baking, the fruit press and the still (read from item attributes, so mods using the same attributes are covered);
   - **casting**: pouring metal into a tool mold costs the mold's `requiredUnits` of that metal (100 units = 1 ingot), shared among what it drops; the mold itself only costs something if it breaks when filled;
   - **processes with several outputs**, read from the behaviours' own settings: squeezing (honeycomb → honey + beeswax), ground-storage processing such as sawing logs into boards (`processedStacks` plus any `remainingItem`), and the fruit press (juice + pressed leftovers). The input's value is shared out so the process neither creates nor destroys EMC: outputs that already have a value keep it, the rest is split evenly;
   - **block drops**: a natural block (ore, crop, plant, clay…) is worth what it drops, and a block that drops one kind of item values that item;
   - **placement states**: blocks that only exist placed (facing another way, snow-covered, a fence joined to its neighbours, an open gate) are worth exactly the item they turn back into when picked or broken;
   - **beehive kiln** firing (the colours pottery, bricks and shingles come out in) and **carburizing** (iron to blister steel), read from item attributes;
   - **things built in stages**, like sailboats on rollers: what every stage takes, for the item the construction ends up as (`constructions` in the value files). Other mods' stage-built things without a rule are found too, by matching the construction's name to an item (`mymod:longshipconstruction` → `mymod:longship`);
   - **hand-written recipes** for anything the mapper can't see being made (`/emc recipe`, the `CustomRecipes` setting, or `recipes` in a value file).
   Smithing and clay forming cost the whole ingots/clay you have to put on the anvil (42 voxels per ingot, 25 per clay), because split-off voxels are lost. Tools used in a recipe (saws, the philosopher's stone…) cost nothing; returned items (buckets…) are subtracted.
3. **Propagates values through that graph**: each item gets the *cheapest* value any recipe produces, so crafting something and burning it can never earn more EMC than its ingredients. When an ingredient accepts several items (wildcards, tags) the cheapest valued one is used. Self-cheapening recipe loops are detected and frozen at their first value.
4. **Adds time**: items that transform on their own over time get a bonus per in-game day it takes (fresh + transition hours). By default drying adds **64 EMC per day** (tall grass that takes a day to dry makes dry grass worth 64 more). Set per transition type with `TransitionEmcPerDay` in the config (`dry`, `cure`, `ripen`, `harden`, `melt`, `convert`).
5. **Works backwards where it can**: if an ore chunk or a raw food has no value but what it smelts/dries/grinds into does, its value is inferred from the output. The same goes for crafting: if a recipe's result has a value and only one ingredient doesn't (loot parts, gold jewellery that breaks down into gold bits…), that ingredient gets what's left over (the highest such value, so nothing that already had a value gets cheaper).
6. **Applies ratio rules** from the value files (`relativeValues`) to anything still unvalued — e.g. a metal bit is worth 1/20 of the matching ingot (5 of its 100 metal units), aged or cracked stone bricks are worth the same as normal ones.
7. **Uses trader prices** for things no recipe makes (clothes, loot armour, decorations…). What one rusty gear is worth is *measured* each start from items traders deal in that already have a value (the median of value ÷ price), and the rusty gear itself is set to that, so selling goods for gears and burning the gears can't create EMC.
8. **Fills remaining gaps from variants**, using the variant groups each block/item type defines. A value is copied freely across groups that only change the look or placement (rock, colour, wood, facing, snow cover…): the same ore in a different rock, the same bricks in another colour. It is **never** copied across groups that change what something is (ore, metal, material, mold type…), so cinnabar ore isn't priced like gold-bearing quartz; those stay unvalued and show in `/emc missing`. Other mods' variants of a base-game family (their own wood types for the game's logs, say) borrow the base game's value the same way.
9. **Estimates from what a thing does**: a fuel nothing else values is worth how hot and how long it burns, at the rate charcoal and coal are valued at; a food is worth how filling it is, at the rate valued food of the same kind (fruit, vegetable, protein, grain, dairy) is.
10. **Guesses across the remaining variant groups** (those in neither list, like a clothes slot's types), taking the *middle* value of the variants so one odd one can't set the rest.
11. **Falls back to `defaultValues`** in the value files: family-level values for natural blocks (coral, seaweed, termite mounds…), ruin decorations and loot that nothing else could value. Recipes always win over these.

After each of steps 6–11 the recipe graph is solved again, so anything made from newly valued things gets its real recipe value before a rougher step guesses at it. `/emc dump` shows which step valued each item.

Soil is worth more the more fertile it is (very low 1 → terra preta 16), and so is farmland.

Self-cheapening recipe loops (where values would creep down every time round) are detected and frozen at their first value.

The finished table is sent to every client.

### Caching

Mapped values are saved per world in `VintagestoryData/ModData/vintagee/cache/` and reused on the next start, so the mapping only runs when something that affects values has changed. That's decided by a fingerprint over every input: the game version, the loaded mods and their versions, this mod's own code, `ModConfig/vintagee.json`, the merged value files, every block/item with its smelting/grinding/crushing/transition/drop/baking/press/still data and `emcValue`, every crafting-grid recipe and every recipe/alloy list any mod holds. Change any of those (add a mod, update one, edit a recipe or the config) and the next start remaps and refreshes the cache. The server log says which happened (`EMC values loaded from cache…` or `EMC mapped…`). `/emc recalc`, `/emc set` and `/emc clear` always remap; set `CacheEmcValues` to `false` to never use the cache.

### Checking that everything has a value

Every time values are mapped, the server log reports coverage in this form:

```
EMC coverage: <valued> of <total> blocks/items in the creative inventory have a value (<percent>%).
EMC: <n> without a value, by family: <domain:family> (<count>), ...
```

The full list is written to `VintagestoryData/ModData/vintagee/emc-missing.csv`, and admins can run `/emc missing` in game to open a **searchable, scrollable list** with item icons, filters by mod and by blocks/items, and a button that copies the codes ready to paste into `BaseValues`. Clicking an icon opens its handbook page. Click names to select several (or **Select shown** for everything the filters show) and give them all one value or blacklist them in one go; with a mod picked in the mod filter, **Blacklist whole mod** blacklists everything from that mod (saved as `modid:*`), while **Blacklist its missing** blacklists only that mod's missing blocks/items, by their own codes, so everything of it that already has a value keeps it. **Gray-list it** puts the selection on the gray list instead: out of this list and never given a guessed value, but valued as soon as a recipe makes it. Picks are kept when you change the filters (the selection line says how many are hidden); **Clear selection** drops them all. Tick **Show blacklisted** to list blacklisted things too (in red) and take them off the blacklist from here; `/emc blacklist` has the mirror, **Hide missing**, which you untick to list everything without a value alongside the blacklist. Give anything on it a value with `BaseValues` in `ModConfig/vintagee.json` (or `/emc set` while holding it), then `/emc recalc`. If you really want *every* listed block and item to have some value, set `FallbackValue` — but anything valued that way can be created for that price, so a small number makes rare loot cheap.

Deliberately left without a value (so they can't be duplicated or burned): creative/debug/technical blocks, work-in-progress items, loot containers, liquid blocks, and story-progression devices (the return-base items, etc.), creatures and NPCs from any mod (live butterflies and fish excepted, since you can catch them), and maps. The list is the `blacklist` in the default value file.

## Items that hold things or keep their material

Some items carry data on the item itself: buckets, jugs, pots, bowls, crocks, barrels and bags hold things; round shields keep their metal and wood, lanterns their metal, lining and glass, chests and clutter their type, chiseled blocks their design. Each of these uses one of three modes:

| Mode | Burning gives | Learning and creating |
|---|---|---|
| **Item only** | the plain item's value (a container that still holds something won't burn while `ProtectContainersWithContents` is on; with it off, it burns and gives its contents' EMC too) | the plain item |
| **Item + contents** | the item plus what it holds or is made of (a gold round shield is worth more than a tin bronze one; a chiseled block is worth its voxels of each material) | exactly that version |
| **Item price only** | the item and its material, not what it holds | exactly that version, for the item's price: contents are free |

Built-in defaults: containers are **item only**; items whose material or type lives on the item, and chiseled blocks, are **item + contents** (a plain copy of those would be the wrong or default version). Admins change them with **`/emc contents`**, a window listing every such block/item with its mode (search, filter by mod, kind and mode, select several, then pick a mode or *Default*), or `/emc contents <item|full|itemprice|default>` for the held item. Changes are saved to `StackDataModes` in the server config and apply straight away. Copies never include wear, freshness or clothing condition: they come out new. Condensers follow the same modes for their target. Each player can know up to `MaxExactKnowledge` (500) exact versions.

## Looks: New, Middle and Original

Vintage-E's blocks and items come in three looks. **New worlds get New; worlds that already had Vintage-E before 1.13.0 keep Middle**, the look they had. Each world remembers its look the first time it runs 1.13.0 or later, so it never changes by itself. A server can pick one look for every world with the **`Look`** setting (its default, `Auto`, means "each world's own").

| `Look` | What it looks like |
|---|---|
| **`New`** (new worlds) | **Models made to blend in with Vintage Story.** Instead of flat icons and plain cubes, things get 3D models built mostly from the game's own materials (its planks, metal plates, polished stone, gems, linen, leather, paper and candles), with muted textures in the game's style for the rest. See below. |
| **`Middle`** (worlds that already had Vintage-E) | Vintage-E's own flat pixel-art textures, the only look before 1.13.0 apart from Original (pictured below). |
| **`Original`** | **ProjectE's original textures** wherever ProjectE has one: rings, stones, amulets, gadgets, matter, fuels, covalence dust, alchemical bags, matter blocks, fuel blocks, collectors, relays, condensers, the alchemical chest, the transmutation table, nova catalysts, the interdiction torch, the projectiles, and the dark/red matter look of tools, armour, the pedestal and the furnaces. Rings and stones light up while switched on, and the Ring of Arcana and Swiftwolf's Rending Gale show their mode, as in ProjectE. Condensers and the alchemical chest use faces cut from ProjectE's chest textures; bags are drawn as ProjectE's flat bags. |

The Middle look:

![The Middle look](docs/preview.png)

What the **New** look changes:

- **Rings** are metal bands with a set gem (Zero Ring: silver and aquamarine, Ignition Ring: copper and ruby, Ring of Arcana: gold and amethyst...), and the **Iron Band** a plain iron band. **Body, Soul and Life Stones** are polished gemstones on a wire loop; the **Evertide and Volcanite Amulets** gems in a bezel on a leather cord; **Archangel's Smite** the same with white wings; the **Repair Talisman** a bronze charm; the **Gem of Eternal Density** crystals in an iron claw; the **Watch of Flowing Time** a brass pocket watch.
- **Gadgets:** the lenses are rimmed lenses on a handle (the Catalytic Lens gold with a ruby glass), the **Destruction Catalyst** a rod tipped with a crystal, the **Mercurial Eye** a marble eye on a stand, the **Tome of Knowledge** a leather-bound book, the **divining rods** forked branches with copper, silver or gold tips.
- **Klein Stars** are six-pointed crystal stars (quartz, citrine, topaz, emerald, sapphire, amethyst by tier), the **Philosopher's Stone** a cut ruby, **dark and red matter** polished orbs, **covalence dust** a heap of powder, **alchemical fuels** lumps like the game's coal, the **Transmutation Tablet** a slate tablet in a wooden frame. **Alchemical bags** are made of the game's linen in their colour.
- **Blocks:** the **Transmutation Table** is a slate slab with the circle cut in, in a dark wooden frame with a candle at each corner; **pedestals** are stone plinths with a matter column; the **Interdiction Torch** a caged crystal on an iron stand; the **dark/red matter furnaces** stone-ringed fire pits like the game's; the **Alchemical Chest** a brass-bound chest; **collectors** timber boxes with a crystal on top, **relays** stone drums around a matter core, **condensers** stone strongboxes with a crystal in the lid, and **nova catalysts** powder kegs with a fuse. Their crystals (citrine, amethyst, ruby for MK1–MK3) glow. Matter blocks and fuel blocks stay full blocks, with new textures.
- **Matter tools and armour** keep the game's tool and armour models, with the new dark and red matter.

In every look, worn rings and Klein Stars keep Vintage-E's look, and projectiles keep their own.

To use one look everywhere, `/emc configure Look New` (or `Middle`, `Original`, or `Auto` to go back to each world's own), in the `/emc configure` window, or in `ModConfig/vintagee.json`. It takes effect the next time the server starts; players don't need to set anything, because the server sends them the models and textures. A server that had the old `ProjectETextures` switch on gets `Look` = `Original`. A world counts as new if its save file doesn't exist yet when the server starts; an existing world on a server that already had Vintage-E's config keeps Middle. The look each world was given is remembered in `ModData/vintagee/looks/` (one small file per world; delete it to have the world decided again).

Which texture or model replaces which is listed in `assets/vintagee/config/vintagee-projecte-look.json` (Original) and `assets/vintagee/config/vintagee-new-look.json` (New, written by `tools/gen_newlook.py`; `tools/render_look.py` draws a preview sheet of it).

## ProjectE's sounds

- **`ProjectESounds`** (off by default): ProjectE's eight sounds (charge, uncharge, heal, power, destruct, transmute, water magic, wind magic) for what ProjectE plays them for: switching rings on and off, shooting with lenses and rings, the Destruction Catalyst and area mining, the Philosopher's Stone, the amulets' water and lava, stones and pedestals healing, putting a ring on a pedestal, and the wind while flying with Swiftwolf's Rending Gale. It takes effect straight away. Turn it on with `/emc configure ProjectESounds true`.

## For modpack makers and mod authors

Values come from these sources, later ones winning:

1. `assets/vintagee/config/vintagee-emc.json` (this mod's defaults).
2. This mod's **compatibility files**, `assets/vintagee/config/vintagee-emc-compat/<modid>.json`: last-resort values for other mods' raw materials that no recipe makes (Primitive Survival's fish, Wildcraft's trees and fruit, Butchering's carcasses and offal, From Golden Combs' full frames, Expanded Matter's crushed ores). Each has a `"requiresMod"` key and only loads when that mod is loaded; its `values` and `litreValues` count as `defaultValues`, so a recipe, trader price, ratio rule or variant that values an item always wins over it. When one is in use the server log and the chat summary say so, and `/emc compat` opens a window listing every compatibility file, whether its mod is loaded and how many items it valued, with a switch to disable or enable each one (the `DisabledCompat` setting; a switch remaps). Any value file with a `"requiresMod"` key (one mod id or a list) is treated the same way, wherever it is. Each value in these files has a `"//<code>"` line next to it saying what it was pegged to (string entries are ignored by the mapper), so the reasoning travels with the number.
3. Any other mod's `assets/<modid>/config/vintagee-emc.json` — same format; ship one with your mod, or JSON-patch ours.
4. An `emcValue` attribute on any item or block: `"attributes": { "emcValue": 256 }` (0 = no EMC).
5. `ModConfig/vintagee.json` on the server (or change it in game with `/emc configure`): `BaseValues` and `FixedValues` (code or wildcard → value), `Blacklist`, and settings like `TransitionEmcPerDay`, `InferValuesFromProcessing`, `InferValuesFromCrafting`, `UseTraderPrices`, `InferFromVariantSiblings`, `FallbackValue`, `CacheEmcValues`, `DumpValuesOnStart`, `AnnounceInChat`, `DurabilityScalesEmc`, `EstimateFuelAndFood`, `StackDataModes`, `MaxExactKnowledge`, `ProtectContainersWithContents`, `MachineSpeedMultiplier`, `CollectorDarkFactor`, `CollectorsChargePlayers`, `TableUseRange`, `TrinketCostMultiplier`, `HostileCreatures` (what the Ignition Ring burns, the Interdiction Torch repels and Archangel's Smite targets), `WatchChangesWorldTime`, `WatchSpeedUp`, `WatchSlowDown`, and the Repair Talisman's clothing settings (`RepairTalismanRepairsClothing`, `RepairTalismanClothingPercentPerMinute`, `RepairTalismanClothingMinCondition`, `RepairTalismanClothingMaxCondition`), `Look` / `ProjectESounds` (see below), `FreeIngredients` (no value of their own, free in recipes), `NoTransmute` (keep their value, but can't be burned, learned or created), `GrayList` (no guessed value and not listed as missing, but valued by recipes), `CustomRecipes` (hand-written recipes, `output = ingredients`) and `DisabledCompat` (compatibility files to leave out, by file name).

Keys accept wildcards and an optional type prefix: `"game:rock-*"`, `"item:game:stick"`, `"block:*:log-*"`.

Value files can also hold:

- `litreValues` — liquids in EMC per litre (`"game:waterportion": 1`), converted using each liquid's `itemsPerLitre`;
- `relativeValues` rules (`{ "code": "game:metalbit-*", "of": "game:ingot-*", "ratio": 0.05 }`, the `*` part carried over);
- `defaultValues` — values used only when nothing else could value an item;
- `traderCurrency` — items worth exactly what one unit of trader currency buys;
- `blacklist` and `allow` (exceptions to the blacklist);
- `freeIngredients` — blacklisted (no value of their own) but free when a recipe uses them, like the customized translocator schematic and the illuminal divergence manifold;
- `grayList` — codes or wildcards left out of `/emc missing` and never given a guessed value, valued only by recipes;
- `recipes` — hand-written recipes, one `"output = ingredients"` line each (`"item:mymod:boat = 40 game:plank-* + 8 game:rope"`: a number before a code is how many, wildcards take the cheapest match);
- `variantGuessing` — `copyAcross` and `neverAcross` lists of variant group codes (`"family:group"` limits one to a family, e.g. `"ore:type"`) and `otherGroups` (`"copy"` or `"never"`);
- `fuelReference` — the fuels whose value per degree-second values unvalued fuels;
- `constructions` — things built in stages (`{ "entity": "game:boatconstruction-*", "makes": "item:game:boat-*" }`): the entity's stages are added up for the item;
- `stackData` — code/wildcard → `"item"`, `"full"` or `"itemprice"`, to change the built-in contents mode for a family;
- the philosopher's stone rules (`worldTransmutation`: `swaps` and `variantCycles`).

`/emc dump` (or `DumpValuesOnStart`) writes every value to `emc-values.csv` (with each block/item's name), the blacklist to `emc-blacklisted.csv` and every item that holds things or keeps its material, with its mode and material-specific values, to `emc-contents.csv`, together with **what it was worked out from** (`grid: 1 game:log-placed-oak-ud -> 12 game:plank-oak`, `trader price: …`, `same as its variant …`), which makes it easy to see why something costs what it does.

### Commands

| Command | Who | |
|---|---|---|
| `/emc` | everyone | Your EMC and the held item's value |
| `/emc help` | everyone | Every command with a short description |
| `/emc set <value>` | admin | Pin the held item's value (0 = remove), saved to the config, remaps |
| `/emc clear` | admin | Remove that pin |
| `/emc recalc` | admin | Re-run the mapper |
| `/emc dump` | admin | Write every value, and where it came from, to `VintagestoryData/ModData/vintagee/emc-values.csv` |
| `/emc dump hand` (or `/emc dump <code>`) | admin | Open a window explaining everything that makes up the held item's value (or any block/item's, by code), with the numbers: Klein Star EMC, contents and contents mode, durability, stack size; the value file or setting it comes from, or the recipe with each ingredient's value, the time bonus and what's given back; the other ways of making it; and for something without a value, which ingredients have none and the value loop it's stuck in. Click an item in it to explain that one. Also written to `emc-dump-<item>.txt` |
| `/emc loop` | admin | Open a window of the value loops: unvalued items that are each only made from the next, so none can get a value; click an item to see why (also written to `emc-loops.txt`) |
| `/emc notransmute` | admin | Open the list of blocks/items that keep their EMC value but can't be burned, learned or created; `/emc notransmute add\|remove` does the held item |
| `/emc missing` | admin | Open the searchable list of blocks/items without a value, where you can give one or many of them a value, blacklist them, or blacklist a whole mod (also written to `emc-missing.csv`) |
| `/emc blacklist` | admin | Open the same window for everything on the blacklist: give something a value or take it off the blacklist |
| `/emc contents` | admin | Open the list of everything that holds things or keeps its material, and switch how each is transmuted |
| `/emc contents <item\|full\|itemprice\|default>` | admin | Set the held item's contents mode |
| `/emc configure` | admin | Open the settings window (every setting, what it does, an editor for it; long lists and tables are shown 100 lines at a time, with a find box); from chat `/emc configure list` lists them, `/emc configure <setting>` explains one, `/emc configure <setting> <value>` changes it (lists: `add`/`remove`/`clear`; tables: `set <key> <value>`/`remove <key>`/`clear`), `/emc configure <setting> default` resets one, `/emc configure resetall` resets every setting, `/emc configure reload` re-reads the file. Saved and applied straight away |
| `/emc graylist` | admin | Open the gray list: blocks/items left out of `/emc missing` and never given a guessed value, but valued as soon as a recipe makes them (for things nothing makes yet, but a mod might later); `/emc graylist add\|remove` does the held item |
| `/emc recipe` | admin | Open the window of hand-written recipes: put the result and its ingredients in the slots (or type the recipe, e.g. `item:mymod:boat = 40 game:plank-* + 8 game:rope`), and edit or remove saved ones. `/emc recipe add\|remove <recipe>` and `/emc recipe list` from chat |
| **Ctrl + K** over a block or item | admin | A small box with its EMC value and switches for the blacklist, the gray list and free ingredients: change any and press **Save**. Works on inventory slots, any window, the handbook's pictures and pages, the block you look at, or the held item. **Unpin** takes a value set by hand off. **Why this value?** opens the `/emc dump` window for it. Change the key in the game's controls |
| `.vintagee` | everyone | This computer's settings: the Watch of Flowing Time's icon (on/off, size, position) and glow (on/off, colour) |
| `/emc add <amount> [player]` | admin | Add personal EMC to a player (yourself if no name is given) |
| `/emc remove <amount> [player]` | admin | Take personal EMC from a player (never below 0) |
| `/emc learn` (or `/emc learn hand`) | everyone | Learn the held item without burning it: anything the table could burn (admins: anything) |

## Recipes

| Makes | Pattern (top / middle / bottom) | Key |
|---|---|---|
| 1× Philosopher's Stone | `CGC / GTG / CGC` | C = Copper ingot, G = Gold ingot, T = Temporal gear |
| 1× Transmutation Table | `RSR / SPS / RSR` | R = Any rock block, S = Any stone, P = Philosopher's Stone (kept, as in every recipe that uses it) |
| 1× Transmutation Tablet | `·D· / DTD / ·D·` | D = Dark Matter, T = Transmutation Table |
| 1× Alchemical Coal | shapeless: 1× Philosopher's Stone + 4× Charcoal | |
| 1× Alchemical Coal | shapeless: 1× Philosopher's Stone + 2× Coke | |
| 1× Mobius Fuel | shapeless: 1× Philosopher's Stone + 4× Alchemical Coal | |
| 1× Aeternalis Fuel | shapeless: 1× Philosopher's Stone + 4× Mobius Fuel | |
| 4× Charcoal | shapeless: 1× Philosopher's Stone + 1× Alchemical Coal | |
| 4× Alchemical Coal | shapeless: 1× Philosopher's Stone + 1× Mobius Fuel | |
| 4× Mobius Fuel | shapeless: 1× Philosopher's Stone + 1× Aeternalis Fuel | |
| 1× Iron ingot | shapeless: 1× Philosopher's Stone + 2× Copper ingot | |
| 1× Silver ingot | shapeless: 1× Philosopher's Stone + 2× Iron ingot | |
| 1× Gold ingot | shapeless: 1× Philosopher's Stone + 4× Silver ingot | |
| 2× Copper ingot | shapeless: 1× Philosopher's Stone + 1× Iron ingot | |
| 2× Iron ingot | shapeless: 1× Philosopher's Stone + 1× Silver ingot | |
| 4× Silver ingot | shapeless: 1× Philosopher's Stone + 1× Gold ingot | |
| 1× Diamond (rough) | shapeless: 1× Philosopher's Stone + 4× Emerald (rough) | |
| 4× Emerald (rough) | shapeless: 1× Philosopher's Stone + 1× Diamond (rough) | |
| 1× Dark Matter | `AAA / ATA / AAA` | A = Aeternalis Fuel, T = Temporal gear |
| 1× Red Matter | `AAA / DDD / AAA` | A = Aeternalis Fuel, D = Dark Matter |
| 1× Dark Matter Block | `DD / DD` | D = Dark Matter |
| 1× Red Matter Block | `RR / RR` | R = Red Matter |
| 4× Dark Matter | `B` | B = Dark Matter Block |
| 4× Red Matter | `B` | B = Red Matter Block |
| 1× Dark Matter Pickaxe | `MMM / ·S· / ·S·` | M = Dark Matter, S = Stick |
| 1× Red Matter Pickaxe | `RRR / ·T· / ·D·` | R = Red Matter, T = Dark Matter Pickaxe, D = Dark Matter |
| 1× Dark Matter Shovel | `·M· / ·S· / ·S·` | M = Dark Matter, S = Stick |
| 1× Red Matter Shovel | `·R· / ·T· / ·D·` | R = Red Matter, T = Dark Matter Shovel, D = Dark Matter |
| 1× Dark Matter Axe | `MM· / MS· / ·S·` | M = Dark Matter, S = Stick |
| 1× Red Matter Axe | `RR· / RT· / ·D·` | R = Red Matter, T = Dark Matter Axe, D = Dark Matter |
| 1× Dark Matter Sword | `·M· / ·M· / ·S·` | M = Dark Matter, S = Stick |
| 1× Red Matter Sword | `·R· / ·T· / ·D·` | R = Red Matter, T = Dark Matter Sword, D = Dark Matter |
| 1× Dark Matter Hoe | `MM· / ·S· / ·S·` | M = Dark Matter, S = Stick |
| 1× Red Matter Hoe | `RR· / ·T· / ·D·` | R = Red Matter, T = Dark Matter Hoe, D = Dark Matter |
| 1× Dark Matter Sledgehammer | `MMM / MSM / ·S·` | M = Dark Matter, S = Stick |
| 1× Red Matter Sledgehammer | `RRR / RTR / ·D·` | R = Red Matter, T = Dark Matter Sledgehammer, D = Dark Matter |
| 1× Energy Collector MK1 | `GGG / AOA / III` | G = Any glass block, A = Alchemical Coal, O = Gold ingot, I = Iron ingot |
| 1× Energy Collector MK2 | `GDG / GCG / GGG` | G = Any glass block, D = Dark Matter, C = Energy Collector MK1 |
| 1× Energy Collector MK3 | `GRG / GCG / GGG` | G = Any glass block, R = Red Matter, C = Energy Collector MK2 |
| 1× Energy Condenser MK1 | `IGI / GDG / IGI` | I = Iron ingot, G = Gold ingot, D = Dark Matter |
| 1× Energy Condenser MK2 | `RDR / DCD / RDR` | R = Red Matter, D = Dark Matter, C = Energy Condenser MK1 |

The ProjectE items (Klein stars, rings, gadgets, armour, chest, bags, furnaces, relays, pedestal…) are all crafting-grid recipes too; look them up in the in-game handbook (they're listed in [`assets/vintagee/recipes/grid/projecte.json`](assets/vintagee/recipes/grid/projecte.json)).

Players' EMC and knowledge are saved on the player, so they persist across sessions and deaths.

## Installing

See [CHANGELOG.md](CHANGELOG.md) (also shown on each [release](https://github.com/demonic69/Vintage-E/releases)) for what changed in each version.

Download `vintagee_<version>.zip` from the [latest release](https://github.com/demonic69/Vintage-E/releases/latest) and drop it (still zipped) into your `Mods` folder. It's needed on both the server and the clients. Every version's zip is also kept in [`dist/`](dist/).

## Building from source

You need the **.NET 10 SDK** and a Vintage Story 1.22 install.

```bash
export VINTAGE_STORY=/path/to/vintagestory      # folder containing VintagestoryAPI.dll
./build/package.sh                               # or build\package.ps1 on Windows
```

That validates the assets, builds `VintageE.dll` and writes `release/vintagee_<version>.zip`. `tools/gen_textures.py` regenerates all textures (they're original pixel art), `tools/gen_newlook.py` regenerates the New look's models and textures, `tools/gen_mattertools.py` and `tools/gen_sledgehammer.py` regenerate the tool definitions and the sledgehammer model, and `tools/validate_assets.py` cross-checks textures, shapes, recipe codes, class names and lang keys.

### Testing on a real server

The **Test on a real server** workflow (Actions tab → Run workflow) downloads the Vintage Story server, compiles the mod against the game's own DLLs, starts a fresh world with the mod and pushes the results to the `ci-analysis` branch: `emc-values.csv` (every value and where it came from), `emc-missing.csv`, the mod's log lines, and short summaries of the vanilla tool settings used while developing.

### Source layout

| Folder | |
|---|---|
| `src/Emc` | The automatic EMC mapper: `ConversionCollector` (finds recipes), `EmcSolver` (propagates values), `EmcMapper` (ties it together), `EmcRegistry` (runtime lookup + network sync) |
| `src/Transmutation` | Table/tablet GUI and server logic, philosopher's stone world rules |
| `src/Items`, `src/Blocks` | Philosopher's stone, tablet, matter-tool behaviour, collectors, condensers |
| `src/Commands` | `/emc` |
| `src/Assets` | The `Look` and `ProjectESounds` settings (`ProjectELook`, `PESounds`) |
| `assets/vintagee` | Item/block types, recipes, shapes, textures, lang, default EMC values; `textures/projecte` and `sounds/projecte` are ProjectE's, copied by `tools/import_projecte_assets.py` |

## Credits

Inspired by ProjectE by sinkillerj and contributors (and Equivalent Exchange 2 by x3n0ph0b3 before it). This is an independent reimplementation for Vintage Story; no code was copied. The Klein Star textures, and the textures and sounds behind the `Look` = `Original` and `ProjectESounds` settings, are ProjectE's own artwork and sounds, used under its MIT licence (see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)); ProjectE in turn credits x3n0ph0b3 for allowing the use of EE2's assets. The matter tools, and the New look's models, use Vintage Story's own models and textures by reference (nothing from the game is included in this mod).
