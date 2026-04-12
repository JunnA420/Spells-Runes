using System.Collections.Generic;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Air;

public class WindVortex : Spell
{
    [ThreadStatic] private static SimpleParticleProperties? _pool;
    private static SimpleParticleProperties Pool => _pool ??= new SimpleParticleProperties();

    public override string Id          => "air_wind_vortex";
    public override string Name        => "Wind Vortex";
    public override string Description => "Surrounds the caster with a sustained sphere of wind while the button is held. Pushes away nearby entities. Releasing the cast collapses the sphere outward.";

    public override SpellTier    Tier    => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Defense;

    public override float FluxCost => 60f;
    public override float CastTime => 0f;

    public override IReadOnlyList<string> Prerequisites => ["air_wind_spear"];

    public override (int col, int row) TreePosition => (2, 6);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        SpawnFx(world, caster.SidedPos.XYZ.Add(0, 0.8, 0), spellLevel, 1.8f);

        if (world.Side != EnumAppSide.Server || world.Api == null) return;

        float duration = 3f + (spellLevel - 1) * 0.2f;
        float elapsed = 0f;
        float radius = 1.8f;
        long listenerId = 0;
        listenerId = world.Api.Event.RegisterGameTickListener(dt =>
        {
            elapsed += dt;
            var center = caster.SidedPos.XYZ.Add(0, 0.8, 0);
            SpawnFx(world, center, spellLevel, radius);

            world.GetEntitiesAround(center, radius + 0.5f, radius + 0.5f, e =>
            {
                if (e.EntityId == caster.EntityId || e is not EntityAgent) return false;
                Vec3d away = e.SidedPos.XYZ - center;
                double dist = Math.Max(0.15, away.Length());
                away = away.Normalize();
                e.SidedPos.Motion.Add(away.X * (0.18 / dist), 0.04, away.Z * (0.18 / dist));
                return false;
            });

            if (elapsed >= duration)
            {
                world.GetEntitiesAround(center, radius + 1.2f, radius + 1.2f, e =>
                {
                    if (e.EntityId == caster.EntityId || e is not EntityAgent) return false;
                    Vec3d away = (e.SidedPos.XYZ - center).Normalize();
                    e.SidedPos.Motion.Add(away.X * 0.45, 0.12, away.Z * 0.45);
                    return false;
                });

                SpawnFx(world, center, spellLevel, radius + 0.6f);
                world.Api.Event.UnregisterGameTickListener(listenerId);
            }
        }, 100);
    }

    public static void SpawnFx(IWorldAccessor world, Vec3d center, int spellLevel = 1, float radius = 1.8f)
    {
        int mult = 1 + (spellLevel - 1) / 4;
        var p = Pool;

        p.ParticleModel = EnumParticleModel.Quad;
        p.ShouldDieInLiquid = false;
        p.WithTerrainCollision = false;
        p.MinQuantity = 1;
        p.AddQuantity = 0;
        p.GravityEffect = 0f;
        p.Color = ColorUtil.ColorFromRgba(225, 245, 255, 150);
        p.AddPos = new Vec3d(0.01, 0.01, 0.01);
        p.AddVelocity = new Vec3f(0, 0, 0);
        p.MinSize = 0.08f;
        p.MaxSize = 0.14f;
        p.LifeLength = 0.12f;

        int points = 10 * mult;
        for (int i = 0; i < points; i++)
        {
            double angle = i * 2 * Math.PI / points;
            p.MinPos = new Vec3d(
                center.X + Math.Cos(angle) * radius,
                center.Y + ((i % 2 == 0) ? -0.45 : 0.45),
                center.Z + Math.Sin(angle) * radius);
            p.MinVelocity = new Vec3f(
                (float)(-Math.Sin(angle) * 0.25),
                0f,
                (float)(Math.Cos(angle) * 0.25));

            world.SpawnParticles(p);
        }
    }
}
