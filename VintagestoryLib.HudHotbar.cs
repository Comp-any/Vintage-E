using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Common;

namespace Vintagestory.Client.NoObf;

public class HudHotbar : HudElement
{
	private IInventory hotbarInv;

	private IInventory backpackInv;

	private GuiElementItemSlotGrid hotbarSlotGrid;

	private GuiElementItemSlotGrid backpackGrid;

	private long itemInfoTextActiveMs;

	private int lastActiveSlotStacksize = 99999;

	private int lastActiveSlotItemId;

	private EnumItemClass lastActiveClassItemClass;

	private bool scrollWheeledSlotInFrame;

	private bool shouldRecompose;

	private LoadedTexture currentToolModeTexture;

	private LoadedTexture toolModeBgTexture;

	private DrawWorldInteractionUtil wiUtil;

	private Matrixf modelMat = new Matrixf();

	private LoadedTexture gearTexture;

	private Dictionary<SoundAttributes, ILoadedSound> leftActiveSlotIdleSound = new Dictionary<SoundAttributes, ILoadedSound>();

	private Dictionary<SoundAttributes, ILoadedSound> rightActiveSlotIdleSound = new Dictionary<SoundAttributes, ILoadedSound>();

	private ItemStack prevLeftStack;

	private ItemStack prevRightStack;

	private ElementBounds dialogBounds;

	private bool temporalStabilityEnabled;

	private int prevValue = int.MaxValue;

	private int prevIndex = -1;

	private double gearPosition;

	public override double InputOrder => 1.0;

	public override string ToggleKeyCombinationCode => null;

	public HudHotbar(ICoreClientAPI capi)
		: base(capi)
	{
		wiUtil = new DrawWorldInteractionUtil(capi, Composers, "-heldItem");
		wiUtil.UnscaledLineHeight = 25.0;
		wiUtil.FontSize = 16f;
		capi.Event.RegisterGameTickListener(OnGameTick, 20);
		capi.Event.AfterActiveSlotChanged += (ActiveSlotChangeEventArgs ev) =>
		{
			OnActiveSlotChanged(ev.ToSlot);
		};
		capi.Event.RegisterGameTickListener(OnCheckToolMode, 100);
	}

