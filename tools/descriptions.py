"""
Item and block descriptions (the "itemdesc-"/"blockdesc-" lang lines shown in tooltips and the handbook), with
the actual numbers the mod uses. Kept in one place so they match the code; gen_projecte.py writes them into
assets/vintagee/lang/en.json last, over anything older. "\\n" starts a new line in the tooltip.
"""

KLEIN = [("ein", "50,000"), ("zwei", "200,000"), ("drei", "800,000"), ("vier", "3,200,000"), ("sphere", "12,800,000"), ("omega", "51,200,000")]

ITEM = {
    # ---------------------------------------------------------------- core
    "philosophersstone": "Right-click a block to transmute it into its neighbour in a chain (stone types, wood types, sand ↔ gravel, ...); sneak + right-click goes the other way.\n"
                         "Tool-mode key: 1x1, 3x3 or 5x5 at once. Free, and never used up when crafting with it.",
    "transmutationtablet": "A transmutation table you carry: right-click to open it anywhere. Burn items into EMC to learn them, then create anything you know from your EMC.",
    "alchemicalfuel-alchemicalcoal": "Burns at 1,400 °C for 160 seconds. In a forge it lasts 4x as long and burns 200 °C hotter.",
    "alchemicalfuel-mobiusfuel": "Burns at 1,500 °C for 640 seconds. In a forge it lasts 16x as long and burns 400 °C hotter. Made from 4 alchemical coal.",
    "alchemicalfuel-aeternalisfuel": "Burns at 1,600 °C for 2,560 seconds. In a forge it lasts 64x as long and burns 600 °C hotter. Made from 4 mobius fuel; the base of dark matter.",
    "matter-dark": "Condensed EMC: 8 aeternalis fuel around a temporal gear. Tier 5 tools and armour made from it never wear out. 4 make a dark matter block.",
    "matter-red": "Denser still: 3 dark matter between 6 aeternalis fuel. Tier 6 tools and armour that never wear out. 4 make a red matter block.",
    # ---------------------------------------------------------------- ProjectE extras
    "covalencedust-low": "Used to build alchemical gear. 40 from 8 stones + 1 charcoal.",
    "covalencedust-medium": "Used to build alchemical gear. 40 from 1 iron ingot + 1 saltpeter.",
    "covalencedust-high": "Used to build alchemical gear. 40 from 1 diamond + 1 coke.",
    "ironband": "The base of every ring. 8 iron ingots around 1 alchemical coal.",
    # ---------------------------------------------------------------- rings, stones, amulets
    "trinket-blackholeband": "While on: pulls loose items within 7 blocks to you. Free.\n"
                             "Right-click a water or lava source to remove it.\n"
                             "On a pedestal: pulls in items within 10 blocks and puts them into containers next to the pedestal.\n"
                             "Works from your hotbar or backpack; right-click to switch on/off.",
    "trinket-swiftwolfsrendinggale": "While on: you can fly (jump to rise, sneak to sink) and/or a shield pushes hostile creatures and arrows away within 8 blocks; pick flight + shield, flight or shield with the tool-mode key.\n"
                                     "The tool-mode key also picks your speed, running and flying: 1x, 1.5x, 2x or 3x (only with flight on; shield only leaves your speed alone).\n"
                                     "Flying costs 6.4 EMC a second x your speed while you are in the air; running faster than normal costs 6.4 EMC a second x (speed - 1) while you move; the shield costs 6.4 EMC a second. No fall damage while it's on and for 15 seconds after you stop flying. It switches off when you run out of EMC.\n"
                                     "On a pedestal: strikes hostile creatures within 10 blocks for 4 damage a second.",
    "trinket-harvestgoddessband": "While on: crops within 5 blocks grow (6 tries a second, 4 EMC per stage) and ripe ones are harvested. Switched off it still grows nearby crops slowly, for free.\n"
                                  "Right-click farmland to plant the seeds you carry on empty farmland up to 8 blocks around.\n"
                                  "On a pedestal: grows and harvests crops within 8 blocks.",
    "trinket-ignitionring": "Carry it (on or off) and fire can't hurt you.\n"
                            "While on: hostile creatures within 5 blocks catch fire and take 3 damage, 4 EMC each, once a second.\n"
                            "Fire-projectile key: shoots a fireball (3 damage and sets the creature alight; it also lights what it hits, as a torch would, and sets wood and other things that burn on fire). Free.\n"
                            "On a pedestal: burns hostile creatures within 8 blocks.",
    "trinket-zeroring": "While on: still water within 3 blocks of you freezes into ice. Free.\n"
                        "On a pedestal: freezes water within 6 blocks and puts out burning creatures.",
    "trinket-ringofarcana": "The Zero Ring, Ignition Ring, Harvest Goddess Band and Swiftwolf's Rending Gale in one, and all free.\n"
                            "The tool-mode key switches each of the four on or off (all on to start). Fire can't hurt you while you carry it.\n"
                            "Fire-projectile key: shoots a fireball while its Ignition effect is on.",
    "trinket-bodystone": "While on: when you are 100 saturation or more below full, it feeds you up to 200 saturation for 0.5 EMC each (every 2 seconds).\n"
                         "Tool-mode key: saturation only, or saturation and nutrition (1 EMC each), which also fills your food groups, the emptiest first.\n"
                         "On a pedestal: feeds players within 6 blocks 25 saturation a second, free (with nutrition too in that mode).",
    "trinket-soulstone": "While on: heals you 1 health a second for 64 EMC each.\n"
                         "On a pedestal: heals players within 6 blocks 0.5 health a second, free.",
    "trinket-lifestone": "Body Stone and Soul Stone in one: feeds you (0.5 EMC per saturation) and heals you (64 EMC per health) while on.\n"
                         "Tool-mode key: saturation only, or saturation and nutrition (1 EMC per saturation), which also fills your food groups.\n"
                         "On a pedestal: feeds and heals players within 6 blocks, free.",
    "trinket-repairtalisman": "Always works while you carry it: repairs 1 durability a second on every damaged tool, weapon and piece of armour you carry or wear, and restores clothing condition (1% a minute by default; the server can change or turn that off). Free.\n"
                              "On a pedestal: does the same for players within 6 blocks.",
    "trinket-gemofeternaldensity": "While on: every 2 seconds it eats cheap junk you carry (anything worth 8 EMC or less: stone, soil, sand, gravel, sticks...) and turns it into the target.\n"
                                   "Targets (tool-mode key): iron ingot, gold ingot, diamond, dark matter, red matter. Leftover EMC is kept for next time.",
    "trinket-voidring": "Gem of Eternal Density and Black Hole Band in one: while on it pulls in items within 7 blocks and condenses junk worth 8 EMC or less into the target (tool-mode key).\n"
                        "Sneak + right-click: teleport to the block you look at, up to 64 blocks away. Free, twice a second at most.\n"
                        "Right-click a water or lava source to remove it.",
    "trinket-evertideamulet": "Right-click a block to place a water source next to it. Free.\n"
                              "Fire-projectile key: shoots a ball of water that leaves a water source where it lands.\n"
                              "On a pedestal: makes it rain until you take it off.",
    "trinket-volcaniteamulet": "Right-click a block to place a lava source next to it, 32 EMC each.\n"
                               "Fire-projectile key: shoots a ball of lava (32 EMC) that leaves a lava source where it lands.\n"
                               "Carry it and fire can't hurt you. On a pedestal: clears the sky until you take it off.",
    "trinket-watchofflowingtime": "While on: time runs 10x faster for the whole world (crops, cooking, curing, drying, day and night) for 8 EMC a second, and crops within 6 blocks get extra growth.\n"
                                  "Tool-mode key: slow time to a third instead (4 EMC a second), which also slows hostile creatures within 10 blocks.\n"
                                  "On a pedestal: grows crops within 12 blocks and slows hostile creatures, free.\n"
                                  "While it's on, an icon on screen shows how fast time runs and the watch glows; type .vintagee in chat to move, resize, recolour or turn these off.",
    # ---------------------------------------------------------------- gadgets
    "gadget-destructioncatalyst": "Right-click a block to break a 3x3 tunnel into it: 1, 4, 8 or 16 blocks deep (charge 0-3, tool-mode key). 8 EMC per block broken; everything drops where you clicked.",
    "gadget-hyperkineticlens": "Right-click (or the fire-projectile key) to shoot an explosive charge. It flies straight at 36 blocks a second and explodes where it hits.\n"
                               "Charge (tool-mode key): radius 4, 6, 8 or 10 for 384, 768, 1,536 or 2,304 EMC.",
    "gadget-catalyticlens": "Right-click a block to break a 3x3 tunnel 1, 16, 24, 32, 40, 48, 56 or 64 blocks deep (charge 0-7, tool-mode key), 8 EMC per block.\n"
                            "Fire-projectile key: shoots the Hyperkinetic Lens's explosive charge (charge 3 and up: radius 10, 2,304 EMC).",
    "gadget-mercurialeye": "Sneak + right-click a block to remember it, then right-click blocks to turn them into it: 1x1, or 3x3, 5x5, 7x7 or 9x9 of the same block as the one clicked (tool-mode key).\n"
                           "You pay the difference in EMC, and get it back when the old block was worth more.",
    "gadget-archangelssmite": "Right-click: fires a homing arrow that steers towards hostile creatures (3 damage). Left-click: a volley of 10.\n"
                              "Each arrow costs what an arrow is worth in EMC.",
    "gadget-tomeofknowledge": "Creative mode only. Right-click to learn every block and item in the creative inventory that can be transmuted. Not used up, and it can't be crafted or made from EMC.",
    "gadget-diviningrodlow": "Right-click a block: reports the total and average EMC of the 3x3 column below it, 16 blocks deep, and the most valuable block found. Lasts as many uses as 10x its EMC value.",
    "gadget-diviningrodmedium": "Right-click a block: reports the total and average EMC of the 3x3 column below it, 32 blocks deep, and the most valuable block found. Lasts as many uses as 10x its EMC value.",
    "gadget-diviningrodhigh": "Right-click a block: reports the total and average EMC of the 3x3 column below it, 128 blocks deep, and the most valuable block found. Lasts as many uses as 10x its EMC value.",
    # ---------------------------------------------------------------- tools
    "redmattertool-katar": "Sword, axe and shears in one: 14 attack (a steel falx does 5.25), reach 3 blocks; chops wood at speed 16 and fells whole trees; cuts plants and leaves at 14, 3x3 or 5x5 with the tool-mode key. Tier 6; never wears out.",
    "redmattertool-morningstar": "Pickaxe, shovel and sledgehammer in one: digs stone, ore, metal, soil, sand and gravel at speed 14, 3x3 at a time (5x5 or one block with the tool-mode key; sneak for one block). 10 attack, tier 6; never wears out.",
}

