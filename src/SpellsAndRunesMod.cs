using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;
using SpellsAndRunes.Blocks;
using SpellsAndRunes.Commands;
using SpellsAndRunes.Flux;
using SpellsAndRunes.HUD;
using SpellsAndRunes.GUI;
using SpellsAndRunes.Spells;
using SpellsAndRunes.Network;
using SpellsAndRunes.Render;

namespace SpellsAndRunes;

public class SpellsAndRunesMod : ModSystem
{
    public static bool DebugHitboxesEnabled;
    private sealed class ActiveChannelSpell
    {
        public string SpellId { get; init; } = "";
        public int SpellLevel { get; init; }
        public float DrainTimer { get; set; }
    }

    private sealed class PendingCast
    {
        public string SpellId { get; init; } = "";
        public long TaskId { get; init; }
    }

    private HudFlux? hudFlux;
    private HudCastBar? castBar;
    private HudRadialMenu? radialMenu;
    private HudChickenCounter? hudChicken;
    private GuiDialogSpellbook? spellbookDialog;
    private SpellConeRenderer?  coneRenderer;
    private SparkGlowRenderer?     sparkGlow;
    public  SylphweedGlowRenderer? SylphGlow { get; private set; }

    private IClientNetworkChannel?  clientChannel;
    private IServerNetworkChannel? serverChannel;
    private readonly Dictionary<long, ActiveChannelSpell> activeChannelSpells = new();
    private readonly Dictionary<long, PendingCast> pendingCasts = new();
    private readonly Dictionary<string, long> recentFxReceipts = new();
    private readonly Dictionary<string, long> recentSpellReceipts = new();
    private bool windVortexHeld;
    private bool lmbHoldActive;
    private string? lmbHoldSpellId;
    private string? activeCastSpellId;

    private const string ChannelName = "spellsandrunes";
    private const string ChickenKillFileName = "chicken-kills.json";
    private int chickenKills;
    private string? chickenKillPath;
    private bool chickenKillsLoaded;

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass("fluxBehavior", typeof(EntityBehaviorFlux));
        api.RegisterBlockClass("IgnisFragment", typeof(Blocks.BlockIgnisFragment));
        api.RegisterBlockEntityClass("ignisfragment", typeof(Blocks.BlockEntityIgnisFragment));
        api.RegisterBlockEntityClass("sylphweed", typeof(Blocks.BlockEntitySylphweed));
        api.RegisterItemClass("SylphweedBong", typeof(Blocks.ItemSylphweedBong));
        api.RegisterEntity("EntityWindSpear", typeof(Entities.EntityWindSpear));
        SpellRegistry.RegisterAll();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Logger.Notification("[Spells & Runes] Server side loaded.");
        DebugCommands.Register(api);
        InitChickenKillCounter(api);
        api.Event.OnEntityDespawn += (entity, despawnData) =>
        {
            if (entity == null) return;
            bool isDeath = despawnData?.Reason == EnumDespawnReason.Death;
            if (!isDeath && despawnData?.DamageSourceForDeath == null && (entity as EntityAgent)?.Alive != false)
                return;
            CountChickenDeath(api, entity);
        };

