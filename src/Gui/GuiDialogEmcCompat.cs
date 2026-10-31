using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// Window opened by /emc compat: every compatibility value file Vintage-E ships (last-resort values for other mods'
    /// raw materials: fish, foraged plants, creature drops, new ores), whether its mod is loaded, how many items it
    /// valued in the last mapping, and a switch to disable or enable it. A switch changes the DisabledCompat setting
    /// in the server config and remaps.
    /// </summary>
    public class GuiDialogEmcCompat : GuiDialog
    {
        const double ListWidth = 620;
        const double ListHeight = 340;

        readonly Action<string, bool> onSet;   // (key, disabled)
        EmcCompatListPacket packet;
        ElementBounds clipBounds;

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogEmcCompat(ICoreClientAPI capi, EmcCompatListPacket packet, Action<string, bool> onSet) : base(capi)
        {
            this.packet = packet;
            this.onSet = onSet;
            Compose();
        }

        /// <summary>A new list from the server (after a switch, with its message).</summary>
        public void Update(EmcCompatListPacket newPacket)
        {
            packet = newPacket;
            Compose();
        }

        int Count => packet.Keys?.Length ?? 0;

        void Compose()
        {
            ClearComposers();
            CairoFont detail = CairoFont.WhiteDetailText();
            int active = 0, notLoaded = 0, disabled = 0;
            for (int i = 0; i < Count; i++)
            {
                if (Get(packet.Disabled, i)) disabled++;
                else if (!string.IsNullOrEmpty(packet.MissingMods?.ElementAtOrDefault(i))) notLoaded++;
                else active++;
            }
            double top = GuiStyle.TitleBarHeight + 6;
            string help = Lang.Get("vintagee:compat-help", Count, active, notLoaded, disabled);
            double helpHeight = GuiLayout.TextHeight(capi, detail, help, ListWidth + 30);
            ElementBounds helpBounds = ElementBounds.Fixed(0, top, ListWidth + 30, helpHeight);
            double row = top + helpHeight + 8;

            ElementBounds listBounds = ElementBounds.Fixed(0, row, ListWidth, ListHeight);
            clipBounds = listBounds.ForkBoundingParent();
            ElementBounds insetBounds = listBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);
            ElementBounds scrollbarBounds = insetBounds.CopyOffsetedSibling(insetBounds.fixedWidth + 4).WithFixedWidth(20);

            double bottom = row + ListHeight + 14;
            string message = packet.Message ?? "";
            CairoFont messageFont = packet.Error ? CairoFont.WhiteDetailText().WithColor(new double[] { 1, 0.45, 0.45, 1 }) : detail;
            double messageHeight = Math.Max(24, GuiLayout.TextHeight(capi, messageFont, message, ListWidth - 100));
            ElementBounds messageBounds = ElementBounds.Fixed(0, bottom + 4, ListWidth - 100, messageHeight);
            ElementBounds closeBounds = ElementBounds.Fixed(ListWidth - 90, bottom, 90, 30);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(helpBounds, clipBounds, messageBounds, closeBounds);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            SingleComposer = capi.Gui
                .CreateCompo("vintagee-compat", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("vintagee:compat-title"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddStaticText(help, detail, helpBounds)
                    .BeginClip(clipBounds)
                        .AddInset(insetBounds, 3)
                        .AddRichtext(Array.Empty<RichTextComponentBase>(), listBounds, "list")
                    .EndClip()
                    .AddVerticalScrollbar(OnScroll, scrollbarBounds, "scrollbar")
                    .AddDynamicText(message, messageFont, messageBounds, "message")
                    .AddSmallButton(Lang.Get("vintagee:missing-close"), () => TryClose(), closeBounds)
                .EndChildElements()
                .Compose();
            Fill();
        }

        static bool Get(bool[] arr, int i) => arr != null && i < arr.Length && arr[i];
        static int Get(int[] arr, int i) => arr != null && i < arr.Length ? arr[i] : 0;

        void Fill()
        {
            try
            {
                var comps = new List<RichTextComponentBase>();
                CairoFont nameFont = CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold);
                CairoFont offFont = CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold).WithColor(new double[] { 0.6, 0.6, 0.6, 1 });
                CairoFont activeFont = CairoFont.WhiteDetailText().WithColor(new double[] { 0.55, 1, 0.55, 1 });
                CairoFont idleFont = CairoFont.WhiteDetailText().WithColor(new double[] { 0.7, 0.7, 0.7, 1 });
                CairoFont disabledFont = CairoFont.WhiteDetailText().WithColor(new double[] { 1, 0.85, 0.45, 1 });
                CairoFont linkFont = CairoFont.WhiteSmallText().WithColor(new double[] { 0.6, 0.8, 1, 1 });
                CairoFont descFont = CairoFont.WhiteDetailText().WithColor(new double[] { 0.75, 0.75, 0.75, 1 });
                for (int i = 0; i < Count; i++)
                {
                    string key = packet.Keys[i];
                    string name = packet.Names?.ElementAtOrDefault(i) ?? key;
                    string mods = packet.Mods?.ElementAtOrDefault(i) ?? "";
                    string missing = packet.MissingMods?.ElementAtOrDefault(i) ?? "";
                    bool disabled = Get(packet.Disabled, i);
                    int used = Get(packet.UsedCounts, i), rules = Get(packet.RuleCounts, i);
                    string state;
                    CairoFont stateFont;
                    if (disabled) { state = Lang.Get("vintagee:compat-state-disabled"); stateFont = disabledFont; }
                    else if (missing.Length > 0) { state = Lang.Get("vintagee:compat-state-notloaded", missing); stateFont = idleFont; }
                    else { state = Lang.Get("vintagee:compat-state-active", used); stateFont = activeFont; }

                    comps.Add(new RichTextComponent(capi, (disabled ? "○ " : missing.Length > 0 ? "· " : "● ") + name + "  ", disabled ? offFont : nameFont));
                    comps.Add(new RichTextComponent(capi, state + "  ", stateFont));
                    string action = Lang.Get(disabled ? "vintagee:compat-enable" : "vintagee:compat-disable");
                    bool nowDisabled = disabled;
                    comps.Add(new LinkTextComponent(capi, "[" + action + "]", linkFont, _ => Switch(key, !nowDisabled)));
                    comps.Add(new RichTextComponent(capi, "\n" + Lang.Get("vintagee:compat-line", mods, rules) + "\n", descFont));
                    string desc = packet.Descriptions?.ElementAtOrDefault(i) ?? "";
                    if (desc.Length > 0) comps.Add(new RichTextComponent(capi, desc + "\n", descFont));
                    comps.Add(new RichTextComponent(capi, "\n", descFont));
                }
                if (Count == 0) comps.Add(new RichTextComponent(capi, Lang.Get("vintagee:compat-none"), CairoFont.WhiteSmallText()));

                GuiElementRichtext list = SingleComposer.GetRichtext("list");
                if (list == null) return;
                list.SetNewText(comps.ToArray());
                list.Bounds.CalcWorldBounds();
                GuiElementScrollbar bar = SingleComposer.GetScrollbar("scrollbar");
                if (bar != null)
                {
                    bar.Bounds.CalcWorldBounds();
                    bar.SetHeights((float)ListHeight, (float)Math.Max(ListHeight, list.Bounds.fixedHeight));
                    bar.CurrentYPosition = 0;
                }
                list.Bounds.fixedY = 0;
                list.Bounds.CalcWorldBounds();
            }
            catch (Exception ex)
            {
                capi.Logger.Error("Vintage-E: could not fill the compatibility list: {0}", ex);
            }
        }

        void Switch(string key, bool disabled)
        {
            SingleComposer?.GetDynamicText("message")?.SetNewText(Lang.Get("vintagee:compat-working"));
            onSet?.Invoke(key, disabled);
        }

        void OnScroll(float value)
        {
            GuiElementRichtext list = SingleComposer?.GetRichtext("list");
            if (list == null) return;
            list.Bounds.fixedY = -value;
            list.Bounds.CalcWorldBounds();
        }
    }
}
