using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// How fast a balance changes: kept from what the balance was over the last <see cref="WindowSeconds"/> seconds,
    /// so a burn shows as a rate for that long and then goes back to nothing. Plain numbers in, so the server's self-test
    /// can check it (1.28.0).
    /// </summary>
    public sealed class EmcRate
    {
        public const double WindowSeconds = 10;
        readonly List<(double at, decimal emc)> samples = new List<(double, decimal)>();

        /// <summary>The balance at a moment (seconds, any clock that only goes forward).</summary>
        public void Add(double at, decimal emc)
        {
            if (double.IsNaN(at) || double.IsInfinity(at)) return;
            if (samples.Count > 0 && at < samples[samples.Count - 1].at) samples.Clear(); // the clock went back (a new world): start again
            samples.Add((at, emc));
            // Keep one sample from before the window as the starting point.
            while (samples.Count > 2 && samples[1].at <= at - WindowSeconds) samples.RemoveAt(0);
        }

        public void Clear() => samples.Clear();

        /// <summary>EMC a second over the window: 0 until a second has passed, or while nothing changed.</summary>
        public double PerSecond
        {
            get
            {
                if (samples.Count < 2) return 0;
                var first = samples[0];
                var last = samples[samples.Count - 1];
                double span = last.at - first.at;
                if (span < 1) return 0;
                return (double)EmcAmount.Add(last.emc, -first.emc) / span;
            }
        }

        /// <summary>The balance as the display shows it: in full up to a trillion, the short form past that.</summary>
        public static string BalanceText(decimal emc) => emc < 1_000_000_000_000m ? EmcFormat.Total(emc) : EmcFormat.ShortTotal(emc);

        /// <summary>"+12.5 EMC/s" or "-300 EMC/s"; null when it would show as 0.</summary>
        public static string RateText(double perSecond)
        {
            if (double.IsNaN(perSecond) || double.IsInfinity(perSecond)) return null;
            double size = Math.Abs(perSecond);
            string shown = size < 1_000_000 ? EmcFormat.Format(size) : EmcFormat.Short(size);
            if (shown == "0") return null;
            return Lang.Get("vintagee:emchud-rate", (perSecond > 0 ? "+" : "-") + shown);
        }
    }

    /// <summary>
    /// Your EMC on screen, with how much it changed a second (Project Expansion's EMC Display, 1.28.0). On, place and
    /// offset are this computer's settings (<see cref="VintageEClientConfig.EmcHud"/>, in the <c>.vintagee</c> window).
    /// Hidden while you have no EMC and it isn't changing, so it stays out of the way of anyone not using EMC. The
    /// balance is the one the server sends this game; nothing is asked of the server for it.
    /// </summary>
    public class HudEmcDisplay : HudElement
    {
        const double TextWidth = 260;
        readonly VintageEModSystem mod;
        readonly EmcRate rate = new EmcRate();
        long listener;
        string shownBalance, shownRate, shownLayout;

        public override string ToggleKeyCombinationCode => null;
        public override bool Focusable => false;
        public override bool ShouldReceiveMouseEvents() => false;
        public override bool ShouldReceiveKeyboardEvents() => false;
        public override double DrawOrder => 0.05;

        public HudEmcDisplay(ICoreClientAPI capi, VintageEModSystem mod) : base(capi)
        {
            this.mod = mod;
            listener = capi.Event.RegisterGameTickListener(Guard.Tick(capi.Logger, "the EMC display", () => Update()), 250);
        }

        /// <summary>Opens, closes or redraws it to match the balance and the settings.</summary>
        public void Update(bool force = false)
        {
            VintageEClientConfig cfg = mod.ClientConfig;
            IPlayer player = capi.World?.Player;
            if (cfg == null || !cfg.EmcHud || player == null || !PlayerEmc.ClientHasEmc)
            {
                Hide();
                rate.Clear();
                return;
            }
            decimal emc = PlayerEmc.GetEmc(player);
            rate.Add(capi.World.ElapsedMilliseconds / 1000.0, emc);
            string rateText = EmcRate.RateText(rate.PerSecond);
            if (emc <= 0 && rateText == null) { Hide(); return; }

            string balanceText = Lang.Get("vintagee:emchud-balance", EmcRate.BalanceText(emc));
            string layout = cfg.EmcHudPosition + "|" + cfg.EmcHudOffsetX + "|" + cfg.EmcHudOffsetY;
            if (force || layout != shownLayout || SingleComposer == null) Compose(cfg, balanceText, rateText ?? "");
            else
            {
                if (balanceText != shownBalance) SingleComposer.GetDynamicText("balance")?.SetNewText(balanceText);
                if ((rateText ?? "") != shownRate) SingleComposer.GetDynamicText("rate")?.SetNewText(rateText ?? "");
            }
            shownLayout = layout;
            shownBalance = balanceText;
            shownRate = rateText ?? "";
            if (!IsOpened()) TryOpen();
        }

        void Hide()
        {
            if (IsOpened()) TryClose();
            shownBalance = shownRate = null;
        }

        void Compose(VintageEClientConfig cfg, string balanceText, string rateText)
        {
            // Text lines up with the side of the screen it sits on.
            string pos = cfg.EmcHudPosition;
            EnumTextOrientation side = pos.EndsWith("Left", StringComparison.Ordinal) ? EnumTextOrientation.Left
                : pos.EndsWith("Right", StringComparison.Ordinal) ? EnumTextOrientation.Right : EnumTextOrientation.Center;
            double[] stroke = { 0, 0, 0, 0.8 };
            CairoFont big = CairoFont.WhiteSmallText().WithOrientation(side).WithStroke(stroke, 2);
            CairoFont small = CairoFont.WhiteDetailText().WithOrientation(side).WithStroke(stroke, 2);

            ElementBounds balanceBounds = ElementBounds.Fixed(0, 0, TextWidth, 22);
            ElementBounds rateBounds = ElementBounds.Fixed(0, 22, TextWidth, 18);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(4);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(balanceBounds, rateBounds);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(cfg.EmcHudArea)
                .WithFixedAlignmentOffset(cfg.EmcHudOffsetX, cfg.EmcHudOffsetY);

            SingleComposer?.Dispose();
            SingleComposer = capi.Gui
                .CreateCompo("vintagee-emchud", dialogBounds)
                .BeginChildElements(bgBounds)
                    .AddDynamicText(balanceText, big, balanceBounds, "balance")
                    .AddDynamicText(rateText, small, rateBounds, "rate")
                .EndChildElements()
                .Compose();
        }

        public override void Dispose()
        {
            if (listener != 0) capi.Event.UnregisterGameTickListener(listener);
            listener = 0;
            base.Dispose();
        }
    }
}
