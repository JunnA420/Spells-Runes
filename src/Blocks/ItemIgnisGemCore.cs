using Vintagestory.API.Common;

namespace SpellsAndRunes.Blocks;

public class ItemIgnisGemCore : Item
{
    private const float PermanentTemperature = 1200f;

    public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
    {
        base.OnHeldIdle(slot, byEntity);

        if (slot?.Itemstack == null) return;
        var world = byEntity.Api.World;
        if (GetTemperature(world, slot.Itemstack) < PermanentTemperature)
        {
            SetTemperature(world, slot.Itemstack, PermanentTemperature);
        }
    }

    public override void OnGroundIdle(EntityItem entityItem)
    {
        base.OnGroundIdle(entityItem);

        var slot = entityItem.Slot;
        if (slot?.Itemstack == null) return;
        var world = entityItem.Api.World;
        if (GetTemperature(world, slot.Itemstack) < PermanentTemperature)
        {
            SetTemperature(world, slot.Itemstack, PermanentTemperature);
        }
    }
}
