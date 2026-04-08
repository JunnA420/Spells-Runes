using System;
using Cairo;
using Vintagestory.API.Client;
using SpellsAndRunes.Spells;

namespace SpellsAndRunes.HUD;

public class HudCastBar : HudElement
{
    private const double W = 300;
    private const double H = 44;
    private const double TotalH = H + 22; // room for label above

    private bool    isCasting  = false;
    private float   elapsed    = 0f;
    private float   castTime   = 1f;
    private string  spellName  = "";
    private Action? onComplete;

    private double cr = 0.55, cg = 0.85, cb2 = 1.0; // element color

    public bool IsCasting => isCasting;

    public HudCastBar(ICoreClientAPI capi) : base(capi)
    {
        Compose();
        TryOpen();
    }

    private void Compose()
    {
        var db = ElementBounds.Fixed(EnumDialogArea.CenterMiddle, 0, 120, W, TotalH);
        var cb = ElementBounds.Fixed(0, 0, W, TotalH);
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
        (SingleComposer?.GetElement("canvas") as GuiElementCustomDraw)?.Redraw();
        base.OnRenderGUI(deltaTime);
    }

    private void Draw(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        if (!isCasting) return;

        double frac = Math.Clamp(elapsed / castTime, 0.0, 1.0);
        double barY = TotalH - H;    // bar starts below the label area
        double r    = 6.0;

        // ── Spell name label ──────────────────────────────────────
        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Bold);
        ctx.SetFontSize(13);
        ctx.SetSourceRGBA(cr, cg, cb2, 0.95);
        CenterText(ctx, spellName, W / 2, barY - 5);

        // Decorative dots flanking name
        double dotY = barY - 9;
        ctx.Arc(W / 2 - 72, dotY, 2.5, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(cr, cg, cb2, 0.55); ctx.Fill();
        ctx.Arc(W / 2 + 72, dotY, 2.5, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(cr, cg, cb2, 0.55); ctx.Fill();

        // ── Bar background ────────────────────────────────────────
        RR(ctx, 0, barY, W, H, r);
        ctx.SetSourceRGBA(0.04, 0.04, 0.14, 0.92); ctx.Fill();

        // ── Filled portion ────────────────────────────────────────
        if (frac > 0.001)
        {
            double fillW = Math.Max((W - 4) * frac, r * 2);
            // Glow layer behind fill
            RR(ctx, 2, barY + 2, fillW, H - 4, r - 2);
            ctx.SetSourceRGBA(cr, cg, cb2, 0.12); ctx.Fill();

            // Main fill
            RR(ctx, 2, barY + 2, fillW, H - 4, r - 2);
            ctx.SetSourceRGBA(cr * 0.55, cg * 0.55, cb2 * 0.55, 0.95); ctx.Fill();

            // Bright top-half sheen
            RR(ctx, 2, barY + 2, fillW, (H - 4) * 0.45, r - 2);
            ctx.SetSourceRGBA(cr, cg, cb2, 0.30); ctx.Fill();

            // Leading edge glow
            double ex = 2 + fillW;
            ctx.Rectangle(ex - 6, barY + 2, 6, H - 4);
            ctx.SetSourceRGBA(cr, cg, cb2, 0.55); ctx.Fill();
        }

        // ── Border with corner ornaments ──────────────────────────
        RR(ctx, 0, barY, W, H, r);
        ctx.SetSourceRGBA(cr, cg, cb2, 0.70);
        ctx.LineWidth = 1.5; ctx.Stroke();

        // Corner diamonds
        DrawDiamond(ctx, 0,     barY,     8, cr, cg, cb2);
        DrawDiamond(ctx, W,     barY,     8, cr, cg, cb2);
        DrawDiamond(ctx, 0,     barY + H, 8, cr, cg, cb2);
        DrawDiamond(ctx, W,     barY + H, 8, cr, cg, cb2);

        // ── Center text (remaining time in seconds) ─────────────────
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(11);
        ctx.SetSourceRGBA(1, 1, 1, 0.90);
        float remaining = Math.Max(0, castTime - elapsed);
        string timeStr = frac >= 0.99 ? "!" : $"{remaining:F1}s";
        CenterText(ctx, timeStr, W / 2, barY + H / 2 + 4);

        // ── Tick marks ────────────────────────────────────────────
        ctx.SetSourceRGBA(cr, cg, cb2, 0.20);
        ctx.LineWidth = 1;
        for (int i = 1; i < 4; i++)
        {
            double tx = W * i / 4.0;
            ctx.MoveTo(tx, barY + 4); ctx.LineTo(tx, barY + H - 4); ctx.Stroke();
        }
    }

    private static void DrawDiamond(Context ctx, double x, double y, double s, double r, double g, double b)
    {
        double h = s * 0.6;
        ctx.NewPath();
        ctx.MoveTo(x,     y - h);
        ctx.LineTo(x + h, y);
        ctx.LineTo(x,     y + h);
        ctx.LineTo(x - h, y);
        ctx.ClosePath();
        ctx.SetSourceRGBA(r, g, b, 0.80); ctx.Fill();
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
        _                  => (0.75, 0.62, 0.25),
    };

    public override string ToggleKeyCombinationCode => null!;
    public override bool ShouldReceiveKeyboardEvents() => false;
    public override bool ShouldReceiveMouseEvents() => false;
    public override EnumDialogType DialogType => EnumDialogType.HUD;
    public override bool TryClose() => false;
}
