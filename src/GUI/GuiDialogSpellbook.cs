using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using SpellsAndRunes.Spells;
using SpellsAndRunes.Network;

namespace SpellsAndRunes.GUI;

public class GuiDialogSpellbook : GuiDialog
{
    // ── Layout constants ──────────────────────────────────────────────────────
    private double DlgW, DlgH;   // computed from screen size in ComposeDialog()
    private const double TitleH  = 28;
    private const double HeaderH = 22;
    private const double TabH    = 26;
    private const double ETabH   = 24;
    private const double NodeW   = 100, NodeH  = 44;
    private const double ColStep = 155, RowStep = 85;
    private const int    WheelSlots = 5;

    // ── Colors (ABGR, identical to ImGui version) ─────────────────────────────
    //                                                Fire         Water        Earth        Air
    private static readonly uint[] ElemColors  = { 0xFF3050E0, 0xFFC88030, 0xFF377DAF, 0xFFEBDCD7 };
    private static readonly uint[] ElemNodeBg  = { 0xFF10152a, 0xFF301e0f, 0xFF0A161E, 0xFF181312 };
    private static readonly uint[] ElemTextCol = { 0xFF5070e0, 0xFFd89050, 0xFF4196C3, 0xFFE4D7CD };
    private static readonly SpellElement[] Elements =
        { SpellElement.Fire, SpellElement.Water, SpellElement.Earth, SpellElement.Air };
    private static readonly string[] MainTabs = { "Spell Tree", "Spell Wheel", "Lore", "Runes", "Flux" };
    private static readonly string[] ElemTabs = { "Fire", "Water", "Earth", "Air" };
    private const uint ClrBg     = 0xFF0F1114;   // R=20 G=17 B=15 → neutral dark, barely warm
    private const uint ClrBgAlt  = 0xFF161B20;   // R=32 G=27 B=22 → slightly lighter
    private const uint ClrBorder = 0xFF6b5c45;
    private const uint ClrSep    = 0xFF455c6b;
    private const uint ClrGold   = 0xFF40A8E8;
    private const uint ClrText   = 0xFF9ab8c8;
    private const uint ClrDim    = 0xFF455c6b;
    private const uint ClrSub    = 0xFF556a7a;
    private const uint ClrActive = 0xFF2A8AC8;

    // ── Tab / tree state ──────────────────────────────────────────────────────
    private int    _mainTab = 0, _elemTab = 0;
    private readonly double[] _panX    = new double[4];
    private readonly double[] _panY    = new double[4];
    private readonly bool[]   _panInit = new bool[4];
    private readonly string?[] _selId  = new string?[4];
    private bool   _isPanning = false, _panMoved = false;
    private double _panStartMx, _panStartMy, _panStartPx, _panStartPy;

    // ── Wheel / drag state ────────────────────────────────────────────────────
    private int     _wheelActive = -1;
    private string? _dragId      = null;
    private double  _dragStartX, _dragStartY;
    private bool    _dragStarted = false;
    private double  _mouseX, _mouseY;   // current local mouse pos (updated in Move)
    private string? _pendingWheelAdd = null;

    // ── Hit regions (rebuilt each draw) ──────────────────────────────────────
    private readonly (double x, double y, double w, double h)[] _mainTabR = new (double,double,double,double)[5];
    private readonly (double x, double y, double w, double h)[] _elemTabR = new (double,double,double,double)[4];
    private (double x, double y, double w, double h) _canvasR;
    private readonly List<(string id, double x, double y, double w, double h)> _nodeR = new();
    private readonly (double cx, double cy, double r)[] _wheelSlotR = new (double,double,double)[WheelSlots];
    private readonly List<(string id, double x, double y, double w, double h)> _cardR  = new();
    private (double x, double y, double w, double h) _addBtn, _unlockBtn;
    private bool _hasAddBtn, _hasUnlockBtn;
    private readonly (double cx, double cy, double r)[] _pickerSlotR = new (double,double,double)[WheelSlots];
    private readonly List<(int elemIdx, double x, double y, double w, double h)> _filterBtnR = new(); // -1 = All

    // ── Wheel element filter (-1 = All) ───────────────────────────────────────
    private int _wheelElemFilter = -1;

    // ── Animation & screen coords ─────────────────────────────────────────────
    private double _t = 0;
    private long   _tickId;
    // ── Grain cache ───────────────────────────────────────────────────────────
    private ImageSurface? _grainSurf = null;
    private double        _grainW = 0, _grainH = 0;
    // ── Text measurement cache ────────────────────────────────────────────────
    private readonly Dictionary<string, (double w, double h)> _textMeasure = new();
    // ── Spell list cache per element ──────────────────────────────────────────
    private readonly List<Spell>[] _elemSpells = { new(), new(), new(), new() };
    private bool _elemSpellsCached = false;
    // ── Compose guard (avoid double-compose on first open) ────────────────────
    private int _lastComposeFrameW = 0, _lastComposeFrameH = 0;

    private readonly IClientNetworkChannel _ch;

    // ── Constructor ───────────────────────────────────────────────────────────
    public GuiDialogSpellbook(ICoreClientAPI capi, IClientNetworkChannel channel) : base(capi)
    {
        _ch = channel;
        ComposeDialog();
        // Static tabs (Lore=2, Runes=3) have no _t animations — only redraw them on state changes
        _tickId = capi.World.RegisterGameTickListener(_ => {
            _t = capi.ElapsedMilliseconds / 1000.0;
            if (IsOpened() && _mainTab != 2 && _mainTab != 3) Redraw();
        }, 1000 / 20);
    }

    public override string ToggleKeyCombinationCode => null!;

    public override void OnGuiOpened()
    {
        // Only recompose if frame dimensions changed since last compose
        int fw = capi.Render.FrameWidth, fh = capi.Render.FrameHeight;
        if (fw != _lastComposeFrameW || fh != _lastComposeFrameH) ComposeDialog();
        base.OnGuiOpened();
    }
    public void ReloadData()            { if (IsOpened()) Redraw(); }

    public override bool OnEscapePressed()
    {
        if (_pendingWheelAdd != null) { _pendingWheelAdd = null; Redraw(); return true; }
        return base.OnEscapePressed();
    }

    public override void Dispose()
    {
        capi.World.UnregisterGameTickListener(_tickId);
        _grainSurf?.Dispose();
        _grainSurf = null;
        base.Dispose();
    }

    private ImageSurface BuildGrainSurface(double w, double h)
    {
        int iw = (int)Math.Ceiling(w), ih = (int)Math.Ceiling(h);
        var surf = new ImageSurface(Format.Argb32, iw, ih);
        using var ctx = new Context(surf);
        ctx.LineWidth = 0.5;
        for (double i = 0; i < w + h; i += 5.0)
        {
            ctx.SetSourceRGBA(0.55, 0.42, 0.28, 0.025);
            ctx.MoveTo(Math.Max(0, i - h), i < h ? h - i : 0);
            ctx.LineTo(Math.Min(w, i),     i < w ? 0     : i - w);
            ctx.Stroke();
        }
        return surf;
    }

    private void ComposeDialog()
    {
        _lastComposeFrameW = capi.Render.FrameWidth;
        _lastComposeFrameH = capi.Render.FrameHeight;
        double sc     = GuiElement.scaled(1.0);
        double vw     = _lastComposeFrameW / sc;
        double vh     = _lastComposeFrameH / sc;
        DlgW = Math.Clamp(vw * 0.90, 600, 820);
        DlgH = Math.Clamp(vh * 0.88, 420, 560);

        var db = ElementBounds.Fixed(EnumDialogArea.CenterMiddle, 0, 0, DlgW, DlgH);
        var cb = ElementBounds.Fixed(0, 0, DlgW, DlgH);
        SingleComposer = capi.Gui
            .CreateCompo("spellsandrunes:spellbook", db)
            .AddDynamicCustomDraw(cb, OnDraw, "canvas")
            .Compose();
        // Invalidate caches that depend on dialog dimensions
        _grainSurf?.Dispose(); _grainSurf = null;
        _elemSpellsCached = false;
        _textMeasure.Clear();
    }

    private void Redraw() =>
        (SingleComposer?.GetElement("canvas") as GuiElementCustomDraw)?.Redraw();

