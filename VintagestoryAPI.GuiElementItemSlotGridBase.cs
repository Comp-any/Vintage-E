using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.API.Client;

/// <summary>
/// A base class for the slot grid.  For all your slot grid needs.
/// </summary>
public abstract class GuiElementItemSlotGridBase : GuiElement
{
	public static double unscaledSlotPadding = 3.0;

	protected IInventory inventory;

	public Vintagestory.API.Datastructures.OrderedDictionary<int, ItemSlot> availableSlots = new Vintagestory.API.Datastructures.OrderedDictionary<int, ItemSlot>();

	public Vintagestory.API.Datastructures.OrderedDictionary<int, ItemSlot> renderedSlots = new Vintagestory.API.Datastructures.OrderedDictionary<int, ItemSlot>();

	protected int cols;

	protected int rows;

	protected int prevSlotQuantity;

	private Dictionary<string, int> slotTextureIdsByBgIconAndColor = new Dictionary<string, int>();

	private Dictionary<int, float> slotNotifiedZoomEffect = new Dictionary<int, float>();

	public ElementBounds[] SlotBounds;

	protected ElementBounds[] scissorBounds;

	public LoadedTexture slotTexture;

	public LoadedTexture highlightSlotTexture;

	public LoadedTexture crossedOutTexture;

	public LoadedTexture[] slotQuantityTextures;

	public GuiElementTextBase textComposer;

	public int highlightSlotId = -1;

	public int hoverSlotId = -1;

	protected string searchText;

	protected Action<object> SendPacketHandler;

	private bool isLastSlotGridInComposite;

	private bool isRightMouseDownStartedInsideElem;

	private bool isLeftMouseDownStartedInsideElem;

	private HashSet<int> wasMouseDownOnSlotIndex = new HashSet<int>();

	private Vintagestory.API.Datastructures.OrderedDictionary<int, int> distributeStacksPrevStackSizeBySlotId = new Vintagestory.API.Datastructures.OrderedDictionary<int, int>();

	private Vintagestory.API.Datastructures.OrderedDictionary<int, int> distributeStacksAddedStackSizeBySlotId = new Vintagestory.API.Datastructures.OrderedDictionary<int, int>();

	private ItemStack referenceDistributStack;

	public CanClickSlotDelegate CanClickSlot;

	public IInventory hoverInv;

	public DrawIconDelegate DrawIconHandler;

	public int tabbedSlotId = -1;

	public bool KeyboardControlEnabled = true;

	public LoadedTexture HighlightSlotTexture => highlightSlotTexture;

	public bool AlwaysRenderIcon { get; set; }

	public override bool Focusable => true;

	/// <summary>
	/// Creates a new instance of this class.
	/// </summary>
	/// <param name="capi">The client API</param>
	/// <param name="inventory">The attached inventory</param>
	/// <param name="sendPacket">A handler that should send supplied network packet to the server, if the inventory modifications should be synced</param>
	/// <param name="columns">The number of columns in the GUI.</param>
	/// <param name="bounds">The bounds of the slot grid.</param>
	public GuiElementItemSlotGridBase(ICoreClientAPI capi, IInventory inventory, Action<object> sendPacket, int columns, ElementBounds bounds)
		: base(capi, bounds)
	{
		slotTexture = new LoadedTexture(capi);
		highlightSlotTexture = new LoadedTexture(capi);
		crossedOutTexture = new LoadedTexture(capi);
		prevSlotQuantity = inventory.Count;
		this.inventory = inventory;
		cols = columns;
		SendPacketHandler = sendPacket;
		inventory.SlotNotified += OnSlotNotified;
		DrawIconHandler = api.Gui.Icons.DrawIconInt;
	}

	private void OnSlotNotified(int slotid)
	{
		slotNotifiedZoomEffect[slotid] = 0.4f;
	}

	public override void ComposeElements(Context unusedCtx, ImageSurface unusedSurface)
	{
		ComposeInteractiveElements();
	}

