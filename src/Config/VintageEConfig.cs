using System;
using System.Collections.Generic;

namespace VintageE
{
    /// <summary>
    /// Server settings, stored in ModConfig/vintagee.json. Created with defaults on first launch.
    /// Base EMC values live in assets (config/vintagee-emc.json) so other mods can ship or patch them;
    /// the dictionaries here are for the server owner and always win over asset values.
    /// </summary>
    public class VintageEConfig
    {
        /// <summary>Extra base values. Keys are codes or wildcards ("game:rock-*", "block:game:log-*", "item:*:gear-*"). Applied after all asset files, so they override them.</summary>
        [System.ComponentModel.Description("Extra base values. Keys are codes or wildcards (\"game:rock-*\", \"block:game:log-*\", \"item:*:gear-*\"). Applied after all asset files, so they override them.")]
        public Dictionary<string, double> BaseValues = new Dictionary<string, double>();

        /// <summary>Hard overrides set by hand or with /emc set. Applied last; recipes never change them. A value of 0 removes the item's EMC entirely.</summary>
        [System.ComponentModel.Description("Hard overrides set by hand or with /emc set. Applied last; recipes never change them. A value of 0 removes the item's EMC entirely.")]
        public Dictionary<string, double> FixedValues = new Dictionary<string, double>();

        /// <summary>Codes/wildcards that never get an EMC value, on top of the asset blacklist. The /emc missing window's Blacklist button adds to this.</summary>
        [System.ComponentModel.Description("Codes/wildcards that never get an EMC value, on top of the asset blacklist. The /emc missing window's Blacklist button adds to this.")]
        public List<string> Blacklist = new List<string>();

        /// <summary>Exceptions to the built-in (asset) blacklist. The /emc blacklist window's "Remove from blacklist" button adds to this.</summary>
        [System.ComponentModel.Description("Exceptions to the built-in (asset) blacklist. The /emc blacklist window's \"Remove from blacklist\" button adds to this.")]
        public List<string> Allow = new List<string>();

        /// <summary>Ignore the default value file shipped with this mod (assets/vintagee/config/vintagee-emc.json). Other mods' files still load.</summary>
        [System.ComponentModel.Description("Ignore the default value file shipped with this mod (assets/vintagee/config/vintagee-emc.json). Other mods' files still load.")]
        public bool DisableBuiltInBaseValues = false;

        /// <summary>Compatibility value files to leave out, by file name ("primitivesurvival") or asset location. These files give other mods' raw materials last-resort values; /emc compat lists them and switches them here.</summary>
        [System.ComponentModel.Description("Compatibility value files to leave out, by file name (\"primitivesurvival\") or asset location. These files give other mods' raw materials last-resort values; /emc compat lists them and switches them here.")]
        public List<string> DisabledCompat = new List<string>();

        /// <summary>When an ore, raw food etc. has no value but what it smelts/grinds/crushes/dries into does, work the input value out backwards.</summary>
        [System.ComponentModel.Description("When an ore, raw food etc. has no value but what it smelts/grinds/crushes/dries into does, work the input value out backwards.")]
        public bool InferValuesFromProcessing = true;

        /// <summary>
        /// Also work values out backwards from crafting: if a recipe's result has a value and only one of its ingredients
        /// doesn't, that ingredient is worth what's left over (e.g. a loot-only part used in a recipe). Uses the highest
        /// such value so nothing already valued gets cheaper. Needs InferValuesFromProcessing.
        /// </summary>
        [System.ComponentModel.Description("Also work values out backwards from crafting: if a recipe's result has a value and only one of its ingredients doesn't, that ingredient is worth what's left over (e.g. a loot-only part used in a recipe). Uses the highest such value so nothing already valued gets cheaper. Needs InferValuesFromProcessing.")]
        public bool InferValuesFromCrafting = true;

        /// <summary>As a last resort, give anything still unvalued the value of its closest variant (e.g. the same ore in a different rock). Uses the highest sibling value, so it can't be exploited.</summary>
        [System.ComponentModel.Description("As a last resort, give anything still unvalued the value of its closest variant (e.g. the same ore in a different rock). Uses the highest sibling value, so it can't be exploited.")]
        public bool InferFromVariantSiblings = true;

