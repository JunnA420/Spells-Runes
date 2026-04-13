using System.Collections.Generic;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;
using Vintagestory.API.MathTools;
using SpellsAndRunes.Entities;

namespace SpellsAndRunes.Spells.Air;

public class WindSpear : Spell
{
    public override string Id          => "air_wind_spear";
    public override string Name        => "Wind Spear";
    public override string Description => "Compresses air into a narrow point and launches it forward at high velocity. Pierces through targets rather than pushing them.";

    public override SpellTier    Tier    => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 50f;
    public override float CastTime => 1.0f;

    public override string? AnimationCode        => "air_wind_spear";
    public override bool    AnimationUpperBodyOnly => false;

    public override IReadOnlyList<string> Prerequisites => ["air_triple_wind_slash"];

    public override (int col, int row) TreePosition => (2, 5);

    public override SpellOriginConfig Origin => new(Forward: 0.5f, Up: -0.1f);

    private const float ProjectileSpeed = 18f;

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        if (world.Side != EnumAppSide.Server) return;

        var look   = caster.SidedPos.GetViewVector().ToVec3d().Normalize();
        var origin = GetOrigin(caster);
        SpawnProjectile(caster, world, origin, look, spellLevel);
    }

    public static Entity? SpawnProjectile(EntityAgent caster, IWorldAccessor world, Vec3d origin, Vec3d lookDir, int spellLevel, int launchDelayMs = 0)
    {
        var entityType = world.GetEntityType(new AssetLocation("spellsandrunes:wind-spear"));
        if (entityType == null) return null;
        var spear = world.ClassRegistry.CreateEntity(entityType);
        if (spear == null) return null;

        spear.ServerPos.SetPos(origin);
        Vec3d launchMotion = lookDir * ProjectileSpeed * (1f + 0.10f * (spellLevel - 1));
        SetOrientationFromDirection(spear, lookDir);
        Vec3d initialMotion = launchDelayMs > 0 ? new Vec3d() : launchMotion;
        spear.ServerPos.Motion.Set(initialMotion);
        spear.Pos.Motion.Set(initialMotion);
        spear.Pos.SetFrom(spear.ServerPos);

        if (spear is EntityProjectile proj)
        {
            proj.FiredBy        = caster;
            proj.Damage         = 12f * (1f + 0.15f * (spellLevel - 1));
            proj.DamageType     = EnumDamageType.PiercingAttack;
            proj.Weight         = 0.001f;
            proj.ProjectileStack = new ItemStack(world.GetItem(new AssetLocation("game:stick")) ?? world.GetItem(new AssetLocation("game:flint")));
        }

        world.SpawnEntity(spear);
        if (spear is EntityWindSpear windSpear)
        {
            windSpear.RestartSpawnAnimation();
        }

        if (launchDelayMs > 0 && world.Api != null)
        {
            world.Api.Event.RegisterCallback(_ =>
            {
                if (!spear.Alive) return;
                SetOrientationFromDirection(spear, lookDir);
                spear.ServerPos.Motion.Set(launchMotion);
                spear.Pos.Motion.Set(launchMotion);
                if (spear is EntityWindSpear delayedWindSpear)
                {
                    delayedWindSpear.RestartSpawnAnimation();
                }
            }, launchDelayMs);
        }

        return spear;
    }

    private static void SetOrientationFromDirection(Entity spear, Vec3d lookDir)
    {
        double yaw = Math.Atan2(lookDir.X, lookDir.Z);
        double horizontal = Math.Sqrt(lookDir.X * lookDir.X + lookDir.Z * lookDir.Z);
        double pitch = -Math.Atan2(lookDir.Y, horizontal);
        spear.ServerPos.Yaw = (float)yaw;
        spear.ServerPos.Pitch = (float)pitch;
        spear.Pos.Yaw = (float)yaw;
        spear.Pos.Pitch = (float)pitch;
    }

    public static void SpawnFx(IWorldAccessor world, Vec3d origin, Vec3d lookDir, int spellLevel = 1, float length = 8f, float yawOffsetRad = 0f)
    {
        if (yawOffsetRad != 0f)
        {
            double cos = Math.Cos(yawOffsetRad);
            double sin = Math.Sin(yawOffsetRad);
            lookDir = new Vec3d(
                lookDir.X * cos - lookDir.Z * sin,
                lookDir.Y,
                lookDir.X * sin + lookDir.Z * cos);
        }
        if (lookDir.LengthSq() > 0.0001) lookDir = lookDir.Normalize();
        int mult = 1 + (spellLevel - 1) / 4;
        var rng = world.Rand;

        for (int i = 0; i < 80 * mult; i++)
        {
            double t = rng.NextDouble() * length;
            var pos = origin + lookDir * t + new Vec3d((rng.NextDouble() - 0.5) * 0.16, (rng.NextDouble() - 0.5) * 0.16, (rng.NextDouble() - 0.5) * 0.16);
            world.SpawnParticles(new SimpleParticleProperties
            {
                MinQuantity = 1,
                AddQuantity = 0,
                Color = ColorUtil.ColorFromRgba(235, 250, 255, 170 + rng.Next(60)),
                MinPos = pos,
                AddPos = new Vec3d(0.04, 0.04, 0.04),
                MinVelocity = new Vec3f((float)(lookDir.X * 10), (float)(lookDir.Y * 10), (float)(lookDir.Z * 10)),
                AddVelocity = new Vec3f(0.12f, 0.12f, 0.12f),
                LifeLength = 0.18f + (float)rng.NextDouble() * 0.12f,
                MinSize = 0.09f,
                MaxSize = 0.22f,
                GravityEffect = 0f,
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = false
            });
        }
    }

    public static void SpawnLingerFx(IWorldAccessor world, Vec3d pos, Vec3d dir, float yawOffsetRad = 0f)
    {
        var rng = world.Rand;
        var p = new SimpleParticleProperties
        {
            MinQuantity = 1,
            AddQuantity = 0,
            MinPos = pos,
            AddPos = new Vec3d(0, 0, 0),
            MinVelocity = new Vec3f(0, 0, 0),
            AddVelocity = new Vec3f(0, 0, 0),
            LifeLength = 0.6f,
            MinSize = 0.16f,
            MaxSize = 0.26f,
            GravityEffect = 0f,
            ParticleModel = EnumParticleModel.Quad,
            WithTerrainCollision = false,
            ShouldDieInLiquid = false
        };

        Vec3d forward = dir.LengthSq() < 0.0001 ? new Vec3d(0, 0, 1) : dir.Normalize();
        if (yawOffsetRad != 0f)
        {
            double cos = Math.Cos(yawOffsetRad);
            double sin = Math.Sin(yawOffsetRad);
            forward = new Vec3d(
                forward.X * cos - forward.Z * sin,
                forward.Y,
                forward.X * sin + forward.Z * cos);
        }
        for (int i = 0; i < 28; i++)
        {
            Vec3d offset = forward * (0.12 * i);
            p.MinPos = pos + offset;
            p.Color = ColorUtil.ColorFromRgba(230, 245, 255, 170 + rng.Next(70));
            world.SpawnParticles(p);
        }
    }

}
