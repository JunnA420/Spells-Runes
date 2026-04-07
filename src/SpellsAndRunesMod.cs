using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SpellsAndRunes;

public class SpellsAndRunesMod : ModSystem
{
    private ICoreClientAPI? capi;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        api.Logger.Notification("[Spells & Runes] Mod loaded.");
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