    // ── Main draw ─────────────────────────────────────────────────────────────
    private void OnDraw(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        _nodeR.Clear(); _cardR.Clear();
        _hasAddBtn = false; _hasUnlockBtn = false;
        double W = bounds.InnerWidth, H = bounds.InnerHeight;

        var entity = capi.World.Player?.Entity;
        var data   = entity != null ? PlayerSpellData.For(entity) : null;

        // Background
        C(ctx, ClrBg); ctx.Paint();

        // Subtle leather grain — cached surface, blit each frame
        if (_grainSurf == null || Math.Abs(_grainW - W) > 1 || Math.Abs(_grainH - H) > 1)
        {
            _grainSurf?.Dispose();
            _grainSurf = BuildGrainSurface(W, H);
            _grainW = W; _grainH = H;
        }
        ctx.SetSourceSurface(_grainSurf, 0, 0);
        ctx.Paint();

        // Title bar
        C(ctx, ClrBgAlt); Rect(ctx, 0, 0, W, TitleH); ctx.Fill();
        C(ctx, ClrBorder, 0.4); ctx.LineWidth = 1;
        ctx.MoveTo(0, TitleH); ctx.LineTo(W, TitleH); ctx.Stroke();
        ctx.SelectFontFace("Serif", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(13); C(ctx, ClrGold);
        var (_, sbH) = TSize(ctx, "Spellbook");
        TextAt(ctx, "Spellbook", 12, (TitleH - sbH) / 2);

        // Header: Flux Alignment + ornamental separator
        int maxLvl = data != null ? Elements.Max(el => data.GetElementLevel(el)) : 1;
        string[] roman = { "", "I","II","III","IV","V","VI","VII","VIII","IX","X" };
        string sub = $"Flux Alignment  {(maxLvl <= 10 ? roman[maxLvl] : maxLvl.ToString())}";
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(10); C(ctx, ClrSub);
        var (subW, subH) = TSize(ctx, sub);
        TextAt(ctx, sub, W - subW - 12, TitleH + (HeaderH - subH) / 2);

        double sepY = TitleH + HeaderH - 3;
        C(ctx, ClrSep); ctx.LineWidth = 1;
        ctx.MoveTo(6, sepY); ctx.LineTo(W - 6, sepY); ctx.Stroke();
        Dot(ctx, 3, sepY, 2); Dot(ctx, W - 3, sepY, 2);

        // Main tabs
        double tabBarY = TitleH + HeaderH;
        double tabW = W / MainTabs.Length;
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(11);
        for (int i = 0; i < MainTabs.Length; i++)
        {
            double tx = i * tabW;
            bool act = _mainTab == i;
            bool isFluxTab = i == 4;
            _mainTabR[i] = (tx, tabBarY, tabW, TabH);
            if (act)
            {
                C(ctx, ClrBgAlt); Rect(ctx, tx, tabBarY, tabW, TabH); ctx.Fill();
                if (isFluxTab)
                {
                    ctx.SetSourceRGBA(0.76, 0.39, 0.85, 0.55); ctx.LineWidth = 2;
                }
                else
                {
                    C(ctx, ClrGold, 0.55); ctx.LineWidth = 2;
                }
                ctx.MoveTo(tx + 4, tabBarY + TabH - 1);
                ctx.LineTo(tx + tabW - 4, tabBarY + TabH - 1); ctx.Stroke();
            }
            if (isFluxTab)
                ctx.SetSourceRGBA(act ? 0.87 : 0.67, act ? 0.55 : 0.38, act ? 0.95 : 0.72, act ? 1.0 : 0.55);
            else
                C(ctx, act ? ClrGold : ClrText, act ? 0.95 : 0.50);
            TextCenter(ctx, MainTabs[i], tx + tabW / 2, tabBarY + TabH / 2 + 4);
        }
        C(ctx, ClrSep, 0.4); ctx.LineWidth = 1;
        ctx.MoveTo(0, tabBarY + TabH); ctx.LineTo(W, tabBarY + TabH); ctx.Stroke();

        // Content
        double cy = tabBarY + TabH + 2;
        switch (_mainTab)
        {
            case 0: DrawSpellTreeContent(ctx, 0, cy, W, H - cy, data); break;
            case 1: DrawSpellWheelTab(ctx, 0, cy, W, H - cy, data);    break;
            case 2: DrawLoreTab(ctx, 0, cy, W, H - cy); break;
            case 3: DrawRunesTab(ctx, 0, cy, W, H - cy); break;
            case 4: DrawFluxAlignmentTab(ctx, 0, cy, W, H - cy, data); break;
        }

        // Slot picker overlay
        if (_pendingWheelAdd != null)
            DrawSlotPickerOverlay(ctx, W, H, data);
    }

    // ── Spell Tree content ────────────────────────────────────────────────────
    private void DrawSpellTreeContent(Context ctx, double x, double y, double w, double h, PlayerSpellData? data)
    {
        // Only show tabs for unlocked elements; null data = show all (debug)
        var visible = Enumerable.Range(0, 4)
            .Where(j => data == null || data.IsElementUnlocked(Elements[j]))
            .ToList();

        if (visible.Count == 0) visible.Add(0); // fallback: always show at least Fire

        // If current tab is no longer visible, snap to first visible
        if (!visible.Contains(_elemTab)) _elemTab = visible[0];

        // Clear all hit regions, then fill only visible ones
        for (int j = 0; j < 4; j++) _elemTabR[j] = (-9999, -9999, 0, 0);

        double etabW = w / visible.Count;
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(11);
        for (int vi = 0; vi < visible.Count; vi++)
        {
            int    j   = visible[vi];
            double tx  = x + vi * etabW;
            bool   act = _elemTab == j;
            uint   ec  = ElemColors[j];
            _elemTabR[j] = (tx, y, etabW, ETabH);

            C(ctx, ec, act ? 0.28 : 0.08); Rect(ctx, tx, y, etabW, ETabH); ctx.Fill();
            if (act)
            {
                C(ctx, ec, 0.7); ctx.LineWidth = 2;
                ctx.MoveTo(tx + 3, y + ETabH - 1); ctx.LineTo(tx + etabW - 3, y + ETabH - 1); ctx.Stroke();
            }
            var (er, eg, eb) = RGBA(ElemColors[j]);
            ctx.SetSourceRGBA(er, eg, eb, act ? 0.95 : 0.55);
            TextCenter(ctx, ElemTabs[j], tx + etabW / 2, y + ETabH / 2 + 4);
            C(ctx, ec, act ? 0.35 : 0.15); ctx.LineWidth = 1;
            Rect(ctx, tx, y, etabW, ETabH); ctx.Stroke();
        }

        double ey = y + ETabH + 2, eh = h - ETabH - 2;
        DrawElementLevelHeader(ctx, x, ey, w, data);
        const double lvlH = 26;
        DrawElementCanvas(ctx, x, ey + lvlH + 2, w, eh - lvlH - 2, data);
    }

    // ── Element level header ──────────────────────────────────────────────────
    private void DrawElementLevelHeader(Context ctx, double x, double y, double w, PlayerSpellData? data)
    {
        var  elem   = Elements[_elemTab];
        uint ec     = ElemColors[_elemTab];
        int  level  = data?.GetElementLevel(elem) ?? 1;
        int  xpIn   = data?.GetElementXpInLevel(elem) ?? 0;
        int  xpNeed = PlayerSpellData.XpForLevel(level);
        int  sp     = data?.GetSkillPoints(elem) ?? 0;
        double xpFr = (double)xpIn / xpNeed;
        const double rH = 24;

        C(ctx, ec, 0.10); RRect(ctx, x + 2, y, w - 4, rH, 3); ctx.Fill();
        C(ctx, ec, 0.28); ctx.LineWidth = 1; RRect(ctx, x + 2, y, w - 4, rH, 3); ctx.Stroke();

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(11);
        string lvlTxt = $"Level {level}";
        var (lvlW, lvlTH) = TSize(ctx, lvlTxt);
        C(ctx, ec, 0.9); TextAt(ctx, lvlTxt, x + 10, y + (rH - lvlTH) / 2);

        string spTxt = $"SP: {sp}";
        var (spW, spTH) = TSize(ctx, spTxt);
        C(ctx, sp > 0 ? 0xFF50E860 : 0xFF605850);
        TextAt(ctx, spTxt, x + w - spW - 14, y + (rH - spTH) / 2);

        double bx = x + lvlW + 22, bw = w - lvlW - spW - 40, by = y + (rH - 8) / 2;
        C(ctx, 0xFF0F0D0A); RRect(ctx, bx, by, bw, 8, 3); ctx.Fill();
        C(ctx, ec, 0.80); RRect(ctx, bx, by, bw * xpFr, 8, 3); ctx.Fill();
        C(ctx, ec, 0.30); ctx.LineWidth = 1; RRect(ctx, bx, by, bw, 8, 3); ctx.Stroke();
    }

    // ── Spell tree canvas ─────────────────────────────────────────────────────
    private void DrawElementCanvas(Context ctx, double x, double y, double cw, double ch, PlayerSpellData? data)
    {
        if (cw < 10 || ch < 10) return;
        const double detailW = 220;
        double canW = cw - detailW - 8;
        int ei = _elemTab;
        if (!_elemSpellsCached)
        {
            for (int j = 0; j < 4; j++)
            {
                _elemSpells[j].Clear();
                _elemSpells[j].AddRange(SpellRegistry.All.Values.Where(s => s.Element == Elements[j]));
            }
            _elemSpellsCached = true;
        }
        var elemSpells = _elemSpells[ei];

        if (!_panInit[ei] && elemSpells.Count > 0)
        {
            int maxC = elemSpells.Max(s => s.TreePosition.col);
            int maxR = elemSpells.Max(s => s.TreePosition.row);
            _panX[ei] = (canW - (maxC * ColStep + NodeW)) / 2;
            _panY[ei] = (ch   - (maxR * RowStep + NodeH)) / 2;
            _panInit[ei] = true;
        }
        int maxRow = elemSpells.Count > 0 ? elemSpells.Max(s => s.TreePosition.row) : 0;
        _canvasR = (x, y, canW, ch);

        ctx.Save();
        ctx.Rectangle(x, y, canW, ch); ctx.Clip();

        // Background — warm dark brown matching ImGui Living Stone theme
        C(ctx, 0xFF060A10); Rect(ctx, x, y, canW, ch); ctx.Fill();

        // Animated star field
        var rng = new Random(ei * 31337);
        for (int i = 0; i < 120; i++)
        {
            double sx  = x + rng.NextDouble() * canW;
            double sy  = y + rng.NextDouble() * ch;
            bool   big = i < 12;
            double r   = big ? rng.NextDouble() * 1.5 + 1.2 : rng.NextDouble() * 0.9 + 0.4;
            double ph  = rng.NextDouble() * Math.PI * 2;
            double spd = big ? rng.NextDouble() * 1.2 + 0.6 : rng.NextDouble() * 2.5 + 0.5;
            double ba  = big ? 0.35 : rng.NextDouble() * 0.20 + 0.05;
            double amp = big ? 0.45 : rng.NextDouble() * 0.30 + 0.10;
            double a   = Math.Clamp(ba + amp * (0.5 + 0.5 * Math.Sin(_t * spd + ph)), 0, 1);
            if (big) ctx.SetSourceRGBA(0.85, 0.78, 1.0, a);
            else     ctx.SetSourceRGBA(0.75, 0.74, 0.68, a);
            ctx.Arc(sx, sy, r, 0, Math.PI * 2); ctx.Fill();
        }

        // Arcane sigil
        double ctrX = x + canW / 2, ctrY = y + ch / 2;
        double sigR = Math.Min(canW, ch) * 0.30;
        var (er2, eg2, eb2) = RGBA(ElemColors[ei]);
        ctx.SetSourceRGBA(er2, eg2, eb2, 0.10); ctx.LineWidth = 1.5;
        ctx.Arc(ctrX, ctrY, sigR, 0, Math.PI * 2); ctx.Stroke();
        ctx.SetSourceRGBA(er2, eg2, eb2, 0.05); ctx.LineWidth = 1;
        ctx.Arc(ctrX, ctrY, sigR * 0.60, 0, Math.PI * 2); ctx.Stroke();
        for (int k = 0; k < 6; k++)
        {
            double a1 = k * Math.PI / 3, a2 = (k + 2) % 6 * Math.PI / 3;
            ctx.MoveTo(ctrX + Math.Cos(a1) * sigR, ctrY + Math.Sin(a1) * sigR);
            ctx.LineTo(ctrX + Math.Cos(a2) * sigR, ctrY + Math.Sin(a2) * sigR);
            ctx.Stroke();
        }

        // Glowing bezier connections
        foreach (var spell in elemSpells)
        {
            var (tx2, ty2) = NodeCtr(x, y, maxRow, spell.TreePosition.col, spell.TreePosition.row);
            foreach (var pid in spell.Prerequisites)
            {
                var pr = SpellRegistry.Get(pid);
                if (pr == null || pr.Element != Elements[ei]) continue;
                var (fx, fy) = NodeCtr(x, y, maxRow, pr.TreePosition.col, pr.TreePosition.row);
                bool both = data?.IsUnlocked(spell.Id) == true && data?.IsUnlocked(pid) == true;
                double dx = (tx2 - fx) * 0.55;
                if (both)
                {
                    ctx.SetSourceRGBA(er2, eg2, eb2, 0.15); ctx.LineWidth = 10;
                    ctx.MoveTo(fx, fy); ctx.CurveTo(fx+dx, fy, tx2-dx, ty2, tx2, ty2); ctx.Stroke();
                    ctx.SetSourceRGBA(er2, eg2, eb2, 0.35); ctx.LineWidth = 3.5;
                    ctx.MoveTo(fx, fy); ctx.CurveTo(fx+dx, fy, tx2-dx, ty2, tx2, ty2); ctx.Stroke();
                    ctx.SetSourceRGBA(er2, eg2, eb2, 0.90); ctx.LineWidth = 1.2;
                    ctx.MoveTo(fx, fy); ctx.CurveTo(fx+dx, fy, tx2-dx, ty2, tx2, ty2); ctx.Stroke();
                }
                else
                {
                    ctx.SetSourceRGBA(0.23, 0.19, 0.13, 1); ctx.LineWidth = 1.5;
                    ctx.MoveTo(fx, fy); ctx.CurveTo(fx+dx, fy, tx2-dx, ty2, tx2, ty2); ctx.Stroke();
                }
            }
        }

        // Nodes
        foreach (var spell in elemSpells)
            DrawSpellNode(ctx, x, y, maxRow, spell, ei, data);

        // Canvas border
        C(ctx, ElemColors[ei], 0.35); ctx.LineWidth = 1.5;
        Rect(ctx, x, y, canW, ch); ctx.Stroke();

        ctx.Restore();

        // Detail panel
        DrawSpellDetail(ctx, x + canW + 8, y, detailW, ch, ei, data);
    }

    // ── Spell node ────────────────────────────────────────────────────────────
    private void DrawSpellNode(Context ctx, double ox, double oy, int maxRow,
                               Spell spell, int ei, PlayerSpellData? data)
    {
        bool unlocked  = data?.IsUnlocked(spell.Id) ?? false;
        bool prereqMet = data != null && SpellTree.CanUnlock(spell.Id, data);
        bool sel       = _selId[ei] == spell.Id;
        var (ntx, nty) = NodeTL(ox, oy, maxRow, spell.TreePosition.col, spell.TreePosition.row);

        _nodeR.Add((spell.Id, ntx, nty, NodeW, NodeH));

        double alpha = unlocked ? 1.0 : prereqMet ? 0.58 : 0.26;
        var (er, eg, eb)  = RGBA(ElemColors[ei]);
        var (nr, ng, nb)  = RGBA(ElemNodeBg[ei]);
        var (tr2, tg, tb) = RGBA(ElemTextCol[ei]);

        // Glow layers
        if (sel)
        {
            double sp = 0.5 + 0.5 * Math.Sin(_t * 5.0);
            ctx.SetSourceRGBA(er, eg, eb, 0.12 + 0.10 * sp);
            RRect(ctx, ntx-16, nty-16, NodeW+32, NodeH+32, 16); ctx.Fill();
            ctx.SetSourceRGBA(er, eg, eb, 0.28 + 0.18 * sp);
            RRect(ctx, ntx-8,  nty-8,  NodeW+16, NodeH+16, 10); ctx.Fill();
        }
        else if (unlocked)
        {
            double up = 0.5 + 0.5 * Math.Sin(_t * 2.0);
            ctx.SetSourceRGBA(er, eg, eb, 0.12 + 0.10 * up);
            RRect(ctx, ntx-10, nty-10, NodeW+20, NodeH+20, 10); ctx.Fill();
            ctx.SetSourceRGBA(er, eg, eb, 0.22 + 0.14 * up);
            RRect(ctx, ntx-5,  nty-5,  NodeW+10, NodeH+10,  7); ctx.Fill();
        }
        else if (prereqMet)
        {
            double pp = 0.5 + 0.5 * Math.Sin(_t * 0.9);
            ctx.SetSourceRGBA(er, eg, eb, 0.08 + 0.07 * pp);
            RRect(ctx, ntx-6, nty-6, NodeW+12, NodeH+12, 7); ctx.Fill();
        }

        // Node body
        ctx.SetSourceRGBA(nr, ng, nb, alpha);
        RRect(ctx, ntx, nty, NodeW, NodeH, 4); ctx.Fill();

        // Selection ring
        if (sel)
        {
            double sp = 0.5 + 0.5 * Math.Sin(_t * 5.0);
            ctx.SetSourceRGBA(er, eg, eb, 0.7 + 0.3 * sp);
            ctx.LineWidth = 2; RRect(ctx, ntx-3, nty-3, NodeW+6, NodeH+6, 6); ctx.Stroke();
        }

        // Border
        ctx.SetSourceRGBA(er, eg, eb, alpha * (sel ? 1.0 : 0.85));
        ctx.LineWidth = sel ? 2.5 : 1.5;
        RRect(ctx, ntx, nty, NodeW, NodeH, 4); ctx.Stroke();

        // Tier pips
        string pip = (int)spell.Tier switch { 1 => "·", 2 => "··", 3 => "···", _ => "····" };
        ctx.SetFontSize(9);
        ctx.SetSourceRGBA(er, eg, eb, alpha * 0.65);
        var (pw, _) = TSizeCached(ctx, pip, 9);
        TextAt(ctx, pip, ntx + NodeW - pw - 4, nty + 12);

        // Name
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(11);
        ctx.SetSourceRGBA(tr2, tg, tb, alpha);
        if (spell.Name.Contains(' '))
        {
            var parts = spell.Name.Split(' ');
            string l1 = string.Join(" ", parts.Take(parts.Length / 2));
            string l2 = string.Join(" ", parts.Skip(parts.Length / 2));
            var (w1, h1) = TSizeCached(ctx, l1, 11); var (w2, h2) = TSizeCached(ctx, l2, 11);
            double sy = nty + (NodeH - h1 - h2 - 3) / 2;
            TextAt(ctx, l1, ntx + (NodeW - w1) / 2, sy);
            TextAt(ctx, l2, ntx + (NodeW - w2) / 2, sy + h1 + 3);
        }
        else
        {
            var (tw, th) = TSizeCached(ctx, spell.Name, 11);
            TextAt(ctx, spell.Name, ntx + (NodeW - tw) / 2, nty + (NodeH - th) / 2);
        }
    }

    // ── Spell detail panel ────────────────────────────────────────────────────
    private void DrawSpellDetail(Context ctx, double x, double y, double w, double h,
                                 int ei, PlayerSpellData? data)
    {
        C(ctx, 0xFF0F0C09, 0.97); Rect(ctx, x, y, w, h); ctx.Fill();
        C(ctx, ElemColors[ei], 0.25); ctx.LineWidth = 1; Rect(ctx, x, y, w, h); ctx.Stroke();

        string? selId = _selId[ei];
        if (selId == null)
        {
            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
            ctx.SetFontSize(11); C(ctx, ClrDim, 0.7);
            TextCenter(ctx, "Select a spell",   x + w/2, y + h/2 - 6);
            TextCenter(ctx, "to view details.", x + w/2, y + h/2 + 10);
            return;
        }
        var spell = SpellRegistry.Get(selId);
        if (spell == null) return;

        bool unlocked  = data?.IsUnlocked(spell.Id) ?? false;
        bool prereqMet = data != null && SpellTree.CanUnlock(spell.Id, data);
        bool canAfford = data != null && SpellTree.CanAffordUnlock(spell.Id, data);
        var (er, eg, eb)  = RGBA(ElemColors[ei]);
        var (tr2, tg, tb) = RGBA(ElemTextCol[ei]);
        double px = x + 8, pw = w - 16, cy = y + 10;

        // Badge
        string tierName = spell.Tier switch
        {
            SpellTier.Novice     => "Novice",
            SpellTier.Apprentice => "Apprentice",
            SpellTier.Adept      => "Adept",
            _                    => "Master",
        };
        string typeName = spell.Type switch
        {
            SpellType.Offense => "Offense",
            SpellType.Defense => "Defense",
            _                 => "Enchant",
        };
        const double bh = 24;
        ctx.SetSourceRGBA(er, eg, eb, unlocked ? 0.35 : 0.16);
        RRect(ctx, px, cy, pw, bh, 3); ctx.Fill();
        ctx.SetSourceRGBA(er, eg, eb, unlocked ? 0.70 : 0.32);
        ctx.LineWidth = 1; RRect(ctx, px, cy, pw, bh, 3); ctx.Stroke();
        ctx.SetFontSize(10);
        ctx.SetSourceRGBA(tr2, tg, tb, unlocked ? 0.95 : 0.48);
        TextCenter(ctx, $"{tierName}  ·  {typeName}", x + w/2, cy + bh/2 + 4);
        cy += bh + 8;

        // Name
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(12);
        C(ctx, ClrText, unlocked ? 1.0 : 0.55);
        TextAt(ctx, spell.Name, px, cy + 2); cy += 18;
        OrnSep(ctx, px, cy, pw); cy += 12;

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        if (unlocked)
        {
            ctx.SetFontSize(10); ctx.SetSourceRGBA(0.69, 0.63, 0.56, 1);
            cy = WrapText(ctx, spell.Description, px, cy, pw, 12) + 4;

            C(ctx, ClrDim, 0.8); ctx.SetFontSize(10);
            TextAt(ctx, $"Flux:  {spell.FluxCost:F0}", px, cy + 2); cy += 14;
            TextAt(ctx, $"Cast:  {spell.CastTime:F1}s", px, cy + 2); cy += 14;
            OrnSep(ctx, px, cy, pw); cy += 12;

            // Spell XP bar
            int sl = data?.GetSpellLevel(spell.Id) ?? 1;
            int xpIn = data?.GetSpellXpInLevel(spell.Id) ?? 0;
            int xpNd = PlayerSpellData.XpForLevel(sl);
            double xpFr = sl >= 10 ? 1.0 : (double)xpIn / xpNd;

            ctx.SetFontSize(10); C(ctx, ClrText, 0.8);
            TextAt(ctx, $"Spell Level  {sl} / 10", px, cy + 2); cy += 14;
            C(ctx, 0xFF0F0D0A); RRect(ctx, px, cy, pw, 8, 3); ctx.Fill();
            ctx.SetSourceRGBA(er, eg, eb, 0.85); RRect(ctx, px, cy, pw * xpFr, 8, 3); ctx.Fill();
            ctx.SetSourceRGBA(er, eg, eb, 0.28); ctx.LineWidth = 1; RRect(ctx, px, cy, pw, 8, 3); ctx.Stroke();
            cy += 10;
            ctx.SetFontSize(10);
            if (sl < 10) { C(ctx, ClrDim, 0.8); TextAt(ctx, $"{xpIn} / {xpNd} XP", px, cy + 2); }
            else { C(ctx, ClrGold, 0.9); TextAt(ctx, "Max Level", px, cy + 2); }
            cy += 14;
            OrnSep(ctx, px, cy, pw); cy += 13;

            // Add to Wheel button
            _addBtn = (px, cy, pw, 22); _hasAddBtn = true;
            ctx.SetSourceRGBA(er, eg, eb, 0.28); RRect(ctx, px, cy, pw, 22, 3); ctx.Fill();
            ctx.SetSourceRGBA(er, eg, eb, 0.55); ctx.LineWidth = 1; RRect(ctx, px, cy, pw, 22, 3); ctx.Stroke();
            ctx.SetFontSize(11); ctx.SetSourceRGBA(tr2, tg, tb, 0.95);
            TextCenter(ctx, "Add to Wheel", x + w/2, cy + 11 + 4);
        }
        else
        {
            int cost = (int)spell.Tier, sp = data?.GetSkillPoints(spell.Element) ?? 0;
            ctx.SetFontSize(10); ctx.SetSourceRGBA(0.91, 0.66, 0.25, 1);
            TextAt(ctx, $"Cost: {cost} SP  (have {sp})", px, cy + 2); cy += 14;

            if (spell.Prerequisites.Count > 0)
            {
                C(ctx, ClrDim, 0.7); TextAt(ctx, "Requires:", px, cy + 2); cy += 14;
                foreach (var pid in spell.Prerequisites)
                {
                    var pr = SpellRegistry.Get(pid);
                    bool done = data?.IsUnlocked(pid) ?? false;
                    if (done) ctx.SetSourceRGBA(0.31, 0.82, 0.31, 1);
                    else      ctx.SetSourceRGBA(0.38, 0.38, 0.78, 1);
                    TextAt(ctx, $"  {(done ? "✓" : "✗")} {pr?.Name ?? pid}", px, cy + 2); cy += 13;
                }
                cy += 4;
            }

            bool canUnlock = prereqMet && canAfford;
            _unlockBtn = (px, cy, pw, 22); _hasUnlockBtn = true;
            ctx.SetSourceRGBA(er, eg, eb, canUnlock ? 0.28 : 0.12);
            RRect(ctx, px, cy, pw, 22, 3); ctx.Fill();
            ctx.SetSourceRGBA(er, eg, eb, canUnlock ? 0.55 : 0.20);
            ctx.LineWidth = 1; RRect(ctx, px, cy, pw, 22, 3); ctx.Stroke();
            ctx.SetFontSize(11); ctx.SetSourceRGBA(tr2, tg, tb, canUnlock ? 0.95 : 0.35);
            TextCenter(ctx, "Unlock Spell", x + w/2, cy + 11); cy += 26;

            if (!prereqMet || !canAfford)
            {
                ctx.SetFontSize(10); ctx.SetSourceRGBA(0.54, 0.31, 0.31, 1);
                TextAt(ctx, !prereqMet ? "Prerequisites not met." : "Not enough Skill Points.", px, cy + 2);
            }
        }
    }

    // ── Spell Wheel tab ───────────────────────────────────────────────────────
    private void DrawSpellWheelTab(Context ctx, double x, double y, double w, double h, PlayerSpellData? data)
    {
        double leftW = Math.Floor(w * 0.42);
        DrawGuiWheel(ctx, x, y, leftW, h, data);
        C(ctx, ClrSep, 0.3); ctx.LineWidth = 1;
        ctx.MoveTo(x + leftW + 4, y); ctx.LineTo(x + leftW + 4, y + h); ctx.Stroke();
        DrawKnownSpells(ctx, x + leftW + 8, y, w - leftW - 8, h, data);

        // Drag preview
        if (_dragId != null)
        {
            var ds = SpellRegistry.Get(_dragId);
            if (ds != null)
            {
                int dei = ElemIdx(ds.Element);
                var (dr, dg, db) = RGBA(ElemColors[dei]);
                const double dw = 100, dh = 30;
                double dtx = _mouseX + 12, dty = _mouseY - dh / 2;
                ctx.SetSourceRGBA(0.063, 0.051, 0.039, 0.91);
                RRect(ctx, dtx, dty, dw, dh, 3); ctx.Fill();
                ctx.SetSourceRGBA(dr, dg, db, 0.9); Rect(ctx, dtx, dty, 4, dh); ctx.Fill();
                ctx.SetSourceRGBA(dr, dg, db, 0.85); ctx.LineWidth = 1;
                RRect(ctx, dtx, dty, dw, dh, 3); ctx.Stroke();
                ctx.SetFontSize(11); C(ctx, ClrText, 0.9);
                TextAt(ctx, ds.Name, dtx + 8, dty + dh / 2 + 4);
            }
        }
    }

    // ── Spell wheel ───────────────────────────────────────────────────────────
    private void DrawGuiWheel(Context ctx, double x, double y, double w, double h, PlayerSpellData? data)
    {
        const double orbitR = 90, slotR = 28;
        double wcx = x + w / 2, wcy = y + h * 0.46;

        // Sigil color = average of all assigned slot element colors; fallback = arcane purple
        double sigCr = 0.57, sigCg = 0.50, sigCb = 0.78;
        {
            double sumR = 0, sumG = 0, sumB = 0; int filled = 0;
            for (int i = 0; i < WheelSlots; i++)
            {
                string? sid = data?.GetHotbarSlot(i);
                var sp2 = sid != null ? SpellRegistry.Get(sid) : null;
                if (sp2 == null) continue;
                var (er, eg, eb) = RGBA(ElemColors[ElemIdx(sp2.Element)]);
                sumR += er; sumG += eg; sumB += eb; filled++;
            }
            if (filled > 0)
            {
                // Blend 60% element average + 40% arcane purple for a mystical feel
                double avgR = sumR / filled, avgG = sumG / filled, avgB = sumB / filled;
                sigCr = avgR * 0.6 + 0.57 * 0.4;
                sigCg = avgG * 0.6 + 0.50 * 0.4;
                sigCb = avgB * 0.6 + 0.78 * 0.4;
            }
        }

        // Arcane sigil (slowly rotating) — glow pass then sharp pass
        double sigR = orbitR + slotR + 18;
        double slow = 0.5 + 0.5 * Math.Sin(_t * 0.4);
        double sigA = slow * 0.35 + 0.22;

        // Glow halo
        ctx.SetSourceRGBA(sigCr, sigCg, sigCb, sigA * 0.35);
        ctx.LineWidth = 6; ctx.Arc(wcx, wcy, sigR, 0, Math.PI * 2); ctx.Stroke();
        // Sharp outer ring
        ctx.SetSourceRGBA(sigCr, sigCg, sigCb, sigA);
        ctx.LineWidth = 1.5; ctx.Arc(wcx, wcy, sigR, 0, Math.PI * 2); ctx.Stroke();
        // Inner ring
        ctx.SetSourceRGBA(sigCr, sigCg, sigCb, sigA * 0.55);
        ctx.LineWidth = 1; ctx.Arc(wcx, wcy, sigR * 0.62, 0, Math.PI * 2); ctx.Stroke();

        // Star lines — glow pass
        ctx.SetSourceRGBA(sigCr, sigCg, sigCb, sigA * 0.20);
        ctx.LineWidth = 4;
        for (int k = 0; k < 6; k++)
        {
            double a1 = k * Math.PI / 3 + _t * 0.04;
            double a2 = (k + 2) % 6 * Math.PI / 3 + _t * 0.04;
            ctx.MoveTo(wcx + Math.Cos(a1)*sigR, wcy + Math.Sin(a1)*sigR);
            ctx.LineTo(wcx + Math.Cos(a2)*sigR, wcy + Math.Sin(a2)*sigR); ctx.Stroke();
        }
        // Star lines — sharp pass
        ctx.SetSourceRGBA(sigCr, sigCg, sigCb, sigA * 0.80);
        ctx.LineWidth = 1;
        for (int k = 0; k < 6; k++)
        {
            double a1 = k * Math.PI / 3 + _t * 0.04;
            double a2 = (k + 2) % 6 * Math.PI / 3 + _t * 0.04;
            ctx.MoveTo(wcx + Math.Cos(a1)*sigR, wcy + Math.Sin(a1)*sigR);
            ctx.LineTo(wcx + Math.Cos(a2)*sigR, wcy + Math.Sin(a2)*sigR); ctx.Stroke();
        }

        // Rings (base)
        ctx.SetSourceRGBA(0.125, 0.110, 0.086, 1); ctx.LineWidth = 1.5;
        ctx.Arc(wcx, wcy, orbitR + slotR + 10, 0, Math.PI * 2); ctx.Stroke();
        ctx.SetSourceRGBA(0.118, 0.102, 0.078, 1); ctx.LineWidth = 1;
        ctx.Arc(wcx, wcy, orbitR, 0, Math.PI * 2); ctx.Stroke();

        // Colored arc segments on orbit ring — one slice per slot, element color
        {
            double sliceAngle = 2 * Math.PI / WheelSlots;
            double gap = 0.10; // radians gap between segments
            for (int i = 0; i < WheelSlots; i++)
            {
                double mid = -Math.PI / 2 + i * sliceAngle;
                string? sid = data?.GetHotbarSlot(i);
                var sp2 = sid != null ? SpellRegistry.Get(sid) : null;
                int sei = sp2 != null ? ElemIdx(sp2.Element) : -1;
                bool active = _wheelActive == i;
                if (sei < 0 && !active) continue;

                var (ar, ag, ab) = sei >= 0 ? RGBA(ElemColors[sei]) : RGBA(ClrGold);
                double pulse = active ? 0.5 + 0.5 * Math.Sin(_t * 5.0) : 1.0;
                double a1 = mid - sliceAngle / 2 + gap;
                double a2 = mid + sliceAngle / 2 - gap;

                // Glow pass
                ctx.SetSourceRGBA(ar, ag, ab, 0.18 * pulse);
                ctx.LineWidth = 9;
                ctx.Arc(wcx, wcy, orbitR, a1, a2); ctx.Stroke();
                // Sharp pass
                ctx.SetSourceRGBA(ar, ag, ab, 0.75 * pulse);
                ctx.LineWidth = 2;
                ctx.Arc(wcx, wcy, orbitR, a1, a2); ctx.Stroke();
            }
        }

        // Center node
        ctx.SetSourceRGBA(0.102, 0.071, 0.031, 1);
        ctx.Arc(wcx, wcy, 22, 0, Math.PI * 2); ctx.Fill();
        ctx.SetSourceRGBA(0.376, 0.314, 0.188, 1); ctx.LineWidth = 1.5;
        ctx.Arc(wcx, wcy, 22, 0, Math.PI * 2); ctx.Stroke();
        ctx.SetFontSize(14); ctx.SetSourceRGBA(0.75, 0.627, 0.251, 1);
        TextCenter(ctx, "●", wcx, wcy + 5);

        // Slots
        for (int i = 0; i < WheelSlots; i++)
        {
            double angle = -Math.PI / 2 + i * 2 * Math.PI / WheelSlots;
            double scx = wcx + Math.Cos(angle) * orbitR;
            double scy = wcy + Math.Sin(angle) * orbitR;
            _wheelSlotR[i] = (scx, scy, slotR);

            string? sid  = data?.GetHotbarSlot(i);
            var spell    = sid != null ? SpellRegistry.Get(sid) : null;
            bool active  = _wheelActive == i;
            bool dragHov = _dragId != null && InCircle(scx, scy, slotR, _mouseX, _mouseY);
            bool hov     = active || dragHov;
            int  sei     = spell != null ? ElemIdx(spell.Element) : -1;
            var (sr, sg, sb) = sei >= 0 ? RGBA(ElemColors[sei]) : (0.22, 0.19, 0.12);
            double vr = hov ? slotR + 4 : slotR;

            ctx.SetSourceRGBA(sr, sg, sb, hov ? 0.30 : 0.14);
            ctx.Arc(scx, scy, vr, 0, Math.PI * 2); ctx.Fill();
            if (hov) { var (ar,ag,ab) = RGBA(ClrActive); ctx.SetSourceRGBA(ar,ag,ab,1); }
            else ctx.SetSourceRGBA(sr, sg, sb, 0.65);
            ctx.LineWidth = hov ? 2.5 : 1.5;
            ctx.Arc(scx, scy, vr, 0, Math.PI * 2); ctx.Stroke();

            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(12);
            if (spell != null)
            {
                // Initial letter inside circle
                ctx.SetSourceRGBA(sr, sg, sb, hov ? 1 : 0.85);
                TextCenter(ctx, spell.Name[0].ToString(), scx, scy + 4);

                // Full name below — split into 2 lines if it contains a space
                ctx.SetFontSize(9);
                ctx.SetSourceRGBA(sr, sg, sb, hov ? 0.95 : 0.70);
                var parts = spell.Name.Split(' ');
                if (parts.Length == 1)
                {
                    TextCenter(ctx, spell.Name, scx, scy + vr + 11);
                }
                else
                {
                    // Split roughly in half by word count
                    int half = parts.Length / 2;
                    string ln1 = string.Join(" ", parts.Take(half));
                    string ln2 = string.Join(" ", parts.Skip(half));
                    TextCenter(ctx, ln1, scx, scy + vr + 11);
                    TextCenter(ctx, ln2, scx, scy + vr + 21);
                }
            }
            else
            {
                ctx.SetSourceRGBA(0.314, 0.282, 0.227, 0.6);
                TextCenter(ctx, "—", scx, scy + 4);
                ctx.SetFontSize(9); C(ctx, ClrDim, 0.4);
                TextCenter(ctx, "Empty", scx, scy + vr + 11);
            }
            // Slot number — bigger and bolder
            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(11); C(ctx, ClrDim, hov ? 0.85 : 0.55);
            TextCenter(ctx, (i + 1).ToString(), scx, scy - vr - 5);
        }
    }

    // ── Known spells ──────────────────────────────────────────────────────────
    private void DrawKnownSpells(Context ctx, double x, double y, double w, double h, PlayerSpellData? data)
    {
        if (data == null) return;
        _filterBtnR.Clear();

        // Header
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(10); C(ctx, ClrDim, 0.7);
        TextAt(ctx, "Known Spells — drag to wheel", x + 4, y + 2);
        C(ctx, ClrSep, 0.4); ctx.LineWidth = 1;
        ctx.MoveTo(x, y + 14); ctx.LineTo(x + w, y + 14); ctx.Stroke();

        // Element filter buttons — only unlocked elements + "All"
        var unlockedElems = Enumerable.Range(0, 4)
            .Where(j => data.IsElementUnlocked(Elements[j]))
            .ToList();

        const double fH = 18;
        double fy = y + 17;
        // "All" button + one per unlocked element
        int btnCount = 1 + unlockedElems.Count;
        double fW = (w - 4) / btnCount;

        // Draw "All" button
        {
            bool act = _wheelElemFilter == -1;
            uint ec = act ? ClrGold : ClrDim;
            C(ctx, ec, act ? 0.22 : 0.10); RRect(ctx, x + 2, fy, fW - 2, fH, 3); ctx.Fill();
            C(ctx, ec, act ? 0.65 : 0.25); ctx.LineWidth = 1;
            RRect(ctx, x + 2, fy, fW - 2, fH, 3); ctx.Stroke();
            C(ctx, ec, act ? 1.0 : 0.50);
            ctx.SetFontSize(10);
            TextCenter(ctx, "All", x + 2 + (fW - 2) / 2, fy + fH / 2 + 4);
            _filterBtnR.Add((-1, x + 2, fy, fW - 2, fH));
        }

        for (int vi = 0; vi < unlockedElems.Count; vi++)
        {
            int j = unlockedElems[vi];
            double bx = x + 2 + (vi + 1) * fW;
            bool act = _wheelElemFilter == j;
            uint ec = ElemColors[j];
            C(ctx, ec, act ? 0.28 : 0.08); RRect(ctx, bx, fy, fW - 2, fH, 3); ctx.Fill();
            C(ctx, ec, act ? 0.70 : 0.22); ctx.LineWidth = 1;
            RRect(ctx, bx, fy, fW - 2, fH, 3); ctx.Stroke();
            var (er, eg, eb) = RGBA(ec);
            ctx.SetSourceRGBA(er, eg, eb, act ? 1.0 : 0.55);
            ctx.SetFontSize(10);
            TextCenter(ctx, ElemTabs[j], bx + (fW - 2) / 2, fy + fH / 2 + 4);
            _filterBtnR.Add((j, bx, fy, fW - 2, fH));
        }

        // Card grid
        double gridY = fy + fH + 4;
        ctx.Save(); ctx.Rectangle(x, gridY, w, h - (gridY - y)); ctx.Clip();

        const int cols = 4; const double cardH = 44;
        double cardW = w / cols, cardCy = gridY + 2;
        int col = 0;
        foreach (var (id, spell) in SpellRegistry.All)
        {
            if (!data.IsUnlocked(id)) continue;
            if (_wheelElemFilter >= 0 && ElemIdx(spell.Element) != _wheelElemFilter) continue;
            DrawSpellCard(ctx, spell, x + col * cardW, cardCy, cardW - 2, cardH);
            if (++col >= cols) { col = 0; cardCy += cardH + 2; }
        }
        ctx.Restore();
    }

    private void DrawSpellCard(Context ctx, Spell spell, double x, double y, double w, double h)
    {
        int ei = ElemIdx(spell.Element);
        var (er, eg, eb) = RGBA(ElemColors[ei]);
        bool hov = InRect(x, y, w, h, _mouseX, _mouseY) && _dragId == null;

        ctx.SetSourceRGBA(hov ? 0.137:0.090, hov ? 0.118:0.075, hov ? 0.094:0.063, 1);
        RRect(ctx, x, y, w, h, 3); ctx.Fill();
        ctx.SetSourceRGBA(er, eg, eb, 0.9); Rect(ctx, x, y, 4, h); ctx.Fill();
        ctx.SetSourceRGBA(er, eg, eb, hov ? 0.60 : 0.28);
        ctx.LineWidth = hov ? 1.5 : 1; RRect(ctx, x, y, w, h, 3); ctx.Stroke();
        ctx.Save(); ctx.Rectangle(x + 7, y, w - 7, h); ctx.Clip();
        ctx.SetFontSize(11); C(ctx, ClrText, 0.9);
        TextAt(ctx, spell.Name, x + 8, y + (h - 11) / 2 + 9);
        ctx.Restore();
        _cardR.Add((spell.Id, x, y, w, h));
    }

    // ── Slot picker overlay ───────────────────────────────────────────────────
    private void DrawSlotPickerOverlay(Context ctx, double W, double H, PlayerSpellData? data)
    {
        var pSpell = SpellRegistry.Get(_pendingWheelAdd!);
        if (pSpell == null) { _pendingWheelAdd = null; return; }
        int  pei = ElemIdx(pSpell.Element);
        var (per, peg, peb) = RGBA(ElemColors[pei]);

        // Full-dialog dim
        ctx.SetSourceRGBA(0, 0, 0, 0.55); Rect(ctx, 0, 0, W, H); ctx.Fill();

        const double orbitR = 105, slotR = 30, backdropR = 160;
        double cx = W / 2, cy = H / 2;

        // Dark backdrop
        double winS = (backdropR + 20) * 2;
        ctx.SetSourceRGBA(0.031, 0.024, 0.016, 0.80);
        RRect(ctx, cx - winS/2, cy - winS/2, winS, winS, 14); ctx.Fill();
        ctx.SetSourceRGBA(0.039, 0.031, 0.024, 0.95);
        ctx.Arc(cx, cy, backdropR, 0, Math.PI * 2); ctx.Fill();
        ctx.SetSourceRGBA(per, peg, peb, 0.30); ctx.LineWidth = 2;
        ctx.Arc(cx, cy, backdropR, 0, Math.PI * 2); ctx.Stroke();
        var (sr2, sg2, sb2) = RGBA(ClrSep);
        ctx.SetSourceRGBA(sr2, sg2, sb2, 1); ctx.LineWidth = 1;
        ctx.Arc(cx, cy, backdropR - 4, 0, Math.PI * 2); ctx.Stroke();

        // Orbit ring
        ctx.SetSourceRGBA(0.118, 0.102, 0.078, 1); ctx.LineWidth = 1.5;
        ctx.Arc(cx, cy, orbitR, 0, Math.PI * 2); ctx.Stroke();

        // Center spell preview
        ctx.SetSourceRGBA(per, peg, peb, 0.18);
        ctx.Arc(cx, cy, 34, 0, Math.PI * 2); ctx.Fill();
        ctx.SetSourceRGBA(per, peg, peb, 0.70); ctx.LineWidth = 2;
        ctx.Arc(cx, cy, 34, 0, Math.PI * 2); ctx.Stroke();
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(13);
        ctx.SetSourceRGBA(per, peg, peb, 1);
        TextCenter(ctx, pSpell.Name[0].ToString(), cx, cy + 5);
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(10);
        C(ctx, ClrText); TextCenter(ctx, pSpell.Name, cx, cy + 48);

        // Header + hint
        var (gr, gg, gb) = RGBA(ClrGold);
        ctx.SetFontSize(11); ctx.SetSourceRGBA(gr, gg, gb, 1);
        TextCenter(ctx, "Choose a wheel slot", cx, cy - backdropR + 18);
        ctx.SetFontSize(10); C(ctx, ClrDim, 0.5);
        TextCenter(ctx, "Esc to cancel", cx, cy + backdropR - 14);

        // Picker slots
        for (int s = 0; s < WheelSlots; s++)
        {
            double angle = -Math.PI / 2 + s * 2 * Math.PI / WheelSlots;
            double scx = cx + Math.Cos(angle) * orbitR;
            double scy = cy + Math.Sin(angle) * orbitR;
            _pickerSlotR[s] = (scx, scy, slotR);

            string? curId = data?.GetHotbarSlot(s);
            var cur = curId != null ? SpellRegistry.Get(curId) : null;
            int sei = cur != null ? ElemIdx(cur.Element) : -1;
            var (ser, seg, seb) = sei >= 0 ? RGBA(ElemColors[sei]) : (0.22, 0.19, 0.12);
            bool hov = InCircle(scx, scy, slotR + 5, _mouseX, _mouseY);
            double vr = hov ? slotR + 5 : slotR;

            ctx.SetSourceRGBA(ser, seg, seb, hov ? 0.30 : 0.14);
            ctx.Arc(scx, scy, vr, 0, Math.PI * 2); ctx.Fill();
            if (hov) ctx.SetSourceRGBA(per, peg, peb, 1);
            else     ctx.SetSourceRGBA(ser, seg, seb, 0.65);
            ctx.LineWidth = hov ? 2.5 : 1.5;
            ctx.Arc(scx, scy, vr, 0, Math.PI * 2); ctx.Stroke();

            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(12);
            if (cur != null)
            {
                ctx.SetSourceRGBA(ser, seg, seb, hov ? 1 : 0.85);
                TextCenter(ctx, cur.Name[0].ToString(), scx, scy + 4);
                ctx.SetFontSize(9); C(ctx, ClrDim, 0.85);
                TextCenter(ctx, cur.Name.Split(' ')[0], scx, scy + vr + 12);
            }
            else
            {
                ctx.SetSourceRGBA(0.314, 0.282, 0.227, 0.7);
                TextCenter(ctx, "—", scx, scy + 4);
                ctx.SetFontSize(9); C(ctx, ClrDim, 0.5);
                TextCenter(ctx, "Empty", scx, scy + vr + 12);
            }
            ctx.SetFontSize(9); C(ctx, ClrDim, 0.65);
            TextCenter(ctx, (s + 1).ToString(), scx, scy - vr - 5);
        }
    }

    // ── Mouse events ──────────────────────────────────────────────────────────
    public override void OnMouseDown(MouseEvent e)
    {
        var (lx, ly) = Local(e);

        if (_pendingWheelAdd != null)
        {
            for (int s = 0; s < WheelSlots; s++)
            {
                var (scx, scy, sr) = _pickerSlotR[s];
                if (InCircle(scx, scy, sr + 5, lx, ly))
                {
                    _ch.SendPacket(new MsgSetHotbarSlot { Slot = s, SpellId = _pendingWheelAdd });
                    _pendingWheelAdd = null; Redraw(); e.Handled = true; return;
                }
            }
            _pendingWheelAdd = null; Redraw(); e.Handled = true; return;
        }

        // Main tabs
        for (int i = 0; i < MainTabs.Length; i++)
            if (InRect(_mainTabR[i], lx, ly)) { _mainTab = i; Redraw(); e.Handled = true; return; }

        // Element tabs
        for (int j = 0; j < 4; j++)
            if (InRect(_elemTabR[j], lx, ly)) { _elemTab = j; _panInit[j] = false; Redraw(); e.Handled = true; return; }

        // Canvas pan/click start
        if (_mainTab == 0 && InRect(_canvasR, lx, ly))
        {
            _isPanning = true; _panMoved = false;
            _panStartMx = lx; _panStartMy = ly;
            _panStartPx = _panX[_elemTab]; _panStartPy = _panY[_elemTab];
            e.Handled = true; return;
        }

        // Wheel slots (left = select, right = clear)
        if (_mainTab == 1)
        {
            // Filter buttons
            foreach (var (fi, fx, fy, fw, fh) in _filterBtnR)
            {
                if (InRect(fx, fy, fw, fh, lx, ly))
                {
                    _wheelElemFilter = _wheelElemFilter == fi ? -1 : fi; // toggle
                    Redraw(); e.Handled = true; return;
                }
            }

            for (int i = 0; i < WheelSlots; i++)
            {
                var (scx, scy, sr) = _wheelSlotR[i];
                if (!InCircle(scx, scy, sr, lx, ly)) continue;
                if (e.Button == EnumMouseButton.Right)
                {
                    _ch.SendPacket(new MsgSetHotbarSlot { Slot = i, SpellId = "" });
                    e.Handled = true; return;
                }
                _wheelActive = _wheelActive == i ? -1 : i;
                Redraw(); e.Handled = true; return;
            }
            // Card click: assign to active slot if one is selected, otherwise start drag
            foreach (var (id, cx, cy, cw, ch) in _cardR)
            {
                if (!InRect(cx, cy, cw, ch, lx, ly)) continue;
                if (_wheelActive >= 0)
                {
                    _ch.SendPacket(new MsgSetHotbarSlot { Slot = _wheelActive, SpellId = id });
                    _wheelActive = -1; Redraw(); e.Handled = true; return;
                }
                _dragStartX = lx; _dragStartY = ly; _dragStarted = true;
                break;
            }
        }

        // Detail buttons
        if (_hasAddBtn && InRect(_addBtn, lx, ly) && _selId[_elemTab] != null)
        { _pendingWheelAdd = _selId[_elemTab]; Redraw(); e.Handled = true; return; }
        if (_hasUnlockBtn && InRect(_unlockBtn, lx, ly) && _selId[_elemTab] != null)
        { _ch.SendPacket(new MsgUnlockSpell { SpellId = _selId[_elemTab]! }); e.Handled = true; return; }

        base.OnMouseDown(e);
    }

    public override void OnMouseMove(MouseEvent e)
    {
        var (lx, ly) = Local(e);
        _mouseX = lx; _mouseY = ly;

        if (_isPanning && _mainTab == 0)
        {
            double dx = lx - _panStartMx, dy = ly - _panStartMy;
            if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3) _panMoved = true;
            if (_panMoved) { _panX[_elemTab] = _panStartPx + dx; _panY[_elemTab] = _panStartPy + dy; Redraw(); }
            e.Handled = true;
        }

        // Initiate drag from card (threshold 6px)
        if (_dragStarted && _dragId == null && _mainTab == 1)
        {
            double dx = lx - _dragStartX, dy = ly - _dragStartY;
            if (dx*dx + dy*dy > 36)
            {
                foreach (var (id, cx, cy, cw, ch) in _cardR)
                    if (InRect(cx, cy, cw, ch, _dragStartX, _dragStartY)) { _dragId = id; break; }
                _dragStarted = false;
            }
        }

        // Redraw during active drag so the dragged card follows the mouse immediately
        if (_dragId != null) Redraw();

        base.OnMouseMove(e);
    }

