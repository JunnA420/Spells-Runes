using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Blocks;

public class BlockEntityIgnisFragment : BlockEntity
{
    private long particleListenerId = -1;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side != EnumAppSide.Client) return;

        particleListenerId = api.Event.RegisterGameTickListener(_ => SpawnIgnisParticles(), 180);
    }

    public override void OnBlockRemoved()
    {
        UnregisterListener();
        base.OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        UnregisterListener();
        base.OnBlockUnloaded();
    }

    private void UnregisterListener()
    {
        if (particleListenerId < 0 || Api == null) return;
        Api.Event.UnregisterGameTickListener(particleListenerId);
        particleListenerId = -1;
    }

    private void SpawnIgnisParticles()
    {
        if (Api?.World == null) return;

        var rng = Api.World.Rand;
        if (rng.NextDouble() > 0.8) return;

        double px = Pos.X + 0.5 + (rng.NextDouble() - 0.5) * 0.42;
        double py = Pos.Y + 0.5 + rng.NextDouble() * 0.55;
        double pz = Pos.Z + 0.5 + (rng.NextDouble() - 0.5) * 0.42;

        //NOTE: The color pattern is BGRA
        int color = ColorUtil.ColorFromRgba(
                    20 + rng.Next(30),    // blue
                    80 + rng.Next(70),    // green
                    220 + rng.Next(35),   // red
                    180 + rng.Next(60));  //alpha
        Api.World.SpawnParticles(new SimpleParticleProperties
        {
            MinPos = new Vec3d(px, py, pz),
            AddPos = new Vec3d(0.03, 0.05, 0.03),
            MinVelocity = new Vec3f(-0.01f, 0.03f, -0.01f),
            AddVelocity = new Vec3f(0.02f, 0.06f, 0.02f),
            MinQuantity = 0.5f,
            AddQuantity = 1,
            Color = color,
            GravityEffect = 0.5f,
            LifeLength = 0.55f,
            addLifeLength = 0.35f,
            MinSize = 0.05f,
            MaxSize = 0.12f,
            ParticleModel = EnumParticleModel.Quad,
            SelfPropelled = true,
            WindAffectednes = 0.1f,
            WithTerrainCollision = true,

        });
    }
}
