using System.Collections.Generic;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Air;

public class WindSlash : Spell
{
    public override string Id          => "air_wind_slash";
    public override string Name        => "Wind Slash";
    public override string Description => "Shapes compressed air into a curved blade and launches it forward. Cuts through the air at considerable speed.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 25f;
    public override float CastTime => 0.8f;

    public override string? AnimationCode        => "air_wind_slash";
    public override bool    AnimationUpperBodyOnly => false;

    public override IReadOnlyList<string> Prerequisites => ["air_air_push"];

    public override (int col, int row) TreePosition => (2, 3);

    public const float Range = 12f;
    public const float HitRadius = 1.1f;
    public const float Damage = 10f;

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        var lookDir = caster.SidedPos.GetViewVector().ToVec3d().Normalize();
        var origin = caster.SidedPos.XYZ.Add(0, caster.LocalEyePos.Y - 0.1, 0);
        float range = Range * GetRangeMultiplier(spellLevel);
        float damage = Damage * GetDamageMultiplier(spellLevel);

        HitAlongLine(caster, world, origin, lookDir, range, HitRadius, damage);
        SpawnFx(world, origin, lookDir, spellLevel, range);
    }

    internal static void HitAlongLine(EntityAgent caster, IWorldAccessor world, Vec3d origin, Vec3d lookDir, float range, float hitRadius, float damage)
    {
        var hitIds = new HashSet<long>();
        world.GetEntitiesAround(origin, range + hitRadius, range + hitRadius, e =>
        {
            if (e.EntityId == caster.EntityId || e is not EntityAgent) return false;

            Vec3d target = e.SidedPos.XYZ.Add(0, e.LocalEyePos.Y * 0.5, 0);
            Vec3d toTarget = target - origin;
            double along = toTarget.Dot(lookDir);
            if (along < 0 || along > range) return false;

            Vec3d closest = origin + lookDir * along;
            if ((target - closest).Length() > hitRadius) return false;
            if (!hitIds.Add(e.EntityId)) return false;

            e.ReceiveDamage(new DamageSource
            {
                Source = EnumDamageSource.Entity,
                SourceEntity = caster,
                Type = EnumDamageType.SlashingAttack,
            }, damage);

            e.SidedPos.Motion.Add(lookDir.X * 0.08, 0.02, lookDir.Z * 0.08);
            return false;
        });
    }

    public static void SpawnFx(IWorldAccessor world, Vec3d origin, Vec3d lookDir, int spellLevel = 1, float? scaledRange = null)
    {
        float range = scaledRange ?? Range;
        int mult = 1 + (spellLevel - 1) / 3;
        var rng = world.Rand;
        Vec3d up = new Vec3d(0, 1, 0);
        Vec3d right = lookDir.Cross(up).Normalize();
        Vec3d bladeTilt = right * 0.16 + up * 0.01;
        Vec3d arcPlane = (right * 0.985 + up * 0.015).Normalize();

        for (int blade = 0; blade < 2 * mult; blade++)
        {
            double bladeOffset = (blade - (2 * mult - 1) * 0.5) * 0.1;
            for (int seg = 0; seg < 150; seg++)
            {
                double t = seg / 149.0;
                double theta = -Math.PI * 0.12 + t * Math.PI * 0.92;
                double forward = t * range;
                double crescentRadius = 0.42 + Math.Sin(t * Math.PI) * 1.35;
                double thickness = 0.028 + Math.Sin(t * Math.PI) * 0.12;
                var pos = origin
                    + lookDir * forward
                    + right * bladeOffset
                    + arcPlane * (Math.Cos(theta) * crescentRadius)
                    + bladeTilt * ((t - 0.5) * 1.2)
                    + right * ((rng.NextDouble() - 0.5) * thickness)
                    + up * ((rng.NextDouble() - 0.5) * 0.018);

                world.SpawnParticles(new SimpleParticleProperties
                {
                    MinQuantity = 1,
                    AddQuantity = 0,
                    Color = ColorUtil.ColorFromRgba(238, 251, 255, 195 + rng.Next(50)),
                    MinPos = pos,
                    AddPos = new Vec3d(0.005, 0.005, 0.005),
                    MinVelocity = new Vec3f(
                        (float)(lookDir.X * (13.2 + rng.NextDouble() * 1.8)),
                        (float)((rng.NextDouble() - 0.5) * 0.03),
                        (float)(lookDir.Z * (13.2 + rng.NextDouble() * 1.8))),
                    AddVelocity = new Vec3f(0.02f, 0.02f, 0.02f),
                    LifeLength = 0.1f + (float)rng.NextDouble() * 0.02f,
                    MinSize = 0.095f,
                    MaxSize = 0.17f,
                    GravityEffect = 0f,
                    ParticleModel = EnumParticleModel.Quad,
                    WithTerrainCollision = false,
                    ShouldDieInLiquid = false
                });
            }
        }
    }
}
