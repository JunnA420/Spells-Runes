using System;
using Cairo;
using Vintagestory.API.Client;
using SpellsAndRunes.Spells;

namespace SpellsAndRunes.HUD;

public class HudCastBar : HudElement
{
    private const double W = 240;
    private const double H = 38;
    private const double TotalH = H + 22; // room for label above

    private bool    isCasting  = false;
    private float   elapsed    = 0f;
    private float   castTime   = 1f;
    private string  spellName  = "";
    private Action? onComplete;

    private double cr = 0.55, cg = 0.85, cb2 = 1.0; // element color
    private double _sc = 1.0, _res = 1.0, _drawScale = 1.0, _vds = 1.0;

    public bool IsCasting => isCasting;

    public HudCastBar(ICoreClientAPI capi) : base(capi)
    {
        Compose();
        TryOpen();
    }

    private void Compose()
    {
        _sc  = GuiElement.scaled(1.0);
        _res = capi.Render.FrameHeight / 1080.0;
        _drawScale = Math.Clamp(_res, 0.60, 1.40);
        _vds       = (_sc > 0) ? _drawScale / _sc : 1.0;
        double cw = W      * _vds;
        double ch = TotalH * _vds;
        double fy = 200.0  / _sc; // 200 screen px below center, fixed
        var db = ElementBounds.Fixed(EnumDialogArea.CenterMiddle, 0, fy, cw, ch);
        var cb = ElementBounds.Fixed(0, 0, cw, ch);
        SingleComposer = capi.Gui
            .CreateCompo("snr:castbar", db)
            .AddDynamicCustomDraw(cb, Draw, "canvas")
            .Compose();
    }

    public void BeginCast(Spell spell, Action onComplete)
    {
        elapsed          = 0f;
        castTime         = Math.Max(spell.CastTime, 0.05f);
        spellName        = spell.Name;
        this.onComplete  = onComplete;
        (cr, cg, cb2)    = ElementColor(spell.Element);
        isCasting        = true;
    }

    /// <summary>Start cast with explicit cast time (from server with level scaling). Cancels any ongoing cast.</summary>
    public void OnBeginCast(string spellId, float scaledCastTime)
    {
        Cancel();  // Kill any previous cast immediately
        var spell = SpellRegistry.Get(spellId);
        if (spell != null)
        {
            elapsed        = 0f;
            castTime       = Math.Max(scaledCastTime, 0.05f);
            spellName      = spell.Name;
            onComplete     = null;
            (cr, cg, cb2)  = ElementColor(spell.Element);
            isCasting      = true;
        }
    }

    public void Cancel()
    {
        isCasting  = false;
        onComplete = null;
    }

    public override void OnRenderGUI(float deltaTime)
    {
        if (isCasting)
        {
            elapsed += deltaTime;
            if (elapsed >= castTime)
            {
                isCasting = false;
                var cb3   = onComplete;
                onComplete = null;
                cb3?.Invoke();
            }
        }
        double sc  = GuiElement.scaled(1.0);
        double res = capi.Render.FrameHeight / 1080.0;
        if (Math.Abs(sc - _sc) > 0.001 || Math.Abs(res - _res) > 0.001) Compose();

        (SingleComposer?.GetElement("canvas") as GuiElementCustomDraw)?.Redraw();
        base.OnRenderGUI(deltaTime);
    }

    private void Draw(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        if (!isCasting) return;
        ctx.Scale(_drawScale, _drawScale);

        double frac  = Math.Clamp(elapsed / castTime, 0.0, 1.0);
        double barY  = TotalH - H;
        double r     = 6.0;
        double cx    = W / 2;
        double cy    = barY + H / 2;
        // Pulse factor (0–1, sine wave ~2Hz)
        double pulse = (Math.Sin(elapsed * Math.PI * 2.2) + 1.0) * 0.5;

        // ── Spell name label ──────────────────────────────────────
        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Bold);
        ctx.SetFontSize(13);
        ctx.SetSourceRGBA(cr, cg, cb2, 0.95);
        CenterText(ctx, spellName, cx, barY - 5);

