using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Common;

namespace Vintagestory.Client.NoObf;

public class GuiDialogInventory : GuiDialog
{
	private ITabbedInventory creativeInv;

	private IInventory backpackInv;

	private IInventory craftingInv;

	private GuiComposer creativeInvDialog;

	private GuiComposer survivalInvDialog;

	private int currentTabIndex;

	private int cols = 15;

	private ElementBounds creativeClippingBounds;

	private int prevRows;

	private EnumGameMode prevGameMode;

	public override double DrawOrder => 0.2;

	public override string ToggleKeyCombinationCode => "inventorydialog";

	public override bool PrefersUngrabbedMouse => Composers["maininventory"] == creativeInvDialog;

	public override float ZSize => 250f;

	public GuiDialogInventory(ICoreClientAPI capi)
		: base(capi)
	{
		(capi.World as ClientMain).eventManager.OnPlayerModeChange.Add(OnPlayerModeChanged);
		capi.Input.RegisterHotKey("creativesearch", Lang.Get("Search Creative inventory"), GlKeys.F, HotkeyType.CreativeTool, altPressed: false, ctrlPressed: true);
		capi.Input.SetHotKeyHandler("creativesearch", onSearchCreative);
	}

	private bool onSearchCreative(KeyCombination t1)
	{
		if (capi.World.Player.WorldData.CurrentGameMode != EnumGameMode.Creative)
		{
			return false;
		}
		if (TryOpen())
		{
			creativeInvDialog.FocusElement(creativeInvDialog.GetTextInput("searchbox").TabIndex);
		}
		return true;
	}

	public override void OnOwnPlayerDataReceived()
	{
		capi.Logger.VerboseDebug("GuiDialogInventory: starting composeGUI");
		ComposeGui(firstBuild: true);
		capi.Logger.VerboseDebug("GuiDialogInventory: done composeGUI");
		TyronThreadPool.QueueTask(() =>
		{
			creativeInv.CreativeTabs.CreateSearchCache(capi.World);
		}, "GuiDialogInventory.CreateSearchCache");
		prevGameMode = capi.World.Player.WorldData.CurrentGameMode;
	}

