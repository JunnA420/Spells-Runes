using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using SpellsAndRunes.Spells;
using SpellsAndRunes.Spells.Air;

namespace SpellsAndRunes.HUD;

/// <summary>
/// Renders a cone preview for the currently selected spell each frame.
/// Uses particle-like block highlights by spawning very short-lived particles
/// along the cone outline so the player can see the AoE before casting.
/// </summary>
public class SpellConeRenderer
{
    private readonly ICoreClientAPI capi;
    private readonly HudRadialMenu radialMenu;
    private float tickAccum = 0f;
    private const float SpawnInterval = 0.08f;

    public bool Enabled { get; set; } = true;

    public SpellConeRenderer(ICoreClientAPI capi, HudRadialMenu radialMenu)
    {
        this.capi       = capi;
        this.radialMenu = radialMenu;
    }

    public void OnGameTick(float dt)
    {
        if (!Enabled) return;

        tickAccum += dt;
        if (tickAccum < SpawnInterval) return;
        tickAccum = 0f;

        var entity = capi.World.Player?.Entity;
        if (entity == null) return;

        string? spellId = radialMenu.GetSelectedSpellId();

        // Try to find a spell with cone info — for now hardcode AirPush check
        var spell = spellId != null ? SpellRegistry.Get(spellId) : null;
        if (spell == null) return;

        float range    = GetRange(spell);
        float angleDeg = GetAngleDeg(spell);
        if (range <= 0) return;

        var origin  = entity.Pos.XYZ.Add(0, 0.5, 0);
        var lookDir = entity.Pos.GetViewVector().ToVec3d().Normalize();

        DrawConeOutline(origin, lookDir, range, angleDeg, ElementColor(spell.Element));
    }

    private void DrawConeOutline(Vec3d origin, Vec3d lookDir, float range, float angleDeg, int color)
    {
        Vec3d right  = lookDir.Cross(new Vec3d(0, 1, 0)).Normalize();
        Vec3d upPerp = lookDir.Cross(right).Normalize();
        double tanA  = Math.Tan(angleDeg * Math.PI / 180.0);

        // 4 edge lines from origin to rim
        for (int edge = 0; edge < 4; edge++)
        {
            double angle = edge * Math.PI / 2.0;
            for (int s = 0; s <= 18; s++)
            {
                double t      = (double)s / 18;
                double dist   = range * t;
                double spread = tanA * dist;
                Vec3d pos = origin
                          + lookDir * dist
                          + right   * (Math.Cos(angle) * spread)
                          + upPerp  * (Math.Sin(angle) * spread);
                SpawnDot(pos, color, 0.20f);
            }
        }

        // Final rim ring only
        {
            double dist   = range;
            double spread = tanA * dist;
            int    pts    = 36;
            for (int j = 0; j < pts; j++)
            {
                double angle = j * 2 * Math.PI / pts;
                Vec3d pos = origin
                          + lookDir * dist
                          + right   * (Math.Cos(angle) * spread)
                          + upPerp  * (Math.Sin(angle) * spread);
                SpawnDot(pos, color, 0.25f);
            }
        }
    }

    private void SpawnDot(Vec3d pos, int color, float size = 0.18f)
    {
        capi.World.SpawnParticles(new SimpleParticleProperties
        {
            MinPos               = new Vec3d(pos.X - 0.05, pos.Y - 0.05, pos.Z - 0.05),
            AddPos               = new Vec3d(0.10, 0.10, 0.10),
            MinVelocity          = new Vec3f(0, 0.01f, 0),
            AddVelocity          = new Vec3f(0, 0, 0),
            MinQuantity          = 1,
            LifeLength           = SpawnInterval * 2.0f,
            MinSize              = size,
            MaxSize              = size,
            GravityEffect        = 0f,
            Color                = color,
            ParticleModel        = EnumParticleModel.Quad,
            WithTerrainCollision = false,
            ShouldDieInLiquid    = false,
        });
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static float GetRange(Spell spell) => spell switch
    {
        AirPush => AirPush.Range,
        _       => 0f,
    };

    private static float GetAngleDeg(Spell spell) => spell switch
    {
        AirPush => AirPush.ConeAngleDeg,
        _       => 0f,
    };

    private static int ElementColor(SpellElement el)
    {
        var (r, g, b) = el switch
        {
            SpellElement.Air   => (180, 235, 255),
            SpellElement.Fire  => (255, 120, 40),
            SpellElement.Water => (50, 150, 255),
            SpellElement.Earth => (100, 200, 70),
            _                  => (200, 200, 200),
        };
        return ColorUtil.ColorFromRgba(r, g, b, 230);
    }
}
