using System;
using System.Globalization;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// The small box the "Set EMC value" hotkey opens for the block or item under the mouse (see <see cref="HoveredStack"/>):
    /// its value, and boxes for the blacklist, the gray list and free ingredients, as the server has them now. Change any
    /// and press Save or Enter: the server applies them all and works the values out once. Unpin takes a value set by hand
    /// off again, so the item goes back to what recipes give it. Admins only; the server checks too.
    /// </summary>
    public class GuiDialogEmcSetValue : GuiDialog
    {
        const double Width = 360;

        readonly ItemStack stack;
        readonly EmcItemStatePacket state;
        readonly Action<EmcItemEditPacket> onSave;
        readonly Action onExplain;
        readonly DisplayInventory iconInventory;
        string initialText = "";

        public override string ToggleKeyCombinationCode => null;
        /// <summary>Above the handbook (0.2) and container windows.</summary>
        public override double DrawOrder => 0.3;
        public override double InputOrder => 0.3;
        public override bool UnregisterOnClose => true;

        public GuiDialogEmcSetValue(ICoreClientAPI capi, ItemStack stack, EmcItemStatePacket state, Action<EmcItemEditPacket> onSave, Action onExplain = null) : base(capi)
        {
            this.onExplain = onExplain;
            this.stack = stack.Clone();
            this.stack.StackSize = 1;
            this.state = state;
            this.onSave = onSave;
            iconInventory = new DisplayInventory(capi, "setvalue-" + Guid.NewGuid().ToString("N"), 1);
            iconInventory[0].Itemstack = this.stack;
            Compose();
        }

        bool FromFiles(string what) => state.FromFiles != null && state.FromFiles.Contains(what);

        void Compose()
        {
            CairoFont detail = CairoFont.WhiteDetailText();
            CairoFont label = CairoFont.WhiteSmallText();
            CairoFont grey = CairoFont.WhiteDetailText().WithColor(new double[] { 0.75, 0.75, 0.75, 1 });
            string name;
            try { name = stack.GetName(); } catch { name = stack.Collectible?.Code?.ToShortString() ?? "?"; }
            string code = (stack.Class == EnumItemClass.Block ? "block:" : "item:") + stack.Collectible.Code;
            string now = state.Free ? Lang.Get("vintagee:setvalue-nowfree")
                : state.Blacklisted ? Lang.Get("vintagee:setvalue-nowblacklisted")
                : state.Value > 0 ? Lang.Get(state.Pinned ? "vintagee:setvalue-nowpinned" : "vintagee:setvalue-now", EmcFormat.Format(state.Value))
                : Lang.Get("vintagee:setvalue-none");
            string info = name + "\n" + code + "\n" + now;
            double textX = 60;
            double infoHeight = Math.Max(52, GuiLayout.TextHeight(capi, detail, info, Width - textX));

            ElementBounds slotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 30, 1, 1);
            ElementBounds infoBounds = ElementBounds.Fixed(textX, 30, Width - textX, infoHeight);
            double y = 30 + infoHeight + 10;
            ElementBounds labelBounds = ElementBounds.Fixed(0, y + 6, 70, 20);
            ElementBounds valueBounds = ElementBounds.Fixed(75, y, Width - 75, 30);
            y += 40;

            // The three lists, each a switch with its label (and a note when the value files decide it).
            (string key, string text, string note)[] rows =
            {
                ("blacklist", Lang.Get("vintagee:setvalue-blacklist"), null),
                ("gray", Lang.Get("vintagee:setvalue-gray"), FromFiles("gray") ? Lang.Get("vintagee:setvalue-fromfiles") : null),
                ("free", Lang.Get("vintagee:setvalue-free"), FromFiles("free") ? Lang.Get("vintagee:setvalue-fromfiles") : null),
            };
            var rowBounds = new (ElementBounds sw, ElementBounds lab)[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                string text = rows[i].text + (rows[i].note == null ? "" : "\n" + rows[i].note);
                double h = Math.Max(30, GuiLayout.TextHeight(capi, label, text, Width - 40));
                rowBounds[i] = (ElementBounds.Fixed(0, y, 30, 30), ElementBounds.Fixed(40, y + 5, Width - 40, h));
                y += h + 6;
            }

            string hint = Lang.Get("vintagee:setvalue-hint");
            ElementBounds hintBounds = ElementBounds.Fixed(0, y, Width, GuiLayout.TextHeight(capi, grey, hint, Width));
            y += hintBounds.fixedHeight + 6;
            double buttonWidth = (Width - 20) / 3;
            ElementBounds setBounds = ElementBounds.Fixed(0, y, buttonWidth, 30);
            ElementBounds unpinBounds = ElementBounds.Fixed(buttonWidth + 10, y, buttonWidth, 30);
            ElementBounds cancelBounds = ElementBounds.Fixed(2 * (buttonWidth + 10), y, buttonWidth, 30);
            ElementBounds explainBounds = ElementBounds.Fixed(0, y + 40, Width, 30);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(slotBounds, infoBounds, labelBounds, valueBounds, hintBounds, setBounds, unpinBounds, cancelBounds);
            if (onExplain != null) bgBounds.WithChildren(explainBounds);
            foreach (var (sw, lab) in rowBounds) bgBounds.WithChildren(sw, lab);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            GuiComposer c = capi.Gui
                .CreateCompo("vintagee-setvalue", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("vintagee:setvalue-title"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddItemSlotGrid(iconInventory, _ => { }, 1, slotBounds, "icon")
                    .AddStaticText(info, detail, infoBounds)
                    .AddStaticText(Lang.Get("vintagee:setvalue-label"), detail, labelBounds)
                    .AddTextInput(valueBounds, null, CairoFont.WhiteSmallText(), "value");
            for (int i = 0; i < rows.Length; i++)
            {
                c.AddSwitch(_ => { }, rowBounds[i].sw, rows[i].key)
                 .AddStaticText(rows[i].text + (rows[i].note == null ? "" : "\n" + rows[i].note), label, rowBounds[i].lab);
            }
            c.AddStaticText(hint, grey, hintBounds)
             .AddSmallButton(Lang.Get("vintagee:setvalue-save"), OnSave, setBounds)
             .AddSmallButton(Lang.Get("vintagee:setvalue-unpin"), OnUnpin, unpinBounds)
             .AddSmallButton(Lang.Get("vintagee:setvalue-cancel"), () => TryClose(), cancelBounds);
            if (onExplain != null) c.AddSmallButton(Lang.Get("vintagee:setvalue-explain"), () => { onExplain(); TryClose(); return true; }, explainBounds);
            SingleComposer = c.EndChildElements().Compose();

            initialText = !state.Blacklisted && !state.Free && state.Value > 0 ? state.Value.ToString("0.####", CultureInfo.InvariantCulture) : "";
            SingleComposer.GetTextInput("value").SetValue(initialText);
            SingleComposer.GetSwitch("blacklist").SetValue(state.Blacklisted);
            SingleComposer.GetSwitch("gray").SetValue(state.Gray);
            SingleComposer.GetSwitch("free").SetValue(state.Free);
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            GuiElementTextInput input = SingleComposer?.GetTextInput("value");
            if (input != null) SingleComposer.FocusElement(input.TabIndex);
        }

        public override void OnKeyDown(KeyEvent args)
        {
            if (args.KeyCode == (int)GlKeys.Enter || args.KeyCode == (int)GlKeys.KeypadEnter)
            {
                OnSave();
                args.Handled = true;
                return;
            }
            base.OnKeyDown(args);
        }

        EmcItemEditPacket Edit() => new EmcItemEditPacket
        {
            Blacklist = SingleComposer.GetSwitch("blacklist").On,
            Gray = SingleComposer.GetSwitch("gray").On,
            Free = SingleComposer.GetSwitch("free").On
        };

        bool OnSave()
        {
            EmcItemEditPacket edit = Edit();
            string text = SingleComposer.GetTextInput("value").GetText()?.Trim() ?? "";
            // A value is only sent when it was changed, so ticking a box doesn't also pin the value it has now.
            if (text != initialText && text.Length > 0)
            {
                if (!TryParse(text, out double value))
                {
                    capi.TriggerIngameError(this, "badvalue", Lang.Get("vintagee:setvalue-bad"));
                    return true;
                }
                edit.SetValue = true;
                edit.Value = value;
            }
            onSave(edit);
            TryClose();
            return true;
        }

        bool OnUnpin()
        {
            EmcItemEditPacket edit = Edit();
            edit.Unpin = true;
            onSave(edit);
            TryClose();
            return true;
        }

        /// <summary>"1234", "1,234.5", "1e6", "12k", "3m"; never negative.</summary>
        public static bool TryParse(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim().Replace(",", "").Replace(" ", "").Replace("_", "");
            if (t.Length == 0) return false;
            double scale = 1;
            char last = char.ToLowerInvariant(t[t.Length - 1]);
            if (last == 'k' || last == 'm' || last == 'b')
            {
                scale = last == 'k' ? 1e3 : last == 'm' ? 1e6 : 1e9;
                t = t.Substring(0, t.Length - 1);
            }
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
            value *= scale;
            return value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            iconInventory[0].Itemstack = null;
        }
    }
}
