using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using SpellsAndRunes.Spells;

namespace SpellsAndRunes.HUD;

/// <summary>
/// Radial spell selector as a proper GuiDialog (same as spellbook) so the mouse cursor is visible.
/// The indicator pill is a separate HudElement.
/// </summary>
public class HudRadialMenu : GuiDialog
{
    private bool isOpen      = false;
    private int  hoveredSlot = -1;
    private int  selectedSlot = 0;  // -1=none, 0/1=hotbar index

    private const double OrbitR    = 115;
    private const double SlotR     = 42;
    private const double InnerDead = 28;
    private const double CanvasSize = (OrbitR + SlotR + 80) * 2;  // 80px padding to avoid clipping

    // Slot 0=left, 1=right, 2=bottom, 3=top(cancel)
    private static readonly double[] SlotAngles = { Math.PI, 0, Math.PI / 2, 3 * Math.PI / 2 };
    private const int ClearSlot = 3;

    private static readonly double[] CB  = { 0.75, 0.62, 0.25, 1.00 };
    private static readonly double[] CBg = { 0.06, 0.06, 0.16, 0.94 };
    private static readonly double[] CT  = { 0.90, 0.88, 1.00, 1.00 };
    private static readonly double[] CS  = { 0.65, 0.60, 0.85, 0.80 };
    private static readonly double[] CA  = { 0.45, 0.20, 0.80, 0.90 };

    // Separate indicator HUD
    private readonly IndicatorHud indicator;

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

