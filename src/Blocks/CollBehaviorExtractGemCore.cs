using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;


namespace SpellsAndRunes.Blocks;

public class CollBehaviorExtractGemCore : CollectibleBehavior
{
    public CollBehaviorExtractGemCore(CollectibleObject collObj) : base(collObj) { }

    public override void OnHeldInteractStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        bool firstEvent,
        ref EnumHandHandling handling,
        ref EnumHandling bhHandling)
    {
        if (blockSel == null || byEntity?.Api == null) return;

        var world = byEntity.Api.World;
        var be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
        if (be is not BlockEntityGroundStorage gs) return;

        ItemSlot? gemSlot = null;
        for (int i = 0; i < gs.Inventory.Count; i++)
        {
            var stack = gs.Inventory[i]?.Itemstack;
            if (stack?.Collectible?.Code?.Path == "ignis-fragment-gem-cracked")
            {
                gemSlot = gs.Inventory[i];
                break;
            }
        }

        if (gemSlot == null) return;

        // stop default tongs action + stop block fallback
        handling = EnumHandHandling.PreventDefault;
        bhHandling = EnumHandling.PreventSubsequent;

        if (world.Side != EnumAppSide.Server) return;

        gemSlot.TakeOut(1);
        gemSlot.MarkDirty();

        var coreItem = world.GetItem(new AssetLocation("spellsandrunes:ignis-fragment-gem-core"));
        if (coreItem != null)
        {
            var outStack = new ItemStack(coreItem);

            if (byEntity is EntityPlayer ep && !ep.Player.InventoryManager.TryGiveItemstack(outStack))
            {
                world.SpawnItemEntity(outStack, blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5));
            }
        }

        bool hasAnyItemsLeft = false;
        for (int i = 0; i < gs.Inventory.Count; i++)
        {
            if (gs.Inventory[i]?.Itemstack != null)
            {
                hasAnyItemsLeft = true;
                break;
            }
        }

        if (!hasAnyItemsLeft)
        {
            world.BlockAccessor.SetBlock(0, blockSel.Position);
        }
        else
        {
            gs.MarkDirty(true);
        }

        world.PlaySoundAt(
            new AssetLocation("sounds/block/glass"),
            blockSel.Position.X,
            blockSel.Position.Y,
            blockSel.Position.Z
        );
    }
}