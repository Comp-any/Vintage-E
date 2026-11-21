using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageE
{
    /// <summary>
    /// Shows the Watch of Flowing Time on screen while yours is switched on or time runs faster or slower because of a
    /// watch (its tick sound is easy to miss), with how fast time goes. Size, place and whether it shows at all are
    /// this computer's settings (<see cref="VintageEClientConfig"/>).
    /// </summary>
    public class HudWatchIndicator : HudElement
    {
        readonly VintageEModSystem mod;
        readonly DummySlot icon = new DummySlot();
        ElementBounds iconBounds;
        long listener;
        string shownText;
        string shownLayout;

        public override string ToggleKeyCombinationCode => null;
        public override bool Focusable => false;
        public override bool ShouldReceiveMouseEvents() => false;
        public override bool ShouldReceiveKeyboardEvents() => false;
        public override double DrawOrder => 0.05;

        public HudWatchIndicator(ICoreClientAPI capi, VintageEModSystem mod) : base(capi)
        {
            this.mod = mod;
            Item watch = capi.World.GetItem(new AssetLocation(VintageEModSystem.ModId, "trinket-watchofflowingtime"));
            if (watch != null)
            {
                icon.Itemstack = new ItemStack(watch);
                icon.Itemstack.Attributes.SetBool(ItemEmcTrinket.ActiveKey, true); // drawn glowing, like a watch that's on
            }
            listener = capi.Event.RegisterGameTickListener(Guard.Tick(capi.Logger, "the watch indicator", () => Update()), 500);
        }

        /// <summary>Opens, closes or redraws it to match the watch and the settings.</summary>
        public void Update(bool force = false)
        {
            VintageEClientConfig cfg = mod.ClientConfig;
            string text = cfg != null && cfg.WatchIcon && icon.Itemstack != null ? StatusText() : null;
            if (text == null)
            {
                if (IsOpened()) TryClose();
                shownText = null;
                return;
            }
            string layout = cfg.WatchIconSize + "|" + cfg.WatchIconPosition + "|" + cfg.WatchIconOffsetX + "|" + cfg.WatchIconOffsetY;
            if (force || layout != shownLayout || SingleComposer == null) Compose(cfg, text);
            else if (text != shownText) SingleComposer.GetDynamicText("speed")?.SetNewText(text);
            shownLayout = layout;
            shownText = text;
            if (!IsOpened()) TryOpen();
        }

        /// <summary>"Time x10" / "Time x1/3" while a watch changes time, "Watch on" for yours when it doesn't; null when neither.</summary>
        string StatusText()
        {
            float modifier = mod.ClientWatchModifier;
            if (Math.Abs(modifier) >= 0.01f)
            {
                float factor = (60 + modifier) / 60f;
                string speed = factor >= 1 ? "x" + factor.ToString("0.#") : "x1/" + (1 / Math.Max(0.01f, factor)).ToString("0.#");
                return Lang.Get("vintagee:watchhud-speed", speed);
            }
            IPlayer player = capi.World.Player;
            if (player == null) return null;
            foreach (ItemSlot slot in EmcWallet.CarriedSlots(player))
            {
                if (slot.Itemstack.Collectible is ItemEmcTrinket t && t.Kind == "watchofflowingtime" && ItemEmcTrinket.IsActive(slot.Itemstack))
                    return Lang.Get("vintagee:watchhud-on");
            }
            return null;
        }

        void Compose(VintageEClientConfig cfg, string text)
        {
            double size = cfg.WatchIconSize;
            double width = Math.Max(size, 90);
            iconBounds = ElementBounds.Fixed((width - size) / 2, 0, size, size);
            ElementBounds textBounds = ElementBounds.Fixed(0, size + 2, width, 20);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(4);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(iconBounds, textBounds);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(cfg.Area)
                .WithFixedAlignmentOffset(cfg.WatchIconOffsetX, cfg.WatchIconOffsetY);

            CairoFont font = CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center).WithStroke(new double[] { 0, 0, 0, 0.8 }, 2);
            SingleComposer?.Dispose();
            SingleComposer = capi.Gui
                .CreateCompo("vintagee-watchhud", dialogBounds)
                .BeginChildElements(bgBounds)
                    .AddDynamicText(text, font, textBounds, "speed")
                .EndChildElements()
                .Compose();
            iconBounds.CalcWorldBounds();
        }

        public override void OnRenderGUI(float deltaTime)
        {
            base.OnRenderGUI(deltaTime);
            if (iconBounds == null || icon.Itemstack == null) return;
            float size = (float)GuiElement.scaled(mod.ClientConfig?.WatchIconSize ?? 48);
            capi.Render.RenderItemstackToGui(icon, iconBounds.renderX + size / 2, iconBounds.renderY + size / 2, 100, size * 0.8f, ColorUtil.WhiteArgb, true, false, false);
        }

        public override void Dispose()
        {
            if (listener != 0) capi.Event.UnregisterGameTickListener(listener);
            listener = 0;
            base.Dispose();
        }
    }
}
