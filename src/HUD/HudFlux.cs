using System;
using Cairo;
using Vintagestory.API.Client;
using SpellsAndRunes.Spells;

namespace SpellsAndRunes.HUD;

public class HudFlux : HudElement
{
    // ── Sizing ────────────────────────────────────────────────────────────────
    private const double CanvasH      = 90;
    private const double OrbColW      = 90;    // orb section width
    private const double OrbR         = 38;    // orb visual radius
    private const double SpellPanelW  = 228;   // spell section width
    private const double Gap          = 6;
    private const double CanvasWFull  = SpellPanelW + Gap + OrbColW;

    // X of orb center: spell(left) + gap + half orb-col
    private const double OrbCxFull  = SpellPanelW + Gap + OrbColW / 2;
    private const double OrbCy      = CanvasH / 2;

    // Spell panel — shorter box, bottom-aligned to orb (extends from SpellPy down to CanvasH)
    private const double SpellPh   = 56;                    // spell panel height
    private const double SpellPy   = CanvasH - SpellPh;    // top of spell panel = 90-56 = 34
    private const double SpellPx   = 4;
    private const double SpellPw   = SpellPanelW - 8;

    private readonly HudRadialMenu _radial;

    public HudFlux(ICoreClientAPI capi, HudRadialMenu radial) : base(capi)
    {
        _radial = radial;
        Compose();
        TryOpen();
    }

    private void Compose()
    {
        double fixedX = -(460.0 / 2 + 80 + CanvasWFull);
        double fixedY = 0;

        var db = ElementBounds.Fixed(EnumDialogArea.CenterBottom, fixedX, fixedY, CanvasWFull, CanvasH);
        var cb = ElementBounds.Fixed(0, 0, CanvasWFull, CanvasH);
        SingleComposer = capi.Gui
            .CreateCompo("spellsandrunes:hud-flux", db)
            .AddDynamicCustomDraw(cb, Draw, "canvas")
            .Compose();
        TryOpen();
    }

    private Spell? GetCurrentSpell()
    {
        var data = capi.World.Player?.Entity != null ? PlayerSpellData.For(capi.World.Player.Entity) : null;
        int sel  = _radial.SelectedSlot;
        return (sel >= 0 ? data?.GetHotbarSlot(sel) : null) is string sid ? SpellRegistry.Get(sid) : null;
    }