	private void ComposeInteractiveElements()
	{
		SlotBounds = new ElementBounds[availableSlots.Count];
		scissorBounds = new ElementBounds[availableSlots.Count];
		if (slotQuantityTextures != null)
		{
			Dispose();
		}
		slotQuantityTextures = new LoadedTexture[availableSlots.Count];
		for (int i = 0; i < slotQuantityTextures.Length; i++)
		{
			slotQuantityTextures[i] = new LoadedTexture(api);
		}
		rows = (int)Math.Ceiling(1f * (float)availableSlots.Count / (float)cols);
		Bounds.CalcWorldBounds();
		double unscaledSlotSize = GuiElementPassiveItemSlot.unscaledSlotSize;
		double unscaledSlotSize2 = GuiElementPassiveItemSlot.unscaledSlotSize;
		double absSlotPadding = GuiElement.scaled(unscaledSlotPadding);
		double num = GuiElement.scaled(GuiElementPassiveItemSlot.unscaledSlotSize);
		double num2 = GuiElement.scaled(GuiElementPassiveItemSlot.unscaledSlotSize);
		ElementBounds bounds = ElementBounds.Fixed(0.0, GuiElementPassiveItemSlot.unscaledSlotSize - GuiStyle.SmallishFontSize - 2.0, GuiElementPassiveItemSlot.unscaledSlotSize - 5.0, GuiElementPassiveItemSlot.unscaledSlotSize - 5.0).WithEmptyParent();
		CairoFont cairoFont = CairoFont.WhiteSmallText().WithFontSize((float)GuiStyle.SmallishFontSize);
		cairoFont.FontWeight = FontWeight.Bold;
		cairoFont.Color = new double[4] { 1.0, 1.0, 1.0, 1.0 };
		cairoFont.StrokeColor = new double[4] { 0.0, 0.0, 0.0, 1.0 };
		cairoFont.StrokeWidth = RuntimeEnv.GUIScale;
		textComposer = new GuiElementStaticText(api, "", EnumTextOrientation.Right, bounds, cairoFont);
		ImageSurface imageSurface = new ImageSurface(Format.Argb32, (int)num, (int)num);
		Context context = genContext(imageSurface);
		context.SetSourceRGBA(GuiStyle.DialogSlotBackColor);
		GuiElement.RoundRectangle(context, 0.0, 0.0, num, num2, GuiStyle.ElementBGRadius);
		context.Fill();
		context.SetSourceRGBA(GuiStyle.DialogSlotFrontColor);
		GuiElement.RoundRectangle(context, 0.0, 0.0, num, num2, GuiStyle.ElementBGRadius);
		context.LineWidth = GuiElement.scaled(4.5);
		context.Stroke();
		imageSurface.BlurFull(GuiElement.scaled(4.0));
		imageSurface.BlurFull(GuiElement.scaled(4.0));
		GuiElement.RoundRectangle(context, 0.0, 0.0, num, num2, 1.0);
		context.LineWidth = GuiElement.scaled(4.5);
		context.SetSourceRGBA(0.0, 0.0, 0.0, 0.8);
		context.Stroke();
		generateTexture(imageSurface, ref slotTexture);
		context.Dispose();
		imageSurface.Dispose();
		foreach (var (_, itemSlot2) in availableSlots)
		{
			string key = itemSlot2.BackgroundIcon + "-" + itemSlot2.HexBackgroundColor;
			if ((itemSlot2.BackgroundIcon != null || itemSlot2.HexBackgroundColor != null) && !slotTextureIdsByBgIconAndColor.ContainsKey(key))
			{
				int value = DrawSlotBackgrounds(itemSlot2, absSlotPadding, num, num2);
				slotTextureIdsByBgIconAndColor.Add(key, value);
			}
		}
		int num4 = (int)num - 4;
		imageSurface = new ImageSurface(Format.Argb32, num4, num4);
		context = genContext(imageSurface);
		context.SetSourceRGBA(0.0, 0.0, 0.0, 0.8);
		api.Gui.Icons.DrawCross(context, 4.0, 4.0, 7.0, num4 - 18, preserverePath: true);
		context.SetSourceRGBA(1.0, 0.2, 0.2, 0.8);
		context.LineWidth = 2.0;
		context.Stroke();
		generateTexture(imageSurface, ref crossedOutTexture);
		context.Dispose();
		imageSurface.Dispose();
		imageSurface = new ImageSurface(Format.Argb32, (int)num + 4, (int)num + 4);
		context = genContext(imageSurface);
		context.SetSourceRGBA(GuiStyle.ActiveSlotColor);
		GuiElement.RoundRectangle(context, 0.0, 0.0, num + 4.0, num2 + 4.0, GuiStyle.ElementBGRadius);
		context.LineWidth = GuiElement.scaled(9.0);
		context.StrokePreserve();
		imageSurface.BlurFull(GuiElement.scaled(6.0));
		context.StrokePreserve();
		imageSurface.BlurFull(GuiElement.scaled(6.0));
		context.LineWidth = GuiElement.scaled(3.0);
		context.Stroke();
		context.LineWidth = GuiElement.scaled(1.0);
		context.SetSourceRGBA(GuiStyle.ActiveSlotColor);
		context.Stroke();
		generateTexture(imageSurface, ref highlightSlotTexture);
		context.Dispose();
		imageSurface.Dispose();
		int num5 = 0;
		foreach (KeyValuePair<int, ItemSlot> availableSlot in availableSlots)
		{
			int num6 = num5 % cols;
			int num7 = num5 / cols;
			double num8 = (double)num6 * (unscaledSlotSize + unscaledSlotPadding);
			double num9 = (double)num7 * (unscaledSlotSize2 + unscaledSlotPadding);
			ItemSlot slot = inventory[availableSlot.Key];
			SlotBounds[num5] = ElementBounds.Fixed(num8, num9, unscaledSlotSize, unscaledSlotSize2).WithParent(Bounds);
			SlotBounds[num5].CalcWorldBounds();
			scissorBounds[num5] = ElementBounds.Fixed(num8 + 1.75, num9 + 1.5, unscaledSlotSize - 4.0, unscaledSlotSize2 - 4.0).WithParent(Bounds);
			scissorBounds[num5].CalcWorldBounds();
			ComposeSlotOverlays(slot, availableSlot.Key, num5);
			num5++;
		}
	}

