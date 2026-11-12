using System.Collections.Generic;
using Vintagestory.API.Common;

namespace VintageE
{
    public enum ConversionKind
    {
        /// <summary>Player-made recipe (grid, smithing, knapping, clay forming, barrel, alloy...).</summary>
        Crafting,
        /// <summary>One item turning into another through heat, grinding, crushing, drying, curing, ripening...</summary>
        Processing
    }

    /// <summary>One way of getting an item, e.g. "4 copper nuggets + 1 tin nugget -> 1 bronze".</summary>
    public sealed class Conversion
    {
        public CollectibleObject Output;
        public double OutputQuantity;
        public List<ConversionInput> Inputs = new List<ConversionInput>();
        /// <summary>Things handed back after crafting (e.g. an empty bucket); their value is subtracted.</summary>
        public List<KeyValuePair<CollectibleObject, double>> Returned;
        /// <summary>
        /// Set for processes with several outputs (squeezing honeycomb → honey + beeswax, ground-storage processing, the
        /// fruit press, other mods' crafting methods with several results and none marked main). What goes in is shared
        /// out among these instead of Output. Most have one input; a crafting method's may have several (see
        /// <see cref="IsSimpleJoint"/>).
        /// </summary>
        public List<KeyValuePair<CollectibleObject, double>> JointOutputs;

        /// <summary>Flat EMC added to every run on top of the inputs (e.g. the drying-time bonus, a machine's power).</summary>
        public double ExtraCost;
        /// <summary>
        /// Data the recipe puts on its output (a round shield's metal and wood, a chest's wood type). The plain item takes
        /// the cheapest recipe; this one also gives a value for that exact version.
        /// </summary>
        public Vintagestory.API.Datastructures.TreeAttribute OutputData;
        public ConversionKind Kind;
        public string Source;

        /// <summary>Set for a recipe another mod sent as a crafting method (see <see cref="MethodRegistry"/>): which one, and what it costs besides its ingredients.</summary>
        public MethodRef Method;
        /// <summary>The parts of <see cref="ExtraCost"/> (power, heat, machine time, flat EMC), for the explanations. Null for most recipes.</summary>
        public List<MethodCostLine> Costs;

        /// <summary>A shared-out process with one ingredient slot, one choice, nothing handed back and no extra cost: the old kind the solver has always handled.</summary>
        /// <remarks>Other mods' crafting methods never take this path: theirs are costed forwards, the cheapest share winning, and a crafting-kind one is only worked backwards as crafting is.</remarks>
        public bool IsSimpleJoint => JointOutputs != null && Method == null && Inputs.Count == 1 && Inputs[0].Choices.Length == 1 && Returned == null && ExtraCost == 0 && !Inputs[0].NoInfer;

        public override string ToString() => $"{Source}: {string.Join(" + ", Inputs)} -> {OutputQuantity}x {Output?.Code}";
    }

    /// <summary>One ingredient slot. When it accepts several items (wildcards, tags) the cheapest valued one is used.</summary>
    public sealed class ConversionInput
    {
        public CollectibleObject[] Choices;
        public double Quantity;
        /// <summary>
        /// Never work this slot's value out backwards from the recipe: a tool's wear (a thousandth of a hammer), where the
        /// rest of the recipe's value would make the tool worth a thousand times what's left.
        /// </summary>
        public bool NoInfer;
        /// <summary>What the slot stands for in an explanation (a tool's wear, an ingredient used up 50% of the time); null for a plain ingredient.</summary>
        public string Note;

        public override string ToString() => Choices.Length == 1 ? $"{Quantity}x {Choices[0].Code}" : $"{Quantity}x [{Choices.Length} options]";
    }

    /// <summary>Which crafting method and recipe a conversion came from.</summary>
    public sealed class MethodRef
    {
        public MethodDef Method;
        public MethodRecipe Recipe;
        /// <summary>What each named * stood for in this copy of the recipe (metal = copper), or null.</summary>
        public Dictionary<string, string> Captures;
        /// <summary>Tools the recipe uses but doesn't wear out (catalysts), for the explanation: they cost nothing.</summary>
        public List<CollectibleObject> Catalysts;
        /// <summary>Notes per output for the explanation ("25% chance: counts as 0.25"), by output.</summary>
        public Dictionary<CollectibleObject, string> OutputNotes;
        /// <summary>Outputs other than the main one, which go into Returned (their value is taken off).</summary>
        public HashSet<CollectibleObject> Byproducts;
        public string ModId => Method?.ModId;
    }

    /// <summary>One extra cost of a crafting method's recipe, priced in EMC.</summary>
    public sealed class MethodCostLine
    {
        /// <summary>energy, heat, time or emc.</summary>
        public string Kind;
        /// <summary>The energy unit's code (energy only).</summary>
        public string Unit;
        /// <summary>How much: units of energy, °C·s of heat, seconds of machine time, EMC.</summary>
        public double Amount;
        /// <summary>EMC per unit of Amount.</summary>
        public double Price;
        /// <summary>Price × Amount, as charged (0 when that kind of cost is switched off).</summary>
        public double Emc;
        /// <summary>Where the price came from, in words.</summary>
        public string PriceSource;
        /// <summary>False when the server's ChargeMethod* setting switched this kind of cost off.</summary>
        public bool Charged = true;
    }
}
