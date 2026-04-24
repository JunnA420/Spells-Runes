using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Fire;

public class FireBackBlastDash : Spell
{
    public override string Id => "fire_back_blast_dash";
    public override string Name => "Back Blast Dash";
    public override string Description => "Kicks fire backward to propel the caster forward.";

    public override SpellTier Tier => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Fire;
    public override SpellType Type => SpellType.Offense;

    public override float FluxCost => 18f;
    public override float CastTime => 0.4f;

    public override string? AnimationCode => "fire_back_blast_dash";
    public override bool AnimationUpperBodyOnly => false;

    public override (int col, int row) TreePosition => (1, 0);

    public const float ForwardForce = 1.35f;

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        var lookDir = caster.SidedPos.GetViewVector().ToVec3d().Normalize();
        caster.SidedPos.Motion.Add(
            lookDir.X * ForwardForce * GetRangeMultiplier(spellLevel),
            0.03,
            lookDir.Z * ForwardForce * GetRangeMultiplier(spellLevel));
    }

    public static void SpawnFx(IWorldAccessor world, Vec3d origin, Vec3d lookDir, int spellLevel)
    {
        var rng = world.Rand;
        Vec3d back = lookDir * -1;
        int mult = 1 + (spellLevel - 1) / 4;
        for (int i = 0; i < 42 * mult; i++)
        {
            double dist = rng.NextDouble() * 2.2;
            var pos = origin + back * dist + new Vec3d((rng.NextDouble() - 0.5) * 0.8, rng.NextDouble() * 0.6, (rng.NextDouble() - 0.5) * 0.8);
            world.SpawnParticles(new SimpleParticleProperties
            {
                MinQuantity = 1,
                AddQuantity = 0,
                MinPos = pos,
                AddPos = new Vec3d(0.04, 0.04, 0.04),
                MinVelocity = new Vec3f((float)(back.X * 3.0), 0.4f, (float)(back.Z * 3.0)),
                AddVelocity = new Vec3f(0.7f, 0.45f, 0.7f),
                LifeLength = 0.25f + (float)rng.NextDouble() * 0.18f,
                MinSize = 0.08f,
                MaxSize = 0.26f,
                GravityEffect = -0.12f,
                Color = ColorUtil.ColorFromRgba(15 + rng.Next(70), 90 + rng.Next(125), 255, 215),
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = true
            });
        }
    }
}