	public static int DrawSlotBackground(ICoreClientAPI api, double absSlotWidth, double absSlotHeight, double[] bgcolor, double[] fontcolor, Action<Context> extraDrawing)
	{
		ImageSurface imageSurface = new ImageSurface(Format.Argb32, (int)absSlotWidth, (int)absSlotWidth);
		Context context = GuiElement.GenContext(imageSurface);
		context.SetSourceRGBA(bgcolor);
		GuiElement.RoundRectangle(context, 0.0, 0.0, absSlotWidth, absSlotHeight, GuiStyle.ElementBGRadius);
		context.Fill();
		context.SetSourceRGBA(fontcolor);
		GuiElement.RoundRectangle(context, 0.0, 0.0, absSlotWidth, absSlotHeight, GuiStyle.ElementBGRadius);
		context.LineWidth = GuiElement.scaled(4.5);
		context.Stroke();
		imageSurface.BlurFull(GuiElement.scaled(4.0));
		imageSurface.BlurFull(GuiElement.scaled(4.0));
		context.SetSourceRGBA(0.0, 0.0, 0.0, 0.8);
		GuiElement.RoundRectangle(context, 0.0, 0.0, absSlotWidth, absSlotHeight, 1.0);
		context.LineWidth = GuiElement.scaled(4.5);
		context.Stroke();
		extraDrawing?.Invoke(context);
		int textureId = 0;
		GuiElement.GenerateTexture(api, imageSurface, ref textureId);
		context.Dispose();
		imageSurface.Dispose();
		return textureId;
	}

	private int DrawSlotBackgrounds(ItemSlot slot, double absSlotPadding, double absSlotWidth, double absSlotHeight)
	{
		double[] array;
		double[] fontcolor;
		if (slot.HexBackgroundColor != null)
		{
			array = ColorUtil.Hex2Doubles(slot.HexBackgroundColor);
			fontcolor = new double[4]
			{
				array[0] * 0.25,
				array[1] * 0.25,
				array[2] * 0.25,
				1.0
			};
		}
		else
		{
			array = GuiStyle.DialogSlotBackColor;
			fontcolor = GuiStyle.DialogSlotFrontColor;
		}
		return DrawSlotBackground(api, absSlotWidth, absSlotHeight, array, fontcolor, (Context slotCtx) =>
		{
			if (slot.BackgroundIcon != null)
			{
				DrawIconHandler?.Invoke(slotCtx, slot.BackgroundIcon, 2 * (int)absSlotPadding, 2 * (int)absSlotPadding, (int)(absSlotWidth - 4.0 * absSlotPadding), (int)(absSlotHeight - 4.0 * absSlotPadding), new double[4] { 0.0, 0.0, 0.0, 0.2 });
			}
		});
	}

	private bool ComposeSlotOverlays(ItemSlot slot, int slotId, int slotIndex)
	{
		if (!availableSlots.ContainsKey(slotId))
		{
			return false;
		}
		if (slot.Itemstack == null)
		{
			return true;
		}
		bool flag = slot.Itemstack.Collectible.ShouldDisplayItemDamage(slot.Itemstack);
		if (!flag)
		{
			slotQuantityTextures[slotIndex].Dispose();
			slotQuantityTextures[slotIndex] = new LoadedTexture(api);
			return true;
		}
		ImageSurface imageSurface = new ImageSurface(Format.Argb32, (int)SlotBounds[slotIndex].InnerWidth, (int)SlotBounds[slotIndex].InnerHeight);
		Context context = genContext(imageSurface);
		context.SetSourceRGBA(0.0, 0.0, 0.0, 0.0);
		context.Paint();
		if (flag)
		{
			double x = GuiElement.scaled(4.0);
			double y = (double)(int)SlotBounds[slotIndex].InnerHeight - GuiElement.scaled(3.0) - GuiElement.scaled(4.0);
			context.SetSourceRGBA(GuiStyle.DialogStrongBgColor);
			double width = SlotBounds[slotIndex].InnerWidth - GuiElement.scaled(8.0);
			double height = GuiElement.scaled(4.0);
			GuiElement.RoundRectangle(context, x, y, width, height, 1.0);
			context.FillPreserve();
			ShadePath(context);
			float[] array = ColorUtil.ToRGBAFloats(slot.Itemstack.Collectible.GetItemDamageColor(slot.Itemstack));
			context.SetSourceRGB(array[0], array[1], array[2]);
			int maxDurability = slot.Itemstack.Collectible.GetMaxDurability(slot.Itemstack);
			width = (double)((float)slot.Itemstack.Collectible.GetRemainingDurability(slot.Itemstack) / (float)maxDurability) * (SlotBounds[slotIndex].InnerWidth - GuiElement.scaled(8.0));
			GuiElement.RoundRectangle(context, x, y, width, height, 1.0);
			context.FillPreserve();
			ShadePath(context);
		}
		generateTexture(imageSurface, ref slotQuantityTextures[slotIndex]);
		context.Dispose();
		imageSurface.Dispose();
		return true;
	}