        /// <summary>
        /// For things nothing else can value: fuels by how hot and long they burn (compared with charcoal and coal),
        /// food by how filling it is (compared with valued food of the same kind).
        /// </summary>
        [System.ComponentModel.Description("For things nothing else can value: fuels by how hot and long they burn (compared with charcoal and coal), food by how filling it is (compared with valued food of the same kind).")]
        public bool EstimateFuelAndFood = true;

        /// <summary>
        /// Blocks/items that hold things or are made of something stored on them (buckets, pots, bags, round shields,
        /// lanterns, chiseled blocks...): code or wildcard → "item" (item only: burns and is created as the plain item),
        /// "full" (item + contents: worth both, and you learn and create exactly that version) or "itemprice" (exactly that
        /// version, but it costs and burns for the item only - its contents are free). Overrides the built-in choice.
        /// Easiest to change with /emc contents.
        /// </summary>
        [System.ComponentModel.Description("Blocks/items that hold things or are made of something stored on them (buckets, pots, bags, round shields, lanterns, chiseled blocks...): code or wildcard → \"item\" (item only: burns and is created as the plain item), \"full\" (item + contents: worth both, and you learn and create exactly that version) or \"itemprice\" (exactly that version, but it costs and burns for the item only - its contents are free). Overrides the built-in choice. Easiest to change with /emc contents.")]
        public Dictionary<string, string> StackDataModes = new Dictionary<string, string>();

        /// <summary>How many exact versions (a filled bucket, a gold shield, a chiseled block...) one player can know.</summary>
        [System.ComponentModel.Description("How many exact versions (a filled bucket, a gold shield, a chiseled block...) one player can know.")]
        public int MaxExactKnowledge = 500;

        /// <summary>
        /// Value things nothing else can value (clothes, loot armour, decorations...) from what traders buy and sell them for.
        /// The worth of one rusty gear is measured on traded items that already have a value, so prices stay in line with everything else.
        /// </summary>
        [System.ComponentModel.Description("Value things nothing else can value (clothes, loot armour, decorations...) from what traders buy and sell them for. The worth of one rusty gear is measured on traded items that already have a value, so prices stay in line with everything else.")]
        public bool UseTraderPrices = true;

        /// <summary>
        /// Flat EMC value given to any creative-inventory block/item that still has no value after everything else (0 = off).
        /// Careful: anything valued this way can be created for that price, so a low number makes rare loot cheap.
        /// Check the server log or /emc missing first and prefer giving those items real values in BaseValues.
        /// </summary>
        [System.ComponentModel.Description("Flat EMC value given to any creative-inventory block/item that still has no value after everything else (0 = off). Careful: anything valued this way can be created for that price, so a low number makes rare loot cheap. Check the server log or /emc missing first and prefer giving those items real values in BaseValues.")]
        public double FallbackValue = 0;

        /// <summary>
        /// Save mapped values per world (ModData/vintagee/cache) and reuse them on the next start when the game version,
        /// mods, config, value files and all recipe/item data are unchanged. /emc recalc always remaps.
        /// </summary>
        [System.ComponentModel.Description("Save mapped values per world (ModData/vintagee/cache) and reuse them on the next start when the game version, mods, config, value files and all recipe/item data are unchanged. /emc recalc always remaps.")]
        public bool CacheEmcValues = true;

        /// <summary>
        /// Extra EMC added per in-game day an item takes to transform on its own, by transition type
        /// (dry, cure, ripen, harden, melt, convert). The time counted is fresh hours + transition hours.
        /// Example: tall grass that takes 1 day to become dry grass makes the dry grass worth 64 more. 0 or missing = no bonus.
        /// </summary>
        [System.ComponentModel.Description("Extra EMC added per in-game day an item takes to transform on its own, by transition type (dry, cure, ripen, harden, melt, convert). The time counted is fresh hours + transition hours. Example: tall grass that takes 1 day to become dry grass makes the dry grass worth 64 more. 0 or missing = no bonus.")]
        public Dictionary<string, double> TransitionEmcPerDay = new Dictionary<string, double> { ["dry"] = 64 };

