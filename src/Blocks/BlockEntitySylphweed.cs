using SpellsAndRunes.Render;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SpellsAndRunes.Blocks;

public class BlockEntitySylphweed : BlockEntity
{
    private SylphweedGlowRenderer? glowRenderer;
    private IdleAnimatedBlockRenderer? animRenderer;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api is not ICoreClientAPI capi) return;

        var mod = capi.ModLoader.GetModSystem<SpellsAndRunesMod>();
        glowRenderer = mod?.SylphGlow;
        glowRenderer?.Register(Pos);

        animRenderer = mod?.IdleAnim;
        if (Block != null) animRenderer?.Register(Block, Pos);
    }

    public override void OnBlockRemoved()
    {
        glowRenderer?.Unregister(Pos);
        animRenderer?.Unregister(Pos);
        base.OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        glowRenderer?.Unregister(Pos);
        animRenderer?.Unregister(Pos);
        base.OnBlockUnloaded();
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        return true;
    }
}
