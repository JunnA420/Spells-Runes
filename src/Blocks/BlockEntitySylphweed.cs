using SpellsAndRunes.Render;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SpellsAndRunes.Blocks;

public class BlockEntitySylphweed : BlockEntity
{
    private SylphweedGlowRenderer? renderer;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api is ICoreClientAPI capi)
        {
            renderer = capi.ModLoader.GetModSystem<SpellsAndRunesMod>()?.SylphGlow;
            renderer?.Register(Pos);
        }
    }

    public override void OnBlockRemoved()
    {
        renderer?.Unregister(Pos);
        base.OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        renderer?.Unregister(Pos);
        base.OnBlockUnloaded();
    }
}