        serverChannel = api.Network.RegisterChannel(ChannelName)
            .RegisterMessageType<MsgUnlockSpell>()
            .RegisterMessageType<MsgSetHotbarSlot>()
            .RegisterMessageType<MsgCastSpell>()
            .RegisterMessageType<MsgChannelSpell>()
            .RegisterMessageType<MsgStartCast>()
            .RegisterMessageType<MsgSpellFx>()
            .RegisterMessageType<MsgFreezeMotion>()
            .RegisterMessageType<MsgLaunchPlayer>()
            .RegisterMessageType<MsgPlayAnimation>()
            .RegisterMessageType<MsgCancelCast>()
            .RegisterMessageType<MsgChickenKills>()
            .SetMessageHandler<MsgUnlockSpell>((player, msg) =>
            {
                var entity = player.Entity;
                if (entity == null) return;
                var data = PlayerSpellData.For(entity);
                SpellTree.TryUnlock(msg.SpellId, data);
            })
            .SetMessageHandler<MsgSetHotbarSlot>((player, msg) =>
            {
                var entity = player.Entity;
                if (entity == null) return;
                var data = PlayerSpellData.For(entity);
                data.SetHotbarSlot(msg.Slot, string.IsNullOrEmpty(msg.SpellId) ? null : msg.SpellId);
            })
            .SetMessageHandler<MsgChannelSpell>((player, msg) =>
            {
                var entity = player.Entity;
                if (entity == null) return;

                if (msg.SpellId != "air_wind_vortex") return;

                if (msg.IsActive) StartWindVortexChannel(api, player, entity, msg.SpellLevel);
                else StopWindVortexChannel(api, entity, collapse: true);
            })
            .SetMessageHandler<MsgCastSpell>((player, msg) =>
            {
                var entity = player.Entity;
                if (entity == null) return;

                var spell = SpellRegistry.Get(msg.SpellId);
                if (spell == null) return;

                var data = PlayerSpellData.For(entity);
                int spellLevel = data.GetSpellLevel(msg.SpellId);
                float scaledFluxCost = spell.FluxCost * spell.GetFluxCostMultiplier(spellLevel);

                float currentFlux = entity.WatchedAttributes.GetFloat("spellsandrunes:flux", 0f);
                api.Logger.Notification($"[SnR] Cast '{msg.SpellId}' lvl {spellLevel}: flux={currentFlux} cost={scaledFluxCost:F1}");
                if (currentFlux < scaledFluxCost)
                {
                    api.Logger.Notification($"[SnR] Flux check FAILED ({currentFlux} < {scaledFluxCost:F1})");
                    return;
                }
                entity.WatchedAttributes.SetFloat("spellsandrunes:flux", currentFlux - scaledFluxCost);
                entity.WatchedAttributes.MarkPathDirty("spellsandrunes:flux");

                // Notify casting client of cast start with scaled cast time
                float scaledCastTime = spell.CastTime * spell.GetCastTimeMultiplier(spellLevel);
                serverChannel?.SendPacket(new MsgStartCast { SpellId = msg.SpellId, CastTime = scaledCastTime }, player);

                // Broadcast animation to all clients
                if (spell.AnimationCode != null)
                {
                    var animMsg = new MsgPlayAnimation
                    {
                        EntityId       = entity.EntityId,
                        AnimationCode  = spell.AnimationCode,
                        UpperBodyOnly  = spell.AnimationUpperBodyOnly,
                        AnimationSpeed = spell.AnimationSpeed,
                    };
                    foreach (var p in api.World.AllOnlinePlayers)
                        serverChannel?.SendPacket(animMsg, p as IServerPlayer);
                }

                // Schedule execution after cast time completes (in milliseconds)
                int delayMs = (int)(scaledCastTime * 1000);
                if (pendingCasts.TryGetValue(entity.EntityId, out var pending))
                {
                    api.Event.UnregisterGameTickListener(pending.TaskId);
                    pendingCasts.Remove(entity.EntityId);
                }
                long taskId = 0;
                taskId = api.Event.RegisterGameTickListener(dt =>
                {
                    api.Event.UnregisterGameTickListener(taskId);

                    if (!entity.Alive)
                    {
                        pendingCasts.Remove(entity.EntityId);
                        return;
                    }

                    if (pendingCasts.TryGetValue(entity.EntityId, out var pendingNow) && pendingNow.TaskId != taskId)
                    {
                        return;
                    }
                    pendingCasts.Remove(entity.EntityId);

                    bool hit = spell.TryCast(entity, api.World, spellLevel);

                    // Broadcast FX packet with spell level for scaling
                    var fxMsg = new MsgSpellFx
                    {
                        SpellId     = msg.SpellId,
                        OriginX     = entity.SidedPos.X,
                        OriginY     = entity.SidedPos.Y,
                        OriginZ     = entity.SidedPos.Z,
                        LookDirX    = entity.SidedPos.GetViewVector().X,
                        LookDirY    = entity.SidedPos.GetViewVector().Y,
                        LookDirZ    = entity.SidedPos.GetViewVector().Z,
                        SpellLevel  = spellLevel,
                    };
                    foreach (var p in api.World.AllOnlinePlayers)
                        serverChannel?.SendPacket(fxMsg, p as IServerPlayer);

                    // Base XP always, hit bonus if spell connected
                    data.AddSpellXp(msg.SpellId, spell.XpPerCast + (hit ? spell.XpPerCast / 2 : 0));
                    data.AddElementXp(spell.Element, spell.ElementXpPerCast + (hit ? 2 : 0));

                    // Spell-specific packets to the casting player only
                    if (msg.SpellId == "air_feather_fall")
                        serverChannel?.SendPacket(new MsgFreezeMotion { NudgeY = 0.06f }, player);

                    if (msg.SpellId == "air_windy_dash")
                    {
                        var look = entity.SidedPos.GetViewVector();
                        serverChannel?.SendPacket(new MsgLaunchPlayer
                        {
                            UpForce = 0f,
                            ForwardForce = Spells.Air.WindyDash.ForwardForce * spell.GetRangeMultiplier(spellLevel),
                            LookDirX = look.X,
                            LookDirY = look.Y,
                            LookDirZ = look.Z,
                            UseLookY = true
                        }, player);
                    }

                    if (msg.SpellId == "air_updraft")
                    {
                        var look = entity.SidedPos.GetViewVector();
                        serverChannel?.SendPacket(new MsgLaunchPlayer
                        {
                            UpForce = Spells.Air.Updraft.UpForce,
                            ForwardForce = Spells.Air.Updraft.ForwardForce * spell.GetRangeMultiplier(spellLevel),
                            LookDirX = look.X,
                            LookDirY = 0f,
                            LookDirZ = look.Z,
                            UseLookY = false
                        }, player);
                    }

                    if (msg.SpellId is "air_wind_step" or "air_cloning_wind_step")
                    {
                        var look = entity.SidedPos.GetViewVector();
                        serverChannel?.SendPacket(new MsgLaunchPlayer
                        {
                            UpForce = Math.Max((float)entity.SidedPos.Motion.Y, 0.05f),
                            ForwardForce = Spells.Air.WindStep.ForwardForce * spell.GetRangeMultiplier(spellLevel),
                            LookDirX = look.X,
                            LookDirY = 0f,
                            LookDirZ = look.Z,
                            UseLookY = false
                        }, player);
                    }

                    if (msg.SpellId == "air_air_kick")
                    {
                        var look = entity.SidedPos.GetViewVector();
                        serverChannel?.SendPacket(new MsgLaunchPlayer
                        {
                            UpForce      = Spells.Air.AirKick.UpForce,
                            ForwardForce = Spells.Air.AirKick.ForwardForce,
                            LookDirX     = look.X,
                            LookDirY     = 0f,
                            LookDirZ     = look.Z,
                            UseLookY     = false
                        }, player);

                        // Play windball follow-up animation after a short delay (player in air)
                        api.Event.RegisterCallback(_ =>
                        {
                            if (!entity.Alive) return;
                            var windballAnim = new MsgPlayAnimation
                            {
                                EntityId      = entity.EntityId,
                                AnimationCode = "air_wind_kick_windball",
                                UpperBodyOnly = false,
                                AnimationSpeed = 1f,
                            };
                            foreach (var p in api.World.AllOnlinePlayers)
                                serverChannel?.SendPacket(windballAnim, p as IServerPlayer);
                        }, 300);

                        // Delay projectile until player reaches apex (~0.5s after launch)
                        Vec3d projLook = look.ToVec3d().Normalize();
                        Vec3d projPos  = null!;
                        float delay    = 0.5f;
                        float delayAcc = 0f;
                        bool  started  = false;

                        long delayId = 0;
                        delayId = api.Event.RegisterGameTickListener(ddt =>
                        {
                            delayAcc += ddt;
                            if (delayAcc < delay) return;
                            api.Event.UnregisterGameTickListener(delayId);
                            // capture look at launch time
                            projLook = entity.SidedPos.GetViewVector().ToVec3d().Normalize();
                            projPos = entity.SidedPos.XYZ.Add(0, 1.8, 0); // spawn at head level
                            started = true;
                        }, 50);

                        // Server-side projectile simulation (starts after delay)
                        float traveled = 0f;
                        hit      = false;
                        long  lid      = 0;
                        const float stepDt   = 0.02f;
                        float rangeMul = spell.GetRangeMultiplier(spellLevel);
                        float stepDist = Spells.Air.AirKick.ProjectileSpeed * stepDt * rangeMul;
                        float hitR     = Spells.Air.AirKick.ProjectileRadius * rangeMul;
                        float armingDist = 0f; // allow immediate hits

                        lid = api.Event.RegisterGameTickListener(dt =>
                        {
                            if (!started) return;
                            if (hit) { api.Event.UnregisterGameTickListener(lid); return; }

                            Vec3d prevPos = projPos;
                            projPos    = projPos.Add(projLook.X * stepDist, projLook.Y * stepDist, projLook.Z * stepDist);
                            traveled  += stepDist;

                            if (traveled > Spells.Air.AirKick.MaxRange * rangeMul)
                            {
                                api.Event.UnregisterGameTickListener(lid); return;
                            }

                            // Terrain collision
                            var block = api.World.BlockAccessor.GetBlock(new BlockPos((int)projPos.X, (int)projPos.Y, (int)projPos.Z, 0));
                            if (block != null && block.BlockId != 0 && !block.IsLiquid())
                            {
                                api.Event.UnregisterGameTickListener(lid); return;
                            }

                            // Broadcast projectile position FX each tick so clients see it moving
                            var trailFx = new MsgSpellFx
                            {
                                SpellId  = "air_air_kick_trail",
                                OriginX  = projPos.X, OriginY = projPos.Y, OriginZ = projPos.Z,
                                LookDirX = (float)projLook.X, LookDirY = (float)projLook.Y, LookDirZ = (float)projLook.Z,
                            };
                            foreach (var p in api.World.AllOnlinePlayers)
                                serverChannel?.SendPacket(trailFx, p as IServerPlayer);

                            var hitboxFx = new MsgSpellFx
                            {
                                SpellId = "air_air_kick_hitbox",
                                OriginX = projPos.X,
                                OriginY = projPos.Y,
                                OriginZ = projPos.Z,
                                LookDirX = (float)projLook.X,
                                LookDirY = (float)projLook.Y,
                                LookDirZ = (float)projLook.Z
                            };
                            foreach (var p in api.World.AllOnlinePlayers)
                                serverChannel?.SendPacket(hitboxFx, p as IServerPlayer);
                            // segment hit test (same idea as slash sweep)
                            api.World.GetEntitiesAround(projPos, hitR + 0.5f, hitR + 0.5f, e =>
                            {
                                if (hit) return false;
                                if (traveled < armingDist) return false;
                                if (e.EntityId == entity.EntityId) return false;
                                if (e is not EntityAgent) return false;
                                Vec3d targetPos = e.SidedPos.XYZ.Add(0, e.LocalEyePos.Y * 0.5, 0);
                                // quick sphere hit (fallback)
                                if (targetPos.DistanceTo(projPos) <= hitR || targetPos.DistanceTo(prevPos) <= hitR)
                                {
                                    hit = true;
                                    api.Event.UnregisterGameTickListener(lid);

                                    e.ReceiveDamage(new DamageSource
                                    {
                                        Source       = EnumDamageSource.Entity,
                                        SourceEntity = entity,
                                        Type         = EnumDamageType.BluntAttack,
                                    }, Spells.Air.AirKick.ImpactDamage);

                                    var impFxx = new MsgSpellFx
                                    {
                                        SpellId  = "air_air_kick",
                                        OriginX  = projPos.X, OriginY = projPos.Y, OriginZ = projPos.Z,
                                        LookDirX = (float)projLook.X, LookDirY = (float)projLook.Y, LookDirZ = (float)projLook.Z,
                                    };
                                    foreach (var p in api.World.AllOnlinePlayers)
                                        serverChannel?.SendPacket(impFxx, p as IServerPlayer);

                                    return false;
                                }

                                // closest point on segment
                                Vec3d toTarget = targetPos - prevPos;
                                Vec3d seg = projPos - prevPos;
                                double segLenSq = seg.LengthSq();
                                if (segLenSq < 0.0001) return false;
                                double t = toTarget.Dot(seg) / segLenSq;
                                if (t < 0 || t > 1) return false;
                                Vec3d closest = prevPos + seg * t;
                                if (targetPos.DistanceTo(closest) > hitR) return false;
                                hit = true;
                                api.Event.UnregisterGameTickListener(lid);

                                e.ReceiveDamage(new DamageSource
                                {
                                    Source       = EnumDamageSource.Entity,
                                    SourceEntity = entity,
                                    Type         = EnumDamageType.BluntAttack,
                                }, Spells.Air.AirKick.ImpactDamage);

                                var impFx = new MsgSpellFx
                                {
                                    SpellId  = "air_air_kick",
                                    OriginX  = projPos.X, OriginY = projPos.Y, OriginZ = projPos.Z,
                                    LookDirX = (float)projLook.X, LookDirY = (float)projLook.Y, LookDirZ = (float)projLook.Z,
                                };
                                foreach (var p in api.World.AllOnlinePlayers)
                                    serverChannel?.SendPacket(impFx, p as IServerPlayer);

                                return false;
                            });
                        }, (int)(stepDt * 1000));
                    }

                    // Broadcast FX to all nearby clients
                    var origin  = GetSpellFxOrigin(entity, msg.SpellId);
                    var lookDir = entity.SidedPos.GetViewVector();
                    var fx = new MsgSpellFx
                    {
                        SpellId  = msg.SpellId,
                        OriginX  = origin.X, OriginY = origin.Y, OriginZ = origin.Z,
                        LookDirX = lookDir.X, LookDirY = lookDir.Y, LookDirZ = lookDir.Z,
                    };
                    foreach (var p in api.World.AllOnlinePlayers)
                        serverChannel?.SendPacket(fx, p as IServerPlayer);
                }, delayMs);

                pendingCasts[entity.EntityId] = new PendingCast
                {
                    SpellId = msg.SpellId,
                    TaskId = taskId
                };
            })
            .SetMessageHandler<MsgCancelCast>((player, msg) =>
            {
                var entity = player.Entity;
                if (entity == null) return;
                if (!pendingCasts.TryGetValue(entity.EntityId, out var pending)) return;
                if (!string.IsNullOrEmpty(msg.SpellId) && pending.SpellId != msg.SpellId) return;

                api.Event.UnregisterGameTickListener(pending.TaskId);
                pendingCasts.Remove(entity.EntityId);

                var cancel = new MsgCancelCast
                {
                    EntityId = entity.EntityId,
                    SpellId = pending.SpellId
                };
                foreach (var p in api.World.AllOnlinePlayers)
                    serverChannel?.SendPacket(cancel, p as IServerPlayer);
            });