for tier, cap in KLEIN:
    ITEM["kleinstar-" + tier] = (f"Holds up to {cap} EMC. Rings, amulets and gadgets draw from the stars you carry once your own EMC runs out.\n"
                                 "Right-click: empty it into your EMC. Sneak + right-click: fill it from your EMC. Sneak + right-click a collector or relay with it to charge it.\n"
                                 "Four stars craft into the next size and keep their EMC. Can be worn in your emblem slot.")

TOOLS = {
    "dark": {"tier": 5, "speed": 9, "sword": 8, "axe": 5, "hammer": 5, "other": 3.5, "hammerspeed": 8, "shears": 12, "hoe": 5, "area": "3x3"},
    "red": {"tier": 6, "speed": 14, "sword": 11, "axe": 7, "hammer": 7, "other": 4.5, "hammerspeed": 12, "shears": 18, "hoe": 8, "area": "3x3 or 5x5"},
}
for m, t in TOOLS.items():
    tail = f"Tier {t['tier']}; never wears out."
    ITEM[f"mattertool-{m}-pickaxe"] = f"Digs stone, ore and metal at speed {t['speed']}. Tool-mode key: {t['area']} at once (sneak for one block). {t['other']} attack. {tail}"
    ITEM[f"mattertool-{m}-shovel"] = f"Digs soil, sand, gravel and snow at speed {t['speed']}. Tool-mode key: {t['area']} at once (sneak for one block). {t['other']} attack. {tail}"
    ITEM[f"mattertool-{m}-axe"] = f"Chops wood at speed {t['speed']} and fells whole trees. Tool-mode key: {t['area']} at once (sneak for one block). {t['axe']} attack. {tail}"
    ITEM[f"mattertool-{m}-sword"] = f"{t['sword']} attack (a steel falx does 5.25), reach 2.75 blocks. {tail}"
    ITEM[f"mattertool-{m}-hoe"] = f"Tills soil into farmland; digs soil and plants at speed {t['hoe']}. {tail}"
    ITEM[f"mattertool-{m}-hammer"] = (f"Sledgehammer: smashes stone, ore and other hard blocks at speed {t['hammerspeed']}, 3x3 at a time"
                                      + (" (5x5 with the tool-mode key)" if m == "red" else "") + f"; sneak or the tool-mode key for one block. {t['hammer']} attack. {tail}")
    ITEM[f"mattertool-{m}-shears"] = f"Cuts leaves at speed {t['shears']} and plants and cloth at {t['shears'] * 2 // 3}. {tail}"