        // Decorative dots flanking name
        double dotY = barY - 9;
        ctx.Arc(cx - 76, dotY, 2.5, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(cr, cg, cb2, 0.55); ctx.Fill();
        ctx.Arc(cx + 76, dotY, 2.5, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(cr, cg, cb2, 0.55); ctx.Fill();

        // ── Bar background ────────────────────────────────────────
        RR(ctx, 0, barY, W, H, r);
        ctx.SetSourceRGBA(0.04, 0.03, 0.07, 0.92); ctx.Fill();

        RR(ctx, 0.6, barY + 0.6, W - 1.2, H - 1.2, r);
        ctx.SetSourceRGBA(0.75, 0.62, 0.25, 0.45);
        ctx.LineWidth = 1.2; ctx.Stroke();

        RR(ctx, 2.5, barY + 2.5, W - 5, H - 5, r - 1);
        ctx.SetSourceRGBA(0.75, 0.62, 0.25, 0.14);
        ctx.LineWidth = 0.7; ctx.Stroke();

        // Corner dots (HudFlux style)
        ctx.SetSourceRGBA(0.75, 0.62, 0.25, 0.50);
        foreach (var (dx, dy) in new[]{
            (3.5, barY + 3.5), (W - 3.5, barY + 3.5),
            (3.5, barY + H - 3.5), (W - 3.5, barY + H - 3.5)})
        { ctx.Arc(dx, dy, 1.8, 0, 2 * Math.PI); ctx.Fill(); }

        // ── Filled bar portion ────────────────────────────────────
        if (frac > 0.001)
        {
            ctx.Save();
            RR(ctx, 2, barY + 2, W - 4, H - 4, r - 2); ctx.Clip();

            double fillW = Math.Max((W - 4) * frac, r * 2);

            // Base fill
            RR(ctx, 2, barY + 2, fillW, H - 4, r - 2);
            ctx.SetSourceRGBA(cr * 0.45, cg * 0.45, cb2 * 0.45, 0.90); ctx.Fill();

            // Top-half sheen
            RR(ctx, 2, barY + 2, fillW, (H - 4) * 0.40, r - 2);
            ctx.SetSourceRGBA(cr, cg, cb2, 0.22); ctx.Fill();

            // Wave surface (B) — liquid surface on fill top edge
            double waveY = barY + 2 + (H - 4) * (1.0 - frac);
            if (frac > 0.04 && frac < 0.97)
            {
                ctx.NewPath();
                ctx.MoveTo(2, waveY + 3);
                ctx.CurveTo(2 + fillW * 0.25, waveY - 4,
                            2 + fillW * 0.55, waveY + 5,
                            2 + fillW,        waveY - 2);
                ctx.LineTo(2 + fillW, waveY + 8);
                ctx.CurveTo(2 + fillW * 0.55, waveY + 6,
                            2 + fillW * 0.25, waveY + 10,
                            2, waveY + 7);
                ctx.ClosePath();
                ctx.SetSourceRGBA(cr, cg, cb2, 0.28); ctx.Fill();
            }

            // Leading edge pulse (E)
            double ex = 2 + fillW;
            double edgeAlpha = 0.45 + 0.35 * pulse;
            ctx.Rectangle(Math.Max(2, ex - 8), barY + 2, 8, H - 4);
            ctx.SetSourceRGBA(cr, cg, cb2, edgeAlpha); ctx.Fill();
            // Edge glow bloom
            ctx.Rectangle(Math.Max(2, ex - 14), barY + 2, 10, H - 4);
            ctx.SetSourceRGBA(cr, cg, cb2, edgeAlpha * 0.30); ctx.Fill();

            ctx.Restore();
        }

        // ── Center text (remaining time) ──────────────────────────
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(11);
        ctx.SetSourceRGBA(1, 1, 1, 0.92);
        float remaining = Math.Max(0, castTime - elapsed);
        string timeStr  = frac >= 0.99 ? "!" : $"{remaining:F1}s";
        CenterText(ctx, timeStr, cx, cy + 4);

        // ── Subtle tick marks ─────────────────────────────────────
        ctx.SetSourceRGBA(0.75, 0.62, 0.25, 0.18);
        ctx.LineWidth = 0.8;
        for (int i = 1; i < 4; i++)
        {
            double tx = W * i / 4.0;
            ctx.MoveTo(tx, barY + 5); ctx.LineTo(tx, barY + H - 5); ctx.Stroke();
        }
    }

    private static void CenterText(Context ctx, string t, double cx, double y)
    {
        var e = ctx.TextExtents(t);
        ctx.MoveTo(cx - e.Width / 2 - e.XBearing, y);
        ctx.ShowText(t);
    }

    private static void RR(Context ctx, double x, double y, double w, double h, double r)
    {
        r = Math.Min(r, Math.Min(w / 2, h / 2));
        ctx.NewPath();
        ctx.Arc(x + r,     y + r,     r, Math.PI,         3 * Math.PI / 2);
        ctx.Arc(x + w - r, y + r,     r, 3 * Math.PI / 2, 0);
        ctx.Arc(x + w - r, y + h - r, r, 0,               Math.PI / 2);
        ctx.Arc(x + r,     y + h - r, r, Math.PI / 2,     Math.PI);
        ctx.ClosePath();
    }

    private static (double r, double g, double b) ElementColor(SpellElement el) => el switch
    {
        SpellElement.Air   => (0.55, 0.85, 1.00),
        SpellElement.Fire  => (1.00, 0.50, 0.15),
        SpellElement.Water => (0.20, 0.65, 1.00),
        SpellElement.Earth => (0.45, 0.80, 0.30),
        SpellElement.Flux  => (0.65, 0.30, 0.88),
        _                  => (0.75, 0.62, 0.25),
    };

    public override string ToggleKeyCombinationCode => null!;
    public override bool ShouldReceiveKeyboardEvents() => false;
    public override bool ShouldReceiveMouseEvents() => false;
    public override EnumDialogType DialogType => EnumDialogType.HUD;
    public override bool TryClose() => false;
}