        api.Event.PlayerJoin += player =>
        {
            if (player is IServerPlayer sp)
                SendChickenKills(sp);
        };

        api.Event.RegisterGameTickListener(dt => TickChannelSpells(api, dt), 100);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Logger.Notification("[Spells & Runes] Client side loaded.");

        clientChannel = api.Network.RegisterChannel(ChannelName)
            .RegisterMessageType<MsgUnlockSpell>()
            .RegisterMessageType<MsgSetHotbarSlot>()
            .RegisterMessageType<MsgCastSpell>()
            .RegisterMessageType<MsgChannelSpell>()
            .RegisterMessageType<MsgStartCast>()
            .RegisterMessageType<MsgSpellFx>()
            .RegisterMessageType<MsgFreezeMotion>()
            .RegisterMessageType<MsgLaunchPlayer>()
            .RegisterMessageType<MsgPlayAnimation>()
            .RegisterMessageType<MsgCancelCast>()
            .RegisterMessageType<MsgChickenKills>()
            .SetMessageHandler<MsgPlayAnimation>(msg =>
            {
                var entity = api.World.GetEntityById(msg.EntityId) as EntityAgent;
                if (entity == null) return;
                SpellAnimations.Play(entity, msg.AnimationCode, msg.UpperBodyOnly, msg.AnimationSpeed);
            })
            .SetMessageHandler<MsgCancelCast>(msg =>
            {
                var entity = api.World.GetEntityById(msg.EntityId) as EntityAgent;
                if (entity != null)
                {
                    var spell = SpellRegistry.Get(msg.SpellId);
                    if (!string.IsNullOrEmpty(spell?.AnimationCode))
                        SpellAnimations.Stop(entity, spell.AnimationCode!);
                }

                if (api.World.Player?.Entity?.EntityId == msg.EntityId)
                {
                    castBar?.Cancel();
                    lmbHoldActive = false;
                    lmbHoldSpellId = null;
                    activeCastSpellId = null;
                }
            })
            .SetMessageHandler<MsgChickenKills>(msg =>
            {
                chickenKills = msg.Count;
                hudChicken?.SetCount(chickenKills);
            })
            .SetMessageHandler<MsgFreezeMotion>(msg =>
            {
                var entity = api.World.Player?.Entity;
                if (entity == null) return;
                entity.Pos.Motion.Set(0, msg.NudgeY, 0);
            })
            .SetMessageHandler<MsgLaunchPlayer>(msg =>
            {
                var entity = api.World.Player?.Entity;
                if (entity == null) return;
                if (msg.UseLookY)
                {
                    var look = new Vec3d(msg.LookDirX, msg.LookDirY, msg.LookDirZ);
                    if (look.LengthSq() < 0.0001) look = new Vec3d(0, 0, 1);
                    look = look.Normalize();
                    var motion = new Vec3d(
                        look.X * msg.ForwardForce,
                        look.Y * msg.ForwardForce,
                        look.Z * msg.ForwardForce);
                    motion.Y += msg.UpForce;
                    entity.Pos.Motion.Set(motion);
                }
                else
                {
                    // Up burst first, then forward dash (horizontal)
                    entity.Pos.Motion.Set(
                        msg.LookDirX * msg.ForwardForce,
                        msg.UpForce,
                        msg.LookDirZ * msg.ForwardForce);
                }
            })
            .SetMessageHandler<MsgSpellFx>(msg =>
            {
                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (msg.SpellId == "air_wind_slash")
                {
                    if (recentSpellReceipts.TryGetValue(msg.SpellId, out long lastSpellMs) && nowMs - lastSpellMs < 150)
                    {
                        return;
                    }
                    recentSpellReceipts[msg.SpellId] = nowMs;
                }
                string fxKey = $"{msg.SpellId}|{msg.SpellLevel}|{msg.OriginX:F3}|{msg.OriginY:F3}|{msg.OriginZ:F3}|{msg.LookDirX:F3}|{msg.LookDirY:F3}|{msg.LookDirZ:F3}";
                if (recentFxReceipts.TryGetValue(fxKey, out long lastMs) && nowMs - lastMs < 120)
                {
                    return;
                }

                recentFxReceipts[fxKey] = nowMs;
                if (recentFxReceipts.Count > 64)
                {
                    string? staleKey = null;
                    foreach (var pair in recentFxReceipts)
                    {
                        if (nowMs - pair.Value > 1000)
                        {
                            staleKey = pair.Key;
                            break;
                        }
                    }
                    if (staleKey != null) recentFxReceipts.Remove(staleKey);
                }

                var origin  = new Vec3d(msg.OriginX, msg.OriginY, msg.OriginZ);
                var lookDir = new Vec3d(msg.LookDirX, msg.LookDirY, msg.LookDirZ).Normalize();
                switch (msg.SpellId)
                {
                    case "air_air_push":
                        Spells.Air.AirPush.SpawnWindParticles(api.World, origin, lookDir, msg.SpellLevel);
                        break;
                    case "air_windy_dash":
                        Spells.Air.WindyDash.SpawnFx(api.World, origin, lookDir, msg.SpellLevel);
                        break;
                    case "air_updraft":
                        Spells.Air.Updraft.SpawnFx(api.World, origin, lookDir, msg.SpellLevel);
                        break;
                    case "air_wind_slash":
                        Spells.Air.WindSlash.SpawnFx(api.World, origin, lookDir, msg.SpellLevel, 0.0);
                        break;
                    case "air_wind_clone":
                        Spells.Air.WindClone.SpawnFx(api.World, origin, msg.SpellLevel);
                        break;
                    case "air_storms_eye":
                        Spells.Air.StormsEye.SpawnFx(api.World, origin, msg.SpellLevel);
                        break;
                    case "air_tornado":
                        Spells.Air.Tornado.SpawnFx(api.World, origin, msg.SpellLevel);
                        break;
                    case "air_wind_vortex":
                        Spells.Air.WindVortex.SpawnFx(api.World, origin, lookDir, msg.SpellLevel);
                        break;
                    case "air_wind_spear":
                        break;
                    case "air_feather_fall":
                        Spells.Air.FeatherFall.SpawnFx(api.World, origin);
                        break;
                    case "air_air_kick":
                        Spells.Air.AirKick.SpawnImpactFx(api.World, origin, lookDir);
                        break;
                    case "air_air_kick_trail":
                        Spells.Air.AirKick.SpawnTrailFx(api.World, origin, lookDir);
                        break;
                    case "air_air_kick_hitbox":
                        if (DebugHitboxesEnabled)
                            Spells.Air.AirKick.SpawnHitboxDebug(api.World, origin, Spells.Air.AirKick.ProjectileRadius);
                        break;
                    case "fire_spark":
                        Spells.Fire.Spark.SpawnFx(api.World, origin, lookDir, msg.SpellLevel);
                        sparkGlow?.AddSparkBurst(origin, lookDir, Spells.Fire.Spark.Range, Spells.Fire.Spark.ConeAngleDeg, 60 * (1 + (msg.SpellLevel - 1) / 4));
                        break;
                }
            });