ARMOUR = {
    "dark": "98% protection, 2.4 flat damage reduction, protection tier 5.",
    "red": "98.5% protection, 3 flat damage reduction, protection tier 6, +3% walk speed per piece.",
    "gem": "99% protection, 3.6 flat damage reduction, protection tier 7, +6% walk speed and +15% healing per piece. A full set (helmet, chestplate, leggings) makes you immune to fall and fire damage.",
}
for m, text in ARMOUR.items():
    for p in ("head", "body", "legs"):
        ITEM[f"matterarmor-{m}-{p}"] = text + " Never wears out."

BLOCK = {
    "transmutationtable": "Right-click to open. Burn items into EMC to learn them (or drop them on Learn to learn them and keep them), then create anything you know from your EMC. The window closes if you walk more than 10 blocks away. A hopper or chute pointing into the table feeds it items, which it burns into its owner's EMC (whoever placed it) a few at a time while they're online.",
    "energycollector-mk1": "Makes up to 4 EMC a second in full sunlight (less in shade, none in the dark) and stores 10,000.\n"
                           "Feeds condensers, furnaces and relays next to it every second; sneak + right-click with an empty hand (or a Klein star) to take the EMC yourself.",
    "energycollector-mk2": "Makes up to 12 EMC a second in full sunlight (less in shade, none in the dark) and stores 30,000.\n"
                           "Feeds condensers, furnaces and relays next to it every second; sneak + right-click with an empty hand (or a Klein star) to take the EMC yourself.",
    "energycollector-mk3": "Makes up to 40 EMC a second in full sunlight (less in shade, none in the dark) and stores 60,000.\n"
                           "Feeds condensers, furnaces and relays next to it every second; sneak + right-click with an empty hand (or a Klein star) to take the EMC yourself.",
    "energycondenser-mk1": "Put an item in the target slot: it burns up to 20 other items a second (plus EMC from collectors) and makes up to 20 copies of the target a second, as in ProjectE.\n"
                           "Stores up to 1,000,000 EMC; 40 storage slots.",
    "energycondenser-mk2": "Put an item in the target slot: it burns up to 64 other items a second (plus EMC from collectors) and makes up to 64 copies of the target a second.\n"
                           "Stores up to 10,000,000 EMC; 40 storage slots.",
    "alchemicalfuelblock-alchemicalcoal": "9 alchemical coal: burns at 1,400 °C for 1,440 seconds.",
    "alchemicalfuelblock-mobiusfuel": "9 mobius fuel: burns at 1,500 °C for 5,760 seconds.",
    "alchemicalfuelblock-aeternalisfuel": "9 aeternalis fuel: burns at 1,600 °C for 23,040 seconds.",
    "alchemicalchest": "104 slots of storage (a vanilla chest has 16).",
    "antimatterrelay-mk1": "Stores up to 100,000 EMC from collectors, adds 1 EMC a second while it holds any, and passes up to 64 EMC a second on to condensers, furnaces and emptier relays.\n"
                           "Sneak + right-click with an empty hand (or a Klein star) to take the EMC yourself.",
    "antimatterrelay-mk2": "Stores up to 1,000,000 EMC from collectors, adds 3 EMC a second while it holds any, and passes up to 192 EMC a second on to condensers, furnaces and emptier relays.\n"
                           "Sneak + right-click with an empty hand (or a Klein star) to take the EMC yourself.",
    "antimatterrelay-mk3": "Stores up to 10,000,000 EMC from collectors, adds 10 EMC a second while it holds any, and passes up to 640 EMC a second on to condensers, furnaces and emptier relays.\n"
                           "Sneak + right-click with an empty hand (or a Klein star) to take the EMC yourself.",
    "novaexplosive-catalyst": "Light it like the game's bombs: hold right-click on it with a torch or a firestarter. It explodes 4 seconds later with a radius of 6 blocks, and everything it breaks drops.",
    "novaexplosive-cataclysm": "Light it like the game's bombs: hold right-click on it with a torch or a firestarter. It explodes 6 seconds later with a radius of 11 blocks, and everything it breaks drops.",
    "interdictiontorch": "Pushes hostile creatures out of an 8-block radius, 4 times a second. Gives off light level 20 (a torch gives 14). Needs no fuel.",
    "pedestal-empty": "Right-click with a ring, stone, amulet, talisman or watch to set it here: it then works on the area around the pedestal for free (see each item's description for what it does on a pedestal, usually 6-12 blocks).",
    "pedestal-active": "Working on the area around it for free. Right-click with an empty hand to take the item back.",
}
for m, name, heat, burn, speed, emc in (("dark", "Dark", 1.4, 3, 4, 2), ("red", "Red", 2.0, 4, 12, 4)):
    text = (f"Works like the game's firepit (same window, fuel slot, cooking pots, crucibles and spits; light it with a firestarter or torch), but made of {name.lower()} matter:\n"
            f"fuel burns {heat:g}x hotter and lasts {burn}x as long, and food, clay and metal cook or melt {speed}x faster once hot.\n"
            f"Collectors and relays next to it can power it instead: with nothing burning it lights itself on EMC, {emc} EMC a second at {int(1300 * heat):,} °C.")
    for state in ("cold", "lit", "extinct"):
        BLOCK[f"furnace-{m}-{state}"] = text

