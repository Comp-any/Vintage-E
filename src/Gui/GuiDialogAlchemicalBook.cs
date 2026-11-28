using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// The Alchemical Book's window (1.29.3): the book's saved places with how far each is and what going there costs
    /// (Go and Delete links), a name box with Save here, and Back. Everything is done by the server
    /// (<see cref="AlchemicalBooks.OnRequest"/>); this only shows what it sends and asks for what's clicked.
    /// </summary>
    public class GuiDialogAlchemicalBook : GuiDialog
    {
        const double Width = 520;
        const double ListHeight = 300;

        static GuiDialogAlchemicalBook open;
        readonly VintageEModSystem mod;
        AlchemicalBookStatePacket state;
        string typed = "";

        public override string ToggleKeyCombinationCode => null;

        GuiDialogAlchemicalBook(ICoreClientAPI capi, VintageEModSystem mod) : base(capi)
        {
            this.mod = mod;
        }

        /// <summary>Right-click with a book: open the window and ask the server for its places.</summary>
        public static void Open(ICoreClientAPI capi)
        {
            var mod = VintageEModSystem.Of(capi);
            if (capi == null || mod == null) return;
            if (open == null || !open.IsOpened()) open = new GuiDialogAlchemicalBook(capi, mod);
            open.Compose();
            open.TryOpen();
            mod.SendAlchemicalBookRequest(new AlchemicalBookRequestPacket { Action = AlchemicalBookAction.Open });
        }

        /// <summary>What the server sent: the places, or a message (and maybe to close).</summary>
        public static void OnState(ICoreClientAPI capi, AlchemicalBookStatePacket packet)
        {
            if (packet == null) return;
            if (packet.Close)
            {
                open?.TryClose();
                if (!string.IsNullOrEmpty(packet.Message) && packet.Names == null) capi?.ShowChatMessage(packet.Message);
                return;
            }
            if (open == null || !open.IsOpened()) return;
            open.state = packet;
            open.Compose();
        }

        int Count => state?.Names?.Length ?? 0;

        void Compose()
        {
            ClearComposers();
            CairoFont detail = CairoFont.WhiteDetailText();
            ItemStack book = AlchemicalBooks.HeldBook(capi.World.Player)?.Itemstack;
            string title = book?.GetName() ?? Lang.Get("vintagee:item-alchemicalbook-basic");
            double top = GuiStyle.TitleBarHeight + 6;

            ElementBounds infoBounds = ElementBounds.Fixed(0, top, Width, 22);
            ElementBounds boundBounds = ElementBounds.Fixed(0, top + 24, Width, 22);
            double row = top + 54;
            ElementBounds nameBounds = ElementBounds.Fixed(0, row, Width - 140, 30);
            ElementBounds saveBounds = ElementBounds.Fixed(Width - 130, row, 130, 30);
            row += 42;
            ElementBounds listBounds = ElementBounds.Fixed(0, row, Width - 24, ListHeight);
            ElementBounds clipBounds = listBounds.ForkBoundingParent();
            ElementBounds insetBounds = listBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);
            ElementBounds scrollbarBounds = insetBounds.CopyOffsetedSibling(insetBounds.fixedWidth + 4).WithFixedWidth(20);
            row += ListHeight + 14;
            ElementBounds backBounds = ElementBounds.Fixed(0, row, 240, 30);
            ElementBounds closeBounds = ElementBounds.Fixed(Width - 100, row, 100, 30);
            row += 40;
            string message = state?.Message ?? "";
            CairoFont messageFont = state?.MessageIsError == true ? CairoFont.WhiteDetailText().WithColor(new double[] { 1, 0.45, 0.45, 1 }) : detail;
            ElementBounds messageBounds = ElementBounds.Fixed(0, row, Width, Math.Max(22, GuiLayout.TextHeight(capi, messageFont, message, Width)));

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(infoBounds, boundBounds, nameBounds, saveBounds, clipBounds, backBounds, closeBounds, messageBounds);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            string backText = Lang.Get("vintagee:abook-noback");
            if (state?.HasBack == true)
            {
                EntityPos me = capi.World.Player.Entity.Pos;
                backText = state.BackDim != me.Dimension ? Lang.Get("vintagee:abook-err-dimension")
                    : Lang.Get("vintagee:abook-back", EmcFormat.Total(AlchemicalBooks.Cost(state.PerBlock, me.X, me.Y, me.Z, state.BackX, state.BackY, state.BackZ)));
            }

            SingleComposer = capi.Gui
                .CreateCompo("vintagee-alchemicalbook", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddDynamicText(Info(), CairoFont.WhiteSmallText(), infoBounds, "info")
                    .AddDynamicText(state == null ? "" : state.BoundTo != null ? Lang.Get("vintagee:abook-bound", state.BoundTo) : Lang.Get("vintagee:abook-own"), detail, boundBounds, "bound")
                    .AddTextInput(nameBounds, t => typed = t ?? "", CairoFont.WhiteSmallText(), "name")
                    .AddSmallButton(Lang.Get("vintagee:abook-save"), OnSave, saveBounds, EnumButtonStyle.Normal, "save")
                    .BeginClip(clipBounds)
                        .AddInset(insetBounds, 3)
                        .AddRichtext(Array.Empty<RichTextComponentBase>(), listBounds, "list")
                    .EndClip()
                    .AddVerticalScrollbar(OnScroll, scrollbarBounds, "scrollbar")
                    .AddSmallButton(backText, OnBack, backBounds, EnumButtonStyle.Normal, "back")
                    .AddSmallButton(Lang.Get("vintagee:missing-close"), () => TryClose(), closeBounds)
                    .AddDynamicText(message, messageFont, messageBounds, "message")
                .EndChildElements()
                .Compose();
            GuiElementTextInput input = SingleComposer.GetTextInput("name");
            input?.SetPlaceHolderText(Lang.Get("vintagee:abook-name"));
            if (typed.Length > 0) input?.SetValue(typed, false);
            Fill();
        }

        string Info()
        {
            if (state == null) return "";
            string rate = state.PerBlock <= 0 ? Lang.Get("vintagee:abook-free") : Lang.Get("vintagee:abook-perblock", EmcFormat.Format(state.PerBlock));
            string storm = state.Storm ? "  " + Lang.Get("vintagee:abook-err-storm") : "";
            return Lang.Get("vintagee:abook-emc", EmcFormat.Total(state.Emc)) + "  ·  " + rate + storm;
        }

        void Fill()
        {
            try
            {
                var comps = new List<RichTextComponentBase>();
                CairoFont nameFont = CairoFont.WhiteSmallText();
                CairoFont linkFont = CairoFont.WhiteSmallText().WithColor(new double[] { 0.6, 0.8, 1, 1 });
                CairoFont dimFont = CairoFont.WhiteDetailText().WithColor(new double[] { 0.7, 0.7, 0.7, 1 });
                EntityPos me = capi.World.Player.Entity.Pos;
                // Nearest first.
                var order = Enumerable.Range(0, Count).OrderBy(i => Dist(me, i)).ToList();
                foreach (int i in order)
                {
                    string name = state.Names[i];
                    bool otherDim = At(state.Dim, i) != me.Dimension;
                    string line = otherDim ? Lang.Get("vintagee:abook-row-otherdim", name)
                        : Lang.Get("vintagee:abook-row", name, ((int)Math.Round(Dist(me, i))).ToString("#,0"),
                            EmcFormat.Total(AlchemicalBooks.Cost(state.PerBlock, me.X, me.Y, me.Z, At(state.X, i), At(state.Y, i), At(state.Z, i))));
                    comps.Add(new RichTextComponent(capi, line + "  ", otherDim ? dimFont : nameFont));
                    string target = name;
                    if (!otherDim) comps.Add(new LinkTextComponent(capi, "[" + Lang.Get("vintagee:abook-go") + "]", linkFont, _ => Ask(AlchemicalBookAction.Go, target)));
                    comps.Add(new RichTextComponent(capi, " ", nameFont));
                    comps.Add(new LinkTextComponent(capi, "[" + Lang.Get("vintagee:abook-delete") + "]", linkFont, _ => Ask(AlchemicalBookAction.Delete, target)));
                    comps.Add(new RichTextComponent(capi, "\n", nameFont));
                }
                if (Count == 0) comps.Add(new RichTextComponent(capi, Lang.Get("vintagee:abook-none"), dimFont));

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
                capi.Logger.Error("Vintage-E: could not fill the Alchemical Book's places: {0}", ex);
            }
        }

        static double At(double[] a, int i) => a != null && i < a.Length ? a[i] : 0;
        static int At(int[] a, int i) => a != null && i < a.Length ? a[i] : 0;

        double Dist(EntityPos me, int i)
        {
            double dx = At(state.X, i) - me.X, dy = At(state.Y, i) - me.Y, dz = At(state.Z, i) - me.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        void Ask(AlchemicalBookAction action, string name)
        {
            mod.SendAlchemicalBookRequest(new AlchemicalBookRequestPacket { Action = action, Name = name });
        }

        bool OnSave()
        {
            Ask(AlchemicalBookAction.Save, typed);
            typed = "";
            return true;
        }

        bool OnBack()
        {
            if (state?.HasBack == true) Ask(AlchemicalBookAction.Back, null);
            return true;
        }

        void OnScroll(float value)
        {
            GuiElementRichtext list = SingleComposer?.GetRichtext("list");
            if (list == null) return;
            list.Bounds.fixedY = -value;
            list.Bounds.CalcWorldBounds();
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            if (open == this) open = null;
        }
    }
}
