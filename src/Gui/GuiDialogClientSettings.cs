using System;
using System.Globalization;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// Window opened by the <c>.vintagee</c> command: this computer's Vintage-E settings (<see cref="VintageEClientConfig"/>).
    /// Every change shows at once and is saved straight away.
    /// </summary>
    public class GuiDialogClientSettings : GuiDialog
    {
        const double Width = 420;
        static readonly (string name, string hex)[] Colors =
        {
            ("sky", "#66CCFF"), ("gold", "#FFCC33"), ("green", "#66FF88"), ("purple", "#CC88FF"),
            ("red", "#FF5555"), ("white", "#FFFFFF"), ("custom", null)
        };

        readonly VintageEModSystem mod;
        bool composing;

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogClientSettings(ICoreClientAPI capi, VintageEModSystem mod) : base(capi)
        {
            this.mod = mod;
            Compose();
        }

        VintageEClientConfig Cfg => mod.ClientConfig;

        void Compose()
        {
            CairoFont label = CairoFont.WhiteSmallText();
            CairoFont detail = CairoFont.WhiteDetailText();
            double y = 30;
            // The look (applied when joining a world).
            ElementBounds lookHeading = ElementBounds.Fixed(0, y, Width, 24);
            y += 30;
            ElementBounds lookDrop = ElementBounds.Fixed(0, y, Width, 30);
            y += 36;
            string lookNote = Lang.Get("vintagee:clientcfg-look-note", ProjectELook.NormalizeLook(capi.World?.Config?.GetString(VintageEModSystem.WorldLookKey, null) ?? ProjectELook.LookDefault), mod.ActiveLook);
            ElementBounds lookNoteBounds = ElementBounds.Fixed(0, y, Width, GuiLayout.TextHeight(capi, detail, lookNote, Width));
            y += lookNoteBounds.fixedHeight + 14;
            ElementBounds hotSwitch = ElementBounds.Fixed(0, y, 30, 30);
            ElementBounds hotLabel = ElementBounds.Fixed(40, y + 5, Width - 40, 22);
            y += 44;
            ElementBounds heading = ElementBounds.Fixed(0, y, Width, 24);
            y += 30;
            ElementBounds iconSwitch = ElementBounds.Fixed(0, y, 30, 30);
            ElementBounds iconLabel = ElementBounds.Fixed(40, y + 5, Width - 40, 22);
            y += 38;
            ElementBounds sizeLabel = ElementBounds.Fixed(0, y + 5, 120, 22);
            ElementBounds sizeSlider = ElementBounds.Fixed(130, y + 4, Width - 130, 22);
            y += 36;
            ElementBounds posLabel = ElementBounds.Fixed(0, y + 5, 120, 22);
            ElementBounds posDrop = ElementBounds.Fixed(130, y, Width - 130, 30);
            y += 38;
            ElementBounds offLabel = ElementBounds.Fixed(0, y + 5, 120, 22);
            ElementBounds offXLabel = ElementBounds.Fixed(130, y + 5, 20, 22);
            ElementBounds offX = ElementBounds.Fixed(150, y, 100, 30);
            ElementBounds offYLabel = ElementBounds.Fixed(265, y + 5, 20, 22);
            ElementBounds offY = ElementBounds.Fixed(285, y, 100, 30);
            y += 42;
            ElementBounds glowSwitch = ElementBounds.Fixed(0, y, 30, 30);
            ElementBounds glowLabel = ElementBounds.Fixed(40, y + 5, Width - 40, 22);
            y += 38;
            ElementBounds colorLabel = ElementBounds.Fixed(0, y + 5, 120, 22);
            ElementBounds colorDrop = ElementBounds.Fixed(130, y, 150, 30);
            ElementBounds colorText = ElementBounds.Fixed(290, y, Width - 290, 30);
            y += 40;
            string note = Lang.Get("vintagee:clientcfg-note");
            ElementBounds noteBounds = ElementBounds.Fixed(0, y, Width, GuiLayout.TextHeight(capi, detail, note, Width));
            y += noteBounds.fixedHeight + 8;
            ElementBounds doneBounds = ElementBounds.Fixed(Width - 120, y, 120, 30);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(lookHeading, lookDrop, lookNoteBounds, hotSwitch, hotLabel, heading, iconSwitch, iconLabel, sizeLabel, sizeSlider, posLabel, posDrop, offLabel, offXLabel, offX, offYLabel, offY,
                glowSwitch, glowLabel, colorLabel, colorDrop, colorText, noteBounds, doneBounds);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            string[] posNames = VintageEClientConfig.Positions.Select(p => Lang.Get("vintagee:clientcfg-pos-" + p.ToLowerInvariant())).ToArray();
            int posIndex = Math.Max(0, Array.IndexOf(VintageEClientConfig.Positions, Cfg.WatchIconPosition));
            string[] colorValues = Colors.Select(c => c.hex ?? "custom").ToArray();
            string[] colorNames = Colors.Select(c => Lang.Get("vintagee:clientcfg-color-" + c.name)).ToArray();
            int colorIndex = Array.FindIndex(Colors, c => c.hex != null && c.hex.Equals(Cfg.WatchGlowColor, StringComparison.OrdinalIgnoreCase));
            if (colorIndex < 0) colorIndex = Colors.Length - 1;

            string[] lookNames = VintageEClientConfig.Looks.Select(l => Lang.Get("vintagee:clientcfg-look-" + l.ToLowerInvariant())).ToArray();
            int lookIndex = Math.Max(0, Array.IndexOf(VintageEClientConfig.Looks, Cfg.Look));

            composing = true;
            SingleComposer?.Dispose();
            SingleComposer = capi.Gui
                .CreateCompo("vintagee-clientsettings", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("vintagee:clientcfg-title"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddStaticText(Lang.Get("vintagee:clientcfg-look"), CairoFont.WhiteSmallishText(), lookHeading)
                    .AddDropDown(VintageEClientConfig.Looks, lookNames, lookIndex, (code, sel) => { if (!composing && sel) { Cfg.Look = code; Changed(); } }, lookDrop, "look")
                    .AddStaticText(lookNote, detail, lookNoteBounds)
                    .AddSwitch(on => { if (!composing) { Cfg.HotLooksReload = on; Changed(); } }, hotSwitch, "hotreload")
                    .AddStaticText(Lang.Get("vintagee:clientcfg-hotreload"), label, hotLabel)
                    .AddStaticText(Lang.Get("vintagee:clientcfg-watch"), CairoFont.WhiteSmallishText(), heading)
                    .AddSwitch(on => { Cfg.WatchIcon = on; Changed(); }, iconSwitch, "icon")
                    .AddStaticText(Lang.Get("vintagee:clientcfg-icon"), label, iconLabel)
                    .AddStaticText(Lang.Get("vintagee:clientcfg-size"), label, sizeLabel)
                    .AddSlider(v => { if (!composing) { Cfg.WatchIconSize = v; Changed(); } return true; }, sizeSlider, "size")
                    .AddStaticText(Lang.Get("vintagee:clientcfg-position"), label, posLabel)
                    .AddDropDown(VintageEClientConfig.Positions, posNames, posIndex, (code, sel) => { Cfg.WatchIconPosition = code; Changed(); }, posDrop, "position")
                    .AddStaticText(Lang.Get("vintagee:clientcfg-offset"), label, offLabel)
                    .AddStaticText("x", label, offXLabel)
                    .AddNumberInput(offX, t => { if (!composing && int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) { Cfg.WatchIconOffsetX = n; Changed(); } }, label, "offx")
                    .AddStaticText("y", label, offYLabel)
                    .AddNumberInput(offY, t => { if (!composing && int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) { Cfg.WatchIconOffsetY = n; Changed(); } }, label, "offy")
                    .AddSwitch(on => { Cfg.WatchGlow = on; Changed(); }, glowSwitch, "glow")
                    .AddStaticText(Lang.Get("vintagee:clientcfg-glow"), label, glowLabel)
                    .AddStaticText(Lang.Get("vintagee:clientcfg-color"), label, colorLabel)
                    .AddDropDown(colorValues, colorNames, colorIndex, OnColorPick, colorDrop, "colorpick")
                    .AddTextInput(colorText, OnColorTyped, label, "color")
                    .AddStaticText(note, detail, noteBounds)
                    .AddSmallButton(Lang.Get("vintagee:clientcfg-done"), () => TryClose(), doneBounds)
                .EndChildElements()
                .Compose();

            SingleComposer.GetSwitch("hotreload").SetValue(Cfg.HotLooksReload);
            SingleComposer.GetSwitch("icon").SetValue(Cfg.WatchIcon);
            SingleComposer.GetSwitch("glow").SetValue(Cfg.WatchGlow);
            SingleComposer.GetSlider("size").SetValues(Cfg.WatchIconSize, 16, 128, 4, "px");
            SingleComposer.GetNumberInput("offx").SetValue(Cfg.WatchIconOffsetX.ToString(CultureInfo.InvariantCulture));
            SingleComposer.GetNumberInput("offy").SetValue(Cfg.WatchIconOffsetY.ToString(CultureInfo.InvariantCulture));
            SingleComposer.GetTextInput("color").SetValue(Cfg.WatchGlowColor);
            composing = false;
        }

        void OnColorPick(string code, bool selected)
        {
            if (composing || code == "custom") return;
            Cfg.WatchGlowColor = code;
            composing = true;
            SingleComposer.GetTextInput("color").SetValue(code);
            composing = false;
            Changed();
        }

        void OnColorTyped(string text)
        {
            if (composing || !VintageEClientConfig.TryParseColor(text, out _)) return;
            Cfg.WatchGlowColor = text.Trim();
            Changed();
        }

        void Changed()
        {
            if (composing) return;
            mod.OnClientConfigChanged();
        }
    }
}