# Rings can be worn in the hand slot and keep working there.
for _ring in ("blackholeband", "swiftwolfsrendinggale", "harvestgoddessband", "ignitionring", "zeroring", "ringofarcana", "voidring"):
    ITEM["trinket-" + _ring] += "\nCan be worn in your hand slot, where it keeps working."


# ------------------------------------------------------------------------------------------------------------------
# Short tooltips. Where one is given, the tooltip shows only that line and the full text above goes into the
# handbook page instead, under "How it works" (the game's "<domain>:<item|block>-handbooktext-<code>" lang keys), so
# tooltips stay short and don't run off the screen.

SHORT_ITEM = {
    "philosophersstone": "Right-click to transmute blocks into their neighbours (stone types, wood types, sand and gravel...). Never used up in crafting.",
    "transmutationtablet": "A transmutation table you carry: right-click to open it anywhere.",
    "alchemicalfuel-mobiusfuel": "Burns at 1,500 °C for 640 seconds; 4x as long in a forge.",
    "alchemicalfuel-aeternalisfuel": "Burns at 1,600 °C for 2,560 seconds; the base of dark matter.",
    "matter-dark": "Condensed EMC for tier 5 tools and armour that never wear out.",
    "matter-red": "Denser still: tier 6 tools and armour that never wear out.",
    "trinket-blackholeband": "Pulls loose items to you while on. Right-click to switch it on or off.",
    "trinket-swiftwolfsrendinggale": "Flight and a shield that pushes hostile creatures away, while on (costs EMC).",
    "trinket-harvestgoddessband": "Grows and harvests crops around you; right-click farmland to plant seeds.",
    "trinket-ignitionring": "Fire can't hurt you; while on, hostile creatures nearby catch fire. Shoots fireballs.",
    "trinket-zeroring": "Freezes still water around you while on.",
    "trinket-ringofarcana": "The Zero Ring, Ignition Ring, Harvest Goddess Band and Swiftwolf's Rending Gale in one, all free.",
    "trinket-bodystone": "Keeps you fed while on, for EMC; the tool-mode key adds nutrition.",
    "trinket-soulstone": "Heals you while on, for EMC.",
    "trinket-lifestone": "The Body Stone and Soul Stone in one: feeds and heals you while on; the tool-mode key adds nutrition.",
    "trinket-repairtalisman": "Repairs the tools, weapons, armour and clothes you carry, for free.",
    "trinket-gemofeternaldensity": "While on, turns cheap junk you carry into iron, gold, diamond or matter.",
    "trinket-voidring": "Black Hole Band and Gem of Eternal Density in one; sneak + right-click to teleport.",
    "trinket-evertideamulet": "Places water sources and shoots balls of water. On a pedestal it brings rain.",
    "trinket-volcaniteamulet": "Places lava sources and shoots balls of lava, for EMC. Fire can't hurt you.",
    "trinket-watchofflowingtime": "While on, time runs faster (or slower) for the whole world, for EMC. It glows and shows an icon while on (.vintagee to change).",
    "gadget-destructioncatalyst": "Breaks a 3x3 tunnel into a block, up to 16 deep, for EMC.",
    "gadget-hyperkineticlens": "Shoots an explosive charge, for EMC.",
    "gadget-catalyticlens": "Breaks 3x3 tunnels up to 64 deep and shoots explosive charges, for EMC.",
    "gadget-mercurialeye": "Turns blocks into a block you picked; you pay or get back the EMC difference.",
    "gadget-archangelssmite": "Fires homing arrows at hostile creatures; left-click for a volley.",
    "gadget-tomeofknowledge": "Creative mode only: learn everything that can be transmuted.",
    "gadget-diviningrodlow": "Right-click a block to see how much EMC lies below it, 16 blocks deep.",
    "gadget-diviningrodmedium": "Right-click a block to see how much EMC lies below it, 32 blocks deep.",
    "gadget-diviningrodhigh": "Right-click a block to see how much EMC lies below it, 128 blocks deep.",
    "redmattertool-katar": "Sword, axe and shears in one. Tier 6; never wears out.",
    "redmattertool-morningstar": "Pickaxe, shovel and sledgehammer in one, 3x3 at a time. Tier 6; never wears out.",
}
for _tier, _cap in KLEIN:
    SHORT_ITEM["kleinstar-" + _tier] = f"Holds up to {_cap} EMC for your rings, amulets and gadgets."