        /// <summary>Upper bound on relaxation passes over the recipe graph. Values still falling after this are recipe loops and get dropped.</summary>
        [System.ComponentModel.Description("Upper bound on relaxation passes over the recipe graph. Values still falling after this are recipe loops and get dropped.")]
        public int MaxMappingPasses = 120;

        /// <summary>Scale a damaged tool's EMC by its remaining durability.</summary>
        [System.ComponentModel.Description("Scale a damaged tool's EMC by its remaining durability.")]
        public bool DurabilityScalesEmc = true;

        /// <summary>Write every mapped value to the log at startup (verbose).</summary>
        [System.ComponentModel.Description("Write every mapped value to the log at startup (verbose).")]
        public bool LogAllValues = false;

        /// <summary>
        /// Write ModData/vintagee/emc-values.csv (every value and what it was worked out from) after each mapping,
        /// like /emc dump does. Handy for modpack authors checking values.
        /// </summary>
        [System.ComponentModel.Description("Write ModData/vintagee/emc-values.csv (every value and what it was worked out from) after each mapping, like /emc dump does. Handy for modpack authors checking values.")]
        public bool DumpValuesOnStart = false;

        /// <summary>How close (in blocks) a player must stay to the transmutation table they opened.</summary>
        [System.ComponentModel.Description("How close (in blocks) a player must stay to the transmutation table they opened.")]
        public double TableUseRange = 8;

        /// <summary>Multiplies every collector's output and every condenser's speed. Per-tier base rates live in the block JSON attributes.</summary>
        [System.ComponentModel.Description("Multiplies every collector's output and every condenser's speed. Per-tier base rates live in the block JSON attributes.")]
        public double MachineSpeedMultiplier = 1.0;

        /// <summary>Fraction of output a collector keeps at night or underground (0 = nothing without sun).</summary>
        [System.ComponentModel.Description("Fraction of output a collector keeps at night or underground (0 = nothing without sun).")]
        public double CollectorDarkFactor = 0.0;

        /// <summary>Allow sneak + right-click with an empty hand on a collector to move its stored EMC into your personal EMC.</summary>
        [System.ComponentModel.Description("Allow sneak + right-click with an empty hand on a collector to move its stored EMC into your personal EMC.")]
        public bool CollectorsChargePlayers = true;

        /// <summary>Refuse to burn bags, pots and other containers that still hold something.</summary>
        [System.ComponentModel.Description("Refuse to burn bags, pots and other containers that still hold something.")]
        public bool ProtectContainersWithContents = true;

        /// <summary>
        /// Post a chat line with the mod version and how many EMC values were calculated / are missing,
        /// to each player when they join and to everyone after /emc recalc, set or clear.
        /// </summary>
        [System.ComponentModel.Description("Post a chat line with the mod version and how many EMC values were calculated / are missing, to each player when they join and to everyone after /emc recalc, set or clear.")]
        public bool AnnounceInChat = true;

        /// <summary>
        /// Creatures the Ignition Ring burns, the Interdiction Torch repels and the Archangel's Smite targets
        /// (entity codes, wildcards allowed). Livestock and players are never affected.
        /// </summary>
        [System.ComponentModel.Description("Creatures the Ignition Ring burns, the Interdiction Torch repels and the Archangel's Smite targets (entity codes, wildcards allowed). Livestock and players are never affected.")]
        public List<string> HostileCreatures = new List<string>
        {
            "game:drifter-*", "game:locust-*", "game:bell-*", "game:shiver-*", "game:bowtorn-*", "game:wolf-*", "game:bear-*",
            "game:hyena-*", "game:eidolon-*", "game:erel-*"
        };

        /// <summary>
        /// The Watch of Flowing Time speeds up (or slows down) time for the whole world while someone has one switched on,
        /// the way sleeping does. Set to false to make it only affect the area around its owner (crops, creatures).
        /// </summary>
        [System.ComponentModel.Description("The Watch of Flowing Time speeds up (or slows down) time for the whole world while someone has one switched on, the way sleeping does. Set to false to make it only affect the area around its owner (crops, creatures).")]
        public bool WatchChangesWorldTime = true;

