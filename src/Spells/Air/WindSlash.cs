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
    public const float HitRadius = 0.3f;
    public const float Damage = 10f;
    public static bool DebugHitboxEnabled = false;
    private static readonly Dictionary<long, HashSet<long>> ActiveDebugSweeps = new();
    private static readonly Dictionary<long, HashSet<long>> ActiveDamageSweeps = new();
    private const float SweepSpeed = 20f;
    private const float MinTravelDistance = 0.6f;

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        var lookDir = caster.SidedPos.GetViewVector().ToVec3d().Normalize();
        var origin = caster.SidedPos.XYZ.Add(0, caster.LocalEyePos.Y - 0.1, 0);
        float range = Range * GetRangeMultiplier(spellLevel);
        float damage = Damage * GetDamageMultiplier(spellLevel);

        if (world.Side == EnumAppSide.Server && world.Api != null)
        {
            StartDamageSweep(caster, world, origin, lookDir, range, HitRadius, damage);
            if (DebugHitboxEnabled)
            {
                StartHitboxSweepDebug(caster, world, origin, lookDir, range, HitRadius);
            }
        }

        // FX is spawned via MsgSpellFx handler to avoid double-spawning on client
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

    internal static void StartDamageSweep(EntityAgent caster, IWorldAccessor world, Vec3d origin, Vec3d lookDir, float range, float hitRadius, float damage, bool replaceExisting = true)
    {
        if (world.Api == null) return;
        float elapsed = 0f;
        long listenerId = 0;

        if (replaceExisting && ActiveDamageSweeps.TryGetValue(caster.EntityId, out var prevIds))
        {
            foreach (var prevId in prevIds)
            {
                world.Api.Event.UnregisterGameTickListener(prevId);
            }
            ActiveDamageSweeps.Remove(caster.EntityId);
        }

        listenerId = world.Api.Event.RegisterGameTickListener(dt =>
        {
            if (!caster.Alive)
            {
                world.Api.Event.UnregisterGameTickListener(listenerId);
                RemoveSweep(ActiveDamageSweeps, caster.EntityId, listenerId);
                return;
            }

            elapsed += dt;
            double prevDist = Math.Max(0.0, Math.Min(range, Math.Max(MinTravelDistance, (elapsed - dt) * SweepSpeed)));
            double dist = Math.Min(range, Math.Max(MinTravelDistance, elapsed * SweepSpeed));
            Vec3d prevPos = origin + lookDir * prevDist;
            Vec3d pos = origin + lookDir * dist;

            if (TryHitAlongSegment(caster, world, prevPos, pos, hitRadius, out EntityAgent? hitEntity, out Vec3d hitPos))
            {
                if (hitEntity != null)
                {
                    hitEntity.ReceiveDamage(new DamageSource
                    {
                        Source = EnumDamageSource.Entity,
                        SourceEntity = caster,
                        Type = EnumDamageType.SlashingAttack,
                    }, damage);
                    hitEntity.SidedPos.Motion.Add(lookDir.X * 0.08, 0.02, lookDir.Z * 0.08);
                }
                SpawnPoof(world, hitPos);
                world.Api.Event.UnregisterGameTickListener(listenerId);
                RemoveSweep(ActiveDamageSweeps, caster.EntityId, listenerId);
                return;
            }

            if (dist >= range)
            {
                world.Api.Event.UnregisterGameTickListener(listenerId);
                RemoveSweep(ActiveDamageSweeps, caster.EntityId, listenerId);
            }
        }, 20);

        if (!ActiveDamageSweeps.TryGetValue(caster.EntityId, out var set))
        {
            set = new HashSet<long>();
            ActiveDamageSweeps[caster.EntityId] = set;
        }
        set.Add(listenerId);
    }

    private static bool TryHitAlongSegment(EntityAgent caster, IWorldAccessor world, Vec3d from, Vec3d to, float hitRadius, out EntityAgent? hitEntity, out Vec3d hitPos)
    {
        hitEntity = null;
        hitPos = to;

        Vec3d seg = to - from;
        double segLenSq = seg.LengthSq();
        if (segLenSq < 0.0001)
        {
            return false;
        }

        double closestT = double.MaxValue;
        double closestDist = double.MaxValue;
        EntityAgent? closestEntity = null;
        double segLen = Math.Sqrt(segLenSq);
        Vec3d mid = from + seg * 0.5;
        float queryRadius = hitRadius + (float)(segLen * 0.5) + 0.2f;

        world.GetEntitiesAround(mid, queryRadius, queryRadius, e =>
        {
            if (e.EntityId == caster.EntityId || e is not EntityAgent agent) return false;

            Vec3d target = e.SidedPos.XYZ.Add(0, e.LocalEyePos.Y * 0.5, 0);
            Vec3d toTarget = target - from;
            double t = toTarget.Dot(seg) / segLenSq;
            if (t < 0 || t > 1) return false;

            Vec3d closestPoint = from + seg * t;
            double dist = target.DistanceTo(closestPoint);
            if (dist <= hitRadius && dist < closestDist)
            {
                closestDist = dist;
                closestT = t;
                closestEntity = agent;
            }
            return false;
        });

        double blockT = double.MaxValue;
        BlockSelection? bsel = null;
        EntitySelection? esel = null;
        world.RayTraceForSelection(from, to, ref bsel, ref esel);
        if (bsel != null)
        {
            Vec3d blockHitPos = bsel.Position.ToVec3d().Add(bsel.HitPosition);
            blockT = (blockHitPos - from).Dot(seg) / segLenSq;
            if (blockT < 0 || blockT > 1) blockT = double.MaxValue;
            else hitPos = blockHitPos;
        }

        if (blockT < closestT)
        {
            hitEntity = null;
            return true;
        }

        if (closestEntity != null && closestT != double.MaxValue)
        {
            hitEntity = closestEntity;
            hitPos = from + seg * closestT;
            return true;
        }

        return false;
    }

    private static void SpawnPoof(IWorldAccessor world, Vec3d center)
    {
        var rng = world.Rand;
        var p = new SimpleParticleProperties
        {
            MinQuantity = 1,
            AddQuantity = 0,
            MinPos = center,
            AddPos = new Vec3d(0.18, 0.18, 0.18),
            MinVelocity = new Vec3f(0, 0, 0),
            AddVelocity = new Vec3f(0.7f, 0.6f, 0.7f),
            LifeLength = 0.35f,
            MinSize = 0.25f,
            MaxSize = 0.6f,
            GravityEffect = 0f,
            ParticleModel = EnumParticleModel.Quad,
            WithTerrainCollision = false,
            ShouldDieInLiquid = false
        };

        for (int i = 0; i < 28; i++)
        {
            p.Color = ColorUtil.ColorFromRgba(235, 245, 255, 180 + rng.Next(70));
            world.SpawnParticles(p);
        }
    }

    private static void SpawnHitboxDebug(IWorldAccessor world, Vec3d center, float radius)
    {
        var rng = world.Rand;
        var p = new SimpleParticleProperties
        {
            MinQuantity = 1,
            AddQuantity = 0,
            MinPos = center,
            AddPos = new Vec3d(0, 0, 0),
            MinVelocity = new Vec3f(0, 0, 0),
            AddVelocity = new Vec3f(0, 0, 0),
            LifeLength = 0.25f,
            MinSize = 0.10f,
            MaxSize = 0.10f,
            GravityEffect = 0f,
            ParticleModel = EnumParticleModel.Quad,
            WithTerrainCollision = false,
            ShouldDieInLiquid = false
        };

        for (int i = 0; i < 16; i++)
        {
            double angle = i * (Math.PI * 2 / 16.0);
            double r = radius * 1.2;
            var pos = new Vec3d(center.X + Math.Cos(angle) * r, center.Y, center.Z + Math.Sin(angle) * r);
            p.Color = ColorUtil.ColorFromRgba(255, 80, 80, 220);
            p.MinPos = pos;
            world.SpawnParticles(p);
        }
    }

    internal static void StartHitboxSweepDebug(EntityAgent caster, IWorldAccessor world, Vec3d origin, Vec3d lookDir, float range, float radius, bool replaceExisting = true)
    {
        if (world.Api == null) return;
        float elapsed = 0f;
        long listenerId = 0;

        if (replaceExisting && ActiveDebugSweeps.TryGetValue(caster.EntityId, out var prevIds))
        {
            foreach (var prevId in prevIds)
            {
                world.Api.Event.UnregisterGameTickListener(prevId);
            }
            ActiveDebugSweeps.Remove(caster.EntityId);
        }

        listenerId = world.Api.Event.RegisterGameTickListener(dt =>
        {
            elapsed += dt;
            double dist = Math.Min(range, elapsed * SweepSpeed);
            Vec3d pos = origin + lookDir * dist;
            SpawnHitboxDebug(world, pos, radius);
            if (dist >= range)
            {
                world.Api.Event.UnregisterGameTickListener(listenerId);
                RemoveSweep(ActiveDebugSweeps, caster.EntityId, listenerId);
            }
        }, 20);

        if (!ActiveDebugSweeps.TryGetValue(caster.EntityId, out var set))
        {
            set = new HashSet<long>();
            ActiveDebugSweeps[caster.EntityId] = set;
        }
        set.Add(listenerId);
    }

    private static void RemoveSweep(Dictionary<long, HashSet<long>> map, long casterId, long listenerId)
    {
        if (!map.TryGetValue(casterId, out var set)) return;
        set.Remove(listenerId);
        if (set.Count == 0)
        {
            map.Remove(casterId);
        }
    }