for _m, _t in TOOLS.items():
    _tail = f"Tier {_t['tier']}; never wears out."
    SHORT_ITEM[f"mattertool-{_m}-pickaxe"] = f"Pickaxe, {_t['area']} at once with the tool-mode key. {_tail}"
    SHORT_ITEM[f"mattertool-{_m}-shovel"] = f"Shovel, {_t['area']} at once with the tool-mode key. {_tail}"
    SHORT_ITEM[f"mattertool-{_m}-axe"] = f"Axe that fells whole trees. {_tail}"
    SHORT_ITEM[f"mattertool-{_m}-hammer"] = f"Sledgehammer that smashes {('3x3 or 5x5' if _m == 'red' else '3x3')} at a time. {_tail}"
for _p in ("head", "body", "legs"):
    SHORT_ITEM[f"matterarmor-gem-{_p}"] = "99% protection. A full set makes you immune to fall and fire damage. Never wears out."

SHORT_BLOCK = {
    "transmutationtable": "Burn items into EMC to learn them, then create anything you know from your EMC.",
    "energycondenser-mk1": "Turns other items (and EMC from collectors) into copies of its target item.",
    "energycondenser-mk2": "Turns other items (and EMC from collectors) into copies of its target item, 4x as fast.",
    "novaexplosive-catalyst": "A big explosive that drops what it breaks. Light it with a torch or firestarter.",
    "novaexplosive-cataclysm": "A huge explosive that drops what it breaks. Light it with a torch or firestarter.",
    "interdictiontorch": "Pushes hostile creatures away. Needs no fuel.",
    "pedestal-empty": "Set a ring, stone, amulet or watch on it and it works on the area around it, for free.",
}
for _mk, _rate in (("mk1", 4), ("mk2", 12), ("mk3", 40)):
    SHORT_BLOCK["energycollector-" + _mk] = f"Makes up to {_rate} EMC a second from sunlight and feeds machines next to it."
    SHORT_BLOCK["antimatterrelay-" + _mk] = "Stores EMC from collectors and passes it on to machines, with a small bonus."
for _m in ("dark", "red"):
    for _state in ("cold", "lit", "extinct"):
        SHORT_BLOCK[f"furnace-{_m}-{_state}"] = f"The game's firepit made of {_m} matter: hotter, longer burning and faster. Can run on EMC."
for _ring in ("blackholeband", "swiftwolfsrendinggale", "harvestgoddessband", "ignitionring", "zeroring", "ringofarcana", "voidring"):
    SHORT_ITEM["trinket-" + _ring] += " Can be worn in your hand slot."