        /// <summary>How many times faster time runs while a Watch of Flowing Time is on in "faster" mode.</summary>
        [System.ComponentModel.Description("How many times faster time runs while a Watch of Flowing Time is on in \"faster\" mode.")]
        public double WatchSpeedUp = 10;

        /// <summary>How many times slower time runs while a Watch of Flowing Time is on in "slower" mode.</summary>
        [System.ComponentModel.Description("How many times slower time runs while a Watch of Flowing Time is on in \"slower\" mode.")]
        public double WatchSlowDown = 3;

        /// <summary>Development only: builds a few test furnaces/projectiles near spawn after start-up and writes ModData/vintagee/selftest.txt. Used by the GitHub server test.</summary>
        [System.ComponentModel.Description("Development only: builds a few test furnaces/projectiles near spawn after start-up and writes ModData/vintagee/selftest.txt. Used by the GitHub server test.")]
        public bool SelfTestOnStart = false;

        /// <summary>The Repair Talisman also restores clothing condition (clothes you wear or carry). False turns that off.</summary>
        [System.ComponentModel.Description("The Repair Talisman also restores clothing condition (clothes you wear or carry). False turns that off.")]
        public bool RepairTalismanRepairsClothing = true;

        /// <summary>How fast the Repair Talisman restores clothing condition, in percent per minute.</summary>
        [System.ComponentModel.Description("How fast the Repair Talisman restores clothing condition, in percent per minute.")]
        public double RepairTalismanClothingPercentPerMinute = 1;

        /// <summary>Clothes below this condition (percent) are left for a sewing kit. 0 = it repairs clothes in any condition.</summary>
        [System.ComponentModel.Description("Clothes below this condition (percent) are left for a sewing kit. 0 = it repairs clothes in any condition.")]
        public double RepairTalismanClothingMinCondition = 0;

        /// <summary>The Repair Talisman stops restoring clothing at this condition (percent). 100 = good as new.</summary>
        [System.ComponentModel.Description("The Repair Talisman stops restoring clothing at this condition (percent). 100 = good as new.")]
        public double RepairTalismanClothingMaxCondition = 100;

        /// <summary>Multiplies the EMC that rings, stones, amulets and gadgets cost to run.</summary>
        [System.ComponentModel.Description("Multiplies the EMC that rings, stones, amulets and gadgets cost to run.")]
        public double TrinketCostMultiplier = 1.0;

        /// <summary>Codes or wildcards of blocks/items that keep their EMC value (recipes still use it) but can't be burned for EMC, learned or created from EMC: not at the transmutation table, in condensers, or by the tablet's magnet. Change it in game with /emc notransmute.</summary>
        [System.ComponentModel.Description("Codes or wildcards of blocks/items that keep their EMC value (recipes still use it) but can't be burned for EMC, learned or created from EMC: not at the transmutation table, in condensers, or by the tablet's magnet. Change it in game with /emc notransmute.")]
        public List<string> NoTransmute = new List<string>();

        /// <summary>Codes or wildcards that have no EMC value themselves (they can't be burned or created) but cost nothing in recipes that use them, like a schematic that isn't used up. Added to the value files' freeIngredients.</summary>
        [System.ComponentModel.Description("Codes or wildcards that have no EMC value themselves (they can't be burned or created) but cost nothing in recipes that use them, like a schematic that isn't used up. Added to the value files' freeIngredients.")]
        public List<string> FreeIngredients = new List<string>();

        /// <summary>Codes or wildcards of blocks/items on the gray list: they're left out of /emc missing and nothing guesses a value for them, but as soon as any recipe can make them (another mod adds one, say) they get its value. For things nothing makes yet that might be used later. Change it in game with /emc graylist.</summary>
        [System.ComponentModel.Description("Codes or wildcards of blocks/items on the gray list: they're left out of /emc missing and nothing guesses a value for them, but as soon as any recipe can make them (another mod adds one, say) they get its value. For things nothing makes yet that might be used later. Change it in game with /emc graylist.")]
        public List<string> GrayList = new List<string>();