    public override void OnMouseUp(MouseEvent e)
    {
        var (lx, ly) = Local(e);

        if (_isPanning)
        {
            _isPanning = false;
            if (!_panMoved && _mainTab == 0)
            {
                bool hit = false;
                foreach (var (id, nx, ny, nw, nh) in _nodeR)
                    if (InRect(nx, ny, nw, nh, lx, ly))
                    { _selId[_elemTab] = _selId[_elemTab] == id ? null : id; hit = true; break; }
                if (!hit) _selId[_elemTab] = null;
                Redraw();
            }
            e.Handled = true;
        }

        if (_dragId != null)
        {
            for (int i = 0; i < WheelSlots; i++)
            {
                var (scx, scy, sr) = _wheelSlotR[i];
                if (InCircle(scx, scy, sr, lx, ly))
                { _ch.SendPacket(new MsgSetHotbarSlot { Slot = i, SpellId = _dragId }); break; }
            }
            _dragId = null; Redraw();
        }
        _dragStarted = false;

        base.OnMouseUp(e);
    }

    // ── Lore tab ─────────────────────────────────────────────────────────────
    // ── Flux Alignment tab ────────────────────────────────────────────────────
    private void DrawFluxAlignmentTab(Context ctx, double x, double y, double w, double h, PlayerSpellData? data)
    {
        bool fluxUnlocked = data?.IsFluxUnlocked ?? false;

        // Flux ability chain
        string[] ids      = { "flux_expression_1", "flux_expression_2", "flux_expression_3", "flux_expression_4" };
        string[] tierName = { "Awakening", "Resonance", "Confluence", "Sovereignty" };
        string[] abilName = { "Flux Expression: Whisper", "Flux Expression: Surge", "Flux Expression: Mantle", "Flux Expression: Sovereignty" };
        string[] abilDesc = {
            "A faint ripple of raw flux bleeds outward. Animals flee. Lesser beings flinch.",
            "A visible surge erupts from the body. Hostile will wavers; limbs grow heavy.",
            "Flux saturates and bleeds outward as a mantle. All within feel a crushing presence.",
            "Body and flux speak as one. Those unprepared are overwhelmed — or simply broken.",
        };
        SpellTier[] tiers = { SpellTier.Novice, SpellTier.Apprentice, SpellTier.Adept, SpellTier.Master };
        string[] tierLabel = { "Novice", "Apprentice", "Adept", "Master" };

        // Current highest unlocked tier (1-4, 0 = none)
        int alignTier = 0;
        for (int i = 3; i >= 0; i--)
            if (data?.IsUnlocked(ids[i]) ?? false) { alignTier = i + 1; break; }

        // ── Background decorations ─────────────────────────────────────────────
        var bgr = new Random(77777);
        ctx.LineWidth = 0.6;
        for (int i = 0; i < 5; i++)
        {
            double ocx = x + bgr.NextDouble() * w;
            double ocy = y + bgr.NextDouble() * h;
            double or2 = 40 + bgr.NextDouble() * 120;
            double a1  = bgr.NextDouble() * Math.PI * 2;
            double a2  = a1 + bgr.NextDouble() * Math.PI + 0.5;
            ctx.SetSourceRGBA(0.76, 0.39, 0.85, 0.04 + bgr.NextDouble() * 0.03);
            ctx.Arc(ocx, ocy, or2, a1, a2); ctx.Stroke();
        }

        // ── Vitruvian figure (right 40% of tab) ───────────────────────────────
        double figCx = x + w * 0.745;
        double figCy = y + h * 0.50;
        double scale = Math.Min(w * 0.32, h * 0.78);

        double headR  = scale * 0.115;
        double torsoH = scale * 0.38;
        double armH   = scale * 0.34;   // vertical position of arms from body center
        double armW   = scale * 0.52;   // half-span
        double legW   = scale * 0.24;   // foot x-offset from center
        double legH   = scale * 0.42;   // leg length down

        // Key points
        double headCy  = figCy - torsoH * 0.5 - headR;
        double shouldY = figCy - torsoH * 0.5 + scale * 0.04;
        double hipY    = figCy + torsoH * 0.5;
        double lArmX   = figCx - armW, rArmX = figCx + armW;
        double lFootX  = figCx - legW, rFootX = figCx + legW;
        double footY   = hipY + legH;

        // Outer circle + square (decorative, very dim)
        double outerR = scale * 0.72;
        ctx.SetSourceRGBA(0.76, 0.39, 0.85, fluxUnlocked ? 0.12 : 0.05);
        ctx.LineWidth = 0.8;
        ctx.Arc(figCx, figCy - scale * 0.06, outerR, 0, Math.PI * 2); ctx.Stroke();
        double sqH = outerR * 1.35;
        double sqW = outerR * 1.42;
        ctx.SetSourceRGBA(0.76, 0.39, 0.85, fluxUnlocked ? 0.07 : 0.03);
        ctx.LineWidth = 0.6;
        Rect(ctx, figCx - sqW / 2, figCy - scale * 0.06 - sqH / 2, sqW, sqH); ctx.Stroke();

        // Glow alpha based on alignment tier
        double figAlpha = fluxUnlocked ? 0.28 + alignTier * 0.12 : 0.08;
        double glowAlpha = fluxUnlocked ? 0.08 + alignTier * 0.05 : 0.03;
        ctx.SetSourceRGBA(0.76, 0.39, 0.85, figAlpha);
        ctx.LineWidth = 1.2;

        // Head
        ctx.Arc(figCx, headCy, headR, 0, Math.PI * 2); ctx.Stroke();
        // Torso
        ctx.MoveTo(figCx, headCy + headR); ctx.LineTo(figCx, hipY); ctx.Stroke();
        // Arms
        ctx.MoveTo(lArmX, shouldY); ctx.LineTo(rArmX, shouldY); ctx.Stroke();
        // Legs
        ctx.MoveTo(figCx, hipY); ctx.LineTo(lFootX, footY); ctx.Stroke();
        ctx.MoveTo(figCx, hipY); ctx.LineTo(rFootX, footY); ctx.Stroke();

        // Energy nodes (chakra points) + connecting channels
        double[] nodeX = { figCx, figCx, figCx, figCx, lArmX, rArmX, lFootX, rFootX };
        double[] nodeY = { headCy - headR * 0.4,
                           figCy - torsoH * 0.33,
                           figCy,
                           hipY,
                           shouldY, shouldY,
                           footY, footY };
        for (int ni = 0; ni < nodeX.Length; ni++)
        {
            double pulse = 0.5 + 0.5 * Math.Sin(_t * 1.8 + ni * 0.9);
            double na    = (fluxUnlocked ? 0.55 : 0.15) + (fluxUnlocked ? 0.25 : 0.05) * pulse;
            double nr    = (ni < 4) ? 4.5 : 3.5;
            // Glow
            ctx.SetSourceRGBA(0.76, 0.39, 0.85, glowAlpha + 0.04 * pulse);
            ctx.Arc(nodeX[ni], nodeY[ni], nr + 4, 0, Math.PI * 2); ctx.Fill();
            // Core
            ctx.SetSourceRGBA(0.87, 0.62, 0.95, na);
            ctx.Arc(nodeX[ni], nodeY[ni], nr, 0, Math.PI * 2); ctx.Fill();
        }

        // Channel lines between nodes (faint)
        ctx.SetSourceRGBA(0.76, 0.39, 0.85, fluxUnlocked ? 0.18 : 0.05);
        ctx.LineWidth = 0.7;
        (int a, int b)[] channels = { (0,1),(1,2),(2,3),(3,4),(3,5),(4,6),(5,7) };
        foreach (var (a, b) in channels)
        {
            ctx.MoveTo(nodeX[a], nodeY[a]); ctx.LineTo(nodeX[b], nodeY[b]); ctx.Stroke();
        }

        // Figure label below
        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal);
        ctx.SetFontSize(9);
        ctx.SetSourceRGBA(0.76, 0.39, 0.85, fluxUnlocked ? 0.45 : 0.20);
        string figLabel = alignTier > 0 ? $"Alignment  {new[]{"I","II","III","IV"}[alignTier-1]}" : "Unaligned";
        var (flW, _) = TSize(ctx, figLabel);
        TextAt(ctx, figLabel, figCx - flW / 2, footY + 16);