    private void Draw(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        float  cur  = capi.World.Player?.Entity?.WatchedAttributes.GetFloat("spellsandrunes:flux",    100f) ?? 100f;
        float  max  = capi.World.Player?.Entity?.WatchedAttributes.GetFloat("spellsandrunes:maxflux", 100f) ?? 100f;
        double frac = max > 0 ? Math.Clamp(cur / max, 0.0, 1.0) : 0.0;

        var spell    = GetCurrentSpell();
        bool hasSpell = spell != null;
        var (er, eg, eb) = spell != null ? Ec(spell.Element) : (0.75, 0.62, 0.25);

        double orbCx   = OrbCxFull;
        double orbColX = SpellPanelW + Gap;

        // ── Orb section box (full canvas height) ──────────────────────────────
        ctx.SetSourceRGBA(0.04, 0.03, 0.07, 0.92);
        RRect(ctx, orbColX, 0, OrbColW, CanvasH, 5); ctx.Fill();

        ctx.SetSourceRGBA(0.75, 0.62, 0.25, 0.45);
        ctx.LineWidth = 1.2;
        RRect(ctx, orbColX + 0.6, 0.6, OrbColW - 1.2, CanvasH - 1.2, 5); ctx.Stroke();

        ctx.SetSourceRGBA(0.75, 0.62, 0.25, 0.14);
        ctx.LineWidth = 0.7;
        RRect(ctx, orbColX + 2.5, 2.5, OrbColW - 5, CanvasH - 5, 4); ctx.Stroke();

        // Orb corner dots
        ctx.SetSourceRGBA(0.75, 0.62, 0.25, 0.50);
        foreach (var (dx, dy) in new[]{
            (orbColX+3.5, 3.5),(orbColX+OrbColW-3.5, 3.5),
            (orbColX+3.5, CanvasH-3.5),(orbColX+OrbColW-3.5, CanvasH-3.5)})
        { ctx.Arc(dx, dy, 1.8, 0, 2*Math.PI); ctx.Fill(); }

        // Orb column inner bg
        ctx.SetSourceRGBA(0.06, 0.04, 0.10, 0.50);
        RRect(ctx, orbColX + 2, 2, OrbColW - 4, CanvasH - 4, 4); ctx.Fill();

        // ── Orb glow ──────────────────────────────────────────────────────────
        if (frac > 0.05)
        {
            ctx.SetSourceRGBA(0.60, 0.25, 0.88, 0.13 * frac);
            ctx.Arc(orbCx, OrbCy, OrbR + 10, 0, 2 * Math.PI); ctx.Fill();
        }

        // ── Liquid fill ───────────────────────────────────────────────────────
        ctx.Save();
        ctx.Arc(orbCx, OrbCy, OrbR, 0, 2 * Math.PI); ctx.Clip();

        ctx.Arc(orbCx, OrbCy, OrbR, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(0.03, 0.02, 0.07, 1.0); ctx.Fill();

        double fillH = OrbR * 2 * frac;
        double fillY = OrbCy + OrbR - fillH;
        ctx.Rectangle(orbCx - OrbR, fillY, OrbR * 2, fillH);
        ctx.SetSourceRGBA(0.50, 0.20, 0.78, 0.92); ctx.Fill();

        if (frac > 0.03 && frac < 0.97)
        {
            ctx.NewPath();
            ctx.MoveTo(orbCx - OrbR, fillY + 2.5);
            ctx.CurveTo(orbCx - OrbR*0.5, fillY - 3.5, orbCx + OrbR*0.5, fillY + 3.5, orbCx + OrbR, fillY - 2);
            ctx.LineTo(orbCx + OrbR, fillY + 7);
            ctx.CurveTo(orbCx + OrbR*0.5, fillY + 4, orbCx - OrbR*0.5, fillY + 8.5, orbCx - OrbR, fillY + 5);
            ctx.ClosePath();
            ctx.SetSourceRGBA(0.68, 0.38, 0.95, 0.40); ctx.Fill();
        }

        ctx.Arc(orbCx - OrbR*0.28, OrbCy - OrbR*0.28, OrbR*0.28, 0, 2*Math.PI);
        ctx.SetSourceRGBA(1, 1, 1, 0.12); ctx.Fill();
        ctx.Restore();

        // Orb border
        ctx.Arc(orbCx, OrbCy, OrbR, 0, 2*Math.PI);
        ctx.SetSourceRGBA(0.62, 0.28, 0.88, 0.80); ctx.LineWidth = 1.8; ctx.Stroke();
        ctx.Arc(orbCx, OrbCy, OrbR + 2, 0, 2*Math.PI);
        ctx.SetSourceRGBA(0.82, 0.55, 0.96, 0.18); ctx.LineWidth = 0.8; ctx.Stroke();

        // Tick marks
        ctx.LineWidth = 1.4;
        for (int ti = 0; ti < 4; ti++)
        {
            double ang = Math.PI/2 + ti * (Math.PI*2/4);
            bool passed = frac >= ti*0.25 + 0.01;
            ctx.SetSourceRGBA(0.75, 0.62, 0.25, passed ? 0.78 : 0.22);
            ctx.MoveTo(orbCx + Math.Cos(ang)*(OrbR+2.5), OrbCy + Math.Sin(ang)*(OrbR+2.5));
            ctx.LineTo(orbCx + Math.Cos(ang)*(OrbR+7.5), OrbCy + Math.Sin(ang)*(OrbR+7.5)); ctx.Stroke();
        }

        // Numbers inside orb
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(14);
        ctx.SetSourceRGBA(0.97, 0.91, 1.00, frac > 0.20 ? 1.0 : 0.70);
        string curStr = ((int)cur).ToString();
        var te = ctx.TextExtents(curStr);
        ctx.MoveTo(orbCx - te.Width/2 - te.XBearing, OrbCy - 1 - te.YBearing - te.Height/2);
        ctx.ShowText(curStr);

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(9);
        ctx.SetSourceRGBA(0.65, 0.44, 0.85, 0.60);
        string maxStr = $"/{(int)max}";
        var te2 = ctx.TextExtents(maxStr);
        ctx.MoveTo(orbCx - te2.Width/2 - te2.XBearing, OrbCy + 12 - te2.YBearing - te2.Height/2);
        ctx.ShowText(maxStr);

        // FLUX label bottom
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(7);
        ctx.SetSourceRGBA(0.62, 0.28, 0.88, 0.45);
        string fluxLbl = "FLUX";
        var tfl = ctx.TextExtents(fluxLbl);
        ctx.MoveTo(orbCx - tfl.Width/2 - tfl.XBearing, CanvasH - 5 - tfl.YBearing - tfl.Height/2);
        ctx.ShowText(fluxLbl);

        // ── Spell panel (only when spell selected) ────────────────────────────
        if (!hasSpell) return;

        // Spell section outer box (bottom-aligned, shorter than orb)
        ctx.SetSourceRGBA(0.04, 0.03, 0.07, 0.92);
        RRect(ctx, 0, SpellPy, SpellPanelW, SpellPh, 5); ctx.Fill();

        ctx.SetSourceRGBA(0.75, 0.62, 0.25, 0.45);
        ctx.LineWidth = 1.2;
        RRect(ctx, 0.6, SpellPy + 0.6, SpellPanelW - 1.2, SpellPh - 1.2, 5); ctx.Stroke();

        ctx.SetSourceRGBA(0.75, 0.62, 0.25, 0.14);
        ctx.LineWidth = 0.7;
        RRect(ctx, 2.5, SpellPy + 2.5, SpellPanelW - 5, SpellPh - 5, 4); ctx.Stroke();

        // Spell section corner dots
        ctx.SetSourceRGBA(0.75, 0.62, 0.25, 0.50);
        foreach (var (dx, dy) in new[]{
            (3.5, SpellPy+3.5),(SpellPanelW-3.5, SpellPy+3.5),
            (3.5, CanvasH-3.5),(SpellPanelW-3.5, CanvasH-3.5)})
        { ctx.Arc(dx, dy, 1.8, 0, 2*Math.PI); ctx.Fill(); }

        // Element tint + accent left border
        ctx.SetSourceRGBA(er, eg, eb, 0.04);
        RRect(ctx, 0, SpellPy, SpellPanelW, SpellPh, 5); ctx.Fill();

        ctx.SetSourceRGBA(er, eg, eb, 0.70); ctx.LineWidth = 2;
        ctx.MoveTo(SpellPx + 1, SpellPy + 5); ctx.LineTo(SpellPx + 1, SpellPy + SpellPh - 5); ctx.Stroke();

        double px   = SpellPx + 10;
        double midY = SpellPy + SpellPh / 2;

        // Slot badge
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(9);
        ctx.SetSourceRGBA(er, eg, eb, 0.52);
        string slotStr = $"#{_radial.SelectedSlot + 1}";
        var ste = ctx.TextExtents(slotStr);
        ctx.MoveTo(SpellPx + SpellPw - ste.Width - ste.XBearing - 4, SpellPy + 11 - ste.YBearing - ste.Height/2);
        ctx.ShowText(slotStr);

        // Spell name
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(12);
        ctx.SetSourceRGBA(0.97, 0.93, 1.00, 0.97);
        var nte = ctx.TextExtents(spell!.Name);
        ctx.MoveTo(px, midY - 5 - nte.YBearing - nte.Height/2);
        ctx.ShowText(spell.Name);

        // Separator
        ctx.SetSourceRGBA(0.75, 0.62, 0.25, 0.16); ctx.LineWidth = 0.7;
        ctx.MoveTo(px, midY + 1); ctx.LineTo(SpellPx + SpellPw - 4, midY + 1); ctx.Stroke();

        // Cost + cast time
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(10);
        ctx.SetSourceRGBA(0.68, 0.44, 0.90, 0.78);
        string info = $"{spell.FluxCost:0} Flux  ·  {spell.CastTime:0.#}s";
        var ite = ctx.TextExtents(info);
        ctx.MoveTo(px, midY + 11 - ite.YBearing - ite.Height/2);
        ctx.ShowText(info);

        // Element name bottom-right
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(8);
        ctx.SetSourceRGBA(er, eg, eb, 0.40);
        string elStr = spell.Element.ToString();
        var ele = ctx.TextExtents(elStr);
        ctx.MoveTo(SpellPx + SpellPw - ele.Width - ele.XBearing - 4,
                   SpellPy + SpellPh - 7 - ele.YBearing - ele.Height/2);
        ctx.ShowText(elStr);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void RRect(Context ctx, double x, double y, double w, double h, double r)
    {
        ctx.MoveTo(x + r, y); ctx.LineTo(x + w - r, y);
        ctx.Arc(x + w - r, y + r, r, -Math.PI/2, 0);
        ctx.LineTo(x + w, y + h - r);
        ctx.Arc(x + w - r, y + h - r, r, 0, Math.PI/2);
        ctx.LineTo(x + r, y + h);
        ctx.Arc(x + r, y + h - r, r, Math.PI/2, Math.PI);
        ctx.LineTo(x, y + r);
        ctx.Arc(x + r, y + r, r, Math.PI, -Math.PI/2);
        ctx.ClosePath();
    }

    private static (double r, double g, double b) Ec(SpellElement el) => el switch
    {
        SpellElement.Fire  => (0.878, 0.314, 0.188),
        SpellElement.Water => (0.188, 0.502, 0.784),
        SpellElement.Earth => (0.686, 0.490, 0.216),
        SpellElement.Air   => (0.843, 0.863, 0.922),
        SpellElement.Flux  => (0.65,  0.30,  0.88),
        _                  => (0.75,  0.62,  0.25),
    };

    public override void OnRenderGUI(float deltaTime)
    {
        var entity = capi.World.Player?.Entity;
        if (entity == null || !PlayerSpellData.For(entity).IsFluxUnlocked) return;

        // Recompose if spell presence changed (to resize the canvas)
        (SingleComposer?.GetElement("canvas") as GuiElementCustomDraw)?.Redraw();
        base.OnRenderGUI(deltaTime);
    }

    public override string ToggleKeyCombinationCode => null!;
    public override bool ShouldReceiveKeyboardEvents() => false;
    public override bool ShouldReceiveMouseEvents() => false;
    public override EnumDialogType DialogType => EnumDialogType.HUD;
    public override bool TryClose() => false;
}
