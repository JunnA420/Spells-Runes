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

    // Canvas size — set at start of each draw from bounds
    private double W, H;

    // Positions as fractions of W/H (matching book_bg.png layout at 940x600)
    // These stay fixed — the book fills the canvas so they always align
    private double LPX  => W * 0.032;  // left page X
    private double LPY  => H * 0.092;  // left page Y
    private double LPW  => W * 0.381;  // left page width
    private double LPH  => H * 0.817;  // left page height
    private double RPX  => W * 0.502;  // right page X
    private double RPY  => H * 0.092;  // right page Y
    private double RPW  => W * 0.466;  // right page width
    private double RPH  => H * 0.817;  // right page height

    // Main tabs — stick out left of book
    private double MTabX      => W * 0.008;
    private double MTabW      => W * 0.160;
    private double MTabStartY => H * 0.188;
    private double MTabGap    => H * 0.008;

    // Left page content area (right of main tabs)
    private double LCX => MTabX + MTabW + W * 0.008;
    private double LCW => LPX + LPW - LCX;

    // Close bookmark — bottom left, sticks out
    private double CloseX => W * 0.008;
    private double CloseY => H * 0.850;
    private double CloseW => W * 0.032;
    private double CloseH => H * 0.063;

    // Element tabs — top of right page, stick out above
    private double ETabGap => W * 0.004;
    private double ETabW   => (RPW - 3 * ETabGap) / 4.0;

    // Node geometry — scaled uniformly
    private double Sc          => Math.Min(W / 940.0, H / 600.0);
    private double NodeR        => 22 * Sc;
    private double NodeSpacingX => 88 * Sc;
    private double NodeSpacingY => 80 * Sc;

    // Memorized
    private double CardW => 195 * Sc;
    private double CardH => 32  * Sc;
    private double SlotR => 26  * Sc;

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

    // Hit regions
    private readonly double[] mainTabY    = new double[4];
    private readonly double[] mainTabH    = new double[4];
    private readonly double[] elemTabX    = new double[4];
    private readonly double[] elemTabY    = new double[4];
    private readonly double[] elemTabHHit = new double[4];
    private int  hoveredMainTab = -1;
    private int  hoveredElemTab = -1;
    private bool closeHovered   = false;

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

    // Element tab display heights (computed after sprite load)
    private readonly double[] eTabDispH  = new double[4];
    private readonly double[] eTabDispHI = new double[4];
    private readonly double[] mTabDispH  = new double[4];
    private readonly double[] mTabDispHI = new double[4];
    private double ETabMaxH => eTabDispH.Any(d => d > 0) ? eTabDispH.Max() : H * 0.060;

    // Dialog size from JSON
    private SpellbookLayout L = new();
    private double DialogW => L.GuiW;
    private double DialogH => L.GuiH;

    // ---- Constructor ----

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

    // ---- Textures ----

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

    private void RecomputeSpriteSizes()
    {
        double tw = ETabW, mw = MTabW;
        for (int i = 0; i < 4; i++)
        {
            var ma = mainTabActive[i];
            mTabDispH[i]  = ma != null ? mw * ma.Height / ma.Width : MTabGap * 5;
            var mi = mainTabInactive[i];
            mTabDispHI[i] = mi != null ? mw * mi.Height / mi.Width : MTabGap * 4;
            var ea = elemTabActive[i];
            eTabDispH[i]  = ea != null ? tw * ea.Height / ea.Width : H * 0.060;
            var ei = elemTabInactive[i];
            eTabDispHI[i] = ei != null ? tw * ei.Height / ei.Width : H * 0.053;
        }
    }

    private ImageSurface? LoadSurface(string assetPath)
    {
        var asset = capi.Assets.TryGet(new AssetLocation("spellsandrunes", assetPath));
        if (asset == null) return null;
        string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snr_" + System.IO.Path.GetFileName(assetPath));
        System.IO.File.WriteAllBytes(tmp, asset.Data);
        return new ImageSurface(tmp);
    }

    // ---- Compose ----

    private void ComposeDialog()
    {
        var db = ElementBounds.Fixed(EnumDialogArea.CenterMiddle, 0, 0, DialogW, DialogH);
        var cb = ElementBounds.Fixed(0, 0, DialogW, DialogH);
        SingleComposer = capi.Gui
            .CreateCompo("spellsandrunes:spellbook", db)
            .AddDynamicCustomDraw(cb, OnDraw, "canvas")
            .Compose();
    }

    // ---- Draw ----

    private void OnDraw(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        W = bounds.InnerWidth;
        H = bounds.InnerHeight;

        RecomputeSpriteSizes();

        // Book background — fill entire canvas
        if (bookBgSurface != null)
        {
            ctx.Save();
            ctx.Scale(W / bookBgSurface.Width, H / bookBgSurface.Height);
            ctx.SetSourceSurface(bookBgSurface, 0, 0);
            ctx.Paint();
            ctx.Restore();
        }

        DrawCloseBookmark(ctx);
        DrawMainTabs(ctx);

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

    // ---- Close bookmark ----

    private void DrawCloseBookmark(Context ctx)
    {
        PaintSprite(ctx, closeBookmarkSurface, CloseX, CloseY, CloseW, CloseH, closeHovered ? 1.0 : 0.85);
        if (closeBookmarkSurface == null)
        {
            RoundedRect(ctx, CloseX, CloseY, CloseW, CloseH, 4);
            ctx.SetSourceRGBA(closeHovered ? 0.55 : 0.35, 0.08, 0.04, 0.85); ctx.Fill();
            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(14 * Sc);
            ctx.SetSourceRGBA(1, 1, 1, 0.9);
            var te = ctx.TextExtents("X");
            ctx.MoveTo(CloseX + CloseW / 2 - te.Width / 2 - te.XBearing, CloseY + CloseH / 2 + te.Height / 2 - 1);
            ctx.ShowText("X");
        }
    }

    // ---- Main tabs ----

    private void DrawMainTabs(Context ctx)
    {
        double ty = MTabStartY;
        double[] ys = new double[4], hs = new double[4];
        for (int i = 0; i < 4; i++)
        {
            bool act = currentTab == i;
            ys[i] = ty; hs[i] = act ? mTabDispH[i] : mTabDispHI[i];
            mainTabY[i] = ty; mainTabH[i] = hs[i];
            ty += hs[i] + MTabGap;
        }
        for (int pass = 0; pass < 2; pass++)
        for (int i = 0; i < 4; i++)
        {
            bool act = currentTab == i;
            if ((pass == 0) == act) continue;
            var surf = act ? mainTabActive[i] : mainTabInactive[i];
            if (surf != null) PaintSprite(ctx, surf, MTabX, ys[i], MTabW, hs[i]);
            else
            {
                RoundedRect(ctx, MTabX, ys[i], MTabW, hs[i], 4);
                ctx.SetSourceRGBA(act ? 0.70 : 0.50, act ? 0.54 : 0.40, act ? 0.24 : 0.20, act ? 0.90 : 0.55); ctx.Fill();
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(10 * Sc);
                ctx.SetSourceRGBA(CT[0], CT[1], CT[2], 0.85);
                var te = ctx.TextExtents(TabNames[i]);
                ctx.MoveTo(MTabX + MTabW / 2 - te.Width / 2 - te.XBearing, ys[i] + hs[i] / 2 + te.Height / 2 - 1);
                ctx.ShowText(TabNames[i]);
            }
        }
    }

    // ---- Element tabs ----

    private void DrawElementTabs(Context ctx)
    {
        double tw = ETabW, maxH = ETabMaxH;
        for (int pass = 0; pass < 2; pass++)
        for (int i = 0; i < 4; i++)
        {
            var el = ElemOrder[i];
            bool act = SelectedElement == el;
            if ((pass == 0) == act) continue;
            bool exists = Elements.Contains(el);
            double tx    = RPX + i * (tw + ETabGap);
            double dispH = act ? eTabDispH[i] : eTabDispHI[i];
            double ty    = RPY + maxH - dispH;
            var surf     = act ? elemTabActive[i] : elemTabInactive[i];
            elemTabX[i] = tx; elemTabY[i] = ty; elemTabHHit[i] = dispH;
            double alpha = exists ? 1.0 : 0.35;
            if (surf != null) PaintSprite(ctx, surf, tx, ty, tw, dispH, alpha);
            else
            {
                var (er, eg, eb) = ElemColor(el);
                RoundedRect(ctx, tx, ty, tw, dispH, 4);
                ctx.SetSourceRGBA(er * (act ? 0.60 : 0.35), eg * (act ? 0.60 : 0.35), eb * (act ? 0.60 : 0.35), (act ? 0.90 : 0.50) * alpha); ctx.Fill();
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(9 * Sc);
                ctx.SetSourceRGBA(1, 1, 1, exists ? 0.88 : 0.40);
                var te = ctx.TextExtents(el.ToString());
                ctx.MoveTo(tx + tw / 2 - te.Width / 2 - te.XBearing, ty + dispH / 2 + te.Height / 2 - 1);
                ctx.ShowText(el.ToString());
            }
        }
    }

    // ---- Spells left ----

    private void DrawSpellsLeft(Context ctx)
    {
        ctx.Save();
        ctx.Rectangle(LCX, LPY, LCW, LPH); ctx.Clip();
        double x = LCX, cw = LCW, y = LPY;
        var (er, eg, eb) = ElemColor(SelectedElement);
        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Bold); ctx.SetFontSize(18 * Sc);
        ctx.SetSourceRGBA(er * 0.65, eg * 0.65, eb * 0.65, 0.90);
        var te = ctx.TextExtents(SelectedElement.ToString());
        ctx.MoveTo(x + cw / 2 - te.Width / 2 - te.XBearing, y + 28 * Sc); ctx.ShowText(SelectedElement.ToString());
        Divider(ctx, x + 4, y + 34 * Sc, cw - 8);

        if (hoveredSpellId != null)
        {
            var spell = SpellRegistry.Get(hoveredSpellId);
            if (spell != null) DrawSpellInfo(ctx, spell, x, y + 44 * Sc, cw, LPH - 44 * Sc - 62 * Sc);
        }
        else
        {
            ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal); ctx.SetFontSize(10 * Sc);
            ctx.SetSourceRGBA(CS[0], CS[1], CS[2], 0.55);
            double hy = y + 80 * Sc;
            foreach (var line in "Hover a spell\nto see details.".Split('\n'))
            {
                var lte = ctx.TextExtents(line);
                ctx.MoveTo(x + cw / 2 - lte.Width / 2 - lte.XBearing, hy); ctx.ShowText(line); hy += 16 * Sc;
            }
        }
        DrawXpBar(ctx, x + 4, y + LPH - 56 * Sc, cw - 8, SelectedElement);
        ctx.Restore();
    }

    private void DrawSpellInfo(Context ctx, Spell spell, double x, double y, double cw, double maxH)
    {
        var (er, eg, eb) = ElemColor(spell.Element);
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(13 * Sc);
        ctx.SetSourceRGBA(er * 0.70, eg * 0.70, eb * 0.70, 1.0);
        var nte = ctx.TextExtents(spell.Name);
        ctx.MoveTo(x + cw / 2 - nte.Width / 2 - nte.XBearing, y + 14 * Sc); ctx.ShowText(spell.Name);
        Divider(ctx, x + 8, y + 18 * Sc, cw - 16);

        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal); ctx.SetFontSize(10 * Sc);
        ctx.SetSourceRGBA(CT[0], CT[1], CT[2], 0.82);
        var words = spell.Description.Split(' ');
        var lines = new List<string>(); string cur = "";
        foreach (var word in words)
        {
            string test = cur.Length == 0 ? word : cur + " " + word;
            if (ctx.TextExtents(test).Width > cw - 16) { lines.Add(cur); cur = word; } else cur = test;
        }
        if (cur.Length > 0) lines.Add(cur);
        double dy = y + 34 * Sc;
        foreach (var line in lines) { if (dy + 12 > y + maxH) break; ctx.MoveTo(x + 8, dy); ctx.ShowText(line); dy += 14 * Sc; }
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(9 * Sc);
        ctx.SetSourceRGBA(CS[0], CS[1], CS[2], 0.85);
        ctx.MoveTo(x + 8, dy + 14 * Sc); ctx.ShowText($"Flux: {spell.FluxCost}   Cost: {(int)spell.Tier} SP");
    }

    // ---- Spells right ----

    private void DrawSpellsRight(Context ctx)
    {
        DrawElementTabs(ctx);
        ctx.Save();
        ctx.Rectangle(RPX, RPY, RPW, RPH); ctx.Clip();
        double treeY = RPY + ETabMaxH + 10 * Sc;
        DrawSpellTree(ctx, RPX + 4, treeY, RPW - 8, RPH - ETabMaxH - 18 * Sc);
        ctx.Restore();
    }

    // ---- Spell tree ----

    private void DrawSpellTree(Context ctx, double ax, double ay, double aw, double ah)
    {
        spellNodes.Clear();
        treeAreaX = ax; treeAreaY = ay; treeAreaW = aw; treeAreaH = ah;
        var spells = SpellRegistry.All.Values.Where(s => s.Element == SelectedElement).ToList();
        if (spells.Count == 0) return;

        int maxCol = spells.Max(s => s.TreePosition.col);
        double gridW   = (maxCol + 1) * NodeSpacingX;
        double originX = ax + aw / 2 - gridW / 2 + NodeSpacingX / 2 + treePanX;
        double originY = ay + ah - NodeSpacingY / 2 - NodeR - 8 * Sc + treePanY;

        var posMap = new Dictionary<string, (double cx, double cy)>();
        foreach (var spell in spells)
        {
            double cx = originX + spell.TreePosition.col * NodeSpacingX;
            double cy = originY - spell.TreePosition.row * NodeSpacingY;
            posMap[spell.Id] = (cx, cy);
            spellNodes.Add(new SpellNode(spell.Id, cx, cy, NodeR));
        }

        var entity = capi.World.Player?.Entity;
        var data   = entity != null ? PlayerSpellData.For(entity) : null;
        var (er, eg, eb) = ElemColor(SelectedElement);

        ctx.LineWidth = 1.5 * Sc;
        foreach (var spell in spells)
        {
            if (!posMap.TryGetValue(spell.Id, out var to)) continue;
            foreach (var prereqId in spell.Prerequisites)
            {
                if (!posMap.TryGetValue(prereqId, out var from)) continue;
                bool pu = data?.IsUnlocked(prereqId) ?? false, tu = data?.IsUnlocked(spell.Id) ?? false;
                ctx.SetSourceRGBA(er, eg, eb, pu && tu ? 0.50 : 0.12);
                ctx.MoveTo(from.cx, from.cy); ctx.LineTo(to.cx, to.cy); ctx.Stroke();
            }
        }
        foreach (var spell in spells)
        {
            if (!posMap.TryGetValue(spell.Id, out var pos)) continue;
            bool unlocked = data?.IsUnlocked(spell.Id) ?? false;
            bool available = data != null && SpellTree.CanUnlock(spell.Id, data);
            DrawNode(ctx, spell, pos.cx, pos.cy, unlocked, available, hoveredSpellId == spell.Id, er, eg, eb);
        }
    }

    private void DrawNode(Context ctx, Spell spell, double cx, double cy,
        bool unlocked, bool available, bool hovered, double er, double eg, double eb)
    {
        double r = NodeR;
        if (hovered) { ctx.Arc(cx, cy, r + 5 * Sc, 0, 2 * Math.PI); ctx.SetSourceRGBA(er, eg, eb, 0.12); ctx.Fill(); }
        ctx.Arc(cx, cy, r, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(unlocked ? er * 0.18 : 0.84, unlocked ? eg * 0.18 : 0.80, unlocked ? eb * 0.18 : 0.68,
                          unlocked ? 0.80 : available ? 0.50 : 0.28); ctx.Fill();
        ctx.Arc(cx, cy, r, 0, 2 * Math.PI);
        if (unlocked)       ctx.SetSourceRGBA(er, eg, eb, hovered ? 1.0 : 0.75);
        else if (available) ctx.SetSourceRGBA(er, eg, eb, hovered ? 0.65 : 0.45);
        else                ctx.SetSourceRGBA(0.38, 0.30, 0.18, 0.45);
        ctx.LineWidth = (unlocked ? 1.8 : 1.1) * Sc; ctx.Stroke();
        if (!unlocked) DrawLock(ctx, cx, cy, available ? 0.50 : 0.25);
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(8 * Sc);
        ctx.SetSourceRGBA(CT[0], CT[1], CT[2], unlocked ? 0.85 : 0.50);
        var te = ctx.TextExtents(spell.Name);
        ctx.MoveTo(cx - te.Width / 2 - te.XBearing, cy + r + 11 * Sc); ctx.ShowText(spell.Name);
        if (!unlocked)
        {
            string sp = $"{(int)spell.Tier} SP"; ctx.SetFontSize(7 * Sc);
            ctx.SetSourceRGBA(er, eg, eb, available ? 0.70 : 0.25);
            var te2 = ctx.TextExtents(sp);
            ctx.MoveTo(cx - te2.Width / 2 - te2.XBearing, cy + r + 20 * Sc); ctx.ShowText(sp);
        }
    }

    private void DrawLock(Context ctx, double cx, double cy, double alpha)
    {
        ctx.SetSourceRGBA(0.38, 0.30, 0.18, alpha); ctx.LineWidth = 1.5 * Sc;
        ctx.Arc(cx, cy - 3 * Sc, 3.5 * Sc, Math.PI, 0); ctx.Stroke();
        RoundedRect(ctx, cx - 4.5 * Sc, cy - 1 * Sc, 9 * Sc, 6 * Sc, 2 * Sc);
        ctx.SetSourceRGBA(0.48, 0.38, 0.22, alpha); ctx.Fill();
    }

    // ---- Memorized left ----

    private void DrawMemorizedLeft(Context ctx)
    {
        ctx.Save();
        ctx.Rectangle(LCX, LPY, LCW, LPH); ctx.Clip();
        double x = LCX, cw = LCW, y = LPY;

        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Bold); ctx.SetFontSize(13 * Sc);
        ctx.SetSourceRGBA(CB[0], CB[1], CB[2], 0.80);
        var te = ctx.TextExtents("Memorized Spells");
        ctx.MoveTo(x + cw / 2 - te.Width / 2 - te.XBearing, y + 26 * Sc); ctx.ShowText("Memorized Spells");
        Divider(ctx, x + 4, y + 33 * Sc, cw - 8);

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(9 * Sc);
        ctx.SetSourceRGBA(CS[0], CS[1], CS[2], 0.60);
        var hte = ctx.TextExtents("Drag spells from the right");
        ctx.MoveTo(x + cw / 2 - hte.Width / 2 - hte.XBearing, y + 50 * Sc); ctx.ShowText("Drag spells from the right");

        double radCX = x + cw / 2, radCY = y + LPH / 2 + 10 * Sc, radR = 80 * Sc;
        ctx.Arc(radCX, radCY, radR, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(CB[0], CB[1], CB[2], 0.10); ctx.LineWidth = 1 * Sc; ctx.Stroke();

        double[] angles = { -Math.PI / 2, -Math.PI / 2 + 2 * Math.PI / 3, -Math.PI / 2 + 4 * Math.PI / 3 };
        string[] labels = { "[1]", "[2]", "[3]" };
        var entity = capi.World.Player?.Entity;
        var data   = entity != null ? PlayerSpellData.For(entity) : null;

        ctx.SetSourceRGBA(CB[0], CB[1], CB[2], 0.18); ctx.LineWidth = 1 * Sc;
        for (int i = 0; i < 3; i++)
        {
            int j = (i + 1) % 3;
            ctx.MoveTo(radCX + radR * Math.Cos(angles[i]), radCY + radR * Math.Sin(angles[i]));
            ctx.LineTo(radCX + radR * Math.Cos(angles[j]), radCY + radR * Math.Sin(angles[j])); ctx.Stroke();
        }
        for (int i = 0; i < 3; i++)
        {
            double scx = radCX + radR * Math.Cos(angles[i]), scy = radCY + radR * Math.Sin(angles[i]);
            hotbarSlotPos[i] = (scx, scy);
            bool isTarget = isDragging && HitTestSlot(i, dragX, dragY);
            string? slotId = data?.GetHotbarSlot(i);

            ctx.Arc(scx, scy, SlotR, 0, 2 * Math.PI); ctx.SetSourceRGBA(0.80, 0.75, 0.60, 0.22); ctx.Fill();
            ctx.Arc(scx, scy, SlotR, 0, 2 * Math.PI);
            ctx.SetSourceRGBA(CB[0], CB[1], CB[2], isTarget ? 0.90 : 0.42);
            ctx.LineWidth = (isTarget ? 2.5 : 1.5) * Sc; ctx.Stroke();

            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(8 * Sc);
            ctx.SetSourceRGBA(CS[0], CS[1], CS[2], 0.40);
            var nle = ctx.TextExtents(labels[i]);
            ctx.MoveTo(scx - nle.Width / 2 - nle.XBearing, scy + SlotR + 11 * Sc); ctx.ShowText(labels[i]);

            if (slotId != null)
            {
                var spell = SpellRegistry.Get(slotId);
                if (spell != null)
                {
                    var (er, eg, eb) = ElemColor(spell.Element);
                    DrawIconInCircle(ctx, spell, scx, scy, SlotR - 3 * Sc, er, eg, eb);
                    ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(8 * Sc);
                    ctx.SetSourceRGBA(CT[0], CT[1], CT[2], 0.75);
                    var snte = ctx.TextExtents(spell.Name);
                    ctx.MoveTo(scx - snte.Width / 2 - snte.XBearing, scy + SlotR + 22 * Sc); ctx.ShowText(spell.Name);
                }
            }
            else
            {
                ctx.SetSourceRGBA(CB[0], CB[1], CB[2], 0.16); ctx.LineWidth = 1.5 * Sc;
                ctx.MoveTo(scx - 6 * Sc, scy); ctx.LineTo(scx + 6 * Sc, scy); ctx.Stroke();
                ctx.MoveTo(scx, scy - 6 * Sc); ctx.LineTo(scx, scy + 6 * Sc); ctx.Stroke();
            }
        }
        ctx.Restore();
    }

    // ---- Memorized right ----

    private void DrawMemorizedRight(Context ctx)
    {
        memorizedSpellCards.Clear();
        ctx.Save();
        ctx.Rectangle(RPX, RPY, RPW, RPH); ctx.Clip();
        double x = RPX + 8, y = RPY + 10, w = RPW - 16, h = RPH - 18;
        var entity = capi.World.Player?.Entity;
        var data   = entity != null ? PlayerSpellData.For(entity) : null;

        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Bold); ctx.SetFontSize(13 * Sc);
        ctx.SetSourceRGBA(CB[0], CB[1], CB[2], 0.80);
        var tte = ctx.TextExtents("Unlocked Spells");
        ctx.MoveTo(x + w / 2 - tte.Width / 2 - tte.XBearing, y + 14 * Sc); ctx.ShowText("Unlocked Spells");
        Divider(ctx, x + 4, y + 20 * Sc, w - 8);

        var ids = data?.GetUnlockedIds().ToList() ?? new List<string>();
        double listY = y + 28 * Sc;
        if (ids.Count == 0)
        {
            ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal); ctx.SetFontSize(12 * Sc);
            ctx.SetSourceRGBA(CS[0], CS[1], CS[2], 0.55);
            var ete = ctx.TextExtents("No spells unlocked yet.");
            ctx.MoveTo(x + w / 2 - ete.Width / 2 - ete.XBearing, listY + (h - 28 * Sc) / 2);
            ctx.ShowText("No spells unlocked yet."); ctx.Restore(); return;
        }

        int    cols   = Math.Max(1, (int)((w + 6) / (CardW + 6)));
        double gridW  = cols * CardW + (cols - 1) * 6;
        double startX = x + w / 2 - gridW / 2;
        for (int ci = 0; ci < ids.Count; ci++)
        {
            int col = ci % cols, row = ci / cols;
            double cx = startX + col * (CardW + 6), cardY = listY + row * (CardH + 6);
            if (cardY + CardH > y + h) break;
            var spell = SpellRegistry.Get(ids[ci]); if (spell == null) continue;
            bool drag = isDragging && dragSpellId == spell.Id && dragFromSlot == -1;
            var (er, eg, eb) = ElemColor(spell.Element);
            memorizedSpellCards.Add(new SpellCard(spell.Id, cx, cardY, CardW, CardH));
            RoundedRect(ctx, cx, cardY, CardW, CardH, 5);
            ctx.SetSourceRGBA(er * 0.10 + 0.74, eg * 0.10 + 0.70, eb * 0.10 + 0.54, drag ? 0.18 : 0.72); ctx.Fill();
            RoundedRect(ctx, cx, cardY, CardW, CardH, 5);
            ctx.SetSourceRGBA(er, eg, eb, drag ? 0.12 : 0.42); ctx.LineWidth = 1.2 * Sc; ctx.Stroke();
            if (!drag)
            {
                ctx.Arc(cx + 11 * Sc, cardY + CardH / 2, 4 * Sc, 0, 2 * Math.PI);
                ctx.SetSourceRGBA(er, eg, eb, 0.75); ctx.Fill();
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(10 * Sc);
                ctx.SetSourceRGBA(CT[0], CT[1], CT[2], 0.88);
                ctx.MoveTo(cx + 21 * Sc, cardY + CardH / 2 - 2 * Sc); ctx.ShowText(spell.Name);
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(8 * Sc);
                ctx.SetSourceRGBA(CS[0], CS[1], CS[2], 0.65);
                ctx.MoveTo(cx + 21 * Sc, cardY + CardH / 2 + 10 * Sc); ctx.ShowText($"{spell.FluxCost} Flux · {spell.Element}");
            }
        }

        if (isDragging && dragSpellId != null)
        {
            var ds = SpellRegistry.Get(dragSpellId);
            if (ds != null)
            {
                var (er, eg, eb) = ElemColor(ds.Element);
                double dx = dragX - CardW / 2, dy = dragY - CardH / 2;
                RoundedRect(ctx, dx, dy, CardW, CardH, 5);
                ctx.SetSourceRGBA(er * 0.10 + 0.74, eg * 0.10 + 0.70, eb * 0.10 + 0.54, 0.95); ctx.Fill();
                RoundedRect(ctx, dx, dy, CardW, CardH, 5);
                ctx.SetSourceRGBA(er, eg, eb, 0.80); ctx.LineWidth = 2 * Sc; ctx.Stroke();
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(10 * Sc);
                ctx.SetSourceRGBA(CT[0], CT[1], CT[2], 1.0);
                ctx.MoveTo(dx + 21 * Sc, dy + CardH / 2 + 4 * Sc); ctx.ShowText(ds.Name);
            }
        }
        ctx.Restore();
    }

    private void DrawComingSoon(Context ctx)
    {
        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal); ctx.SetFontSize(14 * Sc);
        ctx.SetSourceRGBA(CS[0], CS[1], CS[2], 0.65);
        string ph = TabNames[currentTab] + " — coming soon";
        var te = ctx.TextExtents(ph);
        ctx.MoveTo(RPX + RPW / 2 - te.Width / 2 - te.XBearing, RPY + RPH / 2); ctx.ShowText(ph);
    }

    // ---- XP bar ----

    private void DrawXpBar(Context ctx, double x, double y, double maxW, SpellElement element)
    {
        var entity = capi.World.Player?.Entity; if (entity == null) return;
        var data = PlayerSpellData.For(entity);
        int level = data.GetElementLevel(element), xpIn = data.GetElementXpInLevel(element);
        int xpNext = PlayerSpellData.XpForLevel(level), sp = data.GetSkillPoints(element);
        var (er, eg, eb) = ElemColor(element);
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(11 * Sc);
        ctx.SetSourceRGBA(er * 0.60, eg * 0.60, eb * 0.60, 1.0);
        ctx.MoveTo(x, y + 11 * Sc); ctx.ShowText(element.ToString());
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(10 * Sc);
        ctx.SetSourceRGBA(CS[0], CS[1], CS[2], 1.0);
        ctx.MoveTo(x, y + 23 * Sc); ctx.ShowText($"Lvl {level}   SP: {sp}");
        string xpLabel = $"{xpIn}/{xpNext} XP";
        var xpte = ctx.TextExtents(xpLabel);
        ctx.MoveTo(x + maxW - xpte.Width - xpte.XBearing, y + 23 * Sc); ctx.ShowText(xpLabel);
        double barH = 9 * Sc, barY = y + 29 * Sc, barR = barH / 2;
        RoundedRect(ctx, x, barY, maxW, barH, barR);
        ctx.SetSourceRGBA(0.60, 0.48, 0.30, 0.35); ctx.Fill();
        double frac = xpNext > 0 ? Math.Clamp((double)xpIn / xpNext, 0, 1) : 1;
        if (frac > 0.01)
        {
            double fillW = Math.Max(barH, maxW * frac);
            RoundedRect(ctx, x, barY, fillW, barH, barR);
            using var grad = new LinearGradient(x, 0, x + fillW, 0);
            grad.AddColorStop(0, new Color(er * 0.35, eg * 0.35, eb * 0.35, 0.85));
            grad.AddColorStop(1, new Color(er * 0.75, eg * 0.75, eb * 0.75, 0.85));
            ctx.SetSource(grad); ctx.Fill(); ctx.SetSourceRGBA(0, 0, 0, 0);
        }
        RoundedRect(ctx, x, barY, maxW, barH, barR);
        ctx.SetSourceRGBA(CB[0], CB[1], CB[2], 0.35); ctx.LineWidth = 1 * Sc; ctx.Stroke();
    }

    // ---- Helpers ----

    private static void PaintSprite(Context ctx, ImageSurface? surf, double x, double y, double w, double h, double alpha = 1.0)
    {
        if (surf == null) return;
        ctx.Save();
        ctx.Rectangle(x, y, w, h); ctx.Clip();
        ctx.Translate(x, y);
        ctx.Scale(w / surf.Width, h / surf.Height);
        ctx.SetSourceSurface(surf, 0, 0);
        ctx.PaintWithAlpha(alpha);
        ctx.Restore();
    }

    private static void DrawIconInCircle(Context ctx, Spell spell, double cx, double cy, double r,
        double er, double eg, double eb)
    {
        ctx.Save();
        ctx.Arc(cx, cy, r, 0, 2 * Math.PI); ctx.Clip();
        ctx.Arc(cx, cy, r, 0, 2 * Math.PI); ctx.SetSourceRGBA(er * 0.22, eg * 0.22, eb * 0.22, 0.80); ctx.Fill();
        ctx.SelectFontFace("Serif", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(r * 1.1);
        string letter = spell.Name.Length > 0 ? spell.Name[0].ToString() : "?";
        ctx.SetSourceRGBA(er * 0.75, eg * 0.75, eb * 0.75, 0.90);
        var te = ctx.TextExtents(letter);
        ctx.MoveTo(cx - te.Width / 2 - te.XBearing, cy + te.Height / 2 - te.YBearing - te.Height);
        ctx.ShowText(letter);
        ctx.Restore();
    }

    private static void Divider(Context ctx, double x, double y, double w)
    {
        ctx.SetSourceRGBA(0.55, 0.38, 0.14, 0.28); ctx.LineWidth = 1;
        ctx.MoveTo(x, y); ctx.LineTo(x + w, y); ctx.Stroke();
    }

    private static void RoundedRect(Context ctx, double x, double y, double w, double h, double r)
    {
        r = Math.Min(r, Math.Min(w / 2, h / 2));
        ctx.NewPath();
        ctx.Arc(x + r,     y + r,     r, Math.PI,         3 * Math.PI / 2);
        ctx.Arc(x + w - r, y + r,     r, 3 * Math.PI / 2, 0);
        ctx.Arc(x + w - r, y + h - r, r, 0,               Math.PI / 2);
        ctx.Arc(x + r,     y + h - r, r, Math.PI / 2,     Math.PI);
        ctx.ClosePath();
    }

    // ---- Input ----

    public override void OnMouseMove(MouseEvent args)
    {
        double rx = args.X - SingleComposer.Bounds.absX;
        double ry = args.Y - SingleComposer.Bounds.absY;
        if (isDragging) { dragX = rx; dragY = ry; Redraw(); args.Handled = true; return; }
        if (isPanning)  { treePanX = panStartOffsetX + (rx - panStartMouseX); treePanY = panStartOffsetY + (ry - panStartMouseY); Redraw(); args.Handled = true; return; }

        int prevMain = hoveredMainTab, prevElem = hoveredElemTab; bool prevClose = closeHovered; string? prevSpell = hoveredSpellId;
        hoveredMainTab = -1; hoveredElemTab = -1; closeHovered = false; hoveredSpellId = null;

        if (rx >= CloseX && rx <= CloseX + CloseW && ry >= CloseY && ry <= CloseY + CloseH) closeHovered = true;
        else
        {
            for (int i = 0; i < 4; i++)
                if (rx >= MTabX && rx <= MTabX + MTabW && ry >= mainTabY[i] && ry <= mainTabY[i] + mainTabH[i])
                { hoveredMainTab = i; break; }
            if (hoveredMainTab == -1 && currentTab == TabSpells)
            {
                double tw = ETabW;
                for (int i = 0; i < 4; i++)
                    if (rx >= elemTabX[i] && rx <= elemTabX[i] + tw && ry >= elemTabY[i] && ry <= elemTabY[i] + elemTabHHit[i])
                    { hoveredElemTab = i; break; }
            }
            if (hoveredMainTab == -1 && hoveredElemTab == -1 && currentTab == TabSpells)
                foreach (var node in spellNodes)
                {
                    double dx = rx - node.Cx, dy = ry - node.Cy;
                    if (dx * dx + dy * dy <= node.R * node.R) { hoveredSpellId = node.SpellId; break; }
                }
        }
        if (hoveredMainTab != prevMain || hoveredElemTab != prevElem || closeHovered != prevClose || hoveredSpellId != prevSpell) Redraw();
    }

    public override void OnMouseDown(MouseEvent args)
    {
        double rx = args.X - SingleComposer.Bounds.absX;
        double ry = args.Y - SingleComposer.Bounds.absY;

        if (rx >= CloseX && rx <= CloseX + CloseW && ry >= CloseY && ry <= CloseY + CloseH)
        { TryClose(); args.Handled = true; return; }

        for (int i = 0; i < 4; i++)
            if (rx >= MTabX && rx <= MTabX + MTabW && ry >= mainTabY[i] && ry <= mainTabY[i] + mainTabH[i])
            { currentTab = i; Redraw(); args.Handled = true; return; }

        if (currentTab == TabSpells)
        {
            double tw = ETabW;
            for (int i = 0; i < 4; i++)
                if (rx >= elemTabX[i] && rx <= elemTabX[i] + tw && ry >= elemTabY[i] && ry <= elemTabY[i] + elemTabHHit[i])
                {
                    int idx = Array.IndexOf(Elements, ElemOrder[i]);
                    if (idx >= 0) { selectedElementIndex = idx; treePanX = 0; treePanY = 0; Redraw(); }
                    args.Handled = true; return;
                }
            foreach (var node in spellNodes)
            {
                double dx = rx - node.Cx, dy = ry - node.Cy;
                if (dx * dx + dy * dy <= node.R * node.R)
                { channel.SendPacket(new MsgUnlockSpell { SpellId = node.SpellId }); Redraw(); args.Handled = true; return; }
            }
            if (rx >= treeAreaX && rx <= treeAreaX + treeAreaW && ry >= treeAreaY && ry <= treeAreaY + treeAreaH)
            { isPanning = true; panStartMouseX = rx; panStartMouseY = ry; panStartOffsetX = treePanX; panStartOffsetY = treePanY; args.Handled = true; return; }
        }

        if (currentTab == TabMemorized)
        {
            var entity = capi.World.Player?.Entity;
            var data   = entity != null ? PlayerSpellData.For(entity) : null;
            for (int i = 0; i < 3; i++)
                if (HitTestSlot(i, rx, ry))
                {
                    string? ss = data?.GetHotbarSlot(i);
                    if (ss != null) { isDragging = true; dragSpellId = ss; dragFromSlot = i; dragX = rx; dragY = ry; args.Handled = true; return; }
                }
            foreach (var card in memorizedSpellCards)
                if (rx >= card.X && rx <= card.X + card.W && ry >= card.Y && ry <= card.Y + card.H)
                { isDragging = true; dragSpellId = card.SpellId; dragFromSlot = -1; dragX = rx; dragY = ry; args.Handled = true; return; }
        }

        if (rx >= 0 && rx <= W && ry >= 0 && ry <= H) args.Handled = true;
    }

    public override void OnMouseUp(MouseEvent args)
    {
        if (isPanning) { isPanning = false; args.Handled = true; return; }
        if (!isDragging) { base.OnMouseUp(args); return; }
        double rx = args.X - SingleComposer.Bounds.absX, ry = args.Y - SingleComposer.Bounds.absY;
        int dropSlot = FindHotbarSlotAtPos(rx, ry);
        if (dropSlot >= 0 && dragSpellId != null)
        {
            if (dragFromSlot >= 0 && dragFromSlot != dropSlot)
                channel.SendPacket(new MsgSetHotbarSlot { Slot = dragFromSlot, SpellId = "" });
            channel.SendPacket(new MsgSetHotbarSlot { Slot = dropSlot, SpellId = dragSpellId });
        }
        else if (dragFromSlot >= 0)
            channel.SendPacket(new MsgSetHotbarSlot { Slot = dragFromSlot, SpellId = "" });
        isDragging = false; dragSpellId = null; dragFromSlot = -1;
        Redraw(); args.Handled = true;
    }

    public override string ToggleKeyCombinationCode => "spellsandrunes.spellbook";
    public override EnumDialogType DialogType => EnumDialogType.Dialog;

    private bool HitTestSlot(int slot, double rx, double ry)
    {
        if (slot < 0 || slot >= 3) return false;
        var (cx, cy) = hotbarSlotPos[slot];
        double dx = rx - cx, dy = ry - cy;
        return dx * dx + dy * dy <= (SlotR + 8 * Sc) * (SlotR + 8 * Sc);
    }
    private int FindHotbarSlotAtPos(double rx, double ry)
    {
        for (int i = 0; i < 3; i++) if (HitTestSlot(i, rx, ry)) return i;
        return -1;
    }

    private record SpellNode(string SpellId, double Cx, double Cy, double R);
    private record SpellCard(string SpellId, double X, double Y, double W, double H);
}