	public override void PostRenderInteractiveElements(float deltaTime)
	{
		if (slotNotifiedZoomEffect.Count > 0)
		{
			foreach (int item in new List<int>(slotNotifiedZoomEffect.Keys))
			{
				slotNotifiedZoomEffect[item] -= deltaTime;
				if (slotNotifiedZoomEffect[item] <= 0f)
				{
					slotNotifiedZoomEffect.Remove(item);
				}
			}
		}
		if (prevSlotQuantity != inventory.Count)
		{
			prevSlotQuantity = inventory.Count;
			inventory.DirtySlots.Clear();
			ComposeElements(null, null);
		}
		else
		{
			if (inventory.DirtySlots.Count == 0)
			{
				return;
			}
			List<int> list = new List<int>();
			foreach (int dirtySlot in inventory.DirtySlots)
			{
				ItemSlot slot = inventory[dirtySlot];
				if (ComposeSlotOverlays(slot, dirtySlot, availableSlots.IndexOfKey(dirtySlot)))
				{
					list.Add(dirtySlot);
				}
			}
			if (!isLastSlotGridInComposite)
			{
				return;
			}
			foreach (int item2 in list)
			{
				inventory.DirtySlots.Remove(item2);
			}
		}
	}

	public override void RenderInteractiveElements(float deltaTime)
	{
		double absSlotPadding = GuiElement.scaled(unscaledSlotPadding);
		double num = GuiElement.scaled(GuiElementPassiveItemSlot.unscaledSlotSize);
		double num2 = GuiElement.scaled(GuiElementPassiveItemSlot.unscaledItemSize);
		double absSlotHeight = GuiElement.scaled(GuiElementPassiveItemSlot.unscaledSlotSize);
		double num3 = num / 2.0;
		_ = InsideClipBounds?.absFixedY ?? 0.0;
		_ = InsideClipBounds?.OuterHeight;
		bool flag = false;
		double absY = Bounds.ParentBounds.absY;
		int num4 = 0;
		foreach (KeyValuePair<int, ItemSlot> renderedSlot in renderedSlots)
		{
			ElementBounds elementBounds = SlotBounds[num4];
			_ = elementBounds.absFixedY;
			_ = elementBounds.OuterHeight;
			if (elementBounds.absY + elementBounds.absInnerHeight < absY)
			{
				num4++;
				continue;
			}
			if (elementBounds.PartiallyInside(Bounds.ParentBounds))
			{
				flag = true;
				ItemSlot value = renderedSlot.Value;
				int key = renderedSlot.Key;
				if (((value.Itemstack == null || AlwaysRenderIcon) && value.BackgroundIcon != null) || value.HexBackgroundColor != null)
				{
					string key2 = value.BackgroundIcon + "-" + value.HexBackgroundColor;
					if (!slotTextureIdsByBgIconAndColor.TryGetValue(key2, out var value2))
					{
						value2 = DrawSlotBackgrounds(value, absSlotPadding, num, absSlotHeight);
						slotTextureIdsByBgIconAndColor.Add(key2, value2);
					}
					api.Render.Render2DTexturePremultipliedAlpha(value2, elementBounds);
				}
				else
				{
					api.Render.Render2DTexturePremultipliedAlpha(slotTexture.TextureId, elementBounds);
				}
				if (highlightSlotId == key || hoverSlotId == key || distributeStacksPrevStackSizeBySlotId.ContainsKey(key))
				{
					api.Render.Render2DTexturePremultipliedAlpha(highlightSlotTexture.TextureId, (int)(elementBounds.renderX - 2.0), (int)(elementBounds.renderY - 2.0), elementBounds.OuterWidthInt + 4, elementBounds.OuterHeightInt + 4);
				}
				if (value.Itemstack == null)
				{
					num4++;
					continue;
				}
				float num5 = 0f;
				float num6 = 0f;
				if (slotNotifiedZoomEffect.ContainsKey(key))
				{
					num5 = 4f * (float)api.World.Rand.NextDouble() - 2f;
					num6 = 4f * (float)api.World.Rand.NextDouble() - 2f;
				}
				api.Render.PushScissor(scissorBounds[num4], stacking: true);
				api.Render.RenderItemstackToGui(value, SlotBounds[num4].renderX + num3 + (double)num6, SlotBounds[num4].renderY + num3 + (double)num5, 90.0, (float)num2, -1, deltaTime);
				api.Render.PopScissor();
				if (value.DrawUnavailable)
				{
					api.Render.Render2DTexturePremultipliedAlpha(crossedOutTexture.TextureId, (int)elementBounds.renderX, (int)elementBounds.renderY, crossedOutTexture.Width, crossedOutTexture.Height, 250f);
				}
				if (slotQuantityTextures[num4].TextureId != 0)
				{
					api.Render.Render2DTexturePremultipliedAlpha(slotQuantityTextures[num4].TextureId, SlotBounds[num4]);
				}
			}
			else if (flag)
			{
				break;
			}
			num4++;
		}
	}

