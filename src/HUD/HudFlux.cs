using System;
using Cairo;
using Vintagestory.API.Client;
using SpellsAndRunes.Spells;

namespace SpellsAndRunes.HUD;

public class HudFlux : HudElement
{
    private const double D   = 80;  // orb diameter
    private const double Pad = 6;   // padding so border doesn't clip
    private const double S   = D + Pad * 2; // canvas size

    public HudFlux(ICoreClientAPI capi) : base(capi)
    {
        ComposeDialog();
        TryOpen();
    }

    private void ComposeDialog()
    {
        var dialogBounds = ElementBounds.Fixed(EnumDialogArea.CenterBottom, -(460 / 2 + 8 + S), -S - 2, S, S);
        var canvasBounds = ElementBounds.Fixed(0, 0, S, S);

        SingleComposer = capi.Gui
            .CreateCompo("spellsandrunes:hud-flux", dialogBounds)
            .AddDynamicCustomDraw(canvasBounds, DrawOrb, "fluxArc")
            .Compose();
    }

    private void DrawOrb(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        float currentFlux = capi.World.Player?.Entity?.WatchedAttributes
            .GetFloat("spellsandrunes:flux", 100f) ?? 100f;
        float maxFlux = capi.World.Player?.Entity?.WatchedAttributes
            .GetFloat("spellsandrunes:maxflux", 100f) ?? 100f;

        double frac = maxFlux > 0 ? Math.Clamp(currentFlux / maxFlux, 0.0, 1.0) : 0.0;
        double cx = S / 2, cy = S / 2, r = D / 2 - 1;

        // ── Outer dark ring ──────────────────────────────────────
        ctx.Arc(cx, cy, r + 2, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(0.05, 0.05, 0.12, 0.95); ctx.Fill();

        // ── Clip to circle for the fill ──────────────────────────
        ctx.Arc(cx, cy, r, 0, 2 * Math.PI);
        ctx.Clip();

        // Empty background (dark blue-black)
        ctx.Arc(cx, cy, r, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(0.04, 0.04, 0.18, 1.0); ctx.Fill();

        // Filled liquid — rises from bottom
        double fillH = r * 2 * frac;
        double fillY = cy + r - fillH;
        ctx.Rectangle(cx - r, fillY, r * 2, fillH);
        ctx.SetSourceRGBA(0.18, 0.35, 0.95, 0.90); ctx.Fill();

        // Wave on top of fill (simple sine approximation via bezier)
        if (frac > 0.02 && frac < 0.98)
        {
            double wy = fillY;
            ctx.NewPath();
            ctx.MoveTo(cx - r, wy + 3);
            ctx.CurveTo(cx - r * 0.5, wy - 3, cx + r * 0.5, wy + 3, cx + r, wy - 2);
            ctx.LineTo(cx + r, wy + 8);
            ctx.CurveTo(cx + r * 0.5, wy + 4, cx - r * 0.5, wy + 9, cx - r, wy + 5);
            ctx.ClosePath();
            ctx.SetSourceRGBA(0.35, 0.55, 1.0, 0.55); ctx.Fill();
        }

        // Bright highlight (top-left shine) — simple white arc, no gradient
        ctx.Arc(cx - r * 0.28, cy - r * 0.28, r * 0.35, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(1, 1, 1, 0.18);
        ctx.Fill();

        // Reset clip
        ctx.ResetClip();

        // ── Metallic border ring ─────────────────────────────────
        ctx.Arc(cx, cy, r, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(0.25, 0.40, 0.80, 0.70);
        ctx.LineWidth = 2.5; ctx.Stroke();

        ctx.Arc(cx, cy, r + 1.5, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(0.55, 0.65, 0.90, 0.30);
        ctx.LineWidth = 1.0; ctx.Stroke();

        // ── Value text ───────────────────────────────────────────
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(12);
        ctx.SetSourceRGBA(1, 1, 1, frac > 0.15 ? 0.95 : 0.55);
        string label = $"{(int)currentFlux}";
        var te = ctx.TextExtents(label);
        ctx.MoveTo(cx - te.Width / 2 - te.XBearing, cy - te.YBearing - te.Height / 2);
        ctx.ShowText(label);
    }

    public override void OnRenderGUI(float deltaTime)
    {
        var entity = capi.World.Player?.Entity;
        if (entity == null) return;
        if (!PlayerSpellData.For(entity).IsFluxUnlocked) return;

        (SingleComposer.GetElement("fluxArc") as GuiElementCustomDraw)?.Redraw();
        base.OnRenderGUI(deltaTime);
    }

    public override string ToggleKeyCombinationCode => null!;
    public override bool ShouldReceiveKeyboardEvents() => false;
    public override bool ShouldReceiveMouseEvents() => false;
    public override EnumDialogType DialogType => EnumDialogType.HUD;
    public override bool TryClose() => false;
}
