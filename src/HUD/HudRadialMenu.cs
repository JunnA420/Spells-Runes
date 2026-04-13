using System;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using SpellsAndRunes.Spells;

namespace SpellsAndRunes.HUD;

/// <summary>
/// Radial spell selector. Cancel is the center circle; 5 outer slots = hotbar.
/// </summary>
public class HudRadialMenu : GuiDialog
{
    private bool isOpen       = false;
    private int  hoveredSlot  = -1;
    private int  selectedSlot = 0;

    private const double OrbitR      = 115;
    private const double SlotR       = 42;
    private const double InnerR      = 30;   // cancel zone radius
    private const double CanvasSize  = (OrbitR + SlotR + 90) * 2;
    private const int    CenterCancel = 5;   // special slot index = cancel

    // 5 spell slots evenly spaced, top = slot 0
    private static readonly double[] SlotAngles =
    {
        -Math.PI / 2,
        -Math.PI / 2 +     2 * Math.PI / 5,
        -Math.PI / 2 + 2 * 2 * Math.PI / 5,
        -Math.PI / 2 + 3 * 2 * Math.PI / 5,
        -Math.PI / 2 + 4 * 2 * Math.PI / 5,
    };

    private static readonly double[] CB = { 0.75, 0.62, 0.25, 1.00 };

    private readonly IndicatorHud indicator;
    private long _tickId = -1;

    public HudRadialMenu(ICoreClientAPI capi) : base(capi)
    {
        indicator = new IndicatorHud(capi, this);
        indicator.TryOpen();
    }

    private void ComposeRadial()
    {
        var db = ElementBounds.Fixed(EnumDialogArea.CenterMiddle, 0, 0, CanvasSize, CanvasSize);
        var cb = ElementBounds.Fixed(0, 0, CanvasSize, CanvasSize);
        SingleComposer = capi.Gui
            .CreateCompo("snr:radial", db)
            .AddDynamicCustomDraw(cb, DrawRadial, "canvas")
            .Compose();
        Redraw();
    }

    private void Redraw()
        => (SingleComposer?.GetElement("canvas") as GuiElementCustomDraw)?.Redraw();

    // ── Public API ────────────────────────────────────────────────────────────

    public bool IsOpen => isOpen;
    public int SelectedSlot => selectedSlot;

    public string? GetSelectedSpellId()
    {
        if (selectedSlot < 0) return null;
        return capi.World.Player?.Entity
            .WatchedAttributes.GetTreeAttribute("snr:hotbar")
            ?.GetString($"slot{selectedSlot}", null);
    }

    public void Open()
    {
        if (isOpen) return;
        isOpen = true; hoveredSlot = -1;
        ComposeRadial();
        TryOpen();
        _tickId = capi.World.RegisterGameTickListener(_ => { if (isOpen) Redraw(); }, 1000 / 30);
        indicator.RedrawIndicator();
    }

    public void Close()
    {
        if (!isOpen) return;
        if (hoveredSlot >= 0) ApplySlot(hoveredSlot);
        isOpen = false;
        if (_tickId >= 0) { capi.World.UnregisterGameTickListener(_tickId); _tickId = -1; }
        TryClose();
        indicator.RedrawIndicator();
    }

    public void UpdateMouse(double mx, double my)
    {
        if (!isOpen) return;
        double sc    = GuiElement.scaled(1.0);
        var (lx, ly) = Local(mx, my);
        // Local() returns physical offset; divide by sc to get draw-space coords
        int prev     = hoveredSlot;
        hoveredSlot  = SlotAtPos(lx / sc, ly / sc);
        if (hoveredSlot != prev) Redraw();
    }

    private void ApplySlot(int slot)
        => selectedSlot = (slot == CenterCancel) ? -1 : slot;

    private (double lx, double ly) Local(double mx, double my)
    {
        var el = SingleComposer?.GetElement("canvas");
        if (el == null) return (CanvasSize / 2, CanvasSize / 2);
        return (mx - el.Bounds.absX, my - el.Bounds.absY);
    }