	public void OnGuiClosed(ICoreClientAPI api)
	{
		if (hoverSlotId != -1 && inventory[hoverSlotId] != null)
		{
			api.Input.TriggerOnMouseLeaveSlot(inventory[hoverSlotId]);
		}
		hoverSlotId = -1;
		tabbedSlotId = -1;
		(inventory as InventoryBase).InvNetworkUtil.PauseInventoryUpdates = false;
		api.World.Player.InventoryManager.MouseItemSlot.Inventory.InvNetworkUtil.PauseInventoryUpdates = false;
	}

	public override int OutlineColor()
	{
		return -16711936;
	}

	/// <summary>
	/// Renders only a subset of all available slots filtered by searching given text on the item name/description
	/// </summary>
	/// <param name="text"></param>
	/// <param name="searchCache">Can be set to increase search performance, otherwise a slow search is performed</param>
	/// <param name="searchCacheNames"></param>
	public void FilterItemsBySearchText(string text, Dictionary<int, string> searchCache = null, Dictionary<int, string> searchCacheNames = null)
	{
		searchText = text.ToSearchFriendly().ToLowerInvariant();
		renderedSlots.Clear();
		Vintagestory.API.Datastructures.OrderedDictionary<int, WeightedSlot> orderedDictionary = new Vintagestory.API.Datastructures.OrderedDictionary<int, WeightedSlot>();
		foreach (KeyValuePair<int, ItemSlot> availableSlot in availableSlots)
		{
			ItemSlot itemSlot = inventory[availableSlot.Key];
			if (itemSlot.Itemstack == null)
			{
				continue;
			}
			if (searchText == null || searchText.Length == 0)
			{
				renderedSlots.Add(availableSlot.Key, itemSlot);
			}
			else
			{
				if (searchCacheNames == null)
				{
					continue;
				}
				string text2 = searchCacheNames[availableSlot.Key];
				if (searchCache != null && searchCache.TryGetValue(availableSlot.Key, out var value))
				{
					int num = text2.IndexOf(searchText, StringComparison.InvariantCultureIgnoreCase);
					if (num == 0 && text2.Length == searchText.Length)
					{
						orderedDictionary.Add(availableSlot.Key, new WeightedSlot
						{
							slot = itemSlot,
							weight = 0f
						});
					}
					else if (num == 0 && text2.Length > searchText.Length && text2[searchText.Length] == ' ')
					{
						orderedDictionary.Add(availableSlot.Key, new WeightedSlot
						{
							slot = itemSlot,
							weight = 0.125f
						});
					}
					else if (num > 0 && text2[num - 1] == ' ' && num + searchText.Length == text2.Length)
					{
						orderedDictionary.Add(availableSlot.Key, new WeightedSlot
						{
							slot = itemSlot,
							weight = 0.25f
						});
					}
					else if (num > 0 && text2[num - 1] == ' ')
					{
						orderedDictionary.Add(availableSlot.Key, new WeightedSlot
						{
							slot = itemSlot,
							weight = 0.5f
						});
					}
					else if (num == 0)
					{
						orderedDictionary.Add(availableSlot.Key, new WeightedSlot
						{
							slot = itemSlot,
							weight = 0.75f
						});
					}
					else if (num > 0)
					{
						orderedDictionary.Add(availableSlot.Key, new WeightedSlot
						{
							slot = itemSlot,
							weight = 1f
						});
					}
					else if (value.StartsWith(searchText, StringComparison.InvariantCultureIgnoreCase))
					{
						orderedDictionary.Add(availableSlot.Key, new WeightedSlot
						{
							slot = itemSlot,
							weight = 2f
						});
					}
					else if (value.CaseInsensitiveContains(searchText, StringComparison.InvariantCultureIgnoreCase))
					{
						orderedDictionary.Add(availableSlot.Key, new WeightedSlot
						{
							slot = itemSlot,
							weight = 3f
						});
					}
				}
				else if (itemSlot.Itemstack.MatchesSearchText(api.World, searchText))
				{
					renderedSlots.Add(availableSlot.Key, itemSlot);
				}
			}
		}
		foreach (KeyValuePair<int, WeightedSlot> item in orderedDictionary.OrderBy((KeyValuePair<int, WeightedSlot> pair) => pair.Value.weight))
		{
			renderedSlots.Add(item.Key, item.Value.slot);
		}
		rows = (int)Math.Ceiling(1f * (float)renderedSlots.Count / (float)cols);
		ComposeInteractiveElements();
	}

