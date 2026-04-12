using System.Collections.Generic;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Air;

public class Tornado : Spell
{
    public override string Id          => "air_tornado";
    public override string Name        => "Tornado";
    public override string Description => "Summons a tornado at the target location. The tornado moves on its own and cannot be controlled once cast.";

    public override SpellTier    Tier    => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 70f;
    public override float CastTime => 2.0f;

    public override IReadOnlyList<string> Prerequisites => ["air_storms_eye"];

    public override (int col, int row) TreePosition => (0, 6);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        var baseCenter = caster.SidedPos.XYZ.Add(caster.SidedPos.GetViewVector().ToVec3d().Normalize() * 9).Add(0, 0.5, 0);
        SpawnFx(world, baseCenter, spellLevel, 2.2f);

        if (world.Side != EnumAppSide.Server || world.Api == null) return;

        var rng = world.Rand;
        var currentCenter = baseCenter.Clone();
        var targetCenter = baseCenter.Clone();
        float roamRadius = 5f;
        float duration = 8f + (spellLevel - 1) * 0.4f;
        float elapsed = 0f;
        float damage = 7f * GetDamageMultiplier(spellLevel);

        long listenerId = 0;
        listenerId = world.Api.Event.RegisterGameTickListener(dt =>
        {
            elapsed += dt;
            if ((targetCenter - currentCenter).Length() < 0.5)
            {
                targetCenter = baseCenter.AddCopy(
                    (rng.NextDouble() - 0.5) * roamRadius * 2,
                    0,
                    (rng.NextDouble() - 0.5) * roamRadius * 2);
            }

            var move = targetCenter - currentCenter;
            if (move.Length() > 0.01)
            {
                move = move.Normalize() * 0.22;
                currentCenter.Add(move);
            }

            SpawnFx(world, currentCenter, spellLevel, 2.2f);
            world.GetEntitiesAround(currentCenter, 2.3f, 3.5f, e =>
            {
                if (e.EntityId == caster.EntityId || e is not EntityAgent) return false;
                Vec3d toCenter = currentCenter - e.SidedPos.XYZ;
                double dist = Math.Max(0.1, toCenter.Length());
                var dir = toCenter.Normalize();
                e.SidedPos.Motion.Add(dir.X * 0.08, 0.16 + (float)(0.12 / dist), dir.Z * 0.08);
                e.ReceiveDamage(new DamageSource
                {
                    Source = EnumDamageSource.Entity,
                    SourceEntity = caster,
                    Type = EnumDamageType.BluntAttack,
                }, damage * 0.1f);
                return false;
            });

            if (elapsed >= duration)
                world.Api.Event.UnregisterGameTickListener(listenerId);
        }, 100);
    }

    public static void SpawnFx(IWorldAccessor world, Vec3d center, int spellLevel = 1, float radius = 2.2f)
    {
        int mult = 1 + (spellLevel - 1) / 4;
        var rng = world.Rand;

        for (int segment = 0; segment < 6; segment++)
        {
            double segOffset = segment * 0.65;
            double segTwist = (segment % 2 == 0 ? 1 : -1) * 0.35;
            int segCount = 26 * mult;
            for (int i = 0; i < segCount; i++)
            {
                double angle = rng.NextDouble() * Math.PI * 2 + segOffset;
                double height = rng.NextDouble() * 5.2;
                double chaos = 0.12 + rng.NextDouble() * 0.22;
                double r = radius * (0.16 + height / 4.2) * (0.82 + rng.NextDouble() * 0.35);
                var pos = new Vec3d(
                    center.X + Math.Cos(angle) * r + Math.Sin(height * 1.6 + segOffset) * chaos,
                    center.Y + height,
                    center.Z + Math.Sin(angle) * r + Math.Cos(height * 1.4 + segOffset) * chaos);
                var tangent = new Vec3d(-Math.Sin(angle + segTwist), 0.22 + rng.NextDouble() * 0.45, Math.Cos(angle + segTwist)).Normalize();

                world.SpawnParticles(new SimpleParticleProperties
                {
                    MinQuantity = 1,
                    AddQuantity = 0,
                    Color = ColorUtil.ColorFromRgba(220, 244, 255, 115 + rng.Next(75)),
                    MinPos = pos,
                    AddPos = new Vec3d(0.035, 0.035, 0.035),
                    MinVelocity = new Vec3f((float)(tangent.X * (4.8 + rng.NextDouble() * 1.8)), (float)(tangent.Y * 2.4), (float)(tangent.Z * (4.8 + rng.NextDouble() * 1.8))),
                    AddVelocity = new Vec3f(0.08f, 0.08f, 0.08f),
                    LifeLength = 0.22f + (float)rng.NextDouble() * 0.1f,
                    MinSize = 0.09f,
                    MaxSize = 0.22f,
                    GravityEffect = -0.015f,
                    ParticleModel = EnumParticleModel.Quad,
                    WithTerrainCollision = false,
                    ShouldDieInLiquid = false
                });
            }
        }

        for (int i = 0; i < 22 * mult; i++)
        {
            double angle = rng.NextDouble() * Math.PI * 2;
            double ring = radius * (0.95 + rng.NextDouble() * 0.25);
            var pos = new Vec3d(center.X + Math.Cos(angle) * ring, center.Y + rng.NextDouble() * 0.7, center.Z + Math.Sin(angle) * ring);
            var inward = (center - pos).Normalize();

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinQuantity = 1,
                AddQuantity = 0,
                Color = ColorUtil.ColorFromRgba(240, 252, 255, 120 + rng.Next(50)),
                MinPos = pos,
                AddPos = new Vec3d(0.02, 0.02, 0.02),
                MinVelocity = new Vec3f((float)(inward.X * 2.6), (float)(0.25 + rng.NextDouble() * 0.35), (float)(inward.Z * 2.6)),
                AddVelocity = new Vec3f(0.04f, 0.04f, 0.04f),
                LifeLength = 0.14f + (float)rng.NextDouble() * 0.05f,
                MinSize = 0.07f,
                MaxSize = 0.15f,
                GravityEffect = -0.02f,
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = false
            });
        }
    }
}