        // Client-side cast start notification
        clientChannel!.SetMessageHandler<MsgStartCast>(msg =>
        {
            castBar?.OnBeginCast(msg.SpellId, msg.CastTime);
            activeCastSpellId = msg.SpellId;
            var spell = SpellRegistry.Get(msg.SpellId);
            api.ShowChatMessage($"[SnR] Starting cast: {spell?.Name ?? msg.SpellId} ({msg.CastTime:F2}s)");
        });

        // Spellbook hotkey
        api.Input.RegisterHotKey("spellsandrunes.spellbook", "Open Spellbook", GlKeys.K, HotkeyType.GUIOrOtherControls);

        // Radial menu hotkey (hold R)
        api.Input.RegisterHotKey("spellsandrunes.radial", "Spell Radial Menu", GlKeys.R, HotkeyType.GUIOrOtherControls);


        radialMenu      = new HudRadialMenu(api);
        hudFlux         = new HudFlux(api, radialMenu);
        castBar         = new HudCastBar(api);
        hudChicken      = new HudChickenCounter(api);
        spellbookDialog = new GuiDialogSpellbook(api, clientChannel!);
        coneRenderer = new SpellConeRenderer(api, radialMenu);
        sparkGlow    = new SparkGlowRenderer(api);
        api.Event.RegisterRenderer(sparkGlow, EnumRenderStage.AfterOIT, "sparkglow");

