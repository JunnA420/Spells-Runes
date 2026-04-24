using Vintagestory.API.Common;

namespace SpellsAndRunes.Blocks;

public class ItemIgnisPaste : Item
{
    public override void OnHeldInteractStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        bool firstEvent,
        ref EnumHandHandling handling)
    {
        if (!firstEvent) return;

        handling = EnumHandHandling.PreventDefault;

        if (byEntity.Api.Side != EnumAppSide.Server) return;
        if (byEntity is not EntityPlayer) return;

        var world = byEntity.Api.World;

        byEntity.Ignite();

        byEntity.WatchedAttributes.SetBool("snr:firepower", true);
        byEntity.WatchedAttributes.MarkPathDirty("snr:firepower");

        world.PlaySoundAt(
            new AssetLocation("sounds/effect/fire"),
            byEntity.Pos.X,
            byEntity.Pos.Y,
            byEntity.Pos.Z
        );

        slot.TakeOut(1);
        slot.MarkDirty();
    }
}