	public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
	{
		base.OnMouseWheel(api, args);
		if ((!api.Input.KeyboardKeyState[3] && !api.Input.KeyboardKeyState[4]) || !KeyboardControlEnabled || !IsPositionInside(api.Input.MouseX, api.Input.MouseY))
		{
			return;
		}
		for (int i = 0; i < SlotBounds.Length && i < renderedSlots.Count; i++)
		{
			if (SlotBounds[i].PointInside(api.Input.MouseX, api.Input.MouseY))
			{
				SlotMouseWheel(renderedSlots.GetKeyAtIndex(i), args.delta);
				args.SetHandled();
			}
		}
	}

	private void SlotMouseWheel(int slotId, int wheelDelta)
	{
		ItemStackMoveOperation op = new ItemStackMoveOperation(api.World, EnumMouseButton.Wheel, (EnumModifierKey)0, EnumMergePriority.AutoMerge, 1);
		op.WheelDir = ((wheelDelta > 0) ? 1 : (-1));
		op.ActingPlayer = api.World.Player;
		IInventory ownInventory = api.World.Player.InventoryManager.GetOwnInventory("mouse");
		IInventory inventory = this.inventory;
		ItemSlot sourceSlot = ownInventory[0];
		object obj = inventory.ActivateSlot(slotId, sourceSlot, ref op);
		if (obj == null)
		{
			return;
		}
		if (obj is object[] array)
		{
			for (int i = 0; i < array.Length; i++)
			{
				SendPacketHandler(array[i]);
			}
		}
		else
		{
			SendPacketHandler?.Invoke(obj);
		}
	}

	public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
	{
		base.OnKeyDown(api, args);
		if (HasFocus && cols != 0 && rows != 0 && KeyboardControlEnabled)
		{
			if (args.KeyCode == 45)
			{
				tabbedSlotId = Math.Max(-1, tabbedSlotId - cols);
				highlightSlotId = ((tabbedSlotId >= 0) ? renderedSlots.GetKeyAtIndex(tabbedSlotId) : (-1));
			}
			if (args.KeyCode == 46)
			{
				tabbedSlotId = Math.Min(renderedSlots.Count - 1, tabbedSlotId + cols);
				highlightSlotId = renderedSlots.GetKeyAtIndex(tabbedSlotId);
			}
			if (args.KeyCode == 48)
			{
				tabbedSlotId = Math.Min(renderedSlots.Count - 1, tabbedSlotId + 1);
				highlightSlotId = renderedSlots.GetKeyAtIndex(tabbedSlotId);
			}
			if (args.KeyCode == 47)
			{
				tabbedSlotId = Math.Max(-1, tabbedSlotId - 1);
				highlightSlotId = ((tabbedSlotId >= 0) ? renderedSlots.GetKeyAtIndex(tabbedSlotId) : (-1));
			}
			if (args.KeyCode == 49 && highlightSlotId >= 0)
			{
				SlotClick(api, highlightSlotId, EnumMouseButton.Left, shiftPressed: true, ctrlPressed: false, altPressed: false);
			}
		}
	}

	public override void OnMouseDown(ICoreClientAPI api, MouseEvent mouse)
	{
		base.OnMouseDown(api, mouse);
	}

	public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
	{
		if (!Bounds.ParentBounds.PointInside(args.X, args.Y))
		{
			return;
		}
		wasMouseDownOnSlotIndex.Clear();
		distributeStacksPrevStackSizeBySlotId.Clear();
		distributeStacksAddedStackSizeBySlotId.Clear();
		for (int i = 0; i < SlotBounds.Length && i < renderedSlots.Count; i++)
		{
			if (!SlotBounds[i].PointInside(args.X, args.Y))
			{
				continue;
			}
			CanClickSlotDelegate canClickSlot = CanClickSlot;
			if (canClickSlot == null || canClickSlot(i))
			{
				isRightMouseDownStartedInsideElem = args.Button == EnumMouseButton.Right && api.World.Player.InventoryManager.MouseItemSlot.Itemstack != null;
				isLeftMouseDownStartedInsideElem = args.Button == EnumMouseButton.Left && api.World.Player.InventoryManager.MouseItemSlot.Itemstack != null;
				wasMouseDownOnSlotIndex.Add(i);
				int keyAtIndex = renderedSlots.GetKeyAtIndex(i);
				int stackSize = inventory[keyAtIndex].StackSize;
				if (isLeftMouseDownStartedInsideElem)
				{
					referenceDistributStack = api.World.Player.InventoryManager.MouseItemSlot.Itemstack.Clone();
					distributeStacksPrevStackSizeBySlotId.Add(keyAtIndex, inventory[keyAtIndex].StackSize);
				}
				SlotClick(api, renderedSlots.GetKeyAtIndex(i), args.Button, api.Input.KeyboardKeyState[1] || api.Input.KeyboardKeyState[2], api.Input.KeyboardKeyState[3], api.Input.KeyboardKeyState[5]);
				(inventory as InventoryBase).InvNetworkUtil.PauseInventoryUpdates = isLeftMouseDownStartedInsideElem;
				api.World.Player.InventoryManager.MouseItemSlot.Inventory.InvNetworkUtil.PauseInventoryUpdates = isLeftMouseDownStartedInsideElem;
				distributeStacksAddedStackSizeBySlotId[keyAtIndex] = inventory[keyAtIndex].StackSize - stackSize;
				args.Handled = true;
			}
			break;
		}
	}

