using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using SpellsAndRunes.Spells;

namespace SpellsAndRunes.Blocks;

public class ItemSylphweedPipe : Item
{
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        if (byEntity.Api.Side != EnumAppSide.Server) { handling = EnumHandHandling.PreventDefault; return; }
        if (byEntity is not EntityPlayer playerEntity) return;

        var player = playerEntity.Player as IServerPlayer;
        if (player == null) return;

        var data = PlayerSpellData.For(byEntity);
        if (data.IsFluxUnlocked)
        {
            player.SendMessage(0, "The flux already flows through you.", EnumChatType.Notification);
            handling = EnumHandHandling.PreventDefault;
            return;
        }

        // Find ground sylphweed in hotbar
        var inv = player.InventoryManager.GetHotbarInventory();
        ItemSlot? groundSlot = null;
        for (int i = 0; i < inv.Count; i++)
        {
            var s = inv[i];
            if (s.Itemstack?.Collectible?.Code?.Path == "sylphweed-ground")
            { groundSlot = s; break; }
        }

        if (groundSlot == null)
        {
            player.SendMessage(0, "You need ground sylphweed to smoke the pipe.", EnumChatType.Notification);
            handling = EnumHandHandling.PreventDefault;
            return;
        }

        // Consume one ground sylphweed
        groundSlot.TakeOut(1);
        groundSlot.MarkDirty();

        // Unlock flux
        data.UnlockFlux();
        player.SendMessage(0, "The smoke fills your lungs. A deep hum resonates through your body — flux awakens.", EnumChatType.Notification);

        handling = EnumHandHandling.PreventDefault;
    }
}
