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

    public static Vec3d GetCenter(EntityAgent caster)
        => caster.SidedPos.XYZ.Add(caster.SidedPos.GetViewVector().ToVec3d().Normalize() * 8.5).Add(0, 0.5, 0);

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
                for (int i = 0; i < 2; i++)
                {
                    double angle = world.Rand.NextDouble() * Math.PI * 2;
                    double ringRadius = 4.8 + world.Rand.NextDouble() * 1.2;
                    double height = 2.8 + world.Rand.NextDouble() * 1.2;
                    var start = center.AddCopy(Math.Cos(angle) * ringRadius, height, Math.Sin(angle) * ringRadius);
                    var target = center.AddCopy(
                        (world.Rand.NextDouble() - 0.5) * 0.9,
                        (world.Rand.NextDouble() - 0.5) * 0.7,
                        (world.Rand.NextDouble() - 0.5) * 0.9);
                    var dir = (target - start).Normalize();
                    int launchDelay = 220 + world.Rand.Next(180);
                    WindSpear.SpawnProjectile(caster, world, start, dir, spellLevel, launchDelay);
                }

                world.GetEntitiesAround(center, 1.75f, 1.75f, e =>
                {
                    if (e.EntityId == caster.EntityId || e is not EntityAgent) return false;
                    e.ReceiveDamage(new DamageSource
                    {
                        Source = EnumDamageSource.Entity,
                        SourceEntity = caster,
                        Type = EnumDamageType.PiercingAttack,
                    }, damage);
                    return false;
                });
            }

            if (elapsed >= duration)
                world.Api.Event.UnregisterGameTickListener(listenerId);
        }, 100);
    }
}