	public override void OnBlockTexturesLoaded()
	{
		ScreenManager.hotkeyManager.SetHotKeyHandler("hotbarslot1", (KeyCombination viaKeyComb) =>
		{
			OnKeySlot(0, moveItems: true);
			return true;
		});
		ScreenManager.hotkeyManager.SetHotKeyHandler("hotbarslot2", (KeyCombination viaKeyComb) =>
		{
			OnKeySlot(1, moveItems: true);
			return true;
		});
		ScreenManager.hotkeyManager.SetHotKeyHandler("hotbarslot3", (KeyCombination viaKeyComb) =>
		{
			OnKeySlot(2, moveItems: true);
			return true;
		});
		ScreenManager.hotkeyManager.SetHotKeyHandler("hotbarslot4", (KeyCombination viaKeyComb) =>
		{
			OnKeySlot(3, moveItems: true);
			return true;
		});
		ScreenManager.hotkeyManager.SetHotKeyHandler("hotbarslot5", (KeyCombination viaKeyComb) =>
		{
			OnKeySlot(4, moveItems: true);
			return true;
		});
		ScreenManager.hotkeyManager.SetHotKeyHandler("hotbarslot6", (KeyCombination viaKeyComb) =>
		{
			OnKeySlot(5, moveItems: true);
			return true;
		});
		ScreenManager.hotkeyManager.SetHotKeyHandler("hotbarslot7", (KeyCombination viaKeyComb) =>
		{
			OnKeySlot(6, moveItems: true);
			return true;
		});
		ScreenManager.hotkeyManager.SetHotKeyHandler("hotbarslot8", (KeyCombination viaKeyComb) =>
		{
			OnKeySlot(7, moveItems: true);
			return true;
		});
		ScreenManager.hotkeyManager.SetHotKeyHandler("hotbarslot9", (KeyCombination viaKeyComb) =>
		{
			OnKeySlot(8, moveItems: true);
			return true;
		});
		ScreenManager.hotkeyManager.SetHotKeyHandler("hotbarslot10", (KeyCombination viaKeyComb) =>
		{
			OnKeySlot(9, moveItems: true);
			return true;
		});
		ScreenManager.hotkeyManager.SetHotKeyHandler("hotbarslot11", (KeyCombination viaKeyComb) =>
		{
			OnKeySlot(10, moveItems: true);
			return true;
		});
		ScreenManager.hotkeyManager.SetHotKeyHandler("hotbarslot12", (KeyCombination viaKeyComb) =>
		{
			OnKeySlot(11, moveItems: true);
			return true;
		});
		ScreenManager.hotkeyManager.SetHotKeyHandler("hotbarslot13", (KeyCombination viaKeyComb) =>
		{
			OnKeySlot(12, moveItems: true);
			return true;
		});
		ScreenManager.hotkeyManager.SetHotKeyHandler("hotbarslot14", (KeyCombination viaKeyComb) =>
		{
			OnKeySlot(13, moveItems: true);
			return true;
		});
		ScreenManager.hotkeyManager.SetHotKeyHandler("fliphandslots", KeyFlipHandSlots);
		ClientSettings.Inst.AddKeyCombinationUpdatedWatcher((string key, KeyCombination value) =>
		{
			if (key.StartsWithOrdinal("hotbarslot"))
			{
				shouldRecompose = true;
			}
		});
		temporalStabilityEnabled = capi.World.Config.GetBool("temporalStability", defaultValue: true);
		if (temporalStabilityEnabled)
		{
			ClientSettings.Inst.AddWatcher("guiScale", (float s) =>
			{
				genGearTexture();
			});
			genGearTexture();
		}
		int num = (int)GuiElement.scaled(32.0);
		toolModeBgTexture = capi.Gui.Icons.GenTexture(num, num, (Context ctx, ImageSurface surface) =>
		{
			ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.5);
			GuiElement.RoundRectangle(ctx, GuiElement.scaled(2.0), GuiElement.scaled(2.0), GuiElement.scaled(28.0), GuiElement.scaled(28.0), 1.0);
			ctx.Fill();
		});
	}

	private void genGearTexture()
	{
		int size = (int)Math.Ceiling(85f * ClientSettings.GUIScale);
		gearTexture?.Dispose();
		gearTexture = capi.Gui.Icons.GenTexture(size + 10, size + 10, (Context ctx, ImageSurface surface) =>
		{
			capi.Gui.Icons.DrawVSGear(ctx, surface, 5, 5, size, size, new double[4] { 0.2, 0.2, 0.2, 1.0 });
		});
	}

	private void OnGameTick(float dt)
	{
		if (itemInfoTextActiveMs > 0 && capi.ElapsedMilliseconds - itemInfoTextActiveMs > 3500)
		{
			itemInfoTextActiveMs = 0L;
			Composers["hotbar"].GetHoverText("iteminfoHover").SetVisible(on: false);
			wiUtil.ComposeBlockWorldInteractionHelp(Array.Empty<WorldInteraction>());
		}
	}

	private void OnCheckToolMode(float dt)
	{
		UpdateCurrentToolMode(capi.World.Player.InventoryManager.ActiveHotbarSlotNumber);
	}

	private void UpdateCurrentToolMode(int newSlotIndex)
	{
		ItemSlot itemSlot = capi.World.Player.InventoryManager.GetHotbarInventory()[newSlotIndex];
		SkillItem[] array = itemSlot?.Itemstack?.Collectible?.GetToolModes(itemSlot, capi.World.Player, capi.World.Player.CurrentBlockSelection);
		bool num = array != null && array.Length != 0;
		currentToolModeTexture = null;
		if (num)
		{
			int toolMode = itemSlot.Itemstack.Collectible.GetToolMode(itemSlot, capi.World.Player, capi.World.Player.CurrentBlockSelection);
			if (toolMode >= 0 && toolMode < array.Length)
			{
				currentToolModeTexture = array[toolMode].Texture;
			}
		}
	}

	public override void OnOwnPlayerDataReceived()
	{
		capi.Gui.Icons.CustomIcons["left_hand"] = capi.Gui.Icons.SvgIconSource(new AssetLocation("textures/icons/character/left_hand.svg"));
		ComposeGuis();
	}

	public void ComposeGuis()
	{
		hotbarInv = capi.World.Player.InventoryManager.GetOwnInventory("hotbar");
		backpackInv = capi.World.Player.InventoryManager.GetOwnInventory("backpack");
		_ = GuiStyle.ElementToDialogPadding;
		if (hotbarInv != null)
		{
			hotbarInv.Open(capi.World.Player);
			backpackInv.Open(capi.World.Player);
			hotbarInv.SlotModified += OnHotbarSlotModified;
			float num = 850f;
			dialogBounds = new ElementBounds
			{
				Alignment = EnumDialogArea.CenterBottom,
				BothSizing = ElementSizing.Fixed,
				fixedWidth = num,
				fixedHeight = 80.0
			}.WithFixedAlignmentOffset(0.0, 5.0);
			ElementBounds bounds = ElementStdBounds.SlotGrid(EnumDialogArea.LeftFixed, 10.0, 10.0, 10, 1);
			ElementBounds bounds2 = ElementStdBounds.SlotGrid(EnumDialogArea.LeftFixed, 110.0, 10.0, 10, 1);
			ElementBounds bounds3 = ElementStdBounds.SlotGrid(EnumDialogArea.RightFixed, 0.0, 10.0, 4, 1).WithFixedAlignmentOffset(-10.0, 0.0);
			ElementBounds bounds4 = ElementBounds.Fixed(EnumDialogArea.CenterBottom, 0.0, 0.0, 400.0, 0.0).WithFixedAlignmentOffset(0.0, -150.0);
			ElementBounds bounds5 = ElementBounds.Fixed(EnumDialogArea.CenterBottom, 0.0, 0.0, 100.0, 80.0).WithFixedAlignmentOffset(0.0, -80.0);
			double[] array = (double[])GuiStyle.DialogDefaultTextColor.Clone();
			array[0] = (array[0] + 1.0) / 2.0;
			array[1] = (array[1] + 1.0) / 2.0;
			array[2] = (array[2] + 1.0) / 2.0;
			CairoFont font = CairoFont.WhiteSmallText().WithColor(array).WithStroke(GuiStyle.DarkBrownColor, 2.0)
				.WithOrientation(EnumTextOrientation.Center);
			Composers["hotbar"] = capi.Gui.CreateCompo("inventory-hotbar", dialogBounds.FlatCopy().FixedGrow(0.0, 20.0)).BeginChildElements(dialogBounds).AddShadedDialogBG(ElementBounds.Fill, withTitleBar: false)
				.AddStaticCustomDraw(ElementBounds.Fill, (Context ctx, ImageSurface surface, ElementBounds elementBounds) =>
				{
					ctx.Rectangle(0.0, 0.0, elementBounds.OuterWidth, 25f * ClientSettings.GUIScale);
					ctx.Operator = Operator.Clear;
					ctx.Fill();
					ctx.Operator = Operator.Over;
				})
				.AddItemSlotGrid(hotbarInv, SendInvPacket, 1, new int[1] { 11 }, bounds, "offhandgrid")
				.AddItemSlotGrid(hotbarInv, SendInvPacket, 10, new int[10] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, bounds2, "hotbargrid")
				.AddItemSlotGrid(backpackInv, SendInvPacket, 4, new int[4] { 0, 1, 2, 3 }, bounds3, "backpackgrid")
				.AddTranspHoverText("", font, 400, bounds4, "iteminfoHover")
				.AddIf(temporalStabilityEnabled)
				.AddHoverText(" ", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), 50, bounds5, "tempStabHoverText")
				.EndIf()
				.EndChildElements();
			(Composers["hotbar"].GetElement("element-2") as GuiElementDialogBackground).FullBlur = true;
			Composers["hotbar"].Tabbable = false;
			hotbarSlotGrid = Composers["hotbar"].GetSlotGrid("hotbargrid");
			hotbarSlotGrid.KeyboardControlEnabled = false;
			Composers["hotbar"].GetSlotGrid("offhandgrid").KeyboardControlEnabled = false;
			Composers["hotbar"].GetSlotGrid("backpackgrid").KeyboardControlEnabled = false;
			CairoFont numberFont = CairoFont.WhiteDetailText();
			numberFont.Color = GuiStyle.HotbarNumberTextColor;
			hotbarSlotGrid.AlwaysRenderIcon = true;
			hotbarSlotGrid.DrawIconHandler = (Context cr, string type, int x, int y, float w, float h, double[] rgba) =>
			{
				drawIcon(cr, "hotbarslot" + type, x, y, w, numberFont);
			};
			Composers["hotbar"].Compose();
			backpackGrid = Composers["hotbar"].GetSlotGrid("backpackgrid");
			OnActiveSlotChanged(capi.World.Player.InventoryManager.ActiveHotbarSlotNumber, triggerUpdate: false);
			GuiElementHoverText hoverText = Composers["hotbar"].GetHoverText("iteminfoHover");
			hoverText.ZPosition = 50f;
			hoverText.SetFollowMouse(on: false);
			hoverText.SetAutoWidth(on: true);
			hoverText.SetAutoDisplay(on: false);
			hoverText.fillBounds = true;
			TryOpen();
		}
		else
		{
			ScreenManager.Platform.Logger.Notification("Server did not send a hotbar inventory, so I won't display one");
		}
	}

	private static void drawIcon(Context cr, string type, int x, int y, float w, CairoFont numberFont)
	{
		CairoFont cairoFont = numberFont;
		cairoFont.SetupContext(cr);
		string text = ScreenManager.hotkeyManager.GetHotKeyByCode(type).CurrentMapping.ToString();
		double width = numberFont.GetTextExtents(text).Width;
		if (width > GuiElement.scaled(20.0))
		{
			cairoFont = cairoFont.Clone();
			cairoFont.UnscaledFontsize *= 0.6;
			cairoFont.SetupContext(cr);
			width = cairoFont.GetTextExtents(text).Width;
		}
		cr.MoveTo((double)((float)x + w) - width + 1.0, (double)y + cairoFont.GetFontExtents().Ascent - 3.0);
		cr.ShowText(text);
	}

	private void SendInvPacket(object packet)
	{
		capi.Network.SendPacketClient(packet);
	}

	public override bool TryClose()
	{
		return false;
	}

	public override bool ShouldReceiveKeyboardEvents()
	{
		return true;
	}

	public override void OnMouseWheel(MouseWheelEventArgs args)
	{
		args.SetHandled();
		if (!scrollWheeledSlotInFrame && args.delta != 0 && args.value != prevValue)
		{
			moveToHotbarSlot(args.delta);
			prevValue = args.value;
			scrollWheeledSlotInFrame = true;
		}
	}

	public void moveToHotbarSlot(int delta)
	{
		IPlayer player = capi.World.Player;
		int num = ((!hotbarInv[10].Empty) ? 1 : 0);
		int num2 = ((player.InventoryManager.ActiveHotbarSlotNumber >= 10 + num || capi.Input.KeyboardKeyStateRaw[3]) ? 14 : 10);
		OnKeySlot(GameMath.Mod(player.InventoryManager.ActiveHotbarSlotNumber - delta, num2 + num), moveItems: false);
	}

	private bool KeyFlipHandSlots(KeyCombination t1)
	{
		IPlayer player = capi.World.Player;
		Packet_Client packet_Client = (Packet_Client)(hotbarInv?.TryFlipItems(player.InventoryManager.ActiveHotbarSlotNumber, player.Entity.LeftHandItemSlot));
		if (packet_Client != null)
		{
			capi.Network.SendPacketClient(packet_Client);
		}
		return true;
	}

	private void OnKeySlot(int index, bool moveItems)
	{
		IPlayer player = capi.World.Player;
		if (player.InventoryManager.ActiveHotbarSlot?.Itemstack != null && player.Entity.Controls.HandUse != EnumHandInteract.None && index != player.InventoryManager.ActiveHotbarSlotNumber)
		{
			EnumHandInteract handUse = player.Entity.Controls.HandUse;
			if (!player.Entity.TryStopHandAction(false, EnumItemUseCancelReason.ChangeSlot))
			{
				return;
			}
			capi.Network.SendHandInteraction(2, player.CurrentBlockSelection, player.CurrentEntitySelection, handUse, 1, firstEvent: false, EnumItemUseCancelReason.ChangeSlot);
		}
		player.InventoryManager.ActiveHotbarSlotNumber = index;
		if (moveItems && player.InventoryManager.CurrentHoveredSlot != null)
		{
			Packet_Client packet_Client = (Packet_Client)hotbarInv.TryFlipItems(index, player.InventoryManager.CurrentHoveredSlot);
			if (packet_Client != null)
			{
				capi.Network.SendPacketClient(packet_Client);
			}
		}
	}

	private void updateHotbarSounds()
	{
		ItemStack itemstack = capi.World.Player.Entity.LeftHandItemSlot.Itemstack;
		updateHotbarSounds(prevLeftStack, itemstack, leftActiveSlotIdleSound);
		prevLeftStack = itemstack;
		ItemStack itemstack2 = capi.World.Player.Entity.RightHandItemSlot.Itemstack;
		updateHotbarSounds(prevRightStack, itemstack2, rightActiveSlotIdleSound);
		prevRightStack = itemstack2;
	}

	private void updateHotbarSounds(ItemStack prevStack, ItemStack nowStack, Dictionary<SoundAttributes, ILoadedSound> activeSlotIdleSound)
	{
		if (nowStack != null && prevStack != null && nowStack.Equals(capi.World, prevStack, GlobalConstants.IgnoredStackAttributes))
		{
			return;
		}
		HeldSounds heldSounds = nowStack?.Collectible?.HeldSounds;
		HeldSounds prevSounds = prevStack?.Collectible?.HeldSounds;
		if (prevSounds != null)
		{
			if (prevSounds.Unequip.Location != null)
			{
				capi.World.PlaySoundAt(prevSounds.Unequip, 0.0, 0.0, 0.0, 0);
			}
			if (prevSounds.Idle.Location != null && prevSounds.Idle != heldSounds?.Idle && activeSlotIdleSound.TryGetValue(prevSounds.Idle, out var value))
			{
				value.FadeOut(1f, (ILoadedSound s) =>
				{
					s.Stop();
					s.Dispose();
					activeSlotIdleSound.Remove(prevSounds.Idle);
				});
			}
		}
		if (heldSounds != null)
		{
			if (heldSounds.Equip.Location != null)
			{
				capi.World.PlaySoundAt(heldSounds.Equip, 0.0, 0.0, 0.0, 0);
			}
			if (heldSounds.Idle.Location != null && !activeSlotIdleSound.ContainsKey(heldSounds.Idle))
			{
				ILoadedSound loadedSound = capi.World.LoadSound(new SoundParams
				{
					Location = heldSounds.Idle.Location.Clone().WithPathAppendixOnce(".ogg"),
					Pitch = heldSounds.Idle.Pitch.nextFloat(1f, capi.World.Rand),
					RelativePosition = true,
					ShouldLoop = true,
					SoundType = heldSounds.Idle.Type,
					Volume = heldSounds.Idle.Volume.nextFloat(1f, capi.World.Rand)
				});
				activeSlotIdleSound[heldSounds.Idle] = loadedSound;
				loadedSound.Start();
				loadedSound.FadeIn(1f, (ILoadedSound s) =>
				{
				});
			}
		}
	}

	private void OnActiveSlotChanged(int newSlot, bool triggerUpdate = true)
	{
		if (hotbarSlotGrid == null)
		{
			return;
		}
		IClientPlayer player = capi.World.Player;
		ItemSlot activeHotbarSlot = player.InventoryManager.ActiveHotbarSlot;
		(player.Entity.AnimManager as PlayerAnimationManager)?.OnActiveSlotChanged(activeHotbarSlot);
		int num = ((!hotbarInv[10].Empty) ? 1 : 0);
		if (newSlot < 10 + num)
		{
			hotbarSlotGrid.HighlightSlot(newSlot);
			backpackGrid.HighlightSlot(-1);
		}
		else
		{
			hotbarSlotGrid.HighlightSlot(-1);
			backpackGrid.HighlightSlot(newSlot - 10 - num);
		}
		if (triggerUpdate)
		{
			(capi.World as ClientMain).EnqueueMainThreadTask(() =>
			{
				RecomposeActiveSlotHoverText(newSlot);
			}, "recomposeslothovertext");
			UpdateCurrentToolMode(newSlot);
		}
		updateHotbarSounds();
		(activeHotbarSlot.Inventory as InventoryPlayerHotbar)?.updateSlotStatMods(activeHotbarSlot);
	}

	private void OnHotbarSlotModified(int slotId)
	{
		UpdateCurrentToolMode(slotId);
		updateHotbarSounds();
		IPlayer plr = capi.World.Player;
		if (slotId != plr.InventoryManager.ActiveHotbarSlotNumber)
		{
			return;
		}
		IItemStack itemstack = plr.InventoryManager.ActiveHotbarSlot.Itemstack;
		if (itemstack != null && (itemstack.StackSize != lastActiveSlotStacksize || itemstack.Id != lastActiveSlotItemId || itemstack.Class != lastActiveClassItemClass))
		{
			(capi.World as ClientMain).EnqueueMainThreadTask(() =>
			{
				RecomposeActiveSlotHoverText(plr.InventoryManager.ActiveHotbarSlotNumber);
			}, "recomposeslothovertext");
			lastActiveClassItemClass = itemstack.Class;
			lastActiveSlotItemId = itemstack.Id;
			lastActiveSlotStacksize = itemstack.StackSize;
		}
	}

	private void RecomposeActiveSlotHoverText(int newSlotIndex)
	{
		IPlayer player = capi.World.Player;
		int num = ((!hotbarInv[10].Empty) ? 1 : 0);
		ItemSlot itemSlot = ((newSlotIndex < 10 + num) ? player?.InventoryManager?.GetHotbarInventory()[newSlotIndex] : backpackInv[newSlotIndex - 10 - num]);
		if (itemSlot?.Itemstack != null)
		{
			if (prevIndex != newSlotIndex)
			{
				prevIndex = newSlotIndex;
				itemInfoTextActiveMs = capi.ElapsedMilliseconds;
				GuiElementHoverText guiElementHoverText = Composers["hotbar"]?.GetHoverText("iteminfoHover");
				if (guiElementHoverText != null)
				{
					guiElementHoverText.SetNewText(itemSlot.Itemstack.GetName());
					guiElementHoverText.SetVisible(on: true);
				}
				if (ClientSettings.ShowBlockInteractionHelp)
				{
					WorldInteraction[] heldInteractionHelp = itemSlot.Itemstack.Collectible.GetHeldInteractionHelp(itemSlot);
					wiUtil.ComposeBlockWorldInteractionHelp(heldInteractionHelp);
				}
			}
		}
		else
		{
			Composers["hotbar"].GetHoverText("iteminfoHover").SetVisible(on: false);
			wiUtil.ComposeBlockWorldInteractionHelp(Array.Empty<WorldInteraction>());
			prevIndex = newSlotIndex;
		}
	}

	public override void OnRenderGUI(float deltaTime)
	{
		if (shouldRecompose)
		{
			ComposeGuis();
			shouldRecompose = false;
		}
		if (temporalStabilityEnabled && capi.World.Player.WorldData.CurrentGameMode != EnumGameMode.Spectator)
		{
			renderGear(deltaTime);
		}
		float num = capi.Render.FrameWidth;
		float num2 = capi.Render.FrameHeight;
		ElementBounds elementBounds = wiUtil.Composer?.Bounds;
		if (elementBounds != null)
		{
			elementBounds.Alignment = EnumDialogArea.None;
			elementBounds.fixedOffsetX = 0.0;
			elementBounds.fixedOffsetY = 0.0;
			elementBounds.absFixedX = (double)(num / 2f) - wiUtil.ActualWidth / 2.0;
			elementBounds.absFixedY = (double)num2 - GuiElement.scaled(95.0) - elementBounds.OuterHeight;
			elementBounds.absMarginX = 0.0;
			elementBounds.absMarginY = 0.0;
		}
		if (capi.World.Player.WorldData.CurrentGameMode != EnumGameMode.Spectator)
		{
			base.OnRenderGUI(deltaTime);
		}
		if (currentToolModeTexture != null)
		{
			float num3 = (float)GuiElement.scaled(38.0);
			float num4 = (float)GuiElement.scaled(200.0);
			float posY = (float)GuiElement.scaled(8.0);
			capi.Render.Render2DTexture(toolModeBgTexture.TextureId, (float)capi.Render.FrameWidth / 2f + num4, posY, num3, num3);
			capi.Render.Render2DTexture(currentToolModeTexture.TextureId, (float)capi.Render.FrameWidth / 2f + num4, posY, num3, num3);
		}
		if (!hotbarInv[10].Empty)
		{
			float num5 = (float)((double)num2 - dialogBounds.OuterHeight + GuiElement.scaled(15.0));
			float x = num / 2f - (float)GuiElement.scaled(361.0);
			float y = num5 + (float)GuiElement.scaled(10.0);
			(hotbarInv[10].Itemstack.Collectible as ISkillItemRenderer)?.Render(deltaTime, x, y, 200f);
			if (capi.World.Player.InventoryManager.ActiveHotbarSlotNumber == 10)
			{
				ElementBounds elementBounds2 = hotbarSlotGrid.SlotBounds[0];
				capi.Render.Render2DTexturePremultipliedAlpha(hotbarSlotGrid.HighlightSlotTexture.TextureId, (int)(elementBounds2.renderX - 2.0 - (double)elementBounds2.OuterWidthInt - 4.0), (int)(elementBounds2.renderY - 2.0), elementBounds2.OuterWidthInt + 4, elementBounds2.OuterHeightInt + 4);
			}
		}
	}

	private void renderGear(float deltaTime)
	{
		float num = capi.Render.FrameWidth;
		float num2 = capi.Render.FrameHeight;
		IShaderProgram currentActiveShader = capi.Render.CurrentActiveShader;
		currentActiveShader.Stop();
		float num3 = (float)((double)num2 - dialogBounds.OuterHeight + GuiElement.scaled(15.0));
		float y = num3 - (float)gearTexture.Height + (float)gearTexture.Height * 0.45f;
		float num4 = (float)capi.World.Player.Entity.WatchedAttributes.GetDouble("temporalStability");
		float num5 = (float)capi.World.Player.Entity.Attributes.GetDouble("tempStabChangeVelocity");
		GuiElementHoverText hoverText = Composers["hotbar"].GetHoverText("tempStabHoverText");
		if (hoverText.IsNowShown)
		{
			hoverText.SetNewText((int)GameMath.Clamp(num4 * 100f, 0f, 100f) + "%");
		}
		ShaderProgramGuigear guigear = ShaderPrograms.Guigear;
		guigear.Use();
		guigear.Tex2d2D = gearTexture.TextureId;
		modelMat.Set(capi.Render.CurrentModelviewMatrix);
		modelMat.Translate(num / 2f - (float)(gearTexture.Width / 2) - 1f, y, -200f);
		modelMat.Scale((float)gearTexture.Width / 2f, (float)gearTexture.Height / 2f, 0f);
		float num6 = (float)capi.ElapsedMilliseconds / 1000f;
		modelMat.Translate(1f, 1f, 0f);
		float num7 = (GameMath.Sin(num6 / 50f) * 1f + (GameMath.Sin(num6 / 5f) * 0.5f + GameMath.Sin(num6) * 3f + GameMath.Sin(num6 / 2f) * 1.5f) / 20f) / 2f;
		if ((num4 < 1f && num5 > 0f) || (num4 > 0f && num5 < 0f))
		{
			gearPosition += num5;
		}
		modelMat.RotateZ(num7 / 2f + (float)gearPosition + GlobalConstants.GuiGearRotJitter);
		guigear.GearCounter = num6;
		guigear.StabilityLevel = num4;
		guigear.ShadeYPos = num3;
		guigear.GearHeight = gearTexture.Height;
		guigear.HotbarYPos = num3 + (float)GuiElement.scaled(10.0);
		guigear.ProjectionMatrix = capi.Render.CurrentProjectionMatrix;
		guigear.ModelViewMatrix = modelMat.Values;
		capi.Render.RenderMesh(capi.Gui.QuadMeshRef);
		guigear.Stop();
		currentActiveShader.Use();
	}

	public override void OnFinalizeFrame(float dt)
	{
		base.OnFinalizeFrame(dt);
		scrollWheeledSlotInFrame = false;
	}

	public override void Dispose()
	{
		base.Dispose();
		gearTexture?.Dispose();
		toolModeBgTexture?.Dispose();
		wiUtil?.Dispose();
	}
}