        // ── Left panel: 4 tier blocks ──────────────────────────────────────────
        double panelX = x + 14;
        double panelW = w * 0.52 - 20;
        double blockH = (h - 16) / 4.0;

        for (int i = 0; i < 4; i++)
        {
            bool unlocked  = data?.IsUnlocked(ids[i]) ?? false;
            bool available = fluxUnlocked && (i == 0 || (data?.IsUnlocked(ids[i - 1]) ?? false));
            bool isCurrent = alignTier == i + 1;

            double bx = panelX;
            double by = y + 8 + i * blockH;
            double bw = panelW;
            double bh = blockH - 6;

            // Block background
            if (unlocked)
            {
                ctx.SetSourceRGBA(0.76, 0.39, 0.85, 0.08); Rect(ctx, bx, by, bw, bh); ctx.Fill();
            }
            else
            {
                ctx.SetSourceRGBA(0.06, 0.04, 0.08, 0.5); Rect(ctx, bx, by, bw, bh); ctx.Fill();
            }

            // Block border
            double borderAlpha = unlocked ? 0.55 : available ? 0.22 : 0.12;
            ctx.SetSourceRGBA(0.76, 0.39, 0.85, borderAlpha);
            ctx.LineWidth = unlocked ? 1.5 : 1.0;
            Rect(ctx, bx, by, bw, bh); ctx.Stroke();

            // Active tier pulse highlight
            if (isCurrent)
            {
                double pulse = 0.5 + 0.5 * Math.Sin(_t * 2.0);
                ctx.SetSourceRGBA(0.76, 0.39, 0.85, 0.04 + 0.04 * pulse);
                Rect(ctx, bx, by, bw, bh); ctx.Fill();
                ctx.SetSourceRGBA(0.87, 0.62, 0.95, 0.65 + 0.20 * pulse);
                ctx.LineWidth = 2;
                Rect(ctx, bx, by, bw, bh); ctx.Stroke();
            }

            // Tier number circle (left side of block)
            double ncx = bx + 20, ncy = by + bh / 2;
            ctx.SetSourceRGBA(0.76, 0.39, 0.85, unlocked ? 0.70 : 0.18);
            ctx.Arc(ncx, ncy, 13, 0, Math.PI * 2); ctx.Fill();
            ctx.SetSourceRGBA(0.87, 0.62, 0.95, unlocked ? 1.0 : 0.30);
            ctx.Arc(ncx, ncy, 13, 0, Math.PI * 2); ctx.LineWidth = 1.2; ctx.Stroke();

            ctx.SelectFontFace("Serif", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(12);
            ctx.SetSourceRGBA(0.97, 0.90, 1.00, unlocked ? 1.0 : 0.30);
            string[] rom = { "I", "II", "III", "IV" };
            var (rnW, rnH) = TSize(ctx, rom[i]);
            TextAt(ctx, rom[i], ncx - rnW / 2, ncy - rnH / 2);

            double textX = bx + 42;
            double textY = by + 10;

            // Alignment tier name
            ctx.SelectFontFace("Serif", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(12);
            ctx.SetSourceRGBA(0.87, 0.62, 0.95, unlocked ? 0.95 : available ? 0.40 : 0.18);
            TextAt(ctx, $"Flux Alignment — {tierName[i]}", textX, textY);

            // Separator line
            double sepY2 = textY + 16;
            ctx.SetSourceRGBA(0.76, 0.39, 0.85, unlocked ? 0.25 : 0.10);
            ctx.LineWidth = 0.7;
            ctx.MoveTo(textX, sepY2); ctx.LineTo(bx + bw - 8, sepY2); ctx.Stroke();

            // Ability name + tier badge
            double aY = sepY2 + 10;
            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(10);
            ctx.SetSourceRGBA(0.80, 0.55, 0.90, unlocked ? 0.90 : available ? 0.35 : 0.14);
            TextAt(ctx, abilName[i], textX, aY);

            // Tier badge
            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
            ctx.SetFontSize(8);
            ctx.SetSourceRGBA(0.76, 0.39, 0.85, unlocked ? 0.55 : 0.18);
            var (tbW, tbH) = TSize(ctx, tierLabel[i]);
            double badgePad = 4;
            double badgeX = bx + bw - tbW - badgePad * 2 - 6;
            double badgeY = aY - tbH - 1;
            ctx.SetSourceRGBA(0.76, 0.39, 0.85, unlocked ? 0.18 : 0.08);
            Rect(ctx, badgeX, badgeY, tbW + badgePad * 2, tbH + badgePad); ctx.Fill();
            ctx.SetSourceRGBA(0.76, 0.39, 0.85, unlocked ? 0.45 : 0.15);
            ctx.LineWidth = 0.8;
            Rect(ctx, badgeX, badgeY, tbW + badgePad * 2, tbH + badgePad); ctx.Stroke();
            ctx.SetSourceRGBA(0.87, 0.62, 0.95, unlocked ? 0.70 : 0.22);
            TextAt(ctx, tierLabel[i], badgeX + badgePad, badgeY + 2);

            // Description (only if block is tall enough)
            if (bh > 72)
            {
                ctx.SelectFontFace("Sans", FontSlant.Italic, FontWeight.Normal);
                ctx.SetFontSize(9);
                ctx.SetSourceRGBA(0.70, 0.50, 0.80, unlocked ? 0.55 : available ? 0.20 : 0.08);
                WrapText(ctx, abilDesc[i], textX, aY + 16, bw - textX + bx - 10, 13);
            }

            // Lock icon if not available
            if (!unlocked && !available)
            {
                ctx.SetSourceRGBA(0.50, 0.40, 0.55, 0.30);
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
                ctx.SetFontSize(14);
                var (lkW, lkH) = TSize(ctx, "⊘");
                TextAt(ctx, "⊘", bx + bw - lkW - 8, by + bh / 2 - lkH / 2);
            }
        }

        // Not yet unlocked overlay message
        if (!fluxUnlocked)
        {
            ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal);
            ctx.SetFontSize(11);
            ctx.SetSourceRGBA(0.70, 0.45, 0.80, 0.50);
            string msg = "Smoke Sylphweed to awaken your flux alignment.";
            var (mW, _) = TSize(ctx, msg);
            TextAt(ctx, msg, x + (w - mW) / 2, y + h - 18);
        }
    }