	public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
	{
		isRightMouseDownStartedInsideElem = false;
		isLeftMouseDownStartedInsideElem = false;
		wasMouseDownOnSlotIndex.Clear();
		distributeStacksPrevStackSizeBySlotId.Clear();
		distributeStacksAddedStackSizeBySlotId.Clear();
		(inventory as InventoryBase).InvNetworkUtil.PauseInventoryUpdates = false;
		api.World.Player.InventoryManager.MouseItemSlot.Inventory.InvNetworkUtil.PauseInventoryUpdates = false;
		base.OnMouseUp(api, args);
	}

	public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
	{
		if (!Bounds.ParentBounds.PointInside(args.X, args.Y))
		{
			if (hoverSlotId != -1)
			{
				api.Input.TriggerOnMouseLeaveSlot(inventory[hoverSlotId]);
			}
			hoverSlotId = -1;
			return;
		}
		for (int i = 0; i < SlotBounds.Length && i < renderedSlots.Count; i++)
		{
			if (!SlotBounds[i].PointInside(args.X, args.Y))
			{
				continue;
			}
			int keyAtIndex = renderedSlots.GetKeyAtIndex(i);
			ItemSlot itemSlot = inventory[keyAtIndex];
			ItemStack itemstack = itemSlot.Itemstack;
			if (isRightMouseDownStartedInsideElem && !wasMouseDownOnSlotIndex.Contains(i))
			{
				wasMouseDownOnSlotIndex.Add(i);
				if (itemstack == null || itemstack.Equals(api.World, api.World.Player.InventoryManager.MouseItemSlot.Itemstack, GlobalConstants.IgnoredStackAttributes))
				{
					SlotClick(api, keyAtIndex, EnumMouseButton.Right, api.Input.KeyboardKeyState[1], api.Input.KeyboardKeyState[3], api.Input.KeyboardKeyState[5]);
				}
			}
			if (isLeftMouseDownStartedInsideElem && !wasMouseDownOnSlotIndex.Contains(i) && (itemstack == null || itemstack.Equals(api.World, referenceDistributStack, GlobalConstants.IgnoredStackAttributes)))
			{
				wasMouseDownOnSlotIndex.Add(i);
				distributeStacksPrevStackSizeBySlotId.Add(keyAtIndex, itemSlot.StackSize);
				int stackSize = inventory[keyAtIndex].StackSize;
				if (api.World.Player.InventoryManager.MouseItemSlot.StackSize > 0)
				{
					SlotClick(api, keyAtIndex, EnumMouseButton.Left, api.Input.KeyboardKeyState[1], api.Input.KeyboardKeyState[3], api.Input.KeyboardKeyState[5]);
					distributeStacksAddedStackSizeBySlotId[keyAtIndex] = inventory[keyAtIndex].StackSize - stackSize;
				}
				if (api.World.Player.InventoryManager.MouseItemSlot.StackSize <= 0)
				{
					RedistributeStacks(keyAtIndex);
				}
			}
			if (keyAtIndex != hoverSlotId && itemSlot != null)
			{
				api.Input.TriggerOnMouseEnterSlot(itemSlot);
				hoverInv = itemSlot.Inventory;
			}
			if (keyAtIndex != hoverSlotId)
			{
				tabbedSlotId = -1;
			}
			hoverSlotId = keyAtIndex;
			return;
		}
		if (hoverSlotId != -1)
		{
			api.Input.TriggerOnMouseLeaveSlot(inventory[hoverSlotId]);
		}
		hoverSlotId = -1;
	}

	public override bool OnMouseLeaveSlot(ICoreClientAPI api, ItemSlot slot)
	{
		if (slot.Inventory == hoverInv)
		{
			hoverSlotId = -1;
		}
		return false;
	}