    // ---- Public API ----

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
        isOpen      = true;
        hoveredSlot = -1;
        ComposeRadial();
        TryOpen();
        indicator.RedrawIndicator();
    }

    public void Close()
    {
        if (!isOpen) return;
        if (hoveredSlot >= 0) ApplySlot(hoveredSlot);
        isOpen = false;
        TryClose();
        indicator.RedrawIndicator();
    }

    public bool OnRadialMouseDown(double mx, double my)
    {
        if (!isOpen) return false;
        // Convert screen pixels to canvas-local coords
        double cx = capi.Render.FrameWidth  / 2.0;
        double cy = capi.Render.FrameHeight / 2.0;
        int slot = SlotAtPos(mx, my, cx, cy);
        if (slot < 0) return false;
        ApplySlot(slot);
        isOpen = false;
        TryClose();
        indicator.RedrawIndicator();
        return true;
    }

    public void UpdateMouse(double mx, double my)
    {
        if (!isOpen) return;
        double cx = capi.Render.FrameWidth  / 2.0;
        double cy = capi.Render.FrameHeight / 2.0;
        int prev    = hoveredSlot;
        hoveredSlot = SlotAtPos(mx, my, cx, cy);
        if (hoveredSlot != prev) Redraw();
    }

    private void ApplySlot(int slot)
        => selectedSlot = (slot == ClearSlot) ? -1 : slot;

    private int SlotAtPos(double mx, double my, double cx, double cy)
    {
        double dx = mx - cx, dy = my - cy;
        if (Math.Sqrt(dx * dx + dy * dy) <= InnerDead) return -1;
        double angle = Math.Atan2(dy, dx);
        double best  = double.MaxValue;
        int result   = -1;
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

    // ---- Draw: radial ----

    private void DrawRadial(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        double cx = CanvasSize / 2, cy = CanvasSize / 2;
        var data = capi.World.Player?.Entity != null
            ? PlayerSpellData.For(capi.World.Player.Entity) : null;

        // Backdrop
        ctx.Arc(cx, cy, OrbitR + SlotR + 18, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(0, 0, 0, 0.45);
        ctx.Fill();

        ctx.Arc(cx, cy, OrbitR, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(CB[0], CB[1], CB[2], 0.15);
        ctx.LineWidth = 1; ctx.Stroke();

        for (int i = 0; i < SlotAngles.Length; i++)
        {
            double sx = cx + Math.Cos(SlotAngles[i]) * OrbitR;
            double sy = cy + Math.Sin(SlotAngles[i]) * OrbitR;
            bool hov  = hoveredSlot == i;
            bool isCl = i == ClearSlot;
            bool isSel = !isCl && selectedSlot == i;

            string? spellId = (!isCl && data != null) ? data.GetHotbarSlot(i) : null;
            var spell = spellId != null ? SpellRegistry.Get(spellId) : null;
            var (er, eg, eb) = spell != null ? Ec(spell.Element)
                             : isCl           ? (0.55, 0.20, 0.20)
                             :                  (0.40, 0.40, 0.55);

            if (hov) { ctx.Arc(sx, sy, SlotR + 10, 0, 2 * Math.PI); ctx.SetSourceRGBA(er, eg, eb, 0.20); ctx.Fill(); }

            ctx.Arc(sx, sy, SlotR, 0, 2 * Math.PI);
            if (hov) ctx.SetSourceRGBA(isCl ? 0.50 : CA[0], isCl ? 0.10 : CA[1], isCl ? 0.10 : CA[2], 0.92);
            else if (isSel) ctx.SetSourceRGBA(er * 0.30, eg * 0.30, eb * 0.30, 0.92);
            else ctx.SetSourceRGBA(CBg[0], CBg[1], CBg[2], CBg[3]);
            ctx.Fill();

            ctx.Arc(sx, sy, SlotR, 0, 2 * Math.PI);
            ctx.SetSourceRGBA(er, eg, eb, hov ? 1.0 : isSel ? 0.85 : 0.45);
            ctx.LineWidth = hov ? 3.0 : isSel ? 2.5 : 1.8; ctx.Stroke();

            if (isCl)
            {
                ctx.SetSourceRGBA(0.90, 0.35, 0.35, hov ? 1.0 : 0.55); ctx.LineWidth = 2.5;
                double cr = SlotR * 0.35;
                ctx.MoveTo(sx - cr, sy - cr); ctx.LineTo(sx + cr, sy + cr); ctx.Stroke();
                ctx.MoveTo(sx + cr, sy - cr); ctx.LineTo(sx - cr, sy + cr); ctx.Stroke();
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(11);
                ctx.SetSourceRGBA(0.90, 0.55, 0.55, hov ? 1.0 : 0.65);
                C(ctx, "None", sx, sy + SlotR + 16);
            }
            else if (spell != null)
            {
                ctx.SelectFontFace("Serif", FontSlant.Normal, FontWeight.Bold);
                ctx.SetFontSize(SlotR * 1.0);
                ctx.SetSourceRGBA(er, eg, eb, hov ? 1.0 : 0.85);
                var te = ctx.TextExtents(spell.Name[0].ToString());
                ctx.MoveTo(sx - te.Width / 2 - te.XBearing, sy - te.YBearing - te.Height / 2);
                ctx.ShowText(spell.Name[0].ToString());

                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(11);
                ctx.SetSourceRGBA(CT[0], CT[1], CT[2], hov ? 1.0 : 0.70);
                C(ctx, spell.Name, sx, sy + SlotR + 16);

                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(9);
                ctx.SetSourceRGBA(er, eg, eb, 0.65);
                C(ctx, $"{spell.FluxCost} Flux", sx, sy + SlotR + 29);
            }
            else
            {
                ctx.SetSourceRGBA(0.45, 0.45, 0.60, 0.35); ctx.LineWidth = 1.5;
                ctx.MoveTo(sx - 10, sy); ctx.LineTo(sx + 10, sy); ctx.Stroke();
                ctx.MoveTo(sx, sy - 10); ctx.LineTo(sx, sy + 10); ctx.Stroke();
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(10);
                ctx.SetSourceRGBA(CS[0], CS[1], CS[2], 0.45);
                C(ctx, $"Slot {i + 1}", sx, sy + SlotR + 16);
            }

            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(10);
            ctx.SetSourceRGBA(CB[0], CB[1], CB[2], 0.60);
            C(ctx, isCl ? "✕" : $"{i + 1}", sx, sy - SlotR - 5);
        }

        ctx.Arc(cx, cy, 5, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(CB[0], CB[1], CB[2], 0.85); ctx.Fill();

        ctx.SelectFontFace("Sans", FontSlant.Italic, FontWeight.Normal); ctx.SetFontSize(10);
        ctx.SetSourceRGBA(CS[0], CS[1], CS[2], 0.55);
        C(ctx, hoveredSlot >= 0 ? "Click or release R" : "Move mouse to select", cx, cy + 18);
    }

    // ---- Helpers ----

    private static void C(Context ctx, string t, double cx, double y)
    { var e = ctx.TextExtents(t); ctx.MoveTo(cx - e.Width / 2 - e.XBearing, y); ctx.ShowText(t); }

    private static (double r, double g, double b) Ec(SpellElement el) => el switch
    {
        SpellElement.Air   => (0.55, 0.85, 1.00),
        SpellElement.Fire  => (1.00, 0.45, 0.15),
        SpellElement.Water => (0.20, 0.60, 1.00),
        SpellElement.Earth => (0.45, 0.80, 0.30),
        _                  => (0.70, 0.70, 0.70),
    };

    // ---- GuiDialog overrides ----

    public override string ToggleKeyCombinationCode => null!;
    public override bool PrefersUngrabbedMouse => true; // cursor visible while radial open
    public override EnumDialogType DialogType => EnumDialogType.Dialog;

    public override void OnMouseDown(MouseEvent args)
    {
        if (args.Button != EnumMouseButton.Left) { base.OnMouseDown(args); return; }

        double cx = capi.Render.FrameWidth  / 2.0;
        double cy = capi.Render.FrameHeight / 2.0;
        int slot = SlotAtPos(args.X, args.Y, cx, cy);
        if (slot >= 0) ApplySlot(slot);

        isOpen = false;
        TryClose();
        indicator.RedrawIndicator();
        args.Handled = true;
    }

    public override void Dispose()
    {
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
        if (!IsOpened()) { TryOpen(); Compose(); }
        var entity = capi.World.Player?.Entity;
        if (entity == null || !PlayerSpellData.For(entity).IsFluxUnlocked) return;
        RedrawIndicator();
        base.OnRenderGUI(deltaTime);
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
        SpellElement.Air   => (0.55, 0.85, 1.00),
        SpellElement.Fire  => (1.00, 0.45, 0.15),
        SpellElement.Water => (0.20, 0.60, 1.00),
        SpellElement.Earth => (0.45, 0.80, 0.30),
        _                  => (0.70, 0.70, 0.70),
    };

    public override string ToggleKeyCombinationCode => null!;
    public override bool ShouldReceiveKeyboardEvents() => false;
    public override bool ShouldReceiveMouseEvents() => false;
    public override EnumDialogType DialogType => EnumDialogType.HUD;
    public override bool TryClose() => false;
}