        api.Event.RegisterGameTickListener(_ => coneRenderer.OnGameTick(_), 50);

        // /snr debug — toggle all hitbox visualizations together
        api.Input.RegisterHotKey("spellsandrunes.debug", "Toggle Spell Debugs", GlKeys.F8, HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler("spellsandrunes.debug", _ =>
        {
            DebugHitboxesEnabled = !DebugHitboxesEnabled;
            coneRenderer.Enabled = DebugHitboxesEnabled;
            Spells.Air.WindSlash.DebugHitboxEnabled = DebugHitboxesEnabled;
            api.ShowChatMessage($"[SnR] Debug hitboxes: {(DebugHitboxesEnabled ? "ON" : "OFF")}");
            return true;
        });

        // Force spellbook redraw when WatchedAttributes arrive from server (spell data sync)
        api.Event.PlayerEntitySpawn += (player) =>
        {
            if (player != api.World.Player) return;
            player.Entity.WatchedAttributes.RegisterModifiedListener("snr:unlocked", () =>
                spellbookDialog?.ReloadData());
            player.Entity.WatchedAttributes.RegisterModifiedListener("snr:hotbar", () =>
                spellbookDialog?.ReloadData());
            player.Entity.WatchedAttributes.RegisterModifiedListener("snr:activators", () =>
                spellbookDialog?.ReloadData());
        };

        // Spellbook toggle
        api.Input.SetHotKeyHandler("spellsandrunes.spellbook", combo =>
        {
            var entity = api.World.Player?.Entity;
            if (entity == null || !PlayerSpellData.For(entity).IsFluxUnlocked) return true;
            if (spellbookDialog.IsOpened()) spellbookDialog.TryClose();
            else spellbookDialog.TryOpen();
            return true;
        });

        // Radial: open on press — also starts sprint-forward tick
        long sprintTickId = -1;
        var sprintHk = api.Input.GetHotKeyByCode("sprint");
        int sprintKey = sprintHk != null ? (int)sprintHk.CurrentMapping.KeyCode : (int)GlKeys.LShift;

        api.Input.SetHotKeyHandler("spellsandrunes.radial", combo =>
        {
            var entity = api.World.Player?.Entity;
            if (entity == null || !PlayerSpellData.For(entity).IsFluxUnlocked) return true;
            radialMenu.Open();
            sprintTickId = api.Event.RegisterGameTickListener(_ =>
            {
                var player = api.World.Player;
                if (player?.Entity == null) return;
                if (api.Input.KeyboardKeyState[sprintKey])
                    player.Entity.Controls.Sprint = true;
            }, 16);
            return true;
        });

        // Track mouse for radial hover
        api.Event.MouseMove += (MouseEvent e) =>
        {
            if (radialMenu.IsOpen)
                radialMenu.UpdateMouse(e.X, e.Y);
        };

        // R release — close radial and stop sprint tick
        api.Event.KeyUp += (KeyEvent e) =>
        {
            if (e.KeyCode == (int)GlKeys.R && radialMenu.IsOpen)
            {
                radialMenu.Close();
                if (sprintTickId >= 0) { api.Event.UnregisterGameTickListener(sprintTickId); sprintTickId = -1; }
            }
        };

        // RMB cancels cast
        api.Event.MouseDown += (MouseEvent e) =>
        {
            if (e.Button != EnumMouseButton.Right) return;
            if (spellbookDialog.IsOpened() || radialMenu.IsOpen) return;

            string? spellId = radialMenu.GetSelectedSpellId();
            if (spellId == "air_wind_vortex")
            {
                if (windVortexHeld) return;

                var player = api.World.Player;
                if (player?.Entity == null) return;

                var data = PlayerSpellData.For(player.Entity);
                int spellLevel = data.GetSpellLevel(spellId);
                clientChannel!.SendPacket(new MsgChannelSpell
                {
                    SpellId = spellId,
                    IsActive = true,
                    SpellLevel = spellLevel
                });
                windVortexHeld = true;
                e.Handled = true;
                return;
            }

            if (castBar!.IsCasting)
                castBar.Cancel();

            var playerCancel = api.World.Player;
            if (playerCancel?.Entity != null)
            {
                string? cancelSpellId = lmbHoldSpellId ?? activeCastSpellId;
                if (!string.IsNullOrEmpty(cancelSpellId))
                {
                    clientChannel!.SendPacket(new MsgCancelCast
                    {
                        EntityId = playerCancel.Entity.EntityId,
                        SpellId = cancelSpellId
                    });
                }
            }
        };

        // LMB — cast selected spell (radial is a separate dialog that handles its own clicks)
        api.Event.MouseDown += (MouseEvent e) =>
        {
            if (e.Button != EnumMouseButton.Left) return;
            if (spellbookDialog.IsOpened()) return;
            if (radialMenu.IsOpen) return; // radial handles its own LMB via OnMouseDown

            string? spellId = radialMenu.GetSelectedSpellId();
            if (spellId == null) return;

            e.Handled = true;

            if (castBar!.IsCasting) return;

            var spell = SpellRegistry.Get(spellId);
            if (spell == null) return;
            if (spell.Id == "air_wind_vortex") return;

            var player = api.World.Player;
            if (player?.Entity == null) return;
            var data = PlayerSpellData.For(player.Entity);
            int spellLevel = data.GetSpellLevel(spellId);
            float scaledCastTime = spell.CastTime * spell.GetCastTimeMultiplier(spellLevel);
            float scaledFluxCost = spell.FluxCost * spell.GetFluxCostMultiplier(spellLevel);

            // Show cast bar with scaled time (via MsgStartCast from server)
            // Don't call BeginCast here — wait for server confirmation
            api.ShowChatMessage($"[SnR] Sending cast request: {spell.Name} (lvl {spellLevel}, flux {scaledFluxCost:F1})");
            clientChannel!.SendPacket(new MsgCastSpell { SpellId = spellId, SpellLevel = spellLevel });
            lmbHoldActive = true;
            lmbHoldSpellId = spellId;
        };

        api.Event.MouseUp += (MouseEvent e) =>
        {
            if (e.Button == EnumMouseButton.Left && lmbHoldActive)
            {
                var playerCancel = api.World.Player;
                if (playerCancel?.Entity != null && !string.IsNullOrEmpty(lmbHoldSpellId))
                {
                    clientChannel!.SendPacket(new MsgCancelCast
                    {
                        EntityId = playerCancel.Entity.EntityId,
                        SpellId = lmbHoldSpellId!
                    });
                }
                castBar?.Cancel();
                lmbHoldActive = false;
                lmbHoldSpellId = null;
            }

            if (e.Button != EnumMouseButton.Right) return;
            if (!windVortexHeld) return;

            clientChannel!.SendPacket(new MsgChannelSpell
            {
                SpellId = "air_wind_vortex",
                IsActive = false
            });
            windVortexHeld = false;
            e.Handled = true;
        };
    }

