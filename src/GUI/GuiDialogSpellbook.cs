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
    private readonly IClientNetworkChannel channel;
    private int currentTab = 0;
    private static readonly string[] TabNames = { "Spells", "Memorized", "Lore", "Runes" };
    private const int TabSpells    = 0;
    private const int TabMemorized = 1;

    // Layout system — single source of truth for ALL coordinates
    private SpellbookLayout L = new();

    // Dialog size from layout
    private double DialogW => L.GuiW;
    private double DialogH => L.GuiH;

    // Elements
    private SpellElement[] Elements = Array.Empty<SpellElement>();
    private int selectedElementIndex = 0;
    private SpellElement SelectedElement => Elements.Length > 0 ? Elements[selectedElementIndex] : SpellElement.Air;
    private static readonly SpellElement[] ElemOrder   = { SpellElement.Fire, SpellElement.Water, SpellElement.Earth, SpellElement.Air };
    private static readonly string[]       ElemTabKeys = { "fire", "water", "earth", "air" };

    // Spell tree
    private readonly List<SpellNode> spellNodes = new();
    private string? hoveredSpellId = null;
    private double treePanX = 0, treePanY = 0;
    private bool   isPanning = false;
    private double panStartMouseX, panStartMouseY, panStartOffsetX, panStartOffsetY;
    private double treeAreaX, treeAreaY, treeAreaW, treeAreaH;

    // Memorized
    private readonly List<SpellCard> memorizedSpellCards = new();
    private readonly (double cx, double cy)[] hotbarSlotPos = new (double, double)[3];

    // Drag & drop
    private string? dragSpellId  = null;
    private int     dragFromSlot = -1;
    private double  dragX, dragY;
    private bool    isDragging   = false;

    // Sprites
    private ImageSurface? bookBgSurface;
    private ImageSurface? closeBookmarkSurface;
    private readonly ImageSurface?[] mainTabActive   = new ImageSurface?[4];
    private readonly ImageSurface?[] mainTabInactive = new ImageSurface?[4];
    private readonly ImageSurface?[] elemTabActive   = new ImageSurface?[4];
    private readonly ImageSurface?[] elemTabInactive = new ImageSurface?[4];

    // Hit regions — populated each draw, used for input
    private readonly double[] mainTabY    = new double[4];
    private readonly double[] mainTabH    = new double[4];
    private readonly double[] elemTabX    = new double[4];
    private readonly double[] elemTabY    = new double[4];
    private readonly double[] elemTabHHit = new double[4];
    private int  hoveredMainTab = -1;
    private int  hoveredElemTab = -1;
    private bool closeHovered   = false;

    // Sprite display heights (computed after sprites load + after each UpdateActualSize)
    private readonly double[] eTabDispH  = new double[4]; // active
    private readonly double[] eTabDispHI = new double[4]; // inactive
    private readonly double[] mTabDispH  = new double[4]; // active
    private readonly double[] mTabDispHI = new double[4]; // inactive
    private double ETabMaxH => eTabDispH.Any(d => d > 0) ? eTabDispH.Max() : L.Y(36);

    // Colors
    private static readonly double[] CT  = { 0.18, 0.12, 0.06, 1.00 }; // text
    private static readonly double[] CS  = { 0.40, 0.30, 0.18, 0.85 }; // subtext
    private static readonly double[] CB  = { 0.55, 0.38, 0.14, 1.00 }; // border

    private static (double r, double g, double b) ElemColor(SpellElement el) => el switch
    {
        SpellElement.Fire  => (0.75, 0.25, 0.06),
        SpellElement.Water => (0.12, 0.40, 0.70),
        SpellElement.Earth => (0.25, 0.50, 0.15),
        SpellElement.Air   => (0.30, 0.55, 0.80),
        _                  => (0.45, 0.38, 0.28),
    };

    // ── Constructor ──────────────────────────────────────────────────────────

    public GuiDialogSpellbook(ICoreClientAPI capi, IClientNetworkChannel channel) : base(capi)
    {
        this.channel = channel;
        RebuildElements();
        LoadTextures();
        ComposeDialog();
    }

    public override void OnGuiOpened()
    {
        L = SpellbookLayout.Load();
        ComposeDialog();
        base.OnGuiOpened();
    }

    private void RebuildElements()
    {
        Elements = SpellRegistry.All.Values
            .Select(s => s.Element).Distinct().OrderBy(e => e).ToArray();
        if (selectedElementIndex >= Elements.Length) selectedElementIndex = 0;
    }

    public void ReloadData() { if (IsOpened()) Redraw(); }
    private void Redraw() => (SingleComposer?.GetElement("canvas") as GuiElementCustomDraw)?.Redraw();

    // ── Textures ─────────────────────────────────────────────────────────────

    private void LoadTextures()
    {
        bookBgSurface        = LoadSurface("textures/gui/book_bg.png");
        closeBookmarkSurface = LoadSurface("textures/gui/close_bookmark.png");
        string[] mn = { "spells", "memorized", "lore", "runes" };
        for (int i = 0; i < 4; i++)
        {
            mainTabActive[i]   = LoadSurface($"textures/gui/main_tab_{mn[i]}_active.png");
            mainTabInactive[i] = LoadSurface($"textures/gui/main_tab_{mn[i]}_inactive.png");
            elemTabActive[i]   = LoadSurface($"textures/gui/elem_tab_{ElemTabKeys[i]}_active.png");
            elemTabInactive[i] = LoadSurface($"textures/gui/elem_tab_{ElemTabKeys[i]}_inactive.png");
        }
    }

    /// Recomputes sprite display heights using current L scale.
    /// Must be called AFTER L.UpdateActualSize() each frame.
    private void RecomputeSpriteSizes()
    {
        double mw = L.ScMTabW;
        double tw = L.ETabW();
        for (int i = 0; i < 4; i++)
        {
            var ma = mainTabActive[i];
            mTabDispH[i]  = ma  != null ? mw * ma.Height  / ma.Width  : L.Y(40);
            var mi = mainTabInactive[i];
            mTabDispHI[i] = mi  != null ? mw * mi.Height  / mi.Width  : L.Y(34);
            var ea = elemTabActive[i];
            eTabDispH[i]  = ea  != null ? tw * ea.Height  / ea.Width  : L.Y(36);
            var ei = elemTabInactive[i];
            eTabDispHI[i] = ei  != null ? tw * ei.Height  / ei.Width  : L.Y(32);
        }
    }

    private ImageSurface? LoadSurface(string assetPath)
    {
        var asset = capi.Assets.TryGet(new AssetLocation("spellsandrunes", assetPath));
        if (asset == null) return null;
        string tmp = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "snr_" + System.IO.Path.GetFileName(assetPath));
        System.IO.File.WriteAllBytes(tmp, asset.Data);
        return new ImageSurface(tmp);
    }

    // ── Compose ──────────────────────────────────────────────────────────────

    private void ComposeDialog()
    {
        var db = ElementBounds.Fixed(EnumDialogArea.CenterMiddle, 0, 0, DialogW, DialogH);
        var cb = ElementBounds.Fixed(0, 0, DialogW, DialogH);
        SingleComposer = capi.Gui
            .CreateCompo("spellsandrunes:spellbook", db)
            .AddDynamicCustomDraw(cb, OnDraw, "canvas")
            .Compose();
    }

    // ── Draw entry point ─────────────────────────────────────────────────────

    private void OnDraw(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        // ── STEP 1: update layout scale first — everything else depends on this ──
        L.UpdateActualSize(bounds.InnerWidth, bounds.InnerHeight);

        // ── STEP 2: recompute sprite heights at new scale ──
        RecomputeSpriteSizes();

        // ── STEP 3: draw book background stretched to fill canvas ──
        if (bookBgSurface != null)
        {
            ctx.Save();
            ctx.Scale(bounds.InnerWidth  / bookBgSurface.Width,
                      bounds.InnerHeight / bookBgSurface.Height);
            ctx.SetSourceSurface(bookBgSurface, 0, 0);
            ctx.Paint();
            ctx.Restore();
        }

        // ── STEP 4: draw chrome ──
        DrawCloseBookmark(ctx);
        DrawMainTabs(ctx);

        // ── STEP 5: draw tab content ──
        switch (currentTab)
        {
            case TabSpells:
                DrawSpellsLeft(ctx);
                DrawSpellsRight(ctx);
                break;
            case TabMemorized:
                DrawMemorizedLeft(ctx);
                DrawMemorizedRight(ctx);
                break;
            default:
                DrawComingSoon(ctx);
                break;
        }
    }

    // ── Close bookmark ────────────────────────────────────────────────────────

    private void DrawCloseBookmark(Context ctx)
    {
        double x = L.ScCloseBookX, y = L.ScCloseBookY, w = L.ScCloseBookW, h = L.ScCloseBookH;
        PaintSprite(ctx, closeBookmarkSurface, x, y, w, h, closeHovered ? 1.0 : 0.85);
        if (closeBookmarkSurface == null)
        {
            RoundedRect(ctx, x, y, w, h, L.D(4));
            ctx.SetSourceRGBA(closeHovered ? 0.55 : 0.35, 0.08, 0.04, 0.85);
            ctx.Fill();
            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(L.D(14));
            ctx.SetSourceRGBA(1, 1, 1, 0.9);
            var te = ctx.TextExtents("X");
            ctx.MoveTo(x + w / 2 - te.Width / 2 - te.XBearing,
                       y + h / 2 + te.Height / 2 - 1);
            ctx.ShowText("X");
        }
    }

    // ── Main tabs (left side, vertical) ──────────────────────────────────────

    private void DrawMainTabs(Context ctx)
    {
        double tx = L.ScMTabX, tw = L.ScMTabW, gap = L.ScMTabGap;
        double ty = L.ScMTabStartY;

        // Compute Y positions and heights for all tabs
        double[] ys = new double[4];
        double[] hs = new double[4];
        for (int i = 0; i < 4; i++)
        {
            bool act = currentTab == i;
            ys[i] = ty;
            hs[i] = act ? mTabDispH[i] : mTabDispHI[i];
            mainTabY[i] = ty;
            mainTabH[i] = hs[i];
            ty += hs[i] + gap;
        }

        // Draw inactive first, active on top
        for (int pass = 0; pass < 2; pass++)
        for (int i   = 0; i < 4; i++)
        {
            bool act = currentTab == i;
            if ((pass == 0) == act) continue;

            var surf = act ? mainTabActive[i] : mainTabInactive[i];
            if (surf != null)
            {
                PaintSprite(ctx, surf, tx, ys[i], tw, hs[i]);
            }
            else
            {
                RoundedRect(ctx, tx, ys[i], tw, hs[i], L.D(4));
                ctx.SetSourceRGBA(
                    act ? 0.70 : 0.50,
                    act ? 0.54 : 0.40,
                    act ? 0.24 : 0.20,
                    act ? 0.90 : 0.55);
                ctx.Fill();
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                ctx.SetFontSize(L.D(10));
                ctx.SetSourceRGBA(CT[0], CT[1], CT[2], 0.85);
                var te = ctx.TextExtents(TabNames[i]);
                ctx.MoveTo(tx + tw / 2 - te.Width / 2 - te.XBearing,
                           ys[i] + hs[i] / 2 + te.Height / 2 - 1);
                ctx.ShowText(TabNames[i]);
            }
        }
    }

    // ── Element tabs (top of right page, horizontal) ──────────────────────────

    private void DrawElementTabs(Context ctx)
    {
        double tw     = L.ETabW();
        double gap    = L.ScETabGap;
        double maxH   = ETabMaxH;
        double pageX  = L.ScRPageX;
        double pageY  = L.ScRPageY;

        for (int pass = 0; pass < 2; pass++)
        for (int i   = 0; i < 4; i++)
        {
            var el  = ElemOrder[i];
            bool act = SelectedElement == el;
            if ((pass == 0) == act) continue;

            bool   exists = Elements.Contains(el);
            double tx     = pageX + i * (tw + gap);
            double dispH  = act ? eTabDispH[i] : eTabDispHI[i];
            // Tabs hang ABOVE pageY — bottom of each tab aligns with top of page
            double tty    = pageY - dispH;

            // Store hit regions
            elemTabX[i]    = tx;
            elemTabY[i]    = tty;
            elemTabHHit[i] = dispH;

            double alpha = exists ? 1.0 : 0.35;
            var surf     = act ? elemTabActive[i] : elemTabInactive[i];

            if (surf != null)
            {
                PaintSprite(ctx, surf, tx, tty, tw, dispH, alpha);
            }
            else
            {
                var (er, eg, eb) = ElemColor(el);
                RoundedRect(ctx, tx, tty, tw, dispH, L.D(4));
                ctx.SetSourceRGBA(
                    er * (act ? 0.60 : 0.35),
                    eg * (act ? 0.60 : 0.35),
                    eb * (act ? 0.60 : 0.35),
                    (act ? 0.90 : 0.50) * alpha);
                ctx.Fill();
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                ctx.SetFontSize(L.D(9));
                ctx.SetSourceRGBA(1, 1, 1, exists ? 0.88 : 0.40);
                var te = ctx.TextExtents(el.ToString());
                ctx.MoveTo(tx + tw / 2 - te.Width / 2 - te.XBearing,
                           tty + dispH / 2 + te.Height / 2 - 1);
                ctx.ShowText(el.ToString());
            }
        }
    }

    // ── Spells – left page ────────────────────────────────────────────────────

    private void DrawSpellsLeft(Context ctx)
    {
        // Content area: right of main tabs, inside left page bounds
        double x  = L.LContentX;
        double y  = L.ScLPageY;
        double cw = L.LContentW;
        double ch = L.ScLPageH;

        ctx.Save();
        ctx.Rectangle(x, y, cw, ch);
        ctx.Clip();

        var (er, eg, eb) = ElemColor(SelectedElement);

        // Element title
        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Bold);
        ctx.SetFontSize(L.D(18));
        ctx.SetSourceRGBA(er * 0.65, eg * 0.65, eb * 0.65, 0.90);
        var te = ctx.TextExtents(SelectedElement.ToString());
        ctx.MoveTo(x + cw / 2 - te.Width / 2 - te.XBearing, y + L.D(28));
        ctx.ShowText(SelectedElement.ToString());

        Divider(ctx, x + L.D(4), y + L.D(34), cw - L.D(8));

        // Spell info or placeholder
        if (hoveredSpellId != null)
        {
            var spell = SpellRegistry.Get(hoveredSpellId);
            if (spell != null)
                DrawSpellInfo(ctx, spell, x, y + L.D(44), cw, ch - L.D(44) - L.D(62));
        }
        else
        {
            ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal);
            ctx.SetFontSize(L.D(10));
            ctx.SetSourceRGBA(CS[0], CS[1], CS[2], 0.55);
            double hy = y + L.D(80);
            foreach (var line in "Hover a spell\nto see details.".Split('\n'))
            {
                var lte = ctx.TextExtents(line);
                ctx.MoveTo(x + cw / 2 - lte.Width / 2 - lte.XBearing, hy);
                ctx.ShowText(line);
                hy += L.D(16);
            }
        }

        // XP bar at bottom of left page
        DrawXpBar(ctx, x + L.D(4), y + ch - L.D(56), cw - L.D(8), SelectedElement);

        ctx.Restore();
    }

    private void DrawSpellInfo(Context ctx, Spell spell, double x, double y, double cw, double maxH)
    {
        var (er, eg, eb) = ElemColor(spell.Element);

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(L.D(13));
        ctx.SetSourceRGBA(er * 0.70, eg * 0.70, eb * 0.70, 1.0);
        var nte = ctx.TextExtents(spell.Name);
        ctx.MoveTo(x + cw / 2 - nte.Width / 2 - nte.XBearing, y + L.D(14));
        ctx.ShowText(spell.Name);

        Divider(ctx, x + L.D(8), y + L.D(18), cw - L.D(16));

        // Word-wrapped description
        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal);
        ctx.SetFontSize(L.D(10));
        ctx.SetSourceRGBA(CT[0], CT[1], CT[2], 0.82);
        var words = spell.Description.Split(' ');
        var lines = new List<string>(); string cur = "";
        foreach (var word in words)
        {
            string test = cur.Length == 0 ? word : cur + " " + word;
            if (ctx.TextExtents(test).Width > cw - L.D(16)) { lines.Add(cur); cur = word; }
            else cur = test;
        }
        if (cur.Length > 0) lines.Add(cur);

        double dy = y + L.D(34);
        foreach (var line in lines)
        {
            if (dy + L.D(12) > y + maxH) break;
            ctx.MoveTo(x + L.D(8), dy);
            ctx.ShowText(line);
            dy += L.D(14);
        }

        // Stats
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(L.D(9));
        ctx.SetSourceRGBA(CS[0], CS[1], CS[2], 0.85);
        ctx.MoveTo(x + L.D(8), dy + L.D(14));
        ctx.ShowText($"Flux: {spell.FluxCost}   Cost: {(int)spell.Tier} SP");
    }

    // ── Spells – right page ───────────────────────────────────────────────────

    private void DrawSpellsRight(Context ctx)
    {
        DrawElementTabs(ctx);

        double px = L.ScRPageX, py = L.ScRPageY, pw = L.ScRPageW, ph = L.ScRPageH;

        // Clip to right page
        ctx.Save();
        ctx.Rectangle(px, py, pw, ph);
        ctx.Clip();

        // Tree fills the full page — tabs are now above the page, not inside it
        double treeStartY = py + L.D(8);
        DrawSpellTree(ctx,
            px + L.D(4),
            treeStartY,
            pw - L.D(8),
            ph - L.D(16));

        ctx.Restore();
    }

    // ── Spell tree ────────────────────────────────────────────────────────────

    private void DrawSpellTree(Context ctx, double ax, double ay, double aw, double ah)
    {
        spellNodes.Clear();
        treeAreaX = ax; treeAreaY = ay; treeAreaW = aw; treeAreaH = ah;

        var spells = SpellRegistry.All.Values
            .Where(s => s.Element == SelectedElement).ToList();
        if (spells.Count == 0) return;

        double nodeR  = L.ScNodeR;
        double spacX  = L.ScNodeSpacingX;
        double spacY  = L.ScNodeSpacingY;

        int    maxCol  = spells.Max(s => s.TreePosition.col);
        double gridW   = (maxCol + 1) * spacX;
        double originX = ax + aw / 2 - gridW / 2 + spacX / 2 + treePanX;
        double originY = ay + ah - spacY / 2 - nodeR - L.D(8) + treePanY;

        var posMap = new Dictionary<string, (double cx, double cy)>();
        foreach (var spell in spells)
        {
            double cx = originX + spell.TreePosition.col * spacX;
            double cy = originY - spell.TreePosition.row  * spacY;
            posMap[spell.Id] = (cx, cy);
            spellNodes.Add(new SpellNode(spell.Id, cx, cy, nodeR));
        }

        var entity = capi.World.Player?.Entity;
        var data   = entity != null ? PlayerSpellData.For(entity) : null;
        var (er, eg, eb) = ElemColor(SelectedElement);

        // Edges
        ctx.LineWidth = L.D(1.5);
        foreach (var spell in spells)
        {
            if (!posMap.TryGetValue(spell.Id, out var to)) continue;
            foreach (var prereqId in spell.Prerequisites)
            {
                if (!posMap.TryGetValue(prereqId, out var from)) continue;
                bool pu = data?.IsUnlocked(prereqId) ?? false;
                bool tu = data?.IsUnlocked(spell.Id)  ?? false;
                ctx.SetSourceRGBA(er, eg, eb, pu && tu ? 0.50 : 0.12);
                ctx.MoveTo(from.cx, from.cy);
                ctx.LineTo(to.cx,   to.cy);
                ctx.Stroke();
            }
        }

        // Nodes
        foreach (var spell in spells)
        {
            if (!posMap.TryGetValue(spell.Id, out var pos)) continue;
            bool unlocked  = data?.IsUnlocked(spell.Id)                  ?? false;
            bool available = data != null && SpellTree.CanUnlock(spell.Id, data);
            DrawNode(ctx, spell, pos.cx, pos.cy, unlocked, available,
                     hoveredSpellId == spell.Id, er, eg, eb);
        }
    }

    private void DrawNode(Context ctx, Spell spell, double cx, double cy,
        bool unlocked, bool available, bool hovered,
        double er, double eg, double eb)
    {
        double r = L.ScNodeR;

        // Hover glow
        if (hovered)
        {
            ctx.Arc(cx, cy, r + L.D(5), 0, 2 * Math.PI);
            ctx.SetSourceRGBA(er, eg, eb, 0.12);
            ctx.Fill();
        }

        // Fill
        ctx.Arc(cx, cy, r, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(
            unlocked ? er * 0.18 : 0.84,
            unlocked ? eg * 0.18 : 0.80,
            unlocked ? eb * 0.18 : 0.68,
            unlocked ? 0.80 : available ? 0.50 : 0.28);
        ctx.Fill();

        // Border
        ctx.Arc(cx, cy, r, 0, 2 * Math.PI);
        if (unlocked)       ctx.SetSourceRGBA(er, eg, eb, hovered ? 1.0 : 0.75);
        else if (available) ctx.SetSourceRGBA(er, eg, eb, hovered ? 0.65 : 0.45);
        else                ctx.SetSourceRGBA(0.38, 0.30, 0.18, 0.45);
        ctx.LineWidth = L.D(unlocked ? 1.8 : 1.1);
        ctx.Stroke();

        if (!unlocked) DrawLock(ctx, cx, cy, available ? 0.50 : 0.25);

        // Label
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(L.D(8));
        ctx.SetSourceRGBA(CT[0], CT[1], CT[2], unlocked ? 0.85 : 0.50);
        var te = ctx.TextExtents(spell.Name);
        ctx.MoveTo(cx - te.Width / 2 - te.XBearing, cy + r + L.D(11));
        ctx.ShowText(spell.Name);

        // SP cost (locked only)
        if (!unlocked)
        {
            string sp = $"{(int)spell.Tier} SP";
            ctx.SetFontSize(L.D(7));
            ctx.SetSourceRGBA(er, eg, eb, available ? 0.70 : 0.25);
            var te2 = ctx.TextExtents(sp);
            ctx.MoveTo(cx - te2.Width / 2 - te2.XBearing, cy + r + L.D(20));
            ctx.ShowText(sp);
        }
    }

    private void DrawLock(Context ctx, double cx, double cy, double alpha)
    {
        ctx.SetSourceRGBA(0.38, 0.30, 0.18, alpha);
        ctx.LineWidth = L.D(1.5);
        ctx.Arc(cx, cy - L.D(3), L.D(3.5), Math.PI, 0);
        ctx.Stroke();
        RoundedRect(ctx, cx - L.D(4.5), cy - L.D(1), L.D(9), L.D(6), L.D(2));
        ctx.SetSourceRGBA(0.48, 0.38, 0.22, alpha);
        ctx.Fill();
    }

    // ── Memorized – left page ─────────────────────────────────────────────────

    private void DrawMemorizedLeft(Context ctx)
    {
        double x  = L.LContentX;
        double y  = L.ScLPageY;
        double cw = L.LContentW;
        double ch = L.ScLPageH;

        ctx.Save();
        ctx.Rectangle(x, y, cw, ch);
        ctx.Clip();

        // Title
        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Bold);
        ctx.SetFontSize(L.D(13));
        ctx.SetSourceRGBA(CB[0], CB[1], CB[2], 0.80);
        var te = ctx.TextExtents("Memorized Spells");
        ctx.MoveTo(x + cw / 2 - te.Width / 2 - te.XBearing, y + L.D(26));
        ctx.ShowText("Memorized Spells");
        Divider(ctx, x + L.D(4), y + L.D(33), cw - L.D(8));

        // Hint
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(L.D(9));
        ctx.SetSourceRGBA(CS[0], CS[1], CS[2], 0.60);
        var hte = ctx.TextExtents("Drag spells from the right");
        ctx.MoveTo(x + cw / 2 - hte.Width / 2 - hte.XBearing, y + L.D(50));
        ctx.ShowText("Drag spells from the right");

        // Hotbar wheel
        double radCX = x + cw / 2;
        double radCY = y + ch / 2 + L.D(10);
        double radR  = L.D(80);

        ctx.Arc(radCX, radCY, radR, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(CB[0], CB[1], CB[2], 0.10);
        ctx.LineWidth = L.D(1);
        ctx.Stroke();

        double[] angles = { -Math.PI / 2, -Math.PI / 2 + 2 * Math.PI / 3, -Math.PI / 2 + 4 * Math.PI / 3 };
        string[] labels = { "[1]", "[2]", "[3]" };
        var entity = capi.World.Player?.Entity;
        var data   = entity != null ? PlayerSpellData.For(entity) : null;
        double slotR = L.ScSlotR;

        // Triangle lines between slots
        ctx.SetSourceRGBA(CB[0], CB[1], CB[2], 0.18);
        ctx.LineWidth = L.D(1);
        for (int i = 0; i < 3; i++)
        {
            int j = (i + 1) % 3;
            ctx.MoveTo(radCX + radR * Math.Cos(angles[i]), radCY + radR * Math.Sin(angles[i]));
            ctx.LineTo(radCX + radR * Math.Cos(angles[j]), radCY + radR * Math.Sin(angles[j]));
            ctx.Stroke();
        }

        // Slots
        for (int i = 0; i < 3; i++)
        {
            double scx = radCX + radR * Math.Cos(angles[i]);
            double scy = radCY + radR * Math.Sin(angles[i]);
            hotbarSlotPos[i] = (scx, scy);

            bool   isTarget = isDragging && HitTestSlot(i, dragX, dragY);
            string? slotId  = data?.GetHotbarSlot(i);

            // Slot background
            ctx.Arc(scx, scy, slotR, 0, 2 * Math.PI);
            ctx.SetSourceRGBA(0.80, 0.75, 0.60, 0.22);
            ctx.Fill();

            // Slot border
            ctx.Arc(scx, scy, slotR, 0, 2 * Math.PI);
            ctx.SetSourceRGBA(CB[0], CB[1], CB[2], isTarget ? 0.90 : 0.42);
            ctx.LineWidth = L.D(isTarget ? 2.5 : 1.5);
            ctx.Stroke();

            // Key label
            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(L.D(8));
            ctx.SetSourceRGBA(CS[0], CS[1], CS[2], 0.40);
            var nle = ctx.TextExtents(labels[i]);
            ctx.MoveTo(scx - nle.Width / 2 - nle.XBearing, scy + slotR + L.D(11));
            ctx.ShowText(labels[i]);

            if (slotId != null)
            {
                var spell = SpellRegistry.Get(slotId);
                if (spell != null)
                {
                    var (er2, eg2, eb2) = ElemColor(spell.Element);
                    DrawIconInCircle(ctx, spell, scx, scy, slotR - L.D(3), er2, eg2, eb2);
                    ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
                    ctx.SetFontSize(L.D(8));
                    ctx.SetSourceRGBA(CT[0], CT[1], CT[2], 0.75);
                    var snte = ctx.TextExtents(spell.Name);
                    ctx.MoveTo(scx - snte.Width / 2 - snte.XBearing, scy + slotR + L.D(22));
                    ctx.ShowText(spell.Name);
                }
            }
            else
            {
                // Plus icon for empty slot
                ctx.SetSourceRGBA(CB[0], CB[1], CB[2], 0.16);
                ctx.LineWidth = L.D(1.5);
                ctx.MoveTo(scx - L.D(6), scy); ctx.LineTo(scx + L.D(6), scy); ctx.Stroke();
                ctx.MoveTo(scx, scy - L.D(6)); ctx.LineTo(scx, scy + L.D(6)); ctx.Stroke();
            }
        }

        ctx.Restore();
    }

    // ── Memorized – right page ────────────────────────────────────────────────

    private void DrawMemorizedRight(Context ctx)
    {
        memorizedSpellCards.Clear();

        double px = L.ScRPageX, py = L.ScRPageY, pw = L.ScRPageW, ph = L.ScRPageH;

        ctx.Save();
        ctx.Rectangle(px, py, pw, ph);
        ctx.Clip();

        double x = px + L.D(8), y = py + L.D(10);
        double w = pw - L.D(16), h = ph - L.D(18);

        var entity = capi.World.Player?.Entity;
        var data   = entity != null ? PlayerSpellData.For(entity) : null;

        // Title
        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Bold);
        ctx.SetFontSize(L.D(13));
        ctx.SetSourceRGBA(CB[0], CB[1], CB[2], 0.80);
        var tte = ctx.TextExtents("Unlocked Spells");
        ctx.MoveTo(x + w / 2 - tte.Width / 2 - tte.XBearing, y + L.D(14));
        ctx.ShowText("Unlocked Spells");
        Divider(ctx, x + L.D(4), y + L.D(20), w - L.D(8));

        var ids    = data?.GetUnlockedIds().ToList() ?? new List<string>();
        double listY = y + L.D(28);

        if (ids.Count == 0)
        {
            ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal);
            ctx.SetFontSize(L.D(12));
            ctx.SetSourceRGBA(CS[0], CS[1], CS[2], 0.55);
            var ete = ctx.TextExtents("No spells unlocked yet.");
            ctx.MoveTo(x + w / 2 - ete.Width / 2 - ete.XBearing,
                       listY + (h - L.D(28)) / 2);
            ctx.ShowText("No spells unlocked yet.");
            ctx.Restore();
            return;
        }

        double cardW = L.ScCardW, cardH = L.ScCardH;
        int    cols  = Math.Max(1, (int)((w + L.D(6)) / (cardW + L.D(6))));
        double gridW = cols * cardW + (cols - 1) * L.D(6);
        double startX = x + w / 2 - gridW / 2;

        for (int ci = 0; ci < ids.Count; ci++)
        {
            int    col   = ci % cols, row = ci / cols;
            double cx    = startX + col * (cardW + L.D(6));
            double cardY = listY + row * (cardH + L.D(6));
            if (cardY + cardH > y + h) break;

            var spell = SpellRegistry.Get(ids[ci]);
            if (spell == null) continue;

            bool drag = isDragging && dragSpellId == spell.Id && dragFromSlot == -1;
            var (er, eg, eb) = ElemColor(spell.Element);

            memorizedSpellCards.Add(new SpellCard(spell.Id, cx, cardY, cardW, cardH));

            // Card background
            RoundedRect(ctx, cx, cardY, cardW, cardH, L.D(5));
            ctx.SetSourceRGBA(
                er * 0.10 + 0.74, eg * 0.10 + 0.70, eb * 0.10 + 0.54,
                drag ? 0.18 : 0.72);
            ctx.Fill();

            // Card border
            RoundedRect(ctx, cx, cardY, cardW, cardH, L.D(5));
            ctx.SetSourceRGBA(er, eg, eb, drag ? 0.12 : 0.42);
            ctx.LineWidth = L.D(1.2);
            ctx.Stroke();

            if (!drag)
            {
                // Colour dot
                ctx.Arc(cx + L.D(11), cardY + cardH / 2, L.D(4), 0, 2 * Math.PI);
                ctx.SetSourceRGBA(er, eg, eb, 0.75);
                ctx.Fill();

                // Spell name
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                ctx.SetFontSize(L.D(10));
                ctx.SetSourceRGBA(CT[0], CT[1], CT[2], 0.88);
                ctx.MoveTo(cx + L.D(21), cardY + cardH / 2 - L.D(2));
                ctx.ShowText(spell.Name);

                // Sub-info
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
                ctx.SetFontSize(L.D(8));
                ctx.SetSourceRGBA(CS[0], CS[1], CS[2], 0.65);
                ctx.MoveTo(cx + L.D(21), cardY + cardH / 2 + L.D(10));
                ctx.ShowText($"{spell.FluxCost} Flux · {spell.Element}");
            }
        }

        // Dragged card ghost
        if (isDragging && dragSpellId != null)
        {
            var ds = SpellRegistry.Get(dragSpellId);
            if (ds != null)
            {
                var (er, eg, eb) = ElemColor(ds.Element);
                double dx = dragX - cardW / 2, dy = dragY - cardH / 2;
                RoundedRect(ctx, dx, dy, cardW, cardH, L.D(5));
                ctx.SetSourceRGBA(er * 0.10 + 0.74, eg * 0.10 + 0.70, eb * 0.10 + 0.54, 0.95);
                ctx.Fill();
                RoundedRect(ctx, dx, dy, cardW, cardH, L.D(5));
                ctx.SetSourceRGBA(er, eg, eb, 0.80);
                ctx.LineWidth = L.D(2);
                ctx.Stroke();
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                ctx.SetFontSize(L.D(10));
                ctx.SetSourceRGBA(CT[0], CT[1], CT[2], 1.0);
                ctx.MoveTo(dx + L.D(21), dy + cardH / 2 + L.D(4));
                ctx.ShowText(ds.Name);
            }
        }

        ctx.Restore();
    }

    // ── Coming soon placeholder ───────────────────────────────────────────────

    private void DrawComingSoon(Context ctx)
    {
        double px = L.ScRPageX, py = L.ScRPageY, pw = L.ScRPageW, ph = L.ScRPageH;
        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal);
        ctx.SetFontSize(L.D(14));
        ctx.SetSourceRGBA(CS[0], CS[1], CS[2], 0.65);
        string ph2 = TabNames[currentTab] + " — coming soon";
        var te = ctx.TextExtents(ph2);
        ctx.MoveTo(px + pw / 2 - te.Width / 2 - te.XBearing, py + ph / 2);
        ctx.ShowText(ph2);
    }

    // ── XP bar ───────────────────────────────────────────────────────────────

    private void DrawXpBar(Context ctx, double x, double y, double maxW, SpellElement element)
    {
        var entity = capi.World.Player?.Entity;
        if (entity == null) return;
        var data = PlayerSpellData.For(entity);

        int level = data.GetElementLevel(element);
        int xpIn  = data.GetElementXpInLevel(element);
        int xpNext = PlayerSpellData.XpForLevel(level);
        int sp    = data.GetSkillPoints(element);
        var (er, eg, eb) = ElemColor(element);

        // Element name
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(L.D(11));
        ctx.SetSourceRGBA(er * 0.60, eg * 0.60, eb * 0.60, 1.0);
        ctx.MoveTo(x, y + L.D(11));
        ctx.ShowText(element.ToString());

        // Level + SP
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(L.D(10));
        ctx.SetSourceRGBA(CS[0], CS[1], CS[2], 1.0);
        ctx.MoveTo(x, y + L.D(23));
        ctx.ShowText($"Lvl {level}   SP: {sp}");

        // XP label right-aligned
        string xpLabel = $"{xpIn}/{xpNext} XP";
        var xpte = ctx.TextExtents(xpLabel);
        ctx.MoveTo(x + maxW - xpte.Width - xpte.XBearing, y + L.D(23));
        ctx.ShowText(xpLabel);

        // Bar
        double barH = L.D(9), barY = y + L.D(29), barR = barH / 2;
        RoundedRect(ctx, x, barY, maxW, barH, barR);
        ctx.SetSourceRGBA(0.60, 0.48, 0.30, 0.35);
        ctx.Fill();

        double frac = xpNext > 0 ? Math.Clamp((double)xpIn / xpNext, 0, 1) : 1;
        if (frac > 0.01)
        {
            double fillW = Math.Max(barH, maxW * frac);
            RoundedRect(ctx, x, barY, fillW, barH, barR);
            using var grad = new LinearGradient(x, 0, x + fillW, 0);
            grad.AddColorStop(0, new Color(er * 0.35, eg * 0.35, eb * 0.35, 0.85));
            grad.AddColorStop(1, new Color(er * 0.75, eg * 0.75, eb * 0.75, 0.85));
            ctx.SetSource(grad);
            ctx.Fill();
        }

        RoundedRect(ctx, x, barY, maxW, barH, barR);
        ctx.SetSourceRGBA(CB[0], CB[1], CB[2], 0.35);
        ctx.LineWidth = L.D(1);
        ctx.Stroke();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void PaintSprite(Context ctx, ImageSurface? surf,
        double x, double y, double w, double h, double alpha = 1.0)
    {
        if (surf == null) return;
        ctx.Save();
        ctx.Rectangle(x, y, w, h);
        ctx.Clip();
        ctx.Translate(x, y);
        ctx.Scale(w / surf.Width, h / surf.Height);
        ctx.SetSourceSurface(surf, 0, 0);
        ctx.PaintWithAlpha(alpha);
        ctx.Restore();
    }

    private static void DrawIconInCircle(Context ctx, Spell spell,
        double cx, double cy, double r, double er, double eg, double eb)
    {
        ctx.Save();
        ctx.Arc(cx, cy, r, 0, 2 * Math.PI);
        ctx.Clip();
        ctx.Arc(cx, cy, r, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(er * 0.22, eg * 0.22, eb * 0.22, 0.80);
        ctx.Fill();
        ctx.SelectFontFace("Serif", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(r * 1.1);
        string letter = spell.Name.Length > 0 ? spell.Name[0].ToString() : "?";
        ctx.SetSourceRGBA(er * 0.75, eg * 0.75, eb * 0.75, 0.90);
        var te = ctx.TextExtents(letter);
        ctx.MoveTo(cx - te.Width / 2 - te.XBearing,
                   cy + te.Height / 2 - te.YBearing - te.Height);
        ctx.ShowText(letter);
        ctx.Restore();
    }

    private static void Divider(Context ctx, double x, double y, double w)
    {
        ctx.SetSourceRGBA(0.55, 0.38, 0.14, 0.28);
        ctx.LineWidth = 1;
        ctx.MoveTo(x, y); ctx.LineTo(x + w, y);
        ctx.Stroke();
    }

    private static void RoundedRect(Context ctx, double x, double y,
        double w, double h, double r)
    {
        r = Math.Min(r, Math.Min(w / 2, h / 2));
        ctx.NewPath();
        ctx.Arc(x + r,     y + r,     r, Math.PI,         3 * Math.PI / 2);
        ctx.Arc(x + w - r, y + r,     r, 3 * Math.PI / 2, 0);
        ctx.Arc(x + w - r, y + h - r, r, 0,               Math.PI / 2);
        ctx.Arc(x + r,     y + h - r, r, Math.PI / 2,     Math.PI);
        ctx.ClosePath();
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public override void OnMouseMove(MouseEvent args)
    {
        double rx = args.X - SingleComposer.Bounds.absX;
        double ry = args.Y - SingleComposer.Bounds.absY;

        if (isDragging)
        {
            dragX = rx; dragY = ry;
            Redraw(); args.Handled = true; return;
        }
        if (isPanning)
        {
            treePanX = panStartOffsetX + (rx - panStartMouseX);
            treePanY = panStartOffsetY + (ry - panStartMouseY);
            Redraw(); args.Handled = true; return;
        }

        int prevMain = hoveredMainTab, prevElem = hoveredElemTab;
        bool prevClose = closeHovered; string? prevSpell = hoveredSpellId;

        hoveredMainTab = -1; hoveredElemTab = -1;
        closeHovered   = false; hoveredSpellId = null;

        // Close bookmark
        if (rx >= L.ScCloseBookX && rx <= L.ScCloseBookX + L.ScCloseBookW &&
            ry >= L.ScCloseBookY && ry <= L.ScCloseBookY + L.ScCloseBookH)
        {
            closeHovered = true;
        }
        else
        {
            // Main tabs
            for (int i = 0; i < 4; i++)
                if (rx >= L.ScMTabX && rx <= L.ScMTabX + L.ScMTabW &&
                    ry >= mainTabY[i] && ry <= mainTabY[i] + mainTabH[i])
                { hoveredMainTab = i; break; }

            // Element tabs (Spells tab only)
            if (hoveredMainTab == -1 && currentTab == TabSpells)
            {
                double tw = L.ETabW();
                for (int i = 0; i < 4; i++)
                    if (rx >= elemTabX[i] && rx <= elemTabX[i] + tw &&
                        ry >= elemTabY[i] && ry <= elemTabY[i] + elemTabHHit[i])
                    { hoveredElemTab = i; break; }
            }

            // Spell nodes
            if (hoveredMainTab == -1 && hoveredElemTab == -1 && currentTab == TabSpells)
                foreach (var node in spellNodes)
                {
                    double dx = rx - node.Cx, dy = ry - node.Cy;
                    if (dx * dx + dy * dy <= node.R * node.R)
                    { hoveredSpellId = node.SpellId; break; }
                }
        }

        if (hoveredMainTab != prevMain || hoveredElemTab != prevElem ||
            closeHovered != prevClose  || hoveredSpellId != prevSpell)
            Redraw();
    }

    public override void OnMouseDown(MouseEvent args)
    {
        double rx = args.X - SingleComposer.Bounds.absX;
        double ry = args.Y - SingleComposer.Bounds.absY;

        // Close
        if (rx >= L.ScCloseBookX && rx <= L.ScCloseBookX + L.ScCloseBookW &&
            ry >= L.ScCloseBookY && ry <= L.ScCloseBookY + L.ScCloseBookH)
        { TryClose(); args.Handled = true; return; }

        // Main tabs
        for (int i = 0; i < 4; i++)
            if (rx >= L.ScMTabX && rx <= L.ScMTabX + L.ScMTabW &&
                ry >= mainTabY[i] && ry <= mainTabY[i] + mainTabH[i])
            { currentTab = i; Redraw(); args.Handled = true; return; }

        if (currentTab == TabSpells)
        {
            // Element tabs
            double tw = L.ETabW();
            for (int i = 0; i < 4; i++)
                if (rx >= elemTabX[i] && rx <= elemTabX[i] + tw &&
                    ry >= elemTabY[i] && ry <= elemTabY[i] + elemTabHHit[i])
                {
                    int idx = Array.IndexOf(Elements, ElemOrder[i]);
                    if (idx >= 0)
                    { selectedElementIndex = idx; treePanX = 0; treePanY = 0; Redraw(); }
                    args.Handled = true; return;
                }

            // Spell nodes
            foreach (var node in spellNodes)
            {
                double dx = rx - node.Cx, dy = ry - node.Cy;
                if (dx * dx + dy * dy <= node.R * node.R)
                {
                    channel.SendPacket(new MsgUnlockSpell { SpellId = node.SpellId });
                    Redraw(); args.Handled = true; return;
                }
            }

            // Pan start
            if (rx >= treeAreaX && rx <= treeAreaX + treeAreaW &&
                ry >= treeAreaY && ry <= treeAreaY + treeAreaH)
            {
                isPanning = true;
                panStartMouseX = rx; panStartMouseY = ry;
                panStartOffsetX = treePanX; panStartOffsetY = treePanY;
                args.Handled = true; return;
            }
        }

        if (currentTab == TabMemorized)
        {
            var entity = capi.World.Player?.Entity;
            var data   = entity != null ? PlayerSpellData.For(entity) : null;

            for (int i = 0; i < 3; i++)
                if (HitTestSlot(i, rx, ry))
                {
                    string? ss = data?.GetHotbarSlot(i);
                    if (ss != null)
                    { isDragging = true; dragSpellId = ss; dragFromSlot = i; dragX = rx; dragY = ry; args.Handled = true; return; }
                }

            foreach (var card in memorizedSpellCards)
                if (rx >= card.X && rx <= card.X + card.W &&
                    ry >= card.Y && ry <= card.Y + card.H)
                { isDragging = true; dragSpellId = card.SpellId; dragFromSlot = -1; dragX = rx; dragY = ry; args.Handled = true; return; }
        }

        if (rx >= 0 && rx <= L.GuiW && ry >= 0 && ry <= L.GuiH) args.Handled = true;
    }

    public override void OnMouseUp(MouseEvent args)
    {
        if (isPanning) { isPanning = false; args.Handled = true; return; }
        if (!isDragging) { base.OnMouseUp(args); return; }

        double rx = args.X - SingleComposer.Bounds.absX;
        double ry = args.Y - SingleComposer.Bounds.absY;
        int dropSlot = FindHotbarSlotAtPos(rx, ry);

        if (dropSlot >= 0 && dragSpellId != null)
        {
            if (dragFromSlot >= 0 && dragFromSlot != dropSlot)
                channel.SendPacket(new MsgSetHotbarSlot { Slot = dragFromSlot, SpellId = "" });
            channel.SendPacket(new MsgSetHotbarSlot { Slot = dropSlot, SpellId = dragSpellId });
        }
        else if (dragFromSlot >= 0)
        {
            channel.SendPacket(new MsgSetHotbarSlot { Slot = dragFromSlot, SpellId = "" });
        }

        isDragging = false; dragSpellId = null; dragFromSlot = -1;
        Redraw(); args.Handled = true;
    }

    public override string ToggleKeyCombinationCode => "spellsandrunes.spellbook";
    public override EnumDialogType DialogType => EnumDialogType.Dialog;

    // ── Hit testing ───────────────────────────────────────────────────────────

    private bool HitTestSlot(int slot, double rx, double ry)
    {
        if (slot < 0 || slot >= 3) return false;
        var (cx, cy) = hotbarSlotPos[slot];
        double dx = rx - cx, dy = ry - cy;
        return dx * dx + dy * dy <= (L.ScSlotR + L.D(8)) * (L.ScSlotR + L.D(8));
    }

    private int FindHotbarSlotAtPos(double rx, double ry)
    {
        for (int i = 0; i < 3; i++) if (HitTestSlot(i, rx, ry)) return i;
        return -1;
    }

    // ── Records ───────────────────────────────────────────────────────────────

    private record SpellNode(string SpellId, double Cx, double Cy, double R);
    private record SpellCard(string SpellId, double X, double Y, double W, double H);
}