// // Crescent Blade B
// Generated as a per-call SpawnFx() method. If your effect should persist, call this from a tick listener. Courtesy of Fx Visualizer
public static void SpawnFx(IWorldAccessor world, Vec3d origin, Vec3d lookDir, int spellLevel, double rollDeg = 0.0)
{
    var rng = world.Rand;
    int mult = 1 + (spellLevel - 1) / 4;
    Vec3d forward = lookDir.Normalize();
    Vec3d refUp = Math.Abs(forward.Y) > 0.98 ? new Vec3d(1, 0, 0) : new Vec3d(0, 1, 0);
    Vec3d right = refUp.Cross(forward).Normalize();
    Vec3d up = forward.Cross(right).Normalize();
    if (Math.Abs(rollDeg) > 0.0001)
    {
        double rollRad = rollDeg * Math.PI / 180.0;
        double c = Math.Cos(rollRad);
        double s = Math.Sin(rollRad);
        Vec3d r2 = right * c + up * s;
        Vec3d u2 = up * c - right * s;
        right = r2;
        up = u2;
    }

    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    static Vec3d RandomUnit(Random rng)
    {
        Vec3d v;
        do
        {
            v = new Vec3d(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);
        }
        while (v.LengthSq() < 0.0001);
        return v.Normalize();
    }

    static Vec3d DirectionVec(Vec3d dir)
    {
        if (dir.LengthSq() < 0.0001) return new Vec3d(0, 1, 0);
        return dir.Normalize();
    }

    static Vec3d RotateLocalEuler(Vec3d vector, double pitchDeg, double yawDeg, double rollDeg)
    {
        double pitchRad = pitchDeg * Math.PI / 180.0;
        double yawRad = yawDeg * Math.PI / 180.0;
        double rollRad = rollDeg * Math.PI / 180.0;
        double a = Math.Cos(pitchRad);
        double b = Math.Sin(pitchRad);
        double c = Math.Cos(yawRad);
        double d = Math.Sin(yawRad);
        double e = Math.Cos(rollRad);
        double f = Math.Sin(rollRad);

        double m11 = c * e + d * b * f;
        double m12 = d * b * e - c * f;
        double m13 = a * d;
        double m21 = a * f;
        double m22 = a * e;
        double m23 = -b;
        double m31 = c * b * f - d * e;
        double m32 = d * f + c * b * e;
        double m33 = a * c;

        return new Vec3d(
            m11 * vector.X + m12 * vector.Y + m13 * vector.Z,
            m21 * vector.X + m22 * vector.Y + m23 * vector.Z,
            m31 * vector.X + m32 * vector.Y + m33 * vector.Z);
    }

    static Vec3d TransformBasis(Vec3d local, Vec3d forward, Vec3d right, Vec3d up)
    {
        return right * local.X + up * local.Y + forward * local.Z;
    }

    static Vec3d TransformLocal(Vec3d local, Vec3d forward, Vec3d right, Vec3d up, double pitchDeg, double yawDeg, double rollDeg)
    {
        Vec3d rotated = RotateLocalEuler(local, pitchDeg, yawDeg, rollDeg);
        return TransformBasis(rotated, forward, right, up);
    }

    static int LerpColor(int startR, int startG, int startB, int startA, int endR, int endG, int endB, int endA, double t)
    {
        return ColorUtil.ColorFromRgba(
            (int)Math.Round(Lerp(startR, endR, t)),
            (int)Math.Round(Lerp(startG, endG, t)),
            (int)Math.Round(Lerp(startB, endB, t)),
            (int)Math.Round(Lerp(startA, endA, t)));
    }

    static Vec3d SampleLocalPosition(string shape, Random rng, double radius, double radiusTop, double height, double coneAngleDeg, double spreadX, double spreadY, double spreadZ, double chaos)
    {
        if (shape == "cone")
        {
            double angle = (rng.NextDouble() - 0.5) * coneAngleDeg * Math.PI / 180.0;
            double spin = rng.NextDouble() * Math.PI * 2;
            double coneRadius = rng.NextDouble() * radius;
            return new Vec3d(
                Math.Cos(spin) * Math.Sin(angle) * coneRadius,
                Math.Sin(spin) * Math.Sin(angle) * coneRadius,
                rng.NextDouble() * radius);
        }

        if (shape == "slash")
        {
            double t = rng.NextDouble();
            double arcSpan = coneAngleDeg * Math.PI / 180.0;
            double theta = -arcSpan * 0.5 + t * arcSpan;
            double sideJitter = (rng.NextDouble() - 0.5) * Math.Max(0.005, spreadX);
            double upJitter = (rng.NextDouble() - 0.5) * Math.Max(0.005, spreadY);
            double depthJitter = rng.NextDouble() * Math.Max(0.0, Math.Abs(spreadZ) + chaos);
            double arcX = Math.Cos(theta) * radius;
            double arcY = (Math.Sin(theta) * 0.5 + 0.5) * radius;
            return new Vec3d(
                arcX + sideJitter,
                arcY + upJitter,
                depthJitter);
        }


        return RandomUnit(rng) * (rng.NextDouble() * radius);
    }

    static Vec3d ComputeVelocity(string velocityMode, Vec3d direction, Vec3d localPos, Vec3d forward, Vec3d right, Vec3d up, double pitchDeg, double yawDeg, double rollDeg, double speed, double tangentialStrength, double inwardStrength, double verticalStrength)
    {
        Vec3d directional = TransformBasis(DirectionVec(direction) * speed, forward, right, up);
        Vec3d planar = new Vec3d(localPos.X, 0, localPos.Z);
        if (planar.LengthSq() < 0.0001) planar = new Vec3d(0, 0, 1);
        Vec3d radial = planar.Normalize();
        Vec3d tangential = new Vec3d(-radial.Z, 0, radial.X).Normalize();
        Vec3d vertical = new Vec3d(0, verticalStrength, 0);

        if (velocityMode == "radial") return TransformLocal(radial * speed + vertical, forward, right, up, pitchDeg, yawDeg, rollDeg);
        return directional + TransformBasis(vertical, forward, right, up);
    }

    // Crescent Blade B
    // @fxviz eyJsYWJlbCI6IkNyZXNjZW50IEJsYWRlIEIiLCJzaGFwZSI6InNsYXNoIiwidmVsb2NpdHlNb2RlIjoiZGlyZWN0aW9uYWwiLCJidXJzdENvdW50IjoyMDcsImJ1cnN0SW50ZXJ2YWwiOjAsInNwYXduUmF0ZSI6MTEsImR1cmF0aW9uIjoyLCJsb29wIjp0cnVlLCJ3b3JsZFNwYWNlIjpmYWxzZSwicmFkaXVzIjoxLjI3LCJyYWRpdXNUb3AiOjEuNjEsImhlaWdodCI6MCwiY2hhb3MiOjAuMDIsImNvbmVBbmdsZSI6MTI3LCJzcHJlYWRYIjowLCJzcHJlYWRZIjowLjA2LCJzcHJlYWRaIjowLCJtYXhQYXJ0aWNsZXMiOjY4OCwiZGlyZWN0aW9uIjp7IngiOi0wLjAyLCJ5IjowLCJ6IjoxfSwic3BlZWQiOjIwLCJ0YW5nZW50aWFsIjowLCJpbndhcmQiOjAuMSwidmVydGljYWwiOi0wLjEsInJhbmRvbVZlbG9jaXR5IjowLCJkcmFnIjowLjczLCJncmF2aXR5IjowLCJ1cGRyYWZ0IjowLCJsaWZlTWluIjoyLjA1LCJsaWZlTWF4IjoyLjI4LCJzaXplTWluIjowLjA1LCJzaXplTWF4IjowLjEsInN0YXJ0Q29sb3IiOiIjZmZmZmZmIiwiZW5kQ29sb3IiOiIjZmZmZmZmIiwiYWxwaGFTdGFydCI6MSwiYWxwaGFFbmQiOjAsInN0YXJ0RGVsYXkiOjAsIndpdGhUZXJyYWluQ29sbGlzaW9uIjp0cnVlLCJvZmZzZXRYIjotMC4yOCwib2Zmc2V0WSI6MS4xNSwib2Zmc2V0WiI6MCwieWF3IjotODgsInBpdGNoIjotNDYsInJvbGwiOjl9
    {
        const double pitchDeg = -46.0;
        const double yawDeg = -88.0;
        Vec3d emitterOrigin = origin + right * -0.28 + up * 1.15 + forward * 0.0;
        int count = 207 * mult;
    
    
        for (int i = 0; i < count; i++)
        {
            Vec3d localPos = SampleLocalPosition("slash", rng, 1.27, 1.61, 0.0, 127.0, 0.0, 0.06, 0.0, 0.02);
            Vec3d worldPos = emitterOrigin + TransformLocal(localPos, forward, right, up, pitchDeg, yawDeg, rollDeg);
            Vec3d velocity = ComputeVelocity(
                "directional",
                new Vec3d(-0.02, 0.0, 1.0),
                localPos,
                forward, right, up, pitchDeg, yawDeg, rollDeg,
                20.0, 0.0, 0.1, -0.1);
    
            int color = LerpColor(
                255, 255, 255, 255,
                255, 255, 255, 0,
                rng.NextDouble());
    
            world.SpawnParticles(new SimpleParticleProperties
            {
                MinQuantity = 1,
                AddQuantity = 0,
                Color = color,
                MinPos = worldPos,
                AddPos = new Vec3d(0, 0, 0),
                MinVelocity = new Vec3f((float)velocity.X, (float)velocity.Y, (float)velocity.Z),
                AddVelocity = new Vec3f(0, 0, 0),
                LifeLength = (float)Lerp(0.35, 0.5, rng.NextDouble()),
                MinSize = 0.05f,
                MaxSize = 0.1f,
                GravityEffect = 0.0f,
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = false
            });
        }
    }
    
    // Layer 2 (delay 0.15s)
    // @fxviz eyJsYWJlbCI6IkxheWVyIDIiLCJzaGFwZSI6ImNvbmUiLCJ2ZWxvY2l0eU1vZGUiOiJyYWRpYWwiLCJidXJzdENvdW50IjozNDIsImJ1cnN0SW50ZXJ2YWwiOjAsInNwYXduUmF0ZSI6NDYsImR1cmF0aW9uIjowLjEsImxvb3AiOnRydWUsIndvcmxkU3BhY2UiOnRydWUsInJhZGl1cyI6MC40NSwicmFkaXVzVG9wIjowLjQ1LCJoZWlnaHQiOjAuNSwiY2hhb3MiOjAsImNvbmVBbmdsZSI6MzUsInNwcmVhZFgiOjAuNCwic3ByZWFkWSI6MC40LCJzcHJlYWRaIjowLjQsIm1heFBhcnRpY2xlcyI6NTI4LCJkaXJlY3Rpb24iOnsieCI6MCwieSI6LTAuMDEsInoiOjF9LCJzcGVlZCI6NS4zLCJ0YW5nZW50aWFsIjowLCJpbndhcmQiOjAsInZlcnRpY2FsIjowLCJyYW5kb21WZWxvY2l0eSI6MS40LCJkcmFnIjowLjEsImdyYXZpdHkiOjAsInVwZHJhZnQiOjAuMSwibGlmZU1pbiI6MC42MSwibGlmZU1heCI6MC43OSwic2l6ZU1pbiI6MC4wOSwic2l6ZU1heCI6MC4yLCJzdGFydENvbG9yIjoiI2ZmZmZmZiIsImVuZENvbG9yIjoiI2MwYzBjMCIsImFscGhhU3RhcnQiOjEsImFscGhhRW5kIjowLCJzdGFydERlbGF5IjowLjE1LCJ3aXRoVGVycmFpbkNvbGxpc2lvbiI6ZmFsc2UsIm9mZnNldFgiOjAsIm9mZnNldFkiOjAuOTUsIm9mZnNldFoiOjEuNzEsInlhdyI6LTE3MSwicGl0Y2giOi0xMSwicm9sbCI6LTUzfQ==
    {
        const double pitchDeg = -11.0;
        const double yawDeg = -171.0;
        Vec3d emitterOrigin = origin + right * 0.0 + up * 0.95 + forward * 1.71;
        int count = 342 * mult;
        if (world.ElapsedMilliseconds / 1000.0 < 0.15) return;
    
        for (int i = 0; i < count; i++)
        {
            Vec3d localPos = SampleLocalPosition("cone", rng, 0.45, 0.45, 0.5, 35.0, 0.4, 0.4, 0.4, 0.0);
            Vec3d worldPos = emitterOrigin + TransformLocal(localPos, forward, right, up, pitchDeg, yawDeg, rollDeg);
            Vec3d velocity = ComputeVelocity(
                "radial",
                new Vec3d(0.0, -0.01, 1.0),
                localPos,
                forward, right, up, pitchDeg, yawDeg, rollDeg,
                5.3, 0.0, 0.0, 0.0);
    
            if (1.4 > 0)
            {
                velocity += RandomUnit(rng) * (rng.NextDouble() * 1.4);
            }
    
            int color = LerpColor(
                255, 255, 255, 255,
                192, 192, 192, 0,
                rng.NextDouble());
    
            world.SpawnParticles(new SimpleParticleProperties
            {
                MinQuantity = 1,
                AddQuantity = 0,
                Color = color,
                MinPos = worldPos,
                AddPos = new Vec3d(0, 0, 0),
                MinVelocity = new Vec3f((float)velocity.X, (float)velocity.Y, (float)velocity.Z),
                AddVelocity = new Vec3f(0, 0, 0),
                LifeLength = (float)Lerp(0.61, 0.79, rng.NextDouble()),
                MinSize = 0.09f,
                MaxSize = 0.2f,
                GravityEffect = 0.0f,
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = false
            });
        }
    }
}

// Suggested host method name: CrescentBladeB.SpawnFx(...)

}