    public override void Dispose()
    {
        hudFlux?.Dispose();
        castBar?.Dispose();
        radialMenu?.Dispose();
        hudChicken?.Dispose();
        spellbookDialog?.Dispose();
        sparkGlow?.Dispose();
        SylphGlow?.Dispose();
        base.Dispose();
    }

    private void StartWindVortexChannel(ICoreServerAPI api, IServerPlayer player, Entity entity, int spellLevel)
    {
        var spell = SpellRegistry.Get("air_wind_vortex");
        if (spell is not Spells.Air.WindVortex || !PlayerSpellData.For(entity).IsFluxUnlocked) return;

        if (activeChannelSpells.ContainsKey(entity.EntityId)) return;

        activeChannelSpells[entity.EntityId] = new ActiveChannelSpell
        {
            SpellId = spell.Id,
            SpellLevel = Math.Max(1, spellLevel),
            DrainTimer = 0f,
        };

        BroadcastSpellFx(api, entity, spell.Id, spellLevel);
    }

    private void StopWindVortexChannel(ICoreServerAPI api, Entity entity, bool collapse)
    {
        if (!activeChannelSpells.Remove(entity.EntityId, out var state)) return;
        if (state.SpellId != "air_wind_vortex") return;
        if (collapse) ApplyWindVortexCollapse(api.World, entity, state.SpellLevel);
    }