	public void ComposeGui(bool firstBuild)
	{
		IPlayerInventoryManager inventoryManager = capi.World.Player.InventoryManager;
		creativeInv = (ITabbedInventory)inventoryManager.GetOwnInventory("creative");
		craftingInv = inventoryManager.GetOwnInventory("craftinggrid");
		backpackInv = inventoryManager.GetOwnInventory("backpack");
		if (firstBuild)
		{
			backpackInv.SlotModified += BackpackInv_SlotModified;
		}
		if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
		{
			ComposeCreativeInvDialog();
			Composers["maininventory"] = creativeInvDialog;
		}
		if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Guest || capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Survival)
		{
			ComposeSurvivalInvDialog();
			Composers["maininventory"] = survivalInvDialog;
		}
		if (firstBuild)
		{
			OnPlayerModeChanged();
		}
	}

	private void ComposeCreativeInvDialog()
	{
		if (creativeInv == null)
		{
			ScreenManager.Platform.Logger.Notification("Server did not send a creative inventory, so I won't display one");
			return;
		}
		double elementToDialogPadding = GuiStyle.ElementToDialogPadding;
		double unscaledSlotPadding = GuiElementItemSlotGridBase.unscaledSlotPadding;
		int rows = (int)Math.Ceiling((float)creativeInv.Count / (float)cols);
		ElementBounds elementBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, unscaledSlotPadding, unscaledSlotPadding, cols, 9).FixedGrow(2.0 * unscaledSlotPadding, 2.0 * unscaledSlotPadding);
		ElementBounds elementBounds2 = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0.0, 0.0, cols, rows);
		creativeClippingBounds = elementBounds.ForkBoundingParent();
		creativeClippingBounds.Name = "clip";
		ElementBounds elementBounds3 = creativeClippingBounds.ForkBoundingParent(6.0, 3.0, 0.0, 3.0);
		elementBounds3.Name = "inset";
		ElementBounds elementBounds4 = elementBounds3.ForkBoundingParent(elementToDialogPadding, elementToDialogPadding + 70.0, elementToDialogPadding + 31.0, elementToDialogPadding).WithFixedAlignmentOffset(-3.0, -100.0).WithAlignment(EnumDialogArea.CenterBottom);
		ElementBounds bounds = ElementStdBounds.VerticalScrollbar(elementBounds3).WithParent(elementBounds4);
		ElementBounds bounds2 = ElementBounds.Fixed(elementToDialogPadding, 45.0, 250.0, 30.0);
		ElementBounds elementBounds5 = ElementBounds.Fixed(-130.0, 35.0, 130.0, 545.0);
		ElementBounds bounds3 = ElementBounds.Fixed(0.0, 35.0, 130.0, 545.0).FixedRightOf(elementBounds4).WithFixedAlignmentOffset(-4.0, 0.0);
		ElementBounds bounds4 = ElementBounds.Fixed(elementToDialogPadding, 45.0, 250.0, 30.0).WithAlignment(EnumDialogArea.RightFixed).WithFixedAlignmentOffset(-28.0 - elementToDialogPadding, 7.0);
		CreativeTabsConfig creativeTabsConfig = capi.Assets.TryGet("config/creativetabs.json").ToObject<CreativeTabsConfig>();
		IEnumerable<CreativeTab> tabs = creativeInv.CreativeTabs.Tabs;
		List<TabConfig> orderedTabs = new List<TabConfig>();
		foreach (CreativeTab tab in tabs)
		{
			TabConfig tabConfig = creativeTabsConfig.TabConfigs.FirstOrDefault((TabConfig cfg) => cfg.Code == tab.Code);
			if (tabConfig == null)
			{
				tabConfig = new TabConfig
				{
					Code = tab.Code,
					ListOrder = 1.0
				};
			}
			int num = 0;
			for (int num2 = 0; num2 < orderedTabs.Count && orderedTabs[num2].ListOrder < tabConfig.ListOrder; num2++)
			{
				num++;
			}
			orderedTabs.Insert(num, tabConfig);
		}
		int num3 = 0;
		GuiTab[] tabs2 = new GuiTab[orderedTabs.Count];
		double val = 0.0;
		double num4 = GuiElement.scaled(3.0);
		CairoFont cairoFont = CairoFont.WhiteDetailText().WithFontSize(17f);
		int i;
		for (i = 0; i < orderedTabs.Count; i++)
		{
			int index = tabs.FirstOrDefault((CreativeTab creativeTab) => creativeTab.Code == orderedTabs[i].Code).Index;
			if (index == currentTabIndex)
			{
				num3 = i;
			}
			tabs2[i] = new GuiTab
			{
				DataInt = index,
				Name = Lang.Get("tabname-" + orderedTabs[i].Code),
				PaddingTop = orderedTabs[i].PaddingTop
			};
			val = Math.Max(cairoFont.GetTextExtents(tabs2[i].Name).Width + 1.0 + 2.0 * num4, val);
		}
		elementBounds5.fixedWidth = Math.Max(elementBounds5.fixedWidth, val);
		elementBounds5.fixedX = 0.0 - elementBounds5.fixedWidth;
		if (creativeInvDialog != null)
		{
			creativeInvDialog.Dispose();
		}
		GuiTab[] tabs3 = tabs2;
		GuiTab[] array = null;
		if (tabs2.Length > 16)
		{
			tabs3 = tabs2.Take(16).ToArray();
			array = tabs2.Skip(16).ToArray();
		}
		creativeInvDialog = capi.Gui.CreateCompo("inventory-creative", elementBounds4).AddShadedDialogBG(ElementBounds.Fill).AddDialogTitleBar(Lang.Get("Creative Inventory"), CloseIconPressed)
			.AddVerticalTabs(tabs3, elementBounds5, OnTabClicked, "verticalTabs");
		if (array != null)
		{
			creativeInvDialog.AddVerticalTabs(array, bounds3, (int num5, GuiTab guiTab) =>
			{
				OnTabClicked(num5 + 16, tabs2[num5 + 16]);
			}, "verticalTabsR");
		}
		creativeInvDialog.AddInset(elementBounds3, 3).BeginClip(creativeClippingBounds).AddItemSlotGrid(creativeInv, SendInvPacket, cols, elementBounds2, "slotgrid")
			.EndClip()
			.AddVerticalScrollbar(OnNewScrollbarvalue, bounds, "scrollbar")
			.AddTextInput(bounds2, OnTextChanged, null, "searchbox")
			.AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Right), bounds4, "searchResults");
		if (array != null)
		{
			creativeInvDialog.GetVerticalTab("verticalTabsR").Right = true;
		}
		creativeInvDialog.Compose();
		creativeInvDialog.UnfocusOwnElements();
		creativeInvDialog.GetScrollbar("scrollbar").SetHeights((float)elementBounds.fixedHeight, (float)(elementBounds2.fixedHeight + unscaledSlotPadding));
		creativeInvDialog.GetTextInput("searchbox").DeleteOnRefocusBackSpace = true;
		creativeInvDialog.GetTextInput("searchbox").SetPlaceHolderText(Lang.Get("Search..."));
		creativeInvDialog.GetVerticalTab((num3 < 16) ? "verticalTabs" : "verticalTabsR").SetValue((num3 < 16) ? num3 : (num3 - 16), triggerHandler: false);
		creativeInv.SetTab(currentTabIndex);
		update();
	}

	private void ComposeSurvivalInvDialog()
	{
		double elementToDialogPadding = GuiStyle.ElementToDialogPadding;
		double unscaledSlotPadding = GuiElementItemSlotGridBase.unscaledSlotPadding;
		int rows = (prevRows = (int)Math.Ceiling((float)backpackInv.Count / 6f));
		ElementBounds elementBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, unscaledSlotPadding, unscaledSlotPadding, 6, 7).FixedGrow(2.0 * unscaledSlotPadding, 2.0 * unscaledSlotPadding);
		ElementBounds elementBounds2 = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0.0, 0.0, 6, rows);
		ElementBounds elementBounds3 = elementBounds.ForkBoundingParent(3.0, 3.0, 3.0, 3.0);
		ElementBounds elementBounds4 = elementBounds.CopyOffsetedSibling();
		elementBounds4.fixedHeight -= 3.0;
		ElementBounds elementBounds5 = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0.0, 0.0, 3, 3).FixedRightOf(elementBounds3, 45.0);
		elementBounds5.fixedY += 50.0;
		ElementBounds elementBounds6 = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0.0, 0.0, 1, 1).FixedRightOf(elementBounds3, 45.0).FixedUnder(elementBounds5, 20.0);
		elementBounds6.fixedX += unscaledSlotPadding + GuiElementPassiveItemSlot.unscaledSlotSize;
		ElementBounds elementBounds7 = elementBounds3.ForkBoundingParent(elementToDialogPadding, elementToDialogPadding + 30.0, elementToDialogPadding + elementBounds5.fixedWidth + 20.0, elementToDialogPadding);
		if (capi.Settings.Bool["immersiveMouseMode"])
		{
			elementBounds7.WithAlignment(EnumDialogArea.RightMiddle).WithFixedAlignmentOffset(-12.0, 0.0);
		}
		else
		{
			elementBounds7.WithAlignment(EnumDialogArea.CenterMiddle).WithFixedAlignmentOffset(20.0, 0.0);
		}
		ElementBounds elementBounds8 = ElementStdBounds.VerticalScrollbar(elementBounds3).WithParent(elementBounds7);
		elementBounds8.fixedOffsetX -= 2.0;
		elementBounds8.fixedWidth = 15.0;
		survivalInvDialog = capi.Gui.CreateCompo("inventory-backpack", elementBounds7).AddShadedDialogBG(ElementBounds.Fill).AddDialogTitleBar(Lang.Get("Inventory and Crafting"), CloseIconPressed)
			.AddVerticalScrollbar(OnNewScrollbarvalue, elementBounds8, "scrollbar")
			.AddInset(elementBounds3, 3)
			.BeginClip(elementBounds4)
			.AddItemSlotGridExcl(backpackInv, SendInvPacket, 6, new int[4] { 0, 1, 2, 3 }, elementBounds2, "slotgrid")
			.EndClip()
			.AddItemSlotGrid(craftingInv, SendInvPacket, 3, new int[9] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, elementBounds5, "craftinggrid")
			.AddItemSlotGrid(craftingInv, SendInvPacket, 1, new int[1] { 9 }, elementBounds6, "outputslot")
			.Compose();
		survivalInvDialog.GetScrollbar("scrollbar").SetHeights((float)elementBounds.fixedHeight, (float)(elementBounds2.fixedHeight + unscaledSlotPadding));
	}

	private void BackpackInv_SlotModified(int t1)
	{
		if ((int)Math.Ceiling((float)backpackInv.Count / 6f) == prevRows)
		{
			return;
		}
		ComposeSurvivalInvDialog();
		Composers.Remove("maininventory");
		if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
		{
			if (creativeInvDialog == null)
			{
				ComposeCreativeInvDialog();
			}
			Composers["maininventory"] = creativeInvDialog ?? survivalInvDialog;
		}
		if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Guest || capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Survival)
		{
			Composers["maininventory"] = survivalInvDialog;
		}
	}

	private void update()
	{
		OnTextChanged(creativeInvDialog.GetTextInput("searchbox").GetText());
	}

	private void OnTabClicked(int index, GuiTab tab)
	{
		currentTabIndex = tab.DataInt;
		creativeInv.SetTab(tab.DataInt);
		creativeInvDialog.GetSlotGrid("slotgrid").DetermineAvailableSlots();
		GuiElementItemSlotGrid slotGrid = creativeInvDialog.GetSlotGrid("slotgrid");
		ElementBounds elementBounds = ElementStdBounds.SlotGrid(rows: (int)Math.Ceiling((float)slotGrid.renderedSlots.Count / (float)cols), alignment: EnumDialogArea.None, x: 0.0, y: 0.0, cols: cols);
		slotGrid.Bounds.fixedHeight = elementBounds.fixedHeight;
		update();
	}

	private void SendInvPacket(object packet)
	{
		capi.Network.SendPacketClient(packet);
	}

	private void CloseIconPressed()
	{
		TryClose();
	}

	private void OnNewScrollbarvalue(float value)
	{
		if (IsOpened())
		{
			if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
			{
				ElementBounds bounds = creativeInvDialog.GetSlotGrid("slotgrid").Bounds;
				bounds.fixedY = 10.0 - GuiElementItemSlotGridBase.unscaledSlotPadding - (double)value;
				bounds.CalcWorldBounds();
			}
			else if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Survival && survivalInvDialog != null)
			{
				ElementBounds bounds2 = survivalInvDialog.GetSlotGridExcl("slotgrid").Bounds;
				bounds2.fixedY = 10.0 - GuiElementItemSlotGridBase.unscaledSlotPadding - (double)value;
				bounds2.CalcWorldBounds();
			}
		}
	}

	private void OnTextChanged(string text)
	{
		if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
		{
			GuiElementItemSlotGrid slotGrid = creativeInvDialog.GetSlotGrid("slotgrid");
			slotGrid.FilterItemsBySearchText(text, creativeInv.CurrentTab.SearchCache, creativeInv.CurrentTab.SearchCacheNames);
			int rows = (int)Math.Ceiling((float)slotGrid.renderedSlots.Count / (float)cols);
			ElementBounds elementBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0.0, 0.0, cols, rows);
			creativeInvDialog.GetScrollbar("scrollbar").SetNewTotalHeight((float)(elementBounds.fixedHeight + 3.0));
			creativeInvDialog.GetScrollbar("scrollbar").SetScrollbarPosition(0);
			creativeInvDialog.GetDynamicText("searchResults").SetNewText(Lang.Get("creative-searchresults", slotGrid.renderedSlots.Count));
		}
	}

	public override bool TryOpen()
	{
		if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Spectator)
		{
			return false;
		}
		return base.TryOpen();
	}

	public override void OnGuiOpened()
	{
		base.OnGuiOpened();
		ComposeGui(firstBuild: false);
		capi.World.Player.Entity.TryStopHandAction(true, EnumItemUseCancelReason.OpenedGui);
		if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Guest || capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Survival)
		{
			if (craftingInv != null)
			{
				capi.Network.SendPacketClient((Packet_Client)craftingInv.Open(capi.World.Player));
			}
			if (backpackInv != null)
			{
				capi.Network.SendPacketClient((Packet_Client)backpackInv.Open(capi.World.Player));
			}
		}
		if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative && creativeInv != null)
		{
			capi.Network.SendPacketClient((Packet_Client)creativeInv.Open(capi.World.Player));
		}
	}

	public override void OnGuiClosed()
	{
		if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
		{
			creativeInvDialog?.GetTextInput("searchbox")?.SetValue("");
			creativeInvDialog?.GetSlotGrid("slotgrid")?.OnGuiClosed(capi);
			capi.Network.SendPacketClient((Packet_Client)creativeInv.Close(capi.World.Player));
			return;
		}
		if (craftingInv != null)
		{
			foreach (ItemSlot item in craftingInv)
			{
				if (!item.Empty)
				{
					ItemStackMoveOperation op = new ItemStackMoveOperation(capi.World, EnumMouseButton.Left, (EnumModifierKey)0, EnumMergePriority.AutoMerge, item.StackSize);
					op.ActingPlayer = capi.World.Player;
					object[] array = capi.World.Player.InventoryManager.TryTransferAway(item, ref op, onlyPlayerInventory: true);
					int num = 0;
					while (array != null && num < array.Length)
					{
						capi.Network.SendPacketClient((Packet_Client)array[num]);
						num++;
					}
				}
			}
			capi.World.Player.InventoryManager.DropAllInventoryItems(craftingInv);
			capi.Network.SendPacketClient((Packet_Client)craftingInv.Close(capi.World.Player));
			survivalInvDialog.GetSlotGrid("craftinggrid").OnGuiClosed(capi);
			survivalInvDialog.GetSlotGrid("outputslot").OnGuiClosed(capi);
		}
		if (survivalInvDialog != null)
		{
			capi.Network.SendPacketClient((Packet_Client)backpackInv.Close(capi.World.Player));
			survivalInvDialog.GetSlotGridExcl("slotgrid").OnGuiClosed(capi);
		}
	}

	private void OnPlayerModeChanged()
	{
		if (IsOpened() && prevGameMode != capi.World.Player.WorldData.CurrentGameMode)
		{
			Composers.Remove("maininventory");
			ComposeGui(firstBuild: false);
			prevGameMode = capi.World.Player.WorldData.CurrentGameMode;
			if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
			{
				capi.Network.SendPacketClient((Packet_Client)creativeInv.Open(capi.World.Player));
				capi.Network.SendPacketClient((Packet_Client)backpackInv.Close(capi.World.Player));
			}
			if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Guest || capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Survival)
			{
				capi.Network.SendPacketClient((Packet_Client)backpackInv.Open(capi.World.Player));
				capi.Network.SendPacketClient((Packet_Client)creativeInv.Close(capi.World.Player));
			}
		}
	}

	internal override bool OnKeyCombinationToggle(KeyCombination viaKeyComb)
	{
		if (IsOpened() && creativeInv != null && creativeInvDialog != null)
		{
			GuiElementTextInput textInput = creativeInvDialog.GetTextInput("searchbox");
			if (textInput != null && textInput.HasFocus)
			{
				return false;
			}
		}
		return base.OnKeyCombinationToggle(viaKeyComb);
	}

	public override void OnMouseDown(MouseEvent args)
	{
		if (args.Handled)
		{
			return;
		}
		foreach (GuiComposer value in Composers.Values)
		{
			value.OnMouseDown(args);
			if (args.Handled)
			{
				return;
			}
		}
		if (!args.Handled && creativeInv != null && creativeClippingBounds != null && creativeClippingBounds.PointInside(args.X, args.Y) && capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
		{
			ItemSlot itemSlot = capi.World.Player.InventoryManager.GetOwnInventory("mouse")[0];
			if (!itemSlot.Empty)
			{
				ItemStackMoveOperation op = new ItemStackMoveOperation(capi.World, EnumMouseButton.Left, (EnumModifierKey)0, EnumMergePriority.AutoMerge);
				op.ActingPlayer = capi.World.Player;
				op.CurrentPriority = EnumMergePriority.DirectMerge;
				int slotId = (itemSlot.Itemstack.Equals(capi.World, creativeInv[0].Itemstack, GlobalConstants.IgnoredStackAttributes) ? 1 : 0);
				object obj = creativeInv.ActivateSlot(slotId, itemSlot, ref op);
				if (obj != null)
				{
					SendInvPacket(obj);
				}
			}
		}
		if (args.Handled)
		{
			return;
		}
		foreach (GuiComposer value2 in Composers.Values)
		{
			if (value2.Bounds.PointInside(args.X, args.Y))
			{
				args.Handled = true;
			}
		}
	}

	public override bool CaptureAllInputs()
	{
		if (IsOpened())
		{
			return creativeInvDialog?.GetTextInput("searchbox").HasFocus ?? false;
		}
		return false;
	}

	public override void Dispose()
	{
		base.Dispose();
		creativeInvDialog?.Dispose();
		survivalInvDialog?.Dispose();
	}
}