        /// <summary>Recipes of your own, for things the mapper can't see being made (another mod's boats built on the spot, say). One per entry: "output = ingredients", like "item:shipwright:longship-oak = 40 game:plank-oak + 8 game:rope"; a number before a code is how many (2 game:stick), and an ingredient may be a wildcard (the cheapest match counts). Easiest made with the /emc recipe window.</summary>
        [System.ComponentModel.Description("Recipes of your own, for things the mapper can't see being made (another mod's boats built on the spot, say). One per entry: \"output = ingredients\", like \"item:shipwright:longship-oak = 40 game:plank-oak + 8 game:rope\"; a number before a code is how many (2 game:stick), and an ingredient may be a wildcard (the cheapest match counts). Easiest made with the /emc recipe window.")]
        public List<string> CustomRecipes = new List<string>();

        /// <summary>How Vintage-E's blocks and items look. "Default": Vintage-E's own textures. "Original": ProjectE's original textures wherever ProjectE has one (rings and stones also light up while switched on, like in ProjectE). "New": models in Vintage Story's own style, built from the game's planks, metals, stone and gems. Takes effect the next time the server starts; players don't need to change anything.</summary>
        [System.ComponentModel.Description("How Vintage-E's blocks and items look: Default (Vintage-E's own textures), Original (ProjectE's original textures; rings and stones light up while switched on) or New (models in Vintage Story's own style, built from the game's planks, metals, stone and gems). Takes effect the next time the server starts; players don't need to change anything.")]
        public string Look = ProjectELook.LookDefault;

        /// <summary>Before 1.13.0 the look was the switch ProjectETextures; true there means Look = Original. Read once, then dropped from the file.</summary>
        [Newtonsoft.Json.JsonProperty("ProjectETextures", NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        bool? legacyProjectETextures;

        /// <summary>Use ProjectE's original sounds (charge, uncharge, heal, power, destruct, transmute, water and wind magic) for the things ProjectE plays them for. False = Vintage-E's own sounds. Takes effect straight away.</summary>
        [System.ComponentModel.Description("Use ProjectE's original sounds (charge, uncharge, heal, power, destruct, transmute, water and wind magic) for the things ProjectE plays them for. False = Vintage-E's own sounds. Takes effect straight away.")]
        public bool ProjectESounds = false;

        public void Normalize()
        {
            BaseValues ??= new Dictionary<string, double>();
            FixedValues ??= new Dictionary<string, double>();
            Blacklist ??= new List<string>();
            Allow ??= new List<string>();
            FreeIngredients ??= new List<string>();
            NoTransmute ??= new List<string>();
            GrayList ??= new List<string>();
            CustomRecipes ??= new List<string>();
            TransitionEmcPerDay ??= new Dictionary<string, double>();
            HostileCreatures ??= new List<string>();
            StackDataModes ??= new Dictionary<string, string>();
            if (MaxExactKnowledge < 0) MaxExactKnowledge = 0;
            RepairTalismanClothingPercentPerMinute = Math.Max(0, RepairTalismanClothingPercentPerMinute);
            RepairTalismanClothingMinCondition = Math.Clamp(RepairTalismanClothingMinCondition, 0, 100);
            RepairTalismanClothingMaxCondition = Math.Clamp(RepairTalismanClothingMaxCondition, RepairTalismanClothingMinCondition, 100);
            if (legacyProjectETextures == true && ProjectELook.NormalizeLook(Look) == ProjectELook.LookDefault) Look = ProjectELook.LookOriginal;
            legacyProjectETextures = null;
            Look = ProjectELook.NormalizeLook(Look);
            if (TrinketCostMultiplier < 0) TrinketCostMultiplier = 0;
            if (WatchSpeedUp < 1) WatchSpeedUp = 1;
            if (WatchSlowDown < 1) WatchSlowDown = 1;
            if (MachineSpeedMultiplier <= 0) MachineSpeedMultiplier = 1.0;
            if (MaxMappingPasses < 4) MaxMappingPasses = 4;
            if (TableUseRange < 2) TableUseRange = 2;
        }
    }
}