    private void TickChannelSpells(ICoreServerAPI api, float deltaTime)
    {
        if (activeChannelSpells.Count == 0) return;

        var ended = new List<long>();
        foreach (var pair in activeChannelSpells)
        {
            var entity = api.World.GetEntityById(pair.Key);
            if (entity is not EntityAgent agent || !agent.Alive)
            {
                ended.Add(pair.Key);
                continue;
            }

            if (pair.Value.SpellId != "air_wind_vortex") continue;

            ApplyWindVortexAura(api.World, agent, pair.Value.SpellLevel);
            BroadcastSpellFx(api, agent, pair.Value.SpellId, pair.Value.SpellLevel);

            pair.Value.DrainTimer += deltaTime;
            if (pair.Value.DrainTimer < 1f) continue;
            pair.Value.DrainTimer -= 1f;

            var spell = SpellRegistry.Get(pair.Value.SpellId);
            if (spell == null)
            {
                ended.Add(pair.Key);
                continue;
            }

            float sustainCost = spell.FluxCost * spell.GetFluxCostMultiplier(pair.Value.SpellLevel) * 0.25f;
            float currentFlux = agent.WatchedAttributes.GetFloat("spellsandrunes:flux", 0f);
            if (currentFlux < sustainCost)
            {
                ended.Add(pair.Key);
                continue;
            }

            agent.WatchedAttributes.SetFloat("spellsandrunes:flux", currentFlux - sustainCost);
            agent.WatchedAttributes.MarkPathDirty("spellsandrunes:flux");
        }

        foreach (long entityId in ended)
        {
            var entity = api.World.GetEntityById(entityId);
            if (entity != null) StopWindVortexChannel(api, entity, collapse: true);
            else activeChannelSpells.Remove(entityId);
        }
    }