    private void DrawLoreTab(Context ctx, double x, double y, double w, double h)
    {
        var bg = new Random(9001);

        // Decorative background — partial arcs and connecting lines
        ctx.LineWidth = 0.7;
        for (int i = 0; i < 7; i++)
        {
            double ocx = x + bg.NextDouble() * w;
            double ocy = y + bg.NextDouble() * h;
            double or2 = 35 + bg.NextDouble() * 110;
            double a1  = bg.NextDouble() * Math.PI * 2;
            double a2  = a1 + bg.NextDouble() * Math.PI + 0.4;
            ctx.SetSourceRGBA(0.75, 0.65, 0.40, 0.06 + bg.NextDouble() * 0.04);
            ctx.Arc(ocx, ocy, or2, a1, a2); ctx.Stroke();
        }
        ctx.LineWidth = 0.5;
        for (int i = 0; i < 6; i++)
        {
            ctx.SetSourceRGBA(0.70, 0.60, 0.35, 0.04);
            double lx1 = x + bg.NextDouble() * w, ly1 = y + bg.NextDouble() * h;
            double lx2 = x + bg.NextDouble() * w, ly2 = y + bg.NextDouble() * h;
            ctx.MoveTo(lx1, ly1); ctx.LineTo(lx2, ly2); ctx.Stroke();
        }
        for (int i = 0; i < 14; i++)
        {
            double dx = x + bg.NextDouble() * w, dy = y + bg.NextDouble() * h;
            ctx.SetSourceRGBA(0.80, 0.72, 0.48, 0.12 + bg.NextDouble() * 0.10);
            ctx.Arc(dx, dy, 1.2, 0, Math.PI * 2); ctx.Fill();
        }

        // Gibberish manuscript text — very dim, italic serif
        string[] frags = {
            "verath", "sulken", "aevi", "mor", "thel", "krishan", "voss", "elthun",
            "darak", "sael", "phyren", "kathos", "ulveri", "mnost", "theryn", "calsh",
            "vorei", "duneth", "alvar", "sirek", "oreth", "valun", "sykhen", "dra",
            "el", "van", "kor", "thal", "ish", "um", "per", "ael", "ver", "keth",
            "yn", "ash", "elu", "vor", "tis", "aneth", "beryn", "caleth", "dynar",
        };
        string[] punct = { " ·", ",", " —", ":", " ∴", " ∵" };

        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal);
        ctx.SetFontSize(10);
        var lr = new Random(12345);
        double ty = y + 14;
        while (ty < y + h - 12)
        {
            double tx = x + 12 + lr.NextDouble() * 22;
            int wc = lr.Next(4, 11);
            double la = 0.07 + lr.NextDouble() * 0.11;
            for (int wi = 0; wi < wc && tx < x + w - 38; wi++)
            {
                string word = frags[lr.Next(frags.Length)];
                if (lr.Next(6) == 0) word += punct[lr.Next(punct.Length)];
                ctx.SetSourceRGBA(0.88, 0.82, 0.65, la * (0.65 + lr.NextDouble() * 0.70));
                var te = ctx.TextExtents(word);
                ctx.MoveTo(tx - te.XBearing, ty - te.YBearing);
                ctx.ShowText(word);
                tx += te.Width + 4 + lr.NextDouble() * 9;
            }
            ty += 13 + lr.NextDouble() * 4;
        }
    }

    // ── Runes tab ─────────────────────────────────────────────────────────────
    private static void DrawRunesTab(Context ctx, double x, double y, double w, double h)
    {
        var rng = new Random(31415);
        const double cellW = 40, cellH = 44;

        // Stone grid lines
        ctx.LineWidth = 0.4;
        ctx.SetSourceRGBA(0.60, 0.52, 0.35, 0.07);
        for (double gx = x + 8; gx < x + w - 8; gx += cellW)
        { ctx.MoveTo(gx, y + 6); ctx.LineTo(gx, y + h - 6); ctx.Stroke(); }
        for (double gy = y + 8; gy < y + h - 8; gy += cellH)
        { ctx.MoveTo(x + 8, gy); ctx.LineTo(x + w - 8, gy); ctx.Stroke(); }

        // Rune glyphs — programmatic angular strokes
        for (double gy = y + cellH * 0.5 + 4; gy < y + h - cellH * 0.4; gy += cellH)
        for (double gx = x + cellW * 0.5 + 4; gx < x + w - cellW * 0.4; gx += cellW)
        {
            int type = rng.Next(16);
            double alpha = 0.09 + rng.NextDouble() * 0.19;
            // Brighter accent runes
            if (rng.Next(8) == 0) alpha = 0.35 + rng.NextDouble() * 0.20;
            DrawRune(ctx, gx, gy, 13, type, alpha);
        }
    }

    private static void DrawRune(Context ctx, double cx, double cy, double size, int type, double alpha)
    {
        ctx.SetSourceRGBA(0.80, 0.72, 0.50, alpha);
        ctx.LineWidth = 1.1;
        // Vertical staff (always)
        ctx.MoveTo(cx, cy - size); ctx.LineTo(cx, cy + size); ctx.Stroke();
        // Branches from bit flags
        double bx = size * 0.60, by = size * 0.30;
        if ((type & 1)  != 0) { ctx.MoveTo(cx, cy - size * 0.45); ctx.LineTo(cx + bx, cy - size * 0.10); ctx.Stroke(); }
        if ((type & 2)  != 0) { ctx.MoveTo(cx, cy - size * 0.45); ctx.LineTo(cx - bx, cy - size * 0.10); ctx.Stroke(); }
        if ((type & 4)  != 0) { ctx.MoveTo(cx, cy + by);          ctx.LineTo(cx + bx, cy + size * 0.60); ctx.Stroke(); }
        if ((type & 8)  != 0) { ctx.MoveTo(cx, cy + by);          ctx.LineTo(cx - bx, cy + size * 0.60); ctx.Stroke(); }
        if ((type & 12) == 12) { ctx.MoveTo(cx - bx * 0.7, cy); ctx.LineTo(cx + bx * 0.7, cy); ctx.Stroke(); } // mid bar
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private (double lx, double ly) Local(MouseEvent e)
    {
        var el = SingleComposer?.GetElement("canvas");
        if (el == null) return (-1, -1);
        // Mouse coords and absX are both in virtual pixels in VS —
        // no scale division needed; the difference is the virtual-px offset directly.
        return (e.X - el.Bounds.absX, e.Y - el.Bounds.absY);
    }

    private void OrnSep(Context ctx, double x, double y, double w)
    {
        C(ctx, ClrSep); ctx.LineWidth = 1;
        ctx.MoveTo(x + 6, y); ctx.LineTo(x + w - 6, y); ctx.Stroke();
        Dot(ctx, x + 2, y, 2); Dot(ctx, x + w - 2, y, 2);
    }

    private static double WrapText(Context ctx, string text, double x, double y, double maxW, double lineH)
    {
        var words = text.Split(' ');
        string line = "";
        double cy = y;
        foreach (var word in words)
        {
            string test = line.Length == 0 ? word : line + " " + word;
            var (tw, _) = TSize(ctx, test);
            if (tw > maxW && line.Length > 0) { TextAt(ctx, line, x, cy); cy += lineH; line = word; }
            else line = test;
        }
        if (line.Length > 0) { TextAt(ctx, line, x, cy); cy += lineH; }
        return cy; // cy is now past the bottom of the last line
    }

    private (double x, double y) NodeTL(double ox, double oy, int maxRow, int col, int row)
        => (ox + _panX[_elemTab] + col * ColStep,
            oy + _panY[_elemTab] + (maxRow - row) * RowStep);

    private (double x, double y) NodeCtr(double ox, double oy, int maxRow, int col, int row)
    { var (tx, ty) = NodeTL(ox, oy, maxRow, col, row); return (tx + NodeW/2, ty + NodeH/2); }

    private static int ElemIdx(SpellElement el) => el switch
    { SpellElement.Fire => 0, SpellElement.Water => 1, SpellElement.Earth => 2, _ => 3 };

    private static (double r, double g, double b) RGBA(uint abgr)
        => ((abgr & 0xFF)/255.0, ((abgr>>8)&0xFF)/255.0, ((abgr>>16)&0xFF)/255.0);

    private static void C(Context ctx, uint abgr, double aOvr = -1)
    {
        var (r,g,b) = RGBA(abgr);
        ctx.SetSourceRGBA(r, g, b, aOvr >= 0 ? aOvr : ((abgr>>24)&0xFF)/255.0);
    }

    private static void Rect(Context ctx, double x, double y, double w, double h)
        => ctx.Rectangle(x, y, w, h);

    private static void RRect(Context ctx, double x, double y, double w, double h, double r)
    {
        if (r <= 0) { ctx.Rectangle(x, y, w, h); return; }
        ctx.NewPath();
        ctx.Arc(x+r,   y+r,   r, Math.PI,         3*Math.PI/2);
        ctx.Arc(x+w-r, y+r,   r, 3*Math.PI/2,     0);
        ctx.Arc(x+w-r, y+h-r, r, 0,               Math.PI/2);
        ctx.Arc(x+r,   y+h-r, r, Math.PI/2,       Math.PI);
        ctx.ClosePath();
    }

    private static void Dot(Context ctx, double cx, double cy, double r)
        { ctx.Arc(cx, cy, r, 0, Math.PI*2); ctx.Fill(); }

    private static void TextAt(Context ctx, string t, double x, double y)
    { var te = ctx.TextExtents(t); ctx.MoveTo(x - te.XBearing, y - te.YBearing); ctx.ShowText(t); }

    private static void TextCenter(Context ctx, string t, double cx, double cy)
    { var te = ctx.TextExtents(t); ctx.MoveTo(cx - te.Width/2 - te.XBearing, cy - te.Height/2 - te.YBearing); ctx.ShowText(t); }
    
    private static (double w, double h) TSize(Context ctx, string t)
    {
        var te = ctx.TextExtents(t);
        return (te.Width, te.Height);
    }

    private (double w, double h) TSizeCached(Context ctx, string t, double fontSize)
    {
        var key = $"{fontSize:F1}|{t}";
        if (_textMeasure.TryGetValue(key, out var cached)) return cached;
        var te = ctx.TextExtents(t);
        var result = (te.Width, te.Height);
        _textMeasure[key] = result;
        return result;
    }

    private static bool InRect((double x,double y,double w,double h) r, double mx, double my)
        => mx>=r.x && mx<=r.x+r.w && my>=r.y && my<=r.y+r.h;
    private static bool InRect(double x, double y, double w, double h, double mx, double my)
        => mx>=x && mx<=x+w && my>=y && my<=y+h;
    private static bool InCircle(double cx, double cy, double r, double mx, double my)
        => (mx-cx)*(mx-cx)+(my-cy)*(my-cy) <= r*r;
}
