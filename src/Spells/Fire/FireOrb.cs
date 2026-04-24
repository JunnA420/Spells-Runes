using System.Collections.Generic;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using SpellsAndRunes.Network;

namespace SpellsAndRunes.Spells.Fire;

public class FireOrb : Spell
{
    public override string Id => "fire_orb";
    public override string Name => "Fire Orb";
    public override string Description => "Forms a concentrated orb of flame between the caster's hands.";

    public override SpellTier Tier => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Fire;
    public override SpellType Type => SpellType.Offense;

    public override float FluxCost => 20f;
    public override float CastTime => 0f;

    public override string? AnimationCode => "fire_orb";
    public override bool AnimationUpperBodyOnly => false;

    public override IReadOnlyList<string> Prerequisites => ["fire_spark"];
    public override (int col, int row) TreePosition => (2, 2);

    public const float Range = 12f;
    public const float Speed = 16f;
    public const float HitRadius = 0.55f;
    public const float Damage = 7f;

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        if (world.Api == null) return;

        int[] releaseFrames = { 19, 29, 39 };
        for (int i = 0; i < releaseFrames.Length; i++)
        {
            int delayMs = FrameToMs(releaseFrames[i]);
            world.Api.Event.RegisterCallback(_ =>
            {
                if (!caster.Alive) return;
                var lookDir = caster.SidedPos.GetViewVector().ToVec3d().Normalize();
                var origin = caster.SidedPos.XYZ
                    .Add(lookDir * 0.9)
                    .Add(0, caster.LocalEyePos.Y - 0.35, 0);
                StartOrb(caster, world, origin, lookDir, spellLevel);
            }, delayMs);
        }
    }

    private static int FrameToMs(int frame) => (int)Math.Round(frame / 30.0 * 1000.0);

    private static void StartOrb(EntityAgent caster, IWorldAccessor world, Vec3d origin, Vec3d lookDir, int spellLevel)
    {
        if (world.Api == null) return;

        float range = Range * (1f + 0.10f * (spellLevel - 1));
        float damage = Damage * (1f + 0.15f * (spellLevel - 1));
        float traveled = 0f;
        Vec3d pos = origin.Clone();
        long listenerId = 0;
        const float stepDt = 0.02f;
        float stepDist = Speed * stepDt;

        listenerId = world.Api.Event.RegisterGameTickListener(dt =>
        {
            if (!caster.Alive)
            {
                world.Api.Event.UnregisterGameTickListener(listenerId);
                return;
            }

            Vec3d prev = pos.Clone();
            pos.Add(lookDir.X * stepDist, lookDir.Y * stepDist, lookDir.Z * stepDist);
            traveled += stepDist;

            BroadcastFx(world, "fire_orb_trail", pos, lookDir, spellLevel);

            if (HitBlock(world, prev, pos, out Vec3d hitPos) || TryHitEntity(caster, world, prev, pos, HitRadius, damage, out hitPos))
            {
                BroadcastFx(world, "fire_orb_impact", hitPos, lookDir, spellLevel);
                world.Api.Event.UnregisterGameTickListener(listenerId);
                return;
            }

            if (traveled >= range)
            {
                BroadcastFx(world, "fire_orb_impact", pos, lookDir, spellLevel);
                world.Api.Event.UnregisterGameTickListener(listenerId);
            }
        }, (int)(stepDt * 1000));
    }

    private static bool HitBlock(IWorldAccessor world, Vec3d from, Vec3d to, out Vec3d hitPos)
    {
        hitPos = to;
        BlockSelection? bsel = null;
        EntitySelection? esel = null;
        world.RayTraceForSelection(from, to, ref bsel, ref esel);
        if (bsel == null) return false;
        hitPos = bsel.Position.ToVec3d().Add(bsel.HitPosition);
        return true;
    }

    private static bool TryHitEntity(EntityAgent caster, IWorldAccessor world, Vec3d from, Vec3d to, float radius, float damage, out Vec3d hitPos)
    {
        hitPos = to;
        Vec3d seg = to - from;
        double segLenSq = seg.LengthSq();
        if (segLenSq < 0.0001) return false;

        EntityAgent? closest = null;
        double closestDist = double.MaxValue;
        Vec3d closestHitPos = to;
        Vec3d mid = from + seg * 0.5;
        float queryRadius = radius + (float)Math.Sqrt(segLenSq) * 0.5f;

        world.GetEntitiesAround(mid, queryRadius, queryRadius, e =>
        {
            if (e.EntityId == caster.EntityId || e is not EntityAgent agent) return false;
            Vec3d target = e.SidedPos.XYZ.Add(0, e.LocalEyePos.Y * 0.5, 0);
            double t = (target - from).Dot(seg) / segLenSq;
            if (t < 0 || t > 1) return false;
            Vec3d closestPoint = from + seg * t;
            double dist = target.DistanceTo(closestPoint);
            if (dist <= radius && dist < closestDist)
            {
                closestDist = dist;
                closest = agent;
                closestHitPos = closestPoint;
            }
            return false;
        });

        if (closest == null) return false;
        hitPos = closestHitPos;
        closest.ReceiveDamage(new DamageSource
        {
            Source = EnumDamageSource.Entity,
            SourceEntity = caster,
            Type = EnumDamageType.Fire,
        }, damage);
        return true;
    }

    internal static void BroadcastFx(IWorldAccessor world, string spellId, Vec3d origin, Vec3d lookDir, int spellLevel)
    {
        if (world.Api is not ICoreServerAPI sapi) return;
        var msg = new MsgSpellFx
        {
            SpellId = spellId,
            OriginX = origin.X,
            OriginY = origin.Y,
            OriginZ = origin.Z,
            LookDirX = (float)lookDir.X,
            LookDirY = (float)lookDir.Y,
            LookDirZ = (float)lookDir.Z,
            SpellLevel = spellLevel
        };
        var channel = sapi.Network.GetChannel("spellsandrunes");
        foreach (var p in sapi.World.AllOnlinePlayers)
            channel.SendPacket(msg, p as IServerPlayer);
    }

    public static void SpawnTrailFx(IWorldAccessor world, Vec3d origin, Vec3d lookDir, int spellLevel)
    {
        var rng = world.Rand;
        int mult = 1 + (spellLevel - 1) / 4;
        for (int i = 0; i < 8 * mult; i++)
        {
            world.SpawnParticles(new SimpleParticleProperties
            {
                MinQuantity = 1,
                AddQuantity = 0,
                MinPos = origin,
                AddPos = new Vec3d(0.18, 0.18, 0.18),
                MinVelocity = new Vec3f((float)(-lookDir.X * 0.8), 0.12f, (float)(-lookDir.Z * 0.8)),
                AddVelocity = new Vec3f(0.35f, 0.35f, 0.35f),
                LifeLength = 0.22f + (float)rng.NextDouble() * 0.12f,
                MinSize = 0.10f,
                MaxSize = 0.24f,
                GravityEffect = -0.1f,
                Color = ColorUtil.ColorFromRgba(20 + rng.Next(80), 120 + rng.Next(100), 255, 220),
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = true
            });
        }
    }

    public static void SpawnImpactFx(IWorldAccessor world, Vec3d origin, int spellLevel)
    {
        var rng = world.Rand;
        int mult = 1 + (spellLevel - 1) / 4;
        for (int i = 0; i < 38 * mult; i++)
        {
            world.SpawnParticles(new SimpleParticleProperties
            {
                MinQuantity = 1,
                AddQuantity = 0,
                MinPos = origin,
                AddPos = new Vec3d(0.28, 0.28, 0.28),
                MinVelocity = new Vec3f(0, 0.25f, 0),
                AddVelocity = new Vec3f(1.1f, 0.8f, 1.1f),
                LifeLength = 0.3f + (float)rng.NextDouble() * 0.18f,
                MinSize = 0.10f,
                MaxSize = 0.32f,
                GravityEffect = -0.15f,
                Color = ColorUtil.ColorFromRgba(10 + rng.Next(80), 95 + rng.Next(130), 255, 225),
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = true
            });
        }
    }
}