    private void ApplyWindVortexAura(IWorldAccessor world, EntityAgent caster, int spellLevel)
    {
        var center = caster.SidedPos.XYZ.Add(0, 0.8, 0);
        var spell = SpellRegistry.Get("air_wind_vortex");
        if(spell is not Spells.Air.WindVortex) return;
        float radius = Spells.Air.WindVortex.AuraRadius * spell.GetRangeMultiplier(spellLevel);

        world.GetEntitiesAround(center, radius, radius, e =>
        {
            if (e.EntityId == caster.EntityId || e is not EntityAgent) return false;
            Vec3d away = e.SidedPos.XYZ - center;
            double dist = Math.Max(0.15, away.Length());
            away = away.Normalize();
            e.SidedPos.Motion.Add(away.X * (0.18 / dist), 0.04, away.Z * (0.18 / dist));
            return false;
        });
    }

    private void ApplyWindVortexCollapse(IWorldAccessor world, Entity entity, int spellLevel)
    {
        var center = entity.SidedPos.XYZ.Add(0, 0.8, 0);
        var spell = SpellRegistry.Get("air_wind_vortex");
                if(spell is not Spells.Air.WindVortex) return;
        float radius = Spells.Air.WindVortex.AuraRadius * spell.GetRangeMultiplier(spellLevel);

        world.GetEntitiesAround(center, radius + 1.2f, radius + 1.2f, e =>
        {
            if (e.EntityId == entity.EntityId || e is not EntityAgent) return false;
            Vec3d away = (e.SidedPos.XYZ - center).Normalize();
            e.SidedPos.Motion.Add(away.X *1.5, 1, away.Z * 1.5);
            return false;
        });

        serverChannel?.BroadcastPacket(new MsgSpellFx
        {
            SpellId = "air_wind_vortex",
            OriginX = center.X,
            OriginY = center.Y,
            OriginZ = center.Z,     
            LookDirX = 0,
            LookDirY = 1,
            LookDirZ = 0,
            SpellLevel = spellLevel,
        });
    }

    private void BroadcastSpellFx(ICoreServerAPI api, Entity entity, string spellId, int spellLevel)
    {
        var lookDir = entity.SidedPos.GetViewVector();
        var origin = GetSpellFxOrigin(entity, spellId);
        var fx = new MsgSpellFx
        {
            SpellId = spellId,
            OriginX = origin.X,
            OriginY = origin.Y,
            OriginZ = origin.Z,
            LookDirX = lookDir.X,
            LookDirY = lookDir.Y,
            LookDirZ = lookDir.Z,
            SpellLevel = spellLevel
        };

        foreach (var p in api.World.AllOnlinePlayers)
            serverChannel?.SendPacket(fx, p as IServerPlayer);
    }

    private Vec3d GetSpellFxOrigin(Entity entity, string spellId)
    {
        if (entity is not EntityAgent agent)
            return entity.SidedPos.XYZ.Add(0, 0.5, 0);

        return spellId switch
        {
            "air_feather_fall" => entity.SidedPos.XYZ.Add(0, 0.1, 0),
            "air_storms_eye" => Spells.Air.StormsEye.GetCenter(agent),
            "air_spear_in_an_eye" => Spells.Air.SpearInAnEye.GetCenter(agent),
            "air_tornado" => agent.SidedPos.XYZ.Add(agent.SidedPos.GetViewVector().ToVec3d().Normalize() * 9).Add(0, 0.5, 0),
            "air_wind_vortex" => entity.SidedPos.XYZ.Add(0, 0.8, 0),
            _ => entity.SidedPos.XYZ.Add(0, 0.5, 0),
        };
    }

    private void InitChickenKillCounter(ICoreServerAPI api)
    {
        if (chickenKillsLoaded) return;
        chickenKillsLoaded = true;

        string basePath = api.GetOrCreateDataPath("spellsandrunes");
        chickenKillPath = Path.Combine(basePath, ChickenKillFileName);

        if (File.Exists(chickenKillPath))
        {
            try
            {
                var json = File.ReadAllText(chickenKillPath);
                var data = JsonSerializer.Deserialize<ChickenKillStats>(json);
                chickenKills = data?.ChickenKills ?? 0;
            }
            catch
            {
                chickenKills = 0;
            }
        }
    }

    private void CountChickenDeath(ICoreServerAPI api, Entity entity)
    {
        if (entity is not EntityAgent agent) return;
        var code = agent.Code?.Path ?? "";
        if (!code.Contains("chicken", StringComparison.OrdinalIgnoreCase)) return;

        if (!chickenKillsLoaded) InitChickenKillCounter(api);
        chickenKills++;
        SaveChickenKills();
        BroadcastChickenKills(api);
    }

    private void SaveChickenKills()
    {
        if (string.IsNullOrEmpty(chickenKillPath)) return;
        var data = new ChickenKillStats
        {
            ChickenKills = chickenKills,
            UpdatedUtc = DateTime.UtcNow.ToString("o")
        };
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(chickenKillPath, json);
    }

    private void BroadcastChickenKills(ICoreServerAPI api)
    {
        var msg = new MsgChickenKills { Count = chickenKills };
        foreach (var p in api.World.AllOnlinePlayers)
            serverChannel?.SendPacket(msg, p as IServerPlayer);
    }

    private void SendChickenKills(IServerPlayer player)
    {
        var msg = new MsgChickenKills { Count = chickenKills };
        serverChannel?.SendPacket(msg, player);
    }

    private sealed class ChickenKillStats
    {
        public int ChickenKills { get; set; }
        public string? UpdatedUtc { get; set; }
    }
}
