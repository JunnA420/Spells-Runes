using System.Collections.Generic;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Air;

public class SpearInAnEye : Spell
{
    public override string Id          => "air_spear_in_an_eye";
    public override string Name        => "Spear in an Eye";
    public override string Description => "Creates a large Storm's Eye, then continuously fires Wind Spears into it. Targets held within are struck repeatedly.";

    public override SpellTier    Tier    => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 80f;
    public override float CastTime => 2.0f;


    public override IReadOnlyList<string> Prerequisites => ["air_storms_eye", "air_wind_spear"];

    public override (int col, int row) TreePosition => (1, 5);
    // Desired visible speed in blocks per second (see VS physics note: velocities are in blocks per 1/60s tick)
    private const float SpearSpeedBps = 9f;
    private const float ProjectileBaseSpeed = 18f;
    private const float SpeedScale = (SpearSpeedBps / 60f) / ProjectileBaseSpeed;
    private const float EnforceSpeedSeconds = 3.2f;
    private const float StartSpeedFactor = 4.2f;
    private const float AccelExponent = 3.2f;

    public static Vec3d GetCenter(EntityAgent caster)
    {
        var center = caster.SidedPos.XYZ.Add(caster.SidedPos.GetViewVector().ToVec3d().Normalize() * 8.5).Add(0, 0.5, 0);
        return ClampToSurface(caster.World, center);
    }

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        var center = GetCenter(caster);
        StormsEye.SpawnFx(world, center, spellLevel, StormsEye.Radius * 1.5f);

        if (world.Side != EnumAppSide.Server || world.Api == null) return;

        float duration = 6f + (spellLevel - 1) * 0.3f;
        float elapsed = 0f;
        float volleyElapsed = 0f;
        float volleyInterval = 0.7f;
        float damage = 16f * GetDamageMultiplier(spellLevel);

        long listenerId = 0;
        listenerId = world.Api.Event.RegisterGameTickListener(dt =>
        {
            elapsed += dt;
            volleyElapsed += dt;
            StormsEye.SpawnFx(world, center, spellLevel, StormsEye.Radius * 1.5f);

            world.GetEntitiesAround(center, StormsEye.Radius * 1.6f, StormsEye.Radius * 1.6f, e =>
            {
                if (e.EntityId == caster.EntityId || e is not EntityAgent) return false;
                Vec3d toCenter = center - e.SidedPos.XYZ;
                double dist = toCenter.Length();
                if (dist <= 0.01 || dist > StormsEye.Radius * 1.6f) return false;
                var dir = toCenter.Normalize();
                e.SidedPos.Motion.Set(dir.X * 0.1, Math.Max(e.SidedPos.Motion.Y * 0.2, -0.02), dir.Z * 0.1);
                return false;
            });

            if (volleyElapsed >= volleyInterval)
            {
                volleyElapsed = 0f;
                for (int i = 0; i < 1; i++)
                {
                    double spread = 3.2 + world.Rand.NextDouble() * 1.6;
                    double height = 9.0 + world.Rand.NextDouble() * 3.0;
                    var start = center.AddCopy(
                        (world.Rand.NextDouble() - 0.5) * spread,
                        height,
                        (world.Rand.NextDouble() - 0.5) * spread);
                    var target = center.AddCopy(
                        (world.Rand.NextDouble() - 0.5) * 0.9,
                        (world.Rand.NextDouble() - 0.5) * 0.7,
                        (world.Rand.NextDouble() - 0.5) * 0.9);
                    var dir = (target - start).Normalize();
                    var spear = WindSpear.SpawnProjectile(caster, world, start, dir * SpeedScale, spellLevel, 0);
                    if (spear != null && world.Api != null)
                    {
                        SetSpearOrientation(spear, dir);
                        Vec3d launchMotion = dir * (SpearSpeedBps / 60f) * (1f + 0.10f * (spellLevel - 1));
                        spear.ServerPos.Motion.Set(launchMotion);
                        spear.Pos.Motion.Set(launchMotion);
                        spear.SidedPos.Motion.Set(launchMotion);
                        EnforceSpearSpeed(world, spear, launchMotion);
                    }
                }

            }

            if (elapsed >= duration)
                world.Api.Event.UnregisterGameTickListener(listenerId);
        }, 100);
    }

    private static void SetSpearOrientation(Entity spear, Vec3d lookDir)
    {
        double yaw = Math.Atan2(lookDir.X, lookDir.Z);
        double horizontal = Math.Sqrt(lookDir.X * lookDir.X + lookDir.Z * lookDir.Z);
        double pitch = -Math.Atan2(lookDir.Y, horizontal);
        yaw += Math.PI / 2.0;
        spear.ServerPos.Yaw = (float)yaw;
        spear.ServerPos.Pitch = (float)pitch;
        spear.Pos.Yaw = (float)yaw;
        spear.Pos.Pitch = (float)pitch;
    }

    private static void EnforceSpearSpeed(IWorldAccessor world, Entity spear, Vec3d motion)
    {
        if (world.Api == null) return;
        float elapsed = 0f;
        long tickId = 0;
        tickId = world.Api.Event.RegisterGameTickListener(dt =>
        {
            if (!spear.Alive)
            {
                world.Api.Event.UnregisterGameTickListener(tickId);
                return;
            }

            elapsed += dt;
            if (elapsed >= EnforceSpeedSeconds)
            {
                world.Api.Event.UnregisterGameTickListener(tickId);
                return;
            }

            float t = Math.Clamp(elapsed / EnforceSpeedSeconds, 0f, 1f);
            float eased = StartSpeedFactor + (1f - StartSpeedFactor) * (float)Math.Pow(t, AccelExponent);
            Vec3d scaled = motion * eased;
            spear.ServerPos.Motion.Set(scaled);
            spear.Pos.Motion.Set(scaled);
            spear.SidedPos.Motion.Set(scaled);
        }, 50);
    }



}
