using System.Collections.Generic;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using SpellsAndRunes.Network;

namespace SpellsAndRunes.Spells.Fire;

public class FireDance : Spell
{
    public override string Id => "fire_dance";
    public override string Name => "Fire Dance";
    public override string Description => "Performs a flowing fire stance that gathers heat around the caster.";

    public override SpellTier Tier => SpellTier.Adept;
    public override SpellElement Element => SpellElement.Fire;
    public override SpellType Type => SpellType.Enchantment;

    public override float FluxCost => 36f;
    public override float CastTime => 0f;

    public override string? AnimationCode => "fire_dance";
    public override bool AnimationUpperBodyOnly => false;

    public override IReadOnlyList<string> Prerequisites => ["fire_orb", "fire_mine"];
    public override (int col, int row) TreePosition => (1, 3);

    public const float StepDistance = 1.15f;
    public const float ConeRange = 4.5f;
    public const float ConeAngleDeg = 36f;
    public const float Damage = 5f;

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        if (world.Api == null) return;

        var beats = new[]
        {
            new DanceBeat(15, 0),
            new DanceBeat(38, 2),
            new DanceBeat(51, 0),
        };

        foreach (var beat in beats)
        {
            int delayMs = FrameToMs(beat.Frame);
            world.Api.Event.RegisterCallback(_ =>
            {
                if (!caster.Alive) return;
                var lookDir = caster.SidedPos.GetViewVector().ToVec3d().Normalize();
                DoStep(caster, world, lookDir, beat.Side, spellLevel);
                if (beat.Side == 2)
                {
                    EmitCone(caster, world, lookDir, -1, spellLevel);
                    EmitCone(caster, world, lookDir, 1, spellLevel);
                }
                else
                {
                    EmitCone(caster, world, lookDir, beat.Side, spellLevel);
                }
            }, delayMs);
        }
    }

    private readonly record struct DanceBeat(int Frame, int Side);

    private static int FrameToMs(int frame) => (int)Math.Round(frame / 30.0 * 1000.0);

    private static void DoStep(EntityAgent caster, IWorldAccessor world, Vec3d lookDir, int side, int spellLevel)
    {
        float force = StepDistance * (1f + 0.04f * (spellLevel - 1));
        Vec3d right = lookDir.Cross(new Vec3d(0, 1, 0)).Normalize();
        int stepSide = side == 2 ? 0 : side;
        Vec3d stepDir = (lookDir * 0.82 + right * (stepSide * 0.34)).Normalize();
        caster.SidedPos.Motion.Add(stepDir.X * force, 0.03, stepDir.Z * force);

        if (world.Api is not ICoreServerAPI sapi) return;
        var channel = sapi.Network.GetChannel("spellsandrunes");
        foreach (var p in sapi.World.AllOnlinePlayers)
        {
            if (p is not IServerPlayer sp || sp.Entity?.EntityId != caster.EntityId) continue;
            channel.SendPacket(new MsgLaunchPlayer
            {
                UpForce = 0.03f,
                ForwardForce = force,
                LookDirX = (float)stepDir.X,
                LookDirY = 0f,
                LookDirZ = (float)stepDir.Z,
                UseLookY = false
            }, sp);
            break;
        }
    }

    private static void EmitCone(EntityAgent caster, IWorldAccessor world, Vec3d lookDir, int side, int spellLevel)
    {
        Vec3d right = lookDir.Cross(new Vec3d(0, 1, 0)).Normalize();
        Vec3d flameDir = (lookDir * 0.9 + right * (side * 0.5)).Normalize();
        double forwardOffset = side == 0 ? 1.35 : 0.95;
        var origin = caster.SidedPos.XYZ
            .Add(lookDir * forwardOffset)
            .Add(right * (side * 0.72))
            .Add(0, caster.LocalEyePos.Y - 0.48, 0);
        float range = ConeRange * (1f + 0.10f * (spellLevel - 1));
        float damage = Damage * (1f + 0.15f * (spellLevel - 1));
        float cosAngle = (float)Math.Cos(ConeAngleDeg * Math.PI / 180.0);

        world.GetEntitiesAround(origin, range + 1, range + 1, e =>
        {
            if (e.EntityId == caster.EntityId || e is not EntityAgent) return false;
            Vec3d target = e.SidedPos.XYZ.Add(0, e.LocalEyePos.Y * 0.5, 0);
            Vec3d toEntity = target - origin;
            if (toEntity.Length() > range) return false;
            if (flameDir.Dot(toEntity.Normalize()) < cosAngle) return false;
            e.ReceiveDamage(new DamageSource
            {
                Source = EnumDamageSource.Entity,
                SourceEntity = caster,
                Type = EnumDamageType.Fire,
            }, damage);
            return false;
        });

        FireOrb.BroadcastFx(world, "fire_dance_cone", origin, flameDir, spellLevel);
    }

    public static void SpawnConeFx(IWorldAccessor world, Vec3d origin, Vec3d lookDir, int spellLevel)
    {
        var rng = world.Rand;
        Vec3d right = lookDir.Cross(new Vec3d(0, 1, 0)).Normalize();
        Vec3d up = right.Cross(lookDir).Normalize();
        int mult = 1 + (spellLevel - 1) / 4;

        for (int i = 0; i < 118 * mult; i++)
        {
            double dist = 0.2 + rng.NextDouble() * ConeRange;
            double spread = Math.Tan((ConeAngleDeg + 8) * Math.PI / 180.0) * dist;
            double a = rng.NextDouble() * Math.PI * 2;
            double r = Math.Sqrt(rng.NextDouble()) * spread;
            var pos = origin + lookDir * dist + right * (Math.Cos(a) * r) + up * (Math.Sin(a) * r * 0.72);
            world.SpawnParticles(new SimpleParticleProperties
            {
                MinQuantity = 1,
                AddQuantity = 0,
                MinPos = pos,
                AddPos = new Vec3d(0.09, 0.08, 0.09),
                MinVelocity = new Vec3f((float)(lookDir.X * 4.4), 0.55f, (float)(lookDir.Z * 4.4)),
                AddVelocity = new Vec3f(1.25f, 0.75f, 1.25f),
                LifeLength = 0.34f + (float)rng.NextDouble() * 0.26f,
                MinSize = 0.14f,
                MaxSize = 0.42f,
                GravityEffect = -0.22f,
                Color = ColorUtil.ColorFromRgba(5 + rng.Next(85), 90 + rng.Next(145), 255, 230),
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = true
            });
        }
    }
}