	private void RedistributeStacks(int intoSlotId)
	{
		int num = referenceDistributStack.StackSize / distributeStacksPrevStackSizeBySlotId.Count;
		for (int i = 0; i < distributeStacksPrevStackSizeBySlotId.Count - 1; i++)
		{
			int keyAtIndex = distributeStacksPrevStackSizeBySlotId.GetKeyAtIndex(i);
			if (keyAtIndex == intoSlotId)
			{
				continue;
			}
			ItemSlot sourceSlot = inventory[keyAtIndex];
			distributeStacksAddedStackSizeBySlotId.TryGetValue(keyAtIndex, out var value);
			if (value > num)
			{
				int num2 = distributeStacksPrevStackSizeBySlotId[keyAtIndex];
				int num3 = num2 + value;
				ItemSlot targetSlot = inventory[intoSlotId];
				ItemStackMoveOperation op = new ItemStackMoveOperation(api.World, EnumMouseButton.Left, (EnumModifierKey)0, EnumMergePriority.AutoMerge);
				op.ActingPlayer = api.World.Player;
				op.RequestedQuantity = num3 - num2 - num;
				object obj = api.World.Player.InventoryManager.TryTransferTo(sourceSlot, targetSlot, ref op);
				distributeStacksAddedStackSizeBySlotId.TryGetValue(intoSlotId, out var value2);
				distributeStacksAddedStackSizeBySlotId[intoSlotId] = value2 + op.MovedQuantity;
				distributeStacksAddedStackSizeBySlotId[keyAtIndex] -= op.MovedQuantity;
				if (obj != null)
				{
					SendPacketHandler(obj);
				}
			}
		}
	}

	public virtual void SlotClick(ICoreClientAPI api, int slotId, EnumMouseButton mouseButton, bool shiftPressed, bool ctrlPressed, bool altPressed)
	{
		_ = api.World.Player.InventoryManager.OpenedInventories;
		IInventory ownInventory = api.World.Player.InventoryManager.GetOwnInventory("mouse");
		EnumModifierKey modifiers = (EnumModifierKey)((shiftPressed ? 2 : 0) | (ctrlPressed ? 1 : 0) | (altPressed ? 4 : 0));
		ItemStackMoveOperation op = new ItemStackMoveOperation(api.World, mouseButton, modifiers, EnumMergePriority.AutoMerge);
		op.ActingPlayer = api.World.Player;
		object obj;
		if (shiftPressed)
		{
			ItemSlot itemSlot = inventory[slotId];
			op.RequestedQuantity = itemSlot.StackSize;
			obj = inventory.ActivateSlot(slotId, itemSlot, ref op);
		}
		else
		{
			op.CurrentPriority = EnumMergePriority.DirectMerge;
			bool empty = ownInventory.Empty;
			CollectibleObject collectibleObject = ownInventory[0].Itemstack?.Collectible;
			obj = inventory.ActivateSlot(slotId, ownInventory[0], ref op);
			if (empty && !ownInventory.Empty)
			{
				api.World.PlaySound(ownInventory[0].Itemstack.Collectible?.HeldSounds?.InvPickup ?? HeldSounds.InvPickUpDefault);
			}
			else if ((!empty && ownInventory.Empty) || collectibleObject?.Id != ownInventory[0].Itemstack?.Collectible?.Id)
			{
				api.World.PlaySound(collectibleObject?.HeldSounds?.InvPlace ?? HeldSounds.InvPlaceDefault);
			}
		}
		if (obj != null)
		{
			if (obj is object[] array)
			{
				for (int i = 0; i < array.Length; i++)
				{
					SendPacketHandler(array[i]);
				}
			}
			else
			{
				SendPacketHandler?.Invoke(obj);
			}
		}
		api.Input.TriggerOnMouseClickSlot(inventory[slotId]);
	}

	/// <summary>
	/// Highlights a specific slot.
	/// </summary>
	/// <param name="slotId">The slot to highlight.</param>
	public void HighlightSlot(int slotId)
	{
		highlightSlotId = slotId;
	}

	/// <summary>
	/// Removes the active slot highlight.
	/// </summary>
	public void RemoveSlotHighlight()
	{
		highlightSlotId = -1;
	}

	public static void UpdateLastSlotGridFlag(GuiComposer composer)
	{
		Dictionary<IInventory, GuiElementItemSlotGridBase> dictionary = new Dictionary<IInventory, GuiElementItemSlotGridBase>();
		foreach (GuiElement value in composer.interactiveElements.Values)
		{
			if (value is GuiElementItemSlotGridBase)
			{
				GuiElementItemSlotGridBase guiElementItemSlotGridBase = value as GuiElementItemSlotGridBase;
				guiElementItemSlotGridBase.isLastSlotGridInComposite = false;
				dictionary[guiElementItemSlotGridBase.inventory] = guiElementItemSlotGridBase;
			}
		}
		foreach (GuiElementItemSlotGridBase value2 in dictionary.Values)
		{
			value2.isLastSlotGridInComposite = true;
		}
	}

	public override void Dispose()
	{
		base.Dispose();
		int num = 0;
		while (slotQuantityTextures != null && num < slotQuantityTextures.Length)
		{
			slotQuantityTextures[num]?.Dispose();
			num++;
		}
		slotTexture.Dispose();
		highlightSlotTexture.Dispose();
		crossedOutTexture?.Dispose();
	}
}
