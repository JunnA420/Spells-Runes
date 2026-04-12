using System.Collections.Generic;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using SpellsAndRunes.Network;

namespace SpellsAndRunes.Spells.Air;

public class StormsEye : Spell
{
    public override string Id          => "air_storms_eye";
    public override string Name        => "Storm's Eye";
    public override string Description => "Creates a localized eye of turbulence around a target. The winds within hold them in place.";

    public override SpellTier    Tier    => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Enchantment;

    public override float FluxCost => 55f;
    public override float CastTime => 1.5f;

    public override IReadOnlyList<string> Prerequisites => ["air_wind_clone"];

    public override (int col, int row) TreePosition => (0, 5);

    public const float Range = 7f;
    public const float Radius = 2.4f;
    public const float PullStrength = 0.08f;
    public const float DurationSeconds = 5f;

    public static Vec3d GetCenter(EntityAgent caster)
        => caster.SidedPos.XYZ.Add(caster.SidedPos.GetViewVector().ToVec3d().Normalize() * Range).Add(0, 0.5, 0);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        var center = GetCenter(caster);
        SpawnFx(world, center, spellLevel, Radius);

        if (world.Side != EnumAppSide.Server || world.Api == null) return;

        float duration = DurationSeconds + (spellLevel - 1) * 0.25f;
        float elapsed = 0f;
        long listenerId = 0;
        listenerId = world.Api.Event.RegisterGameTickListener(dt =>
        {
            elapsed += dt;
            BroadcastFx(world, center, spellLevel);

            world.GetEntitiesAround(center, Radius + 0.5f, Radius + 0.5f, e =>
            {
                if (e.EntityId == caster.EntityId || e is not EntityAgent) return false;
                Vec3d toCenter = center - e.SidedPos.XYZ;
                double dist = toCenter.Length();
                if (dist <= 0.01 || dist > Radius + 0.4f) return false;

                var dir = toCenter.Normalize();
                float holdFactor = dist < 0.6 ? 0.025f : PullStrength;
                e.SidedPos.Motion.Set(
                    dir.X * holdFactor,
                    Math.Max(e.SidedPos.Motion.Y * 0.2, -0.02),
                    dir.Z * holdFactor);
                return false;
            });

            if (elapsed >= duration)
                world.Api.Event.UnregisterGameTickListener(listenerId);
        }, 100);
    }

    private static void BroadcastFx(IWorldAccessor world, Vec3d center, int spellLevel)
    {
        if (world.Api is not ICoreServerAPI sapi) return;

        var fx = new MsgSpellFx
        {
            SpellId = "air_storms_eye",
            OriginX = center.X,
            OriginY = center.Y,
            OriginZ = center.Z,
            LookDirX = 0,
            LookDirY = 1,
            LookDirZ = 0,
            SpellLevel = spellLevel
        };

        var channel = sapi.Network.GetChannel("spellsandrunes");
        foreach (var p in sapi.World.AllOnlinePlayers)
            channel.SendPacket(fx, p as IServerPlayer);
    }

    public static void SpawnFx(IWorldAccessor world, Vec3d center, int spellLevel = 1, float radius = Radius)
    {
        int mult = 1 + (spellLevel - 1) / 4;
        var rng = world.Rand;

        for (int i = 0; i < 52 * mult; i++)
        {
            double angle = rng.NextDouble() * Math.PI * 2;
            double height = rng.NextDouble() * 1.45 - 0.35;
            double funnel = 0.28 + (height + 0.35) * 0.32;
            double r = radius * funnel * (0.75 + rng.NextDouble() * 0.22);
            double swirl = 4.6 + rng.NextDouble() * 2.4;
            var pos = new Vec3d(center.X + Math.Cos(angle) * r, center.Y + height, center.Z + Math.Sin(angle) * r);
            var toCenter = center - pos;
            var dir = toCenter.Normalize();
            var tangent = new Vec3d(-Math.Sin(angle), 0.06 + rng.NextDouble() * 0.1, Math.Cos(angle));

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinQuantity = 1,
                AddQuantity = 0,
                Color = ColorUtil.ColorFromRgba(220, 247, 255, 145 + rng.Next(70)),
                MinPos = pos,
                AddPos = new Vec3d(0.025, 0.025, 0.025),
                MinVelocity = new Vec3f(
                    (float)(dir.X * 1.8 + tangent.X * swirl),
                    (float)(0.03 + rng.NextDouble() * 0.18),
                    (float)(dir.Z * 1.8 + tangent.Z * swirl)),
                AddVelocity = new Vec3f(0.06f, 0.06f, 0.06f),
                LifeLength = 0.18f + (float)rng.NextDouble() * 0.06f,
                MinSize = 0.09f,
                MaxSize = 0.22f,
                GravityEffect = -0.035f,
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = false
            });
        }

        for (int i = 0; i < 18 * mult; i++)
        {
            double a = rng.NextDouble() * Math.PI * 2;
            double ringR = radius * (0.95 + rng.NextDouble() * 0.12);
            var pos = new Vec3d(center.X + Math.Cos(a) * ringR, center.Y + (rng.NextDouble() - 0.5) * 0.35, center.Z + Math.Sin(a) * ringR);
            var inward = (center - pos).Normalize();

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinQuantity = 1,
                AddQuantity = 0,
                Color = ColorUtil.ColorFromRgba(235, 252, 255, 120 + rng.Next(60)),
                MinPos = pos,
                AddPos = new Vec3d(0.02, 0.02, 0.02),
                MinVelocity = new Vec3f((float)(inward.X * 3.2), (float)((rng.NextDouble() - 0.5) * 0.1), (float)(inward.Z * 3.2)),
                AddVelocity = new Vec3f(0.04f, 0.04f, 0.04f),
                LifeLength = 0.12f + (float)rng.NextDouble() * 0.04f,
                MinSize = 0.08f,
                MaxSize = 0.16f,
                GravityEffect = 0f,
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = false
            });
        }
    }
}
