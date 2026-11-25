using System;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VintageE
{
    /// <summary>
    /// 1.26.8: shift-click to burn. The click itself happens in a player's game, which the test doesn't have; what it
    /// can check is everything around it: that the game's method the hook goes on is still the one it was written
    /// for, which clicks, slots and stacks are taken, and the server's rules for the request.
    /// </summary>
    public partial class SelfTest
    {
        void CheckShiftClickBurn()
        {
            Line("Shift-click to burn (1.26.8)");

            // The game's slot click: there, and with every parameter the hook asks Harmony for by name.
            MethodInfo target = null, prefix = null;
            FieldInfo field = null;
            try
            {
                target = ShiftClickBurn.Target();
                field = ShiftClickBurn.InventoryField();
                prefix = typeof(ShiftClickBurn).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
            }
            catch (Exception e) { Line("  looking for the game's slot click threw " + e.Message); }
            var theirs = target?.GetParameters().ToDictionary(p => p.Name, p => p.ParameterType);
            var ours = prefix?.GetParameters().Where(p => !p.Name.StartsWith("___", StringComparison.Ordinal)).ToArray();
            bool namesFit = theirs != null && ours != null && ours.Length == 5 && ours.All(p => theirs.TryGetValue(p.Name, out Type t) && t == p.ParameterType);
            Check(target != null && target.ReturnType == typeof(void) && namesFit && prefix?.ReturnType == typeof(bool),
                $"the game's slot click is there for the hook, with the parameters it reads ({target?.DeclaringType?.Name}.{target?.Name}: {(theirs == null ? "?" : string.Join(", ", theirs.Keys))})");
            Check(field != null && typeof(IInventory).IsAssignableFrom(field.FieldType), $"a grid of slots still keeps its inventory where the hook reads it ({field?.FieldType?.Name ?? "not found"} {field?.Name})");

            // Which clicks: a plain left shift-click, the window open, a server that knows the request, not switched off.
            const EnumMouseButton left = EnumMouseButton.Left;
            Check(ShiftClickBurn.Wanted(true, true, true, left, false, false), "a shift-click with the window open is taken");
            Check(!ShiftClickBurn.Wanted(false, true, true, left, false, false) && !ShiftClickBurn.Wanted(true, false, true, left, false, false) && !ShiftClickBurn.Wanted(true, true, false, left, false, false),
                "not with the window closed, on a server before 1.26.8, or switched off in the player's settings");
            Check(!ShiftClickBurn.Wanted(true, true, true, EnumMouseButton.Right, false, false) && !ShiftClickBurn.Wanted(true, true, true, left, true, false) && !ShiftClickBurn.Wanted(true, true, true, left, false, true),
                "the right button, and Ctrl or Alt held too, stay the game's");

            // Which slots: the player's own hotbar and backpack, not a bag slot, not a tablet.
            const string uid = "selftestplayer";
            CollectibleObject granite = sapi.World.GetBlock(new AssetLocation("game:rock-granite"));
            CollectibleObject tabletItem = sapi.World.GetItem(new AssetLocation("vintagee:transmutationtablet"));
            CollectibleObject star = sapi.World.GetItem(new AssetLocation("vintagee:kleinstar-ein"));
            CollectibleObject tome = sapi.World.GetItem(new AssetLocation("vintagee:gadget-tomeofknowledge"));
            if (granite == null || tabletItem == null || star == null || tome == null) { Check(false, "granite, the tablet, a Klein star and the Tome exist for the shift-click checks"); return; }
            InventoryGeneric Inv(string kind, string owner) => new InventoryGeneric(3, kind, owner, sapi, (id, self) => id == 2 ? new ItemSlotBackpack(self) : new ItemSlot(self));
            InventoryGeneric hotbar = Inv(GlobalConstantsHotbar, uid), backpack = Inv(GlobalConstantsBackpack, uid);
            InventoryGeneric someoneElses = Inv(GlobalConstantsHotbar, "someoneelse"), grid = Inv("craftinggrid", uid), worn = Inv("character", uid);
            foreach (InventoryGeneric inv in new[] { hotbar, backpack, someoneElses, grid, worn })
            {
                inv[0].Itemstack = new ItemStack(granite, 5);
                inv[2].Itemstack = new ItemStack(granite, 5); // the bag slot
            }
            hotbar[1].Itemstack = new ItemStack(tabletItem);
            Check(ShiftClickBurn.Eligible(uid, hotbar, hotbar[0]) && ShiftClickBurn.Eligible(uid, backpack, backpack[0]), $"a slot of the player's own hotbar or backpack is one to burn from ({hotbar.InventoryID}, {backpack.InventoryID})");
            Check(!ShiftClickBurn.Eligible(uid, someoneElses, someoneElses[0]) && !ShiftClickBurn.Eligible(uid, grid, grid[0]) && !ShiftClickBurn.Eligible(uid, worn, worn[0]),
                "another player's hotbar, the crafting grid and what is worn are not");
            Check(!ShiftClickBurn.Eligible(uid, backpack, backpack[2]) && !ShiftClickBurn.Eligible(uid, hotbar, hotbar[1]) && !ShiftClickBurn.Eligible(uid, backpack, backpack[1]),
                "nor a bag slot, a slot holding a tablet, or an empty one");

            // Which stacks.
            var starStack = new ItemStack(star);
            StoredEmc.Set(starStack, 1000);
            Check(ShiftClickBurn.CanBurn(mod, new ItemStack(granite, 5)) && !ShiftClickBurn.CanBurn(mod, new ItemStack(tome)) && !ShiftClickBurn.CanBurn(mod, starStack) && !ShiftClickBurn.CanBurn(mod, new ItemStack(tabletItem)),
                $"granite burns; the Tome (no value), a Klein star and a tablet don't (granite is worth {mod.Emc.GetValue(granite)})");
            var stuffed = new ItemStack(granite);
            var inside = new TreeAttribute();
            inside["0"] = new ItemstackAttribute(new ItemStack(granite, 4));
            stuffed.Attributes["contents"] = inside;
            Check(!ShiftClickBurn.CanBurn(mod, stuffed), "something with other things inside it isn't burned by a shift-click");
            StackDataTable data = mod.Emc.StackData;
            Block bucket = sapi.World.GetBlock(new AssetLocation("game:woodbucket"));
            Item water = sapi.World.GetItem(new AssetLocation("game:waterportion"));
            ItemStack full = null;
            if (bucket != null && water != null && data.CarriesData(bucket))
            {
                full = new ItemStack(bucket);
                var contents = new TreeAttribute();
                contents["0"] = new ItemstackAttribute(new ItemStack(water, 1000));
                full.Attributes["contents"] = contents;
                StackDataMode before = data.ModeOf(bucket);
                data.Modes[bucket] = StackDataMode.Full;
                bool withWater = ShiftClickBurn.CanBurn(mod, full);
                data.Modes[bucket] = StackDataMode.Item;
                bool itemOnly = ShiftClickBurn.CanBurn(mod, full);
                data.Modes[bucket] = before;
                Check(withWater && !itemOnly, $"a bucket of water burns where it is valued with its water, and not where it is valued as the bucket alone (with water {withWater}, item only {itemOnly})");
            }
            else Check(false, "a wooden bucket that carries its contents exists for the shift-click checks");

            // The server's side of the request (no player here: the rules, not the taking).
            TransmutationService service = mod.Transmutation;
            string code = granite.Code.ToString();
            string ok = service.PlanBurnSlot(null, uid, hotbar[0], code, out TransmutationService.BurnPlan plan, out _);
            Check(ok == null && plan != null && Math.Abs(plan.Each - mod.Emc.GetValue(granite)) < 1e-9, $"the server burns a slot's granite for its value ({ok ?? "allowed"}, {plan?.Each} each)");
            string changed = service.PlanBurnSlot(null, uid, hotbar[0], "game:rock-andesite", out _, out _);
            string empty = service.PlanBurnSlot(null, uid, backpack[1], code, out _, out _);
            string noSlot = service.PlanBurnSlot(null, uid, null, code, out _, out _);
            Check(changed == "" && empty == "" && noSlot == "", $"a slot that holds something else by now, nothing, or isn't there is left alone without a word ('{changed}', '{empty}', '{noSlot}')");
            backpack[1].Itemstack = starStack;
            string starRefused = service.PlanBurnSlot(null, uid, backpack[1], star.Code.ToString(), out _, out _);
            string bagRefused = service.PlanBurnSlot(null, uid, backpack[2], code, out _, out _);
            string gridRefused = service.PlanBurnSlot(null, uid, grid[0], code, out _, out _);
            string otherRefused = service.PlanBurnSlot(null, uid, someoneElses[0], code, out _, out _);
            Check(starRefused == "shiftburn" && bagRefused == "shiftburn" && gridRefused == "shiftburn" && otherRefused == "shiftburn",
                $"the server refuses a Klein star, a bag slot, the crafting grid and someone else's slot by itself, whatever a player's game asks ({starRefused}, {bagRefused}, {gridRefused}, {otherRefused})");
            Check(ShiftClickBurn.SlotOf(null, hotbar.InventoryID, 0) == null, "a request without a player names no slot");
            Check(Vintagestory.API.Config.Lang.HasTranslation("vintagee:error-shiftburn", false, false), "the refusal has its text");

            // What goes over the wire: the request with its slot (slot 0 as 1, since a 0 isn't sent), and the server's "I know this".
            var sent = new TransmuteRequestPacket { Action = TransmuteAction.BurnSlot, Key = code, SlotInv = hotbar.InventoryID, Slot = 1 };
            TransmuteRequestPacket got;
            EmcTablePacket knows, old;
            using (var ms = new System.IO.MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, sent);
                ms.Position = 0;
                got = ProtoBuf.Serializer.Deserialize<TransmuteRequestPacket>(ms);
            }
            using (var ms = new System.IO.MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, new EmcTablePacket { BurnBySlot = true });
                ms.Position = 0;
                knows = ProtoBuf.Serializer.Deserialize<EmcTablePacket>(ms);
            }
            using (var ms = new System.IO.MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, new EmcTablePacket());
                ms.Position = 0;
                old = ProtoBuf.Serializer.Deserialize<EmcTablePacket>(ms);
            }
            Check(got != null && got.Action == TransmuteAction.BurnSlot && got.Key == code && got.SlotInv == hotbar.InventoryID && got.Slot == 1,
                $"the request reaches the server with its slot ({got?.Action}, {got?.SlotInv}, slot {got?.Slot - 1})");
            var asClient = new EmcRegistry();
            asClient.LoadFromPacket(old, sapi.World);
            bool before1268 = asClient.BurnBySlot;
            asClient.LoadFromPacket(knows, sapi.World);
            Check(mod.Emc.BuildPacket().BurnBySlot && knows.BurnBySlot && asClient.BurnBySlot && !old.BurnBySlot && !before1268,
                "this server tells players' games that it burns by slot, and a server that doesn't say so is taken as one that can't");
        }

        static readonly string GlobalConstantsHotbar = Vintagestory.API.Config.GlobalConstants.hotBarInvClassName;
        static readonly string GlobalConstantsBackpack = Vintagestory.API.Config.GlobalConstants.backpackInvClassName;
    }
}