    private int SlotAtPos(double lx, double ly)
    {
        double dx = lx - CanvasSize / 2, dy = ly - CanvasSize / 2;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist <= InnerR) return CenterCancel;
        double angle = Math.Atan2(dy, dx);
        double best = double.MaxValue; int result = -1;
        for (int i = 0; i < SlotAngles.Length; i++)
        {
            double diff = Math.Abs(AngleDiff(angle, SlotAngles[i]));
            if (diff < best) { best = diff; result = i; }
        }
        return result;
    }

    private static double AngleDiff(double a, double b)
    {
        double d = a - b;
        while (d >  Math.PI) d -= 2 * Math.PI;
        while (d < -Math.PI) d += 2 * Math.PI;
        return d;
    }

    // ── Draw ─────────────────────────────────────────────────────────────────

    private void DrawRadial(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        // VS does not pre-scale Cairo for GuiDialog — apply GUI scale so draw fills the canvas correctly
        double sc = GuiElement.scaled(1.0);
        ctx.Scale(sc, sc);
        double cx = CanvasSize / 2, cy = CanvasSize / 2;
        double t  = capi.ElapsedMilliseconds / 1000.0;
        var data  = capi.World.Player?.Entity != null
            ? PlayerSpellData.For(capi.World.Player.Entity) : null;

        // ── Dark background disc ──────────────────────────────────────────────
        double bgR = OrbitR + SlotR + 18;
        ctx.Arc(cx, cy, bgR, 0, Math.PI * 2);
        ctx.SetSourceRGBA(0.04, 0.03, 0.06, 0.72); ctx.Fill();
        ctx.Arc(cx, cy, bgR, 0, Math.PI * 2);
        ctx.SetSourceRGBA(0.18, 0.14, 0.10, 0.55); ctx.LineWidth = 1.5; ctx.Stroke();

        // ── Sigil color — blend from assigned spell elements ──────────────────
        double sigCr = 0.57, sigCg = 0.50, sigCb = 0.78;
        {
            double sumR = 0, sumG = 0, sumB = 0; int filled = 0;
            for (int i = 0; i < 5; i++)
            {
                var sp = data?.GetHotbarSlot(i) is string sid ? SpellRegistry.Get(sid) : null;
                if (sp == null) continue;
                var (er, eg, eb) = Ec(sp.Element);
                sumR += er; sumG += eg; sumB += eb; filled++;
            }
            if (filled > 0)
            {
                sigCr = sumR / filled * 0.6 + 0.57 * 0.4;
                sigCg = sumG / filled * 0.6 + 0.50 * 0.4;
                sigCb = sumB / filled * 0.6 + 0.78 * 0.4;
            }
        }

        // ── Rotating arcane sigil ─────────────────────────────────────────────
        double sigR = OrbitR + SlotR + 24;
        double slow = 0.5 + 0.5 * Math.Sin(t * 0.4);
        double sigA = slow * 0.35 + 0.22;

        ctx.SetSourceRGBA(sigCr, sigCg, sigCb, sigA * 0.35);
        ctx.LineWidth = 6; ctx.Arc(cx, cy, sigR, 0, Math.PI * 2); ctx.Stroke();
        ctx.SetSourceRGBA(sigCr, sigCg, sigCb, sigA);
        ctx.LineWidth = 1.5; ctx.Arc(cx, cy, sigR, 0, Math.PI * 2); ctx.Stroke();
        ctx.SetSourceRGBA(sigCr, sigCg, sigCb, sigA * 0.55);
        ctx.LineWidth = 1; ctx.Arc(cx, cy, sigR * 0.62, 0, Math.PI * 2); ctx.Stroke();

        ctx.SetSourceRGBA(sigCr, sigCg, sigCb, sigA * 0.20);
        ctx.LineWidth = 4;
        for (int k = 0; k < 6; k++)
        {
            double a1 = k * Math.PI / 3 + t * 0.04, a2 = (k + 2) % 6 * Math.PI / 3 + t * 0.04;
            ctx.MoveTo(cx + Math.Cos(a1)*sigR, cy + Math.Sin(a1)*sigR);
            ctx.LineTo(cx + Math.Cos(a2)*sigR, cy + Math.Sin(a2)*sigR); ctx.Stroke();
        }
        ctx.SetSourceRGBA(sigCr, sigCg, sigCb, sigA * 0.80);
        ctx.LineWidth = 1;
        for (int k = 0; k < 6; k++)
        {
            double a1 = k * Math.PI / 3 + t * 0.04, a2 = (k + 2) % 6 * Math.PI / 3 + t * 0.04;
            ctx.MoveTo(cx + Math.Cos(a1)*sigR, cy + Math.Sin(a1)*sigR);
            ctx.LineTo(cx + Math.Cos(a2)*sigR, cy + Math.Sin(a2)*sigR); ctx.Stroke();
        }

        // ── Orbit rings ───────────────────────────────────────────────────────
        ctx.SetSourceRGBA(0.125, 0.110, 0.086, 1); ctx.LineWidth = 1.5;
        ctx.Arc(cx, cy, OrbitR + SlotR + 10, 0, Math.PI * 2); ctx.Stroke();
        ctx.SetSourceRGBA(0.118, 0.102, 0.078, 1); ctx.LineWidth = 1;
        ctx.Arc(cx, cy, OrbitR, 0, Math.PI * 2); ctx.Stroke();

        // ── Element-colored arc segments ──────────────────────────────────────
        double sliceAngle = 2 * Math.PI / 5;
        double gap = 0.10;
        for (int i = 0; i < 5; i++)
        {
            double mid = SlotAngles[i];
            var sp2 = data?.GetHotbarSlot(i) is string s2 ? SpellRegistry.Get(s2) : null;
            bool hov2 = hoveredSlot == i;
            if (sp2 == null && !hov2) continue;
            var (ar, ag, ab) = sp2 != null ? Ec(sp2.Element) : (CB[0], CB[1], CB[2]);
            double pulse = hov2 ? 0.5 + 0.5 * Math.Sin(t * 5.0) : 1.0;

            ctx.SetSourceRGBA(ar, ag, ab, 0.18 * pulse);
            ctx.LineWidth = 9;
            ctx.Arc(cx, cy, OrbitR, mid - sliceAngle/2 + gap, mid + sliceAngle/2 - gap); ctx.Stroke();
            ctx.SetSourceRGBA(ar, ag, ab, 0.75 * pulse);
            ctx.LineWidth = 2;
            ctx.Arc(cx, cy, OrbitR, mid - sliceAngle/2 + gap, mid + sliceAngle/2 - gap); ctx.Stroke();
        }

        // ── Spell slots ───────────────────────────────────────────────────────
        for (int i = 0; i < 5; i++)
        {
            double sx = cx + Math.Cos(SlotAngles[i]) * OrbitR;
            double sy = cy + Math.Sin(SlotAngles[i]) * OrbitR;
            bool hov  = hoveredSlot == i;
            bool isSel = selectedSlot == i;

            var spell = data?.GetHotbarSlot(i) is string sid ? SpellRegistry.Get(sid) : null;
            var (er, eg, eb) = spell != null ? Ec(spell.Element) : (0.38, 0.36, 0.52);
            double vr = hov ? SlotR + 5 : SlotR;

            // Glow
            if (hov || isSel)
            {
                ctx.SetSourceRGBA(er, eg, eb, hov ? 0.28 : 0.14);
                ctx.Arc(sx, sy, vr + 10, 0, Math.PI * 2); ctx.Fill();
            }
            // Fill
            ctx.SetSourceRGBA(er * 0.14, eg * 0.14, eb * 0.14, 0.92);
            ctx.Arc(sx, sy, vr, 0, Math.PI * 2); ctx.Fill();
            // Border
            ctx.SetSourceRGBA(er, eg, eb, hov ? 1.0 : isSel ? 0.85 : 0.55);
            ctx.LineWidth = hov ? 2.5 : isSel ? 2.0 : 1.5;
            ctx.Arc(sx, sy, vr, 0, Math.PI * 2); ctx.Stroke();

            if (spell != null)
            {
                // Large initial letter
                ctx.SelectFontFace("Serif", FontSlant.Normal, FontWeight.Bold);
                ctx.SetFontSize(SlotR * 0.95);
                ctx.SetSourceRGBA(er, eg, eb, hov ? 1.0 : 0.85);
                var te = ctx.TextExtents(spell.Name[0].ToString());
                ctx.MoveTo(sx - te.Width/2 - te.XBearing, sy - te.Height/2 - te.YBearing);
                ctx.ShowText(spell.Name[0].ToString());

                // Full name below (1–2 lines, fixed position)
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                ctx.SetFontSize(10);
                ctx.SetSourceRGBA(er, eg, eb, hov ? 0.95 : 0.70);
                var parts = spell.Name.Split(' ');
                if (parts.Length == 1)
                {
                    Tc(ctx, spell.Name, sx, sy + SlotR + 16);
                }
                else
                {
                    int half = parts.Length / 2;
                    Tc(ctx, string.Join(" ", parts.Take(half)), sx, sy + SlotR + 16);
                    Tc(ctx, string.Join(" ", parts.Skip(half)), sx, sy + SlotR + 28);
                }
            }
            else
            {
                ctx.SetSourceRGBA(0.38, 0.36, 0.55, 0.35); ctx.LineWidth = 1.5;
                ctx.MoveTo(sx - 10, sy); ctx.LineTo(sx + 10, sy); ctx.Stroke();
                ctx.MoveTo(sx, sy - 10); ctx.LineTo(sx, sy + 10); ctx.Stroke();
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
                ctx.SetFontSize(10); ctx.SetSourceRGBA(0.50, 0.48, 0.65, 0.45);
                Tc(ctx, $"Slot {i + 1}", sx, sy + SlotR + 16);
            }

            // Slot number
            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(11); ctx.SetSourceRGBA(CB[0], CB[1], CB[2], hov ? 0.85 : 0.55);
            Tc(ctx, (i + 1).ToString(), sx, sy - SlotR - 6);
        }

        // ── Center cancel circle ──────────────────────────────────────────────
        bool centHov = hoveredSlot == CenterCancel;
        double pulse2 = centHov ? 0.5 + 0.5 * Math.Sin(t * 5.0) : 0;

        if (centHov)
        {
            ctx.SetSourceRGBA(0.75, 0.20, 0.20, 0.20 + 0.10 * pulse2);
            ctx.Arc(cx, cy, InnerR + 10, 0, Math.PI * 2); ctx.Fill();
        }
        ctx.SetSourceRGBA(0.12, 0.08, 0.07, 1);
        ctx.Arc(cx, cy, InnerR, 0, Math.PI * 2); ctx.Fill();
        ctx.SetSourceRGBA(centHov ? 0.90 : 0.55, centHov ? 0.25 : 0.22, centHov ? 0.25 : 0.20,
                          centHov ? 0.9 + 0.1 * pulse2 : 0.55);
        ctx.LineWidth = centHov ? 2 : 1.5;
        ctx.Arc(cx, cy, InnerR, 0, Math.PI * 2); ctx.Stroke();

        double xr = InnerR * 0.42;
        ctx.SetSourceRGBA(0.88, 0.28, 0.28, centHov ? 0.90 + 0.10 * pulse2 : 0.55);
        ctx.LineWidth = centHov ? 2.5 : 1.8;
        ctx.MoveTo(cx - xr, cy - xr); ctx.LineTo(cx + xr, cy + xr); ctx.Stroke();
        ctx.MoveTo(cx + xr, cy - xr); ctx.LineTo(cx - xr, cy + xr); ctx.Stroke();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Draw text horizontally centered at (cx, baseline y).</summary>
    private static void Tc(Context ctx, string t, double cx, double y)
    { var e = ctx.TextExtents(t); ctx.MoveTo(cx - e.Width/2 - e.XBearing, y); ctx.ShowText(t); }

    private static (double r, double g, double b) Ec(SpellElement el) => el switch
    {
        SpellElement.Fire  => (0.878, 0.314, 0.188),
        SpellElement.Water => (0.188, 0.502, 0.784),
        SpellElement.Earth => (0.686, 0.490, 0.216),
        SpellElement.Air   => (0.843, 0.863, 0.922),
        _                  => (0.55, 0.55, 0.65),
    };

    // ── GuiDialog overrides ───────────────────────────────────────────────────

    public override string ToggleKeyCombinationCode => null!;
    public override bool PrefersUngrabbedMouse => true;
    public override bool ShouldReceiveKeyboardEvents() => false;
    public override bool ShouldReceiveMouseEvents()    => false;
    public override EnumDialogType DialogType => EnumDialogType.HUD;

    /// <summary>Called from api.Event.MouseDown so HUD type doesn't block player movement.</summary>
    public bool HandleClick(int x, int y)
    {
        if (!isOpen) return false;
        double sc    = GuiElement.scaled(1.0);
        var (lx, ly) = Local(x, y);
        int slot     = SlotAtPos(lx / sc, ly / sc);
        if (slot >= 0) ApplySlot(slot);
        isOpen = false;
        if (_tickId >= 0) { capi.World.UnregisterGameTickListener(_tickId); _tickId = -1; }
        TryClose();
        indicator.RedrawIndicator();
        return true;
    }

    public override void Dispose()
    {
        if (_tickId >= 0) capi.World.UnregisterGameTickListener(_tickId);
        indicator.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// Small indicator pill HUD — always visible, shows selected spell.
/// </summary>
public class IndicatorHud : HudElement
{
    private readonly HudRadialMenu owner;

    public IndicatorHud(ICoreClientAPI capi, HudRadialMenu owner) : base(capi)
    {
        this.owner = owner;
        Compose();
    }

    private const double BoxSize = 46;
    private const double LabelW  = 130;
    private const double TotalW  = BoxSize + 6 + LabelW;

    private void Compose()
    {
        // Left of flux orb which sits at -(460/2 + 8 + 92) from center
        var db = ElementBounds.Fixed(EnumDialogArea.CenterBottom, -(460 / 2 + 8 + 92 + 6 + TotalW), -BoxSize - 4, TotalW, BoxSize);
        var cb = ElementBounds.Fixed(0, 0, TotalW, BoxSize);
        SingleComposer = capi.Gui
            .CreateCompo("snr:indicator", db)
            .AddDynamicCustomDraw(cb, DrawIndicator, "canvas")
            .Compose();
    }

    public void RedrawIndicator()
        => (SingleComposer?.GetElement("canvas") as GuiElementCustomDraw)?.Redraw();

    public override void OnRenderGUI(float deltaTime)
    {
        // Rendering handled by HudFlux (combined orb + spell panel). No-op here.
    }

    private void DrawIndicator(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        var data    = capi.World.Player?.Entity != null ? PlayerSpellData.For(capi.World.Player.Entity) : null;
        int sel     = owner.SelectedSlot;
        string? spellId = sel >= 0 ? data?.GetHotbarSlot(sel) : null;
        var spell   = spellId != null ? SpellRegistry.Get(spellId) : null;
        var (er, eg, eb) = spell != null ? Ec(spell.Element) : (0.35, 0.35, 0.50);

        // ── Box ──────────────────────────────────────────────────
        ctx.Rectangle(0, 0, BoxSize, BoxSize);
        ctx.SetSourceRGBA(0.05, 0.05, 0.14, 0.92); ctx.Fill();

        if (spell != null)
        {
            ctx.Rectangle(0, 0, BoxSize, BoxSize);
            ctx.SetSourceRGBA(er, eg, eb, 0.10); ctx.Fill();
        }

        ctx.Rectangle(0.75, 0.75, BoxSize - 1.5, BoxSize - 1.5);
        ctx.SetSourceRGBA(0.75, 0.62, 0.25, spell != null ? 0.85 : 0.25);
        ctx.LineWidth = spell != null ? 1.8 : 1.0; ctx.Stroke();

        if (spell != null)
        {
            ctx.SelectFontFace("Serif", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(24);
            ctx.SetSourceRGBA(er, eg, eb, 1.0);
            string letter = spell.Name[0].ToString();
            var te = ctx.TextExtents(letter);
            ctx.MoveTo(BoxSize / 2 - te.Width / 2 - te.XBearing, BoxSize / 2 - te.YBearing - te.Height / 2 + 1);
            ctx.ShowText(letter);
        }
        else
        {
            ctx.SetSourceRGBA(0.35, 0.35, 0.50, 0.35); ctx.LineWidth = 1.5;
            ctx.MoveTo(BoxSize/2 - 8, BoxSize/2); ctx.LineTo(BoxSize/2 + 8, BoxSize/2); ctx.Stroke();
            ctx.MoveTo(BoxSize/2, BoxSize/2 - 8); ctx.LineTo(BoxSize/2, BoxSize/2 + 8); ctx.Stroke();
        }

        // ── Name + flux label ─────────────────────────────────────
        double lx = BoxSize + 6;
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(12);
        ctx.SetSourceRGBA(0.90, 0.88, 1.00, spell != null ? 0.95 : 0.35);
        ctx.MoveTo(lx, BoxSize / 2 - 1);
        ctx.ShowText(spell?.Name ?? "No spell");

        if (spell != null)
        {
            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
            ctx.SetFontSize(9);
            ctx.SetSourceRGBA(er, eg, eb, 0.70);
            ctx.MoveTo(lx, BoxSize / 2 + 13);
            ctx.ShowText($"{spell.FluxCost} Flux  ·  {spell.CastTime:0.#}s");
        }
    }

    private static (double r, double g, double b) Ec(SpellElement el) => el switch
    {
        SpellElement.Fire  => (0.878, 0.314, 0.188),
        SpellElement.Water => (0.188, 0.502, 0.784),
        SpellElement.Earth => (0.686, 0.490, 0.216),
        SpellElement.Air   => (0.843, 0.863, 0.922),
        _                  => (0.55, 0.55, 0.65),
    };

    public override string ToggleKeyCombinationCode => null!;
    public override bool ShouldReceiveKeyboardEvents() => false;
    public override bool ShouldReceiveMouseEvents() => false;
    public override EnumDialogType DialogType => EnumDialogType.HUD;
    public override bool TryClose() => false;
}
