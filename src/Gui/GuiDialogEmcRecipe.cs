using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// Window opened by /emc recipe: hand-written recipes for things the mapper can't see being made (a modded boat
    /// built in stages, a multiblock, a ritual). Put the result in the first slot and what it takes in the others by
    /// clicking them with an item held on the mouse; the recipe line underneath can also be edited by hand (wildcards
    /// such as game:plank-* work there). The server checks each recipe, saves it to the CustomRecipes setting and remaps.
    /// </summary>
    public class GuiDialogEmcRecipe : GuiDialog
    {
        const double Width = 560;
        const int IngredientSlots = 12;

        readonly Action<string, string> onAdd;   // (line, line it replaces or null)
        readonly Action<string> onRemove;
        readonly DisplayInventory outputInv;
        readonly DisplayInventory ingredientInv;

        EmcRecipeListPacket packet;
        int selectedSlot = -1;        // 0 = output, 1.. = ingredient slot + 1
        int pickedRecipe;
        string editing;               // the saved line being changed, or null for a new one
        bool composing;

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogEmcRecipe(ICoreClientAPI capi, EmcRecipeListPacket packet, Action<string, string> onAdd, Action<string> onRemove) : base(capi)
        {
            this.packet = packet;
            this.onAdd = onAdd;
            this.onRemove = onRemove;
            string id = Guid.NewGuid().ToString("N");
            outputInv = new DisplayInventory(capi, "recipe-out-" + id, 1);
            ingredientInv = new DisplayInventory(capi, "recipe-in-" + id, IngredientSlots);
            Compose();
        }

        /// <summary>New list from the server (after an add or remove): keeps the slots unless the recipe went in.</summary>
        public void Update(EmcRecipeListPacket newPacket)
        {
            packet = newPacket;
            string line = LineText();
            if (adding && !newPacket.Error)
            {
                editing = null;
                ClearSlots();
                line = "";
            }
            adding = false;
            pickedRecipe = Math.Min(pickedRecipe, Math.Max(0, (packet.Lines?.Length ?? 0) - 1));
            Compose();
            SetLine(line);
        }

        /// <summary>An add (or save) is on its way to the server.</summary>
        bool adding;

        string LineText() => SingleComposer?.GetTextInput("line")?.GetText() ?? "";

        void SetLine(string text)
        {
            GuiElementTextInput input = SingleComposer?.GetTextInput("line");
            if (input == null) return;
            composing = true;
            input.SetValue(text ?? "");
            composing = false;
        }

        void Compose()
        {
            CairoFont detail = CairoFont.WhiteDetailText();
            CairoFont small = CairoFont.WhiteSmallText();
            string intro = Lang.Get("vintagee:recipe-intro");
            double y = 30;
            ElementBounds introBounds = ElementBounds.Fixed(0, y, Width, GuiLayout.TextHeight(capi, detail, intro, Width));
            y += introBounds.fixedHeight + 6;

            ElementBounds makesLabel = ElementBounds.Fixed(0, y, 90, 20);
            ElementBounds fromLabel = ElementBounds.Fixed(100, y, Width - 100, 20);
            y += 22;
            ElementBounds outputBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, y, 1, 1);
            ElementBounds arrowBounds = ElementBounds.Fixed(56, y + 14, 30, 24);
            ElementBounds ingredientBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 100, y, 6, 2);
            y += 2 * (GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGridBase.unscaledSlotPadding) + 8;

            string selectedText = SelectedText();
            ElementBounds selectedBounds = ElementBounds.Fixed(0, y + 6, 250, 20);
            ElementBounds amountLabel = ElementBounds.Fixed(260, y + 6, 70, 20);
            ElementBounds amountBounds = ElementBounds.Fixed(330, y, 90, 30);
            ElementBounds takeOutBounds = ElementBounds.Fixed(430, y, Width - 430, 30);
            y += 40;

            ElementBounds lineLabel = ElementBounds.Fixed(0, y, Width, 20);
            y += 22;
            ElementBounds lineBounds = ElementBounds.Fixed(0, y, Width, 30);
            y += 38;
            ElementBounds addBounds = ElementBounds.Fixed(0, y, 200, 30);
            ElementBounds clearBounds = ElementBounds.Fixed(210, y, 120, 30);
            y += 42;

            string[] lines = packet.Lines ?? Array.Empty<string>();
            string savedTitle = Lang.Get("vintagee:recipe-saved", lines.Length) + (packet.FromFiles > 0 ? " " + Lang.Get("vintagee:recipe-fromfiles", packet.FromFiles) : "");
            ElementBounds savedLabel = ElementBounds.Fixed(0, y, Width, GuiLayout.TextHeight(capi, detail, savedTitle, Width));
            y += savedLabel.fixedHeight + 4;
            ElementBounds pickBounds = ElementBounds.Fixed(0, y, Width - 220, 30);
            ElementBounds editBounds = ElementBounds.Fixed(Width - 210, y, 100, 30);
            ElementBounds removeBounds = ElementBounds.Fixed(Width - 100, y, 100, 30);
            y += 36;
            string pickedInfo = PickedInfo();
            ElementBounds pickedInfoBounds = ElementBounds.Fixed(0, y, Width, Math.Max(20, GuiLayout.TextHeight(capi, detail, pickedInfo, Width)));
            y += pickedInfoBounds.fixedHeight + 6;
            string message = packet.Message ?? "";
            CairoFont messageFont = packet.Error ? CairoFont.WhiteDetailText().WithColor(new double[] { 1, 0.45, 0.45, 1 }) : detail;
            ElementBounds messageBounds = ElementBounds.Fixed(0, y, Width, Math.Max(20, GuiLayout.TextHeight(capi, messageFont, message, Width)));
            y += messageBounds.fixedHeight + 6;
            ElementBounds closeBounds = ElementBounds.Fixed(Width - 120, y, 120, 30);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(introBounds, makesLabel, fromLabel, outputBounds, arrowBounds, ingredientBounds, selectedBounds, amountLabel, amountBounds, takeOutBounds,
                lineLabel, lineBounds, addBounds, clearBounds, savedLabel, pickBounds, editBounds, removeBounds, pickedInfoBounds, messageBounds, closeBounds);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            string[] pickValues = lines.Length == 0 ? new[] { "" } : lines.Select((_, i) => i.ToString(CultureInfo.InvariantCulture)).ToArray();
            string[] pickNames = lines.Length == 0 ? new[] { Lang.Get("vintagee:recipe-none") }
                : lines.Select((l, i) => (string.IsNullOrEmpty(packet.Problems?.ElementAtOrDefault(i)) ? "" : "(!) ") + l).ToArray();

            composing = true;
            SingleComposer?.Dispose();
            SingleComposer = capi.Gui
                .CreateCompo("vintagee-recipe", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("vintagee:recipe-title"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddStaticText(intro, detail, introBounds)
                    .AddStaticText(Lang.Get("vintagee:recipe-makes"), detail, makesLabel)
                    .AddStaticText(Lang.Get("vintagee:recipe-from"), detail, fromLabel)
                    .AddItemSlotGrid(outputInv, p => OnSlotClick(p, true), 1, outputBounds, "output")
                    .AddStaticText("=", CairoFont.WhiteMediumText(), arrowBounds)
                    .AddItemSlotGrid(ingredientInv, p => OnSlotClick(p, false), 6, ingredientBounds, "ingredients")
                    .AddDynamicText(selectedText, detail, selectedBounds, "selected")
                    .AddStaticText(Lang.Get("vintagee:recipe-amount"), detail, amountLabel)
                    .AddTextInput(amountBounds, OnAmountChanged, small, "amount")
                    .AddSmallButton(Lang.Get("vintagee:recipe-takeout"), OnTakeOut, takeOutBounds)
                    .AddStaticText(Lang.Get("vintagee:recipe-line"), detail, lineLabel)
                    .AddTextInput(lineBounds, null, small, "line")
                    .AddSmallButton(Lang.Get(editing == null ? "vintagee:recipe-add" : "vintagee:recipe-save"), OnAdd, addBounds)
                    .AddSmallButton(Lang.Get("vintagee:recipe-clear"), OnClear, clearBounds)
                    .AddStaticText(savedTitle, detail, savedLabel)
                    .AddDropDown(pickValues, pickNames, Math.Min(pickedRecipe, pickValues.Length - 1), OnPickRecipe, pickBounds, "pick")
                    .AddSmallButton(Lang.Get("vintagee:recipe-edit"), OnEdit, editBounds)
                    .AddSmallButton(Lang.Get("vintagee:recipe-remove"), OnRemove, removeBounds)
                    .AddDynamicText(pickedInfo, detail, pickedInfoBounds, "pickedinfo")
                    .AddDynamicText(message, messageFont, messageBounds, "message")
                    .AddSmallButton(Lang.Get("vintagee:missing-close"), () => TryClose(), closeBounds)
                .EndChildElements()
                .Compose();
            composing = false;
            ShowSelection();
        }

        string PickedInfo()
        {
            string[] lines = packet.Lines ?? Array.Empty<string>();
            if (pickedRecipe < 0 || pickedRecipe >= lines.Length) return "";
            string problem = packet.Problems?.ElementAtOrDefault(pickedRecipe);
            if (!string.IsNullOrEmpty(problem)) return Lang.Get("vintagee:recipe-problem", problem);
            string value = packet.Values?.ElementAtOrDefault(pickedRecipe);
            return string.IsNullOrEmpty(value) ? Lang.Get("vintagee:recipe-outnovalue") : Lang.Get("vintagee:recipe-outvalue", value);
        }

        ItemSlot SlotOf(int selected) => selected == 0 ? outputInv[0] : selected > 0 ? ingredientInv[selected - 1] : null;

        string SelectedText()
        {
            ItemStack s = SlotOf(selectedSlot)?.Itemstack;
            return s == null ? Lang.Get("vintagee:recipe-noselection") : Lang.Get("vintagee:recipe-selected", s.GetName());
        }

        void ShowSelection()
        {
            if (SingleComposer == null) return;
            ItemStack s = SlotOf(selectedSlot)?.Itemstack;
            if (s == null) selectedSlot = -1;
            SingleComposer.GetDynamicText("selected")?.SetNewText(SelectedText());
            GuiElementItemSlotGrid outGrid = SingleComposer.GetSlotGrid("output");
            GuiElementItemSlotGrid inGrid = SingleComposer.GetSlotGrid("ingredients");
            if (outGrid != null) outGrid.highlightSlotId = selectedSlot == 0 ? 0 : -1;
            if (inGrid != null) inGrid.highlightSlotId = selectedSlot > 0 ? selectedSlot - 1 : -1;
            composing = true;
            SingleComposer.GetTextInput("amount")?.SetValue(s == null ? "" : s.StackSize.ToString(CultureInfo.InvariantCulture));
            composing = false;
        }

        /// <summary>
        /// Holding an item: puts a copy of it (as many as are held) in the slot, nothing is taken from you. Right click
        /// adds one more of the held item. Empty hand: picks the slot, to change its amount or take it out.
        /// </summary>
        void OnSlotClick(object p, bool output)
        {
            if (!(p is DisplaySlotClick click) || click.Button == EnumMouseButton.Wheel) return;
            int index = output ? 0 : click.SlotId + 1;
            ItemSlot slot = SlotOf(index);
            if (slot == null) return;
            ItemStack held = capi.World.Player?.InventoryManager?.MouseItemSlot?.Itemstack;
            if (held != null)
            {
                if (click.Button == EnumMouseButton.Right && slot.Itemstack != null && slot.Itemstack.Equals(capi.World, held, GlobalConstants.IgnoredStackAttributes))
                {
                    slot.Itemstack.StackSize++;
                }
                else
                {
                    ItemStack copy = held.Clone();
                    copy.StackSize = click.Button == EnumMouseButton.Right ? 1 : Math.Max(1, held.StackSize);
                    slot.Itemstack = copy;
                }
                slot.MarkDirty();
                selectedSlot = index;
                RebuildLine();
            }
            else selectedSlot = slot.Itemstack == null ? -1 : index;
            ShowSelection();
        }

        void OnAmountChanged(string text)
        {
            if (composing) return;
            ItemSlot slot = SlotOf(selectedSlot);
            if (slot?.Itemstack == null) return;
            if (!int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 1) return;
            slot.Itemstack.StackSize = Math.Min(n, 1000000);
            slot.MarkDirty();
            RebuildLine();
        }

        bool OnTakeOut()
        {
            ItemSlot slot = SlotOf(selectedSlot);
            if (slot?.Itemstack == null) return true;
            slot.Itemstack = null;
            slot.MarkDirty();
            selectedSlot = -1;
            RebuildLine();
            ShowSelection();
            return true;
        }

        static string CodeOf(ItemStack s) => (s.Class == EnumItemClass.Block ? "block:" : "item:") + s.Collectible.Code;

        static string Term(ItemStack s) => (s.StackSize == 1 ? "" : s.StackSize.ToString(CultureInfo.InvariantCulture) + " ") + CodeOf(s);

        /// <summary>Writes the slots out as a recipe line (the same ingredient in two slots is added up).</summary>
        void RebuildLine()
        {
            ItemStack output = outputInv[0].Itemstack;
            var amounts = new List<(string code, int n)>();
            for (int i = 0; i < IngredientSlots; i++)
            {
                ItemStack s = ingredientInv[i].Itemstack;
                if (s == null) continue;
                string code = CodeOf(s);
                int at = amounts.FindIndex(a => a.code == code);
                if (at >= 0) amounts[at] = (code, amounts[at].n + s.StackSize);
                else amounts.Add((code, s.StackSize));
            }
            string left = output == null ? "?" : Term(output);
            string right = amounts.Count == 0 ? "?" : string.Join(" + ", amounts.Select(a => (a.n == 1 ? "" : a.n + " ") + a.code));
            SetLine(left + " = " + right);
        }

        bool OnAdd()
        {
            string line = LineText().Trim();
            if (line.Length == 0 || line.Contains("?"))
            {
                ShowMessage(Lang.Get("vintagee:recipe-incomplete"), true);
                return true;
            }
            if (CustomRecipe.Parse(line, out string error) == null)
            {
                ShowMessage(Lang.Get("vintagee:recipe-bad", error), true);
                return true;
            }
            ShowMessage(Lang.Get("vintagee:recipe-working"), false);
            adding = true;
            onAdd(line, editing);
            return true;
        }

        bool OnClear()
        {
            editing = null;
            ClearSlots();
            Recompose(() => SetLine(""));
            return true;
        }

        void ClearSlots()
        {
            outputInv[0].Itemstack = null;
            for (int i = 0; i < IngredientSlots; i++) ingredientInv[i].Itemstack = null;
            selectedSlot = -1;
        }

        void OnPickRecipe(string code, bool selected)
        {
            if (int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) pickedRecipe = i;
            SingleComposer.GetDynamicText("pickedinfo")?.SetNewText(PickedInfo());
        }

        string PickedLine()
        {
            string[] lines = packet.Lines ?? Array.Empty<string>();
            return pickedRecipe >= 0 && pickedRecipe < lines.Length ? lines[pickedRecipe] : null;
        }

        /// <summary>Loads a saved recipe into the slots (where its codes are plain blocks/items) and the line, to change it.</summary>
        bool OnEdit()
        {
            string line = PickedLine();
            if (line == null) return true;
            CustomRecipe r = CustomRecipe.Parse(line, out _);
            ClearSlots();
            if (r != null)
            {
                outputInv[0].Itemstack = StackOf(r.Output, r.OutputQuantity);
                int slot = 0;
                foreach (var (code, qty) in r.Ingredients)
                {
                    if (slot >= IngredientSlots) break;
                    ItemStack s = StackOf(code, qty);
                    if (s != null) ingredientInv[slot++].Itemstack = s;
                }
            }
            editing = line;
            packet.Message = Lang.Get("vintagee:recipe-editing");
            packet.Error = false;
            Recompose(() => SetLine(line));
            return true;
        }

        ItemStack StackOf(string code, double qty)
        {
            if (code == null || code.Contains('*') || code.StartsWith("@")) return null;
            CollectibleObject c;
            if (code.StartsWith("item:")) c = capi.World.GetItem(new AssetLocation(code.Substring(5)));
            else if (code.StartsWith("block:")) c = capi.World.GetBlock(new AssetLocation(code.Substring(6)));
            else c = (CollectibleObject)capi.World.GetItem(new AssetLocation(code)) ?? capi.World.GetBlock(new AssetLocation(code));
            if (c == null || c.Id == 0 && c is Block) return null;
            int n = Math.Max(1, (int)Math.Round(qty));
            return new ItemStack(c, n);
        }

        bool OnRemove()
        {
            string line = PickedLine();
            if (line == null) return true;
            ShowMessage(Lang.Get("vintagee:recipe-working"), false);
            adding = false;
            onRemove(line);
            return true;
        }

        void ShowMessage(string text, bool error)
        {
            packet.Message = text;
            packet.Error = error;
            string line = LineText();
            Recompose(() => SetLine(line));
        }

        /// <summary>Rebuilds the window a frame later (these run from clicks on elements the rebuild tears down), then runs <paramref name="then"/>.</summary>
        void Recompose(Action then = null)
        {
            capi.Event.EnqueueMainThreadTask(() =>
            {
                if (!IsOpened()) return;
                Compose();
                then?.Invoke();
            }, "vintagee-recipe-recompose");
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            ClearSlots();
        }
    }
}
