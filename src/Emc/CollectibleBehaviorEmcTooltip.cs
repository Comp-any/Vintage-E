using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// Client-only behaviour attached to every block and item at load: adds "EMC: x" to the tooltip.
    /// Liquids are shown per litre (the game stores them as 100 small portions per litre, which made a litre of
    /// water look like 0.01), and containers show what their contents are worth.
    /// </summary>
    public class CollectibleBehaviorEmcTooltip : CollectibleBehavior
    {
        readonly VintageEModSystem mod;

        public CollectibleBehaviorEmcTooltip(CollectibleObject collObj, VintageEModSystem mod) : base(collObj)
        {
            this.mod = mod;
        }

        public override bool ClientSideOptional => true;

        /// <summary>Tooltips that threw, by item family, for /emc diagnose (the exception itself is swallowed so the tooltip still shows).</summary>
        public static readonly System.Collections.Generic.Dictionary<string, (int count, string last)> Failures = new System.Collections.Generic.Dictionary<string, (int, string)>();

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            try { Info(inSlot, dsc); }
            catch (Exception e)
            {
                string family = inSlot?.Itemstack?.Collectible?.Code == null ? "?" : inSlot.Itemstack.Collectible.Code.Domain + ":" + inSlot.Itemstack.Collectible.Code.Path.Split('-')[0];
                lock (Failures)
                {
                    Failures.TryGetValue(family, out var f);
                    Failures[family] = (f.count + 1, e.GetType().Name + ": " + e.Message);
                }
            }
        }

        void Info(ItemSlot inSlot, StringBuilder dsc)
        {
            ItemStack stack = inSlot?.Itemstack;
            if (stack == null) return;
            double value = mod.Emc.GetValue(stack);
            if (value > 0)
            {
                double perLitre = EmcRegistry.ItemsPerLitre(stack.Collectible);
                if (perLitre > 1)
                {
                    dsc.AppendLine(Lang.Get("vintagee:tooltip-emc-litre", EmcFormat.Format(value * perLitre)));
                    if (stack.StackSize > 1)
                    {
                        dsc.AppendLine(Lang.Get("vintagee:tooltip-emc-litres", EmcFormat.Format(stack.StackSize / perLitre), EmcFormat.Format(value * stack.StackSize)));
                    }
                }
                else
                {
                    dsc.AppendLine(Lang.Get("vintagee:tooltip-emc", EmcFormat.Format(value)));
                    if (stack.StackSize > 1)
                    {
                        dsc.AppendLine(Lang.Get("vintagee:tooltip-emc-stack", EmcFormat.Format(value * stack.StackSize)));
                    }
                }
                if (mod.Emc.IsNoTransmute(stack.Collectible)) dsc.AppendLine(Lang.Get("vintagee:tooltip-notransmute"));
            }

            StackDataTable data = mod.Emc.StackData;
            StackDataMode mode = data.ModeOf(stack.Collectible);
            bool carries = data.CarriesData(stack.Collectible);
            bool hasContents = StackContents.HasNestedStacks(stack);
            // Item only / item price: the contents aren't in the value above, so say what they're worth.
            if (hasContents && (!carries || mode != StackDataMode.Full))
            {
                double contents = mod.Emc.ContentsValue(stack);
                if (contents > 0) dsc.AppendLine(Lang.Get("vintagee:tooltip-emc-contents", EmcFormat.Format(contents)));
            }
            if (carries && StackDataUtil.KeptData(stack) != null)
            {
                string line = mode switch
                {
                    StackDataMode.Full => hasContents ? "vintagee:tooltip-data-full-contents" : "vintagee:tooltip-data-full",
                    StackDataMode.ItemPrice => hasContents ? "vintagee:tooltip-data-itemprice-contents" : "vintagee:tooltip-data-full",
                    _ => "vintagee:tooltip-data-item"
                };
                dsc.AppendLine(Lang.Get(line));
            }
        }
    }
}
