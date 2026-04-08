using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;
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
    private HudFlux? hudFlux;
    private HudCastBar? castBar;
    private HudRadialMenu? radialMenu;
    private GuiDialogSpellbook? spellbookDialog;
    private SpellConeRenderer?  coneRenderer;
    private SparkGlowRenderer? sparkGlow;

    private IClientNetworkChannel?  clientChannel;
    private IServerNetworkChannel? serverChannel;

    private const string ChannelName = "spellsandrunes";

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass("fluxBehavior", typeof(EntityBehaviorFlux));
        SpellRegistry.RegisterAll();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Logger.Notification("[Spells & Runes] Server side loaded.");
        DebugCommands.Register(api);

        serverChannel = api.Network.RegisterChannel(ChannelName)
            .RegisterMessageType<MsgUnlockSpell>()
            .RegisterMessageType<MsgSetHotbarSlot>()
            .RegisterMessageType<MsgCastSpell>()
            .RegisterMessageType<MsgSpellFx>()
            .RegisterMessageType<MsgFreezeMotion>()
            .RegisterMessageType<MsgLaunchPlayer>()
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
            .SetMessageHandler<MsgCastSpell>((player, msg) =>
            {
                var entity = player.Entity;
                if (entity == null) return;

                var spell = SpellRegistry.Get(msg.SpellId);
                if (spell == null) return;

                float currentFlux = entity.WatchedAttributes.GetFloat("spellsandrunes:flux", 0f);
                api.Logger.Notification($"[SnR] Cast '{msg.SpellId}': flux={currentFlux} cost={spell.FluxCost}");
                if (currentFlux < spell.FluxCost)
                {
                    api.Logger.Notification($"[SnR] Flux check FAILED ({currentFlux} < {spell.FluxCost})");
                    return;
                }
                entity.WatchedAttributes.SetFloat("spellsandrunes:flux", currentFlux - spell.FluxCost);
                entity.WatchedAttributes.MarkPathDirty("spellsandrunes:flux");

                var data = PlayerSpellData.For(entity);
                int spellLevel = data.GetSpellLevel(msg.SpellId);
                bool hit = spell.TryCast(entity, api.World, spellLevel);

                // Base XP always, hit bonus if spell connected
                data.AddSpellXp(msg.SpellId, spell.XpPerCast + (hit ? spell.XpPerCast / 2 : 0));
                data.AddElementXp(spell.Element, spell.ElementXpPerCast + (hit ? 2 : 0));

                // Spell-specific packets to the casting player only
                if (msg.SpellId == "air_feather_fall")
                    serverChannel?.SendPacket(new MsgFreezeMotion { NudgeY = 0.06f }, player);

                if (msg.SpellId == "air_air_kick")
                {
                    var look = entity.SidedPos.GetViewVector();
                    serverChannel?.SendPacket(new MsgLaunchPlayer
                    {
                        UpForce      = Spells.Air.AirKick.UpForce,
                        ForwardForce = Spells.Air.AirKick.ForwardForce,
                        LookDirX     = look.X,
                        LookDirZ     = look.Z,
                    }, player);

                    // Delay projectile until player reaches apex (~0.5s after launch)
                    var projLook   = look.ToVec3d().Normalize(); // capture look dir at cast time
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
                        projPos = entity.SidedPos.XYZ.Add(0, 1.8, 0); // spawn at head level
                        started = true;
                    }, 50);

                    // Server-side projectile simulation (starts after delay)
                    float traveled = 0f;
                    bool  hit      = false;
                    long  lid      = 0;
                    const float stepDt   = 0.05f;
                    float stepDist = Spells.Air.AirKick.ProjectileSpeed * stepDt;
                    float hitR     = Spells.Air.AirKick.ProjectileRadius;
                    float armingDist = 3f; // must travel 3 blocks before it can hit anything

                    lid = api.Event.RegisterGameTickListener(dt =>
                    {
                        if (!started) return;
                        if (hit) { api.Event.UnregisterGameTickListener(lid); return; }

                        projPos    = projPos.Add(projLook.X * stepDist, projLook.Y * stepDist, projLook.Z * stepDist);
                        traveled  += stepDist;

                        if (traveled > Spells.Air.AirKick.MaxRange)
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

                        api.World.GetEntitiesAround(projPos, hitR, hitR, e =>
                        {
                            if (hit) return false;
                            if (traveled < armingDist) return false;
                            if (e.EntityId == entity.EntityId) return false;
                            if (e is not EntityAgent) return false;
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
                double originY = msg.SpellId == "air_feather_fall" ? 0.1 : 0.5;
                var origin  = entity.SidedPos.XYZ.Add(0, originY, 0);
                var lookDir = entity.SidedPos.GetViewVector();
                var fx = new MsgSpellFx
                {
                    SpellId  = msg.SpellId,
                    OriginX  = origin.X, OriginY = origin.Y, OriginZ = origin.Z,
                    LookDirX = lookDir.X, LookDirY = lookDir.Y, LookDirZ = lookDir.Z,
                };
                foreach (var p in api.World.AllOnlinePlayers)
                    serverChannel?.SendPacket(fx, p as IServerPlayer);
            });
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Logger.Notification("[Spells & Runes] Client side loaded.");

        clientChannel = api.Network.RegisterChannel(ChannelName)
            .RegisterMessageType<MsgUnlockSpell>()
            .RegisterMessageType<MsgSetHotbarSlot>()
            .RegisterMessageType<MsgCastSpell>()
            .RegisterMessageType<MsgSpellFx>()
            .RegisterMessageType<MsgFreezeMotion>()
            .RegisterMessageType<MsgLaunchPlayer>()
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
                // Up burst first, then forward dash
                entity.Pos.Motion.Set(
                    msg.LookDirX * msg.ForwardForce,
                    msg.UpForce,
                    msg.LookDirZ * msg.ForwardForce);
            })
            .SetMessageHandler<MsgSpellFx>(msg =>
            {
                var origin  = new Vec3d(msg.OriginX, msg.OriginY, msg.OriginZ);
                var lookDir = new Vec3d(msg.LookDirX, msg.LookDirY, msg.LookDirZ).Normalize();
                switch (msg.SpellId)
                {
                    case "air_air_push":
                        Spells.Air.AirPush.SpawnWindParticles(api.World, origin, lookDir);
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
                    case "fire_spark":
                        Spells.Fire.Spark.SpawnFx(api.World, origin, lookDir);
                        sparkGlow?.AddSparkBurst(origin, lookDir, Spells.Fire.Spark.Range, Spells.Fire.Spark.ConeAngleDeg);
                        break;
                }
            });

        // Spellbook hotkey
        api.Input.RegisterHotKey("spellsandrunes.spellbook", "Open Spellbook", GlKeys.K, HotkeyType.GUIOrOtherControls);

        // Radial menu hotkey (hold R)
        api.Input.RegisterHotKey("spellsandrunes.radial", "Spell Radial Menu", GlKeys.R, HotkeyType.GUIOrOtherControls);

        hudFlux         = new HudFlux(api);
        castBar         = new HudCastBar(api);
        radialMenu      = new HudRadialMenu(api);
        spellbookDialog = new GuiDialogSpellbook(api, clientChannel!);
        coneRenderer = new SpellConeRenderer(api, radialMenu);
        sparkGlow    = new SparkGlowRenderer(api);
        api.Event.RegisterRenderer(sparkGlow, EnumRenderStage.AfterOIT, "sparkglow");

        api.Event.RegisterGameTickListener(_ => coneRenderer.OnGameTick(_), 50);

        // /snr debug — toggle cone preview
        api.Input.RegisterHotKey("spellsandrunes.debug", "Toggle Spell Debug Cone", GlKeys.F8, HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler("spellsandrunes.debug", _ =>
        {
            coneRenderer.Enabled = !coneRenderer.Enabled;
            api.ShowChatMessage($"[SnR] Cone debug: {(coneRenderer.Enabled ? "ON" : "OFF")}");
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
        };

        // Spellbook toggle
        api.Input.SetHotKeyHandler("spellsandrunes.spellbook", combo =>
        {
            if (spellbookDialog.IsOpened()) spellbookDialog.TryClose();
            else spellbookDialog.TryOpen();
            return true;
        });

        // Radial: open on press
        api.Input.SetHotKeyHandler("spellsandrunes.radial", combo =>
        {
            radialMenu.Open();
            return true;
        });

        // Track mouse for radial hover
        api.Event.MouseMove += (MouseEvent e) =>
        {
            if (radialMenu.IsOpen)
                radialMenu.UpdateMouse(e.X, e.Y);
        };

        // R release — close radial
        api.Event.KeyUp += (KeyEvent e) =>
        {
            if (e.KeyCode == (int)GlKeys.R && radialMenu.IsOpen)
                radialMenu.Close();
        };

        // RMB cancels cast
        api.Event.MouseDown += (MouseEvent e) =>
        {
            if (e.Button == EnumMouseButton.Right && castBar!.IsCasting)
                castBar.Cancel();
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

            api.ShowChatMessage($"[SnR] Starting cast: {spellId} ({spell.CastTime}s)");
            castBar.BeginCast(spell, () =>
            {
                api.ShowChatMessage($"[SnR] Cast complete, sending: {spellId}");
                var entity = api.World.Player?.Entity;
                if (entity != null)
                    api.World.PlaySoundAt(new AssetLocation("game", "sounds/effect/latch"), entity, null, true, 16f, 0.9f);
                clientChannel!.SendPacket(new MsgCastSpell { SpellId = spellId });
            });
        };
    }

    public override void Dispose()
    {
        hudFlux?.Dispose();
        castBar?.Dispose();
        radialMenu?.Dispose();
        spellbookDialog?.Dispose();
        sparkGlow?.Dispose();
        base.Dispose();
    }
}
