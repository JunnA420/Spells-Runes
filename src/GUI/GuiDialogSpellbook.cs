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

    // Live layout — reloaded from JSON every time the dialog opens
    private SpellbookLayout L = new();

    // Dialog size is fixed (matches book_bg.png)
    private const double DialogW = 940, DialogH = 600;

    // Elements
    private SpellElement[] Elements = Array.Empty<SpellElement>();
    private int selectedElementIndex = 0;
    private SpellElement SelectedElement => Elements.Length > 0 ? Elements[selectedElementIndex] : SpellElement.Air;

    private static readonly SpellElement[] ElemOrder   = { SpellElement.Fire, SpellElement.Water, SpellElement.Earth, SpellElement.Air };
    private static readonly string[]       ElemTabKeys = { "fire", "water", "earth", "air" };

    // Spell tree
    private readonly List<SpellNode> spellNodes = new();
    private string? hoveredSpellId = null;

    // Memorized
    private readonly List<SpellCard> memorizedSpellCards = new();
    private readonly (double cx, double cy)[] hotbarSlotPos = new (double, double)[3];

    // Drag & drop
    private string? dragSpellId  = null;
    private int     dragFromSlot = -1;
    private double  dragX, dragY;
    private bool    isDragging   = false;

    // ---- Sprites ----
    private ImageSurface? bookBgSurface;
    private ImageSurface? closeBookmarkSurface;
    private readonly ImageSurface?[] mainTabActive   = new ImageSurface?[4];
    private readonly ImageSurface?[] mainTabInactive = new ImageSurface?[4];
    private readonly double[]        mainTabDispH    = new double[4];
    private readonly double[]        mainTabDispHI   = new double[4];
    private readonly ImageSurface?[] elemTabActive   = new ImageSurface?[4];
    private readonly ImageSurface?[] elemTabInactive = new ImageSurface?[4];
    private readonly double[]        elemTabDispH    = new double[4];
    private readonly double[]        elemTabDispHI   = new double[4];

    // ---- Colors ----
    private static readonly double[] ColorText    = { 0.18, 0.12, 0.06, 1.00 };
    private static readonly double[] ColorSubtext = { 0.40, 0.30, 0.18, 0.85 };
    private static readonly double[] ColorBorder  = { 0.55, 0.38, 0.14, 1.00 };
    private static readonly double[] ColorXpBarBg = { 0.60, 0.48, 0.30, 0.35 };

    private static (double r, double g, double b) ElementColor(SpellElement el) => el switch
    {
        SpellElement.Fire  => (0.75, 0.25, 0.06),
        SpellElement.Water => (0.12, 0.40, 0.70),
        SpellElement.Earth => (0.25, 0.50, 0.15),
        SpellElement.Air   => (0.30, 0.55, 0.80),
        _                  => (0.45, 0.38, 0.28),
    };

    // ---- Computed from layout ----
    private double ETabW    => L.ETabW();
    private double ETabMaxH => elemTabDispH.Any(d => d > 0) ? elemTabDispH.Max() : 36;

    // Hit regions (populated during draw)
    private readonly double[] mainTabY    = new double[4];
    private readonly double[] mainTabH    = new double[4];
    private readonly double[] elemTabX    = new double[4];
    private readonly double[] elemTabY    = new double[4];
    private readonly double[] elemTabHHit = new double[4];
    private int  hoveredMainTab = -1;
    private int  hoveredElemTab = -1;
    private bool closeHovered   = false;

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
        RecomputeTabSizes();
        base.OnGuiOpened();
    }

    private void RebuildElements()
    {
        Elements = SpellRegistry.All.Values
            .Select(s => s.Element).Distinct().OrderBy(e => e).ToArray();
        if (selectedElementIndex >= Elements.Length) selectedElementIndex = 0;
    }

    public void ReloadData()
    {
        if (IsOpened()) Redraw();
    }

    private void Redraw() =>
        (SingleComposer?.GetElement("spellbookCanvas") as GuiElementCustomDraw)?.Redraw();

    // ---- Asset loading ----

    private void LoadTextures()
    {
        bookBgSurface        = LoadSurface("textures/gui/book_bg.png");
        closeBookmarkSurface = LoadSurface("textures/gui/close_bookmark.png");

        string[] mainNames = { "spells", "memorized", "lore", "runes" };
        for (int i = 0; i < 4; i++)
        {
            mainTabActive[i]   = LoadSurface($"textures/gui/main_tab_{mainNames[i]}_active.png");
            mainTabInactive[i] = LoadSurface($"textures/gui/main_tab_{mainNames[i]}_inactive.png");
            elemTabActive[i]   = LoadSurface($"textures/gui/elem_tab_{ElemTabKeys[i]}_active.png");
            elemTabInactive[i] = LoadSurface($"textures/gui/elem_tab_{ElemTabKeys[i]}_inactive.png");
        }
        RecomputeTabSizes();
    }

    /// Recomputes sprite display sizes from current L values — no image reload needed.
    private void RecomputeTabSizes()
    {
        double tw = ETabW;
        for (int i = 0; i < 4; i++)
        {
            var a = mainTabActive[i];
            mainTabDispH[i]  = a != null ? L.MTabW * a.Height / a.Width : 38;
            var n = mainTabInactive[i];
            mainTabDispHI[i] = n != null ? L.MTabW * n.Height / n.Width : 34;

            var ea = elemTabActive[i];
            elemTabDispH[i]  = ea != null ? tw * ea.Height / ea.Width : 36;
            var ei = elemTabInactive[i];
            elemTabDispHI[i] = ei != null ? tw * ei.Height / ei.Width : 32;
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

    // ---- Compose ----

    private void ComposeDialog()
    {
        var db = ElementBounds.Fixed(EnumDialogArea.CenterMiddle, 0, 0, DialogW, DialogH);
        var cb = ElementBounds.Fixed(0, 0, DialogW, DialogH);
        SingleComposer = capi.Gui
            .CreateCompo("spellsandrunes:spellbook", db)
            .AddDynamicCustomDraw(cb, DrawSpellbook, "spellbookCanvas")
            .Compose();
    }

    // ---- Main draw ----

    private void DrawSpellbook(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        double w = bounds.InnerWidth, h = bounds.InnerHeight;

        if (bookBgSurface != null)
        {
            ctx.Save();
            ctx.Scale(w / bookBgSurface.Width, h / bookBgSurface.Height);
            ctx.SetSourceSurface(bookBgSurface, 0, 0);
            ctx.Paint();
            ctx.Restore();
        }

        DrawCloseBookmark(ctx);
        DrawMainTabs(ctx);

        switch (currentTab)
        {
            case TabSpells:
                DrawSpellsLeftPage(ctx);
                DrawSpellsRightPage(ctx);
                break;
            case TabMemorized:
                DrawMemorizedLeftPage(ctx);
                DrawMemorizedRightPage(ctx);
                break;
            default:
                DrawComingSoon(ctx);
                break;
        }
    }

    // ---- Close bookmark ----

    private void DrawCloseBookmark(Context ctx)
    {
        double x = L.CloseBookX, y = L.CloseBookY, bw = L.CloseBookW, bh = L.CloseBookH;

        if (closeBookmarkSurface != null)
        {
            ctx.Save();
            ctx.Rectangle(x, y, bw, bh);
            ctx.Clip();
            ctx.Translate(x, y);
            ctx.Scale(bw / closeBookmarkSurface.Width, bh / closeBookmarkSurface.Height);
            ctx.SetSourceSurface(closeBookmarkSurface, 0, 0);
            ctx.PaintWithAlpha(closeHovered ? 1.0 : 0.85);
            ctx.Restore();
        }
        else
        {
            RoundedRect(ctx, x, y, bw, bh, 4);
            ctx.SetSourceRGBA(closeHovered ? 0.55 : 0.35, closeHovered ? 0.12 : 0.08, 0.04, 0.80);
            ctx.Fill();
            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(16);
            ctx.SetSourceRGBA(1, 1, 1, 0.85);
            var te = ctx.TextExtents("X");
            ctx.MoveTo(x + bw / 2 - te.Width / 2 - te.XBearing,
                       y + bh / 2 + te.Height / 2 - 1);
            ctx.ShowText("X");
        }
    }

    // ---- Main tabs (vertical) ----

    private void DrawMainTabs(Context ctx)
    {
        double ty = L.MTabStartY;
        double[] ys = new double[4];
        double[] hs = new double[4];
        for (int i = 0; i < 4; i++)
        {
            bool active = currentTab == i;
            ys[i] = ty;
            hs[i] = active ? mainTabDispH[i] : mainTabDispHI[i];
            mainTabY[i] = ty;
            mainTabH[i] = hs[i];
            ty += hs[i] + L.MTabGap;
        }

        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < 4; i++)
            {
                bool active = currentTab == i;
                if ((pass == 0) == active) continue;

                var    surf  = active ? mainTabActive[i] : mainTabInactive[i];
                double dispH = hs[i];

                if (surf != null)
                {
                    ctx.Save();
                    ctx.Rectangle(L.MTabX, ys[i], L.MTabW, dispH);
                    ctx.Clip();
                    ctx.Translate(L.MTabX, ys[i]);
                    ctx.Scale(L.MTabW / surf.Width, dispH / surf.Height);
                    ctx.SetSourceSurface(surf, 0, 0);
                    ctx.Paint();
                    ctx.Restore();
                }
                else
                {
                    RoundedRect(ctx, L.MTabX, ys[i], L.MTabW, dispH, 4);
                    ctx.SetSourceRGBA(active ? 0.70 : 0.50, active ? 0.54 : 0.40,
                                      active ? 0.24 : 0.20, active ? 0.90 : 0.55);
                    ctx.Fill();
                    ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                    ctx.SetFontSize(10);
                    ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], 0.85);
                    var te = ctx.TextExtents(TabNames[i]);
                    ctx.MoveTo(L.MTabX + L.MTabW / 2 - te.Width / 2 - te.XBearing,
                               ys[i] + dispH / 2 + te.Height / 2 - 1);
                    ctx.ShowText(TabNames[i]);
                }
            }
        }
    }

    // ---- Element tabs (horizontal) ----

    private void DrawElementTabs(Context ctx)
    {
        double tw   = ETabW;
        double maxH = ETabMaxH;

        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < 4; i++)
            {
                var    el     = ElemOrder[i];
                bool   active = SelectedElement == el;
                if ((pass == 0) == active) continue;

                bool   exists = Elements.Contains(el);
                double tx     = L.RPageX + i * (tw + L.ETabGap);
                double dispH  = active ? elemTabDispH[i] : elemTabDispHI[i];
                double ty     = L.RPageY + maxH - dispH;
                var    surf   = active ? elemTabActive[i] : elemTabInactive[i];

                elemTabX[i]    = tx;
                elemTabY[i]    = ty;
                elemTabHHit[i] = dispH;

                double alpha = exists ? 1.0 : 0.35;

                if (surf != null)
                {
                    ctx.Save();
                    ctx.Rectangle(tx, ty, tw, dispH);
                    ctx.Clip();
                    ctx.Translate(tx, ty);
                    ctx.Scale(tw / surf.Width, dispH / surf.Height);
                    ctx.SetSourceSurface(surf, 0, 0);
                    ctx.PaintWithAlpha(alpha);
                    ctx.Restore();
                }
                else
                {
                    var (er, eg, eb) = ElementColor(el);
                    RoundedRect(ctx, tx, ty, tw, dispH, 4);
                    ctx.SetSourceRGBA(er * (active ? 0.60 : 0.35), eg * (active ? 0.60 : 0.35),
                                      eb * (active ? 0.60 : 0.35), active ? 0.90 * alpha : 0.50 * alpha);
                    ctx.Fill();
                    ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                    ctx.SetFontSize(9);
                    ctx.SetSourceRGBA(1, 1, 1, exists ? 0.88 : 0.40);
                    var te = ctx.TextExtents(el.ToString());
                    ctx.MoveTo(tx + tw / 2 - te.Width / 2 - te.XBearing,
                               ty + dispH / 2 + te.Height / 2 - 1);
                    ctx.ShowText(el.ToString());
                }
            }
        }
    }

    // ---- Spells tab — left page ----

    private void DrawSpellsLeftPage(Context ctx)
    {
        double x = L.LContentX, cw = L.LContentW;
        double y = L.LPageY;
        var (er, eg, eb) = ElementColor(SelectedElement);

        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Bold);
        ctx.SetFontSize(18);
        ctx.SetSourceRGBA(er * 0.65, eg * 0.65, eb * 0.65, 0.90);
        string elName = SelectedElement.ToString();
        var te = ctx.TextExtents(elName);
        ctx.MoveTo(x + cw / 2 - te.Width / 2 - te.XBearing, y + 28);
        ctx.ShowText(elName);
        DrawDivider(ctx, x + 4, y + 34, cw - 8);

        if (hoveredSpellId != null)
        {
            var spell = SpellRegistry.Get(hoveredSpellId);
            if (spell != null)
                DrawSpellInfoPanel(ctx, spell, x, y + 44, cw, L.LPageH - 44 - 62);
        }
        else
        {
            ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal);
            ctx.SetFontSize(10);
            ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.55);
            string hint = "Hover a spell\nto see details.";
            double hy = y + 80;
            foreach (var line in hint.Split('\n'))
            {
                var lte = ctx.TextExtents(line);
                ctx.MoveTo(x + cw / 2 - lte.Width / 2 - lte.XBearing, hy);
                ctx.ShowText(line);
                hy += 16;
            }
        }

        DrawElementXpBar(ctx, x + 4, y + L.LPageH - 56, cw - 8, SelectedElement);
    }

    private void DrawSpellInfoPanel(Context ctx, Spell spell, double x, double y, double cw, double maxH)
    {
        var (er, eg, eb) = ElementColor(spell.Element);

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(13);
        ctx.SetSourceRGBA(er * 0.70, eg * 0.70, eb * 0.70, 1.0);
        var nte = ctx.TextExtents(spell.Name);
        ctx.MoveTo(x + cw / 2 - nte.Width / 2 - nte.XBearing, y + 14);
        ctx.ShowText(spell.Name);
        DrawDivider(ctx, x + 8, y + 18, cw - 16);

        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal);
        ctx.SetFontSize(10);
        ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], 0.82);

        var words = spell.Description.Split(' ');
        var lines = new List<string>();
        string cur = "";
        foreach (var word in words)
        {
            string test = cur.Length == 0 ? word : cur + " " + word;
            if (ctx.TextExtents(test).Width > cw - 16) { lines.Add(cur); cur = word; }
            else cur = test;
        }
        if (cur.Length > 0) lines.Add(cur);

        double dy = y + 34;
        foreach (var line in lines)
        {
            if (dy + 12 > y + maxH) break;
            ctx.MoveTo(x + 8, dy); ctx.ShowText(line); dy += 14;
        }

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(9);
        ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.85);
        ctx.MoveTo(x + 8, dy + 14);
        ctx.ShowText($"Flux: {spell.FluxCost}   Cost: {(int)spell.Tier} SP");
        ctx.MoveTo(x + 8, dy + 26);
        ctx.ShowText(spell.Element.ToString());

        var entity = capi.World.Player?.Entity;
        var data   = entity != null ? PlayerSpellData.For(entity) : null;
        bool unlocked = data?.IsUnlocked(spell.Id) ?? false;
        if (unlocked && data != null)
        {
            int lvl    = data.GetSpellLevel(spell.Id);
            int xpIn   = data.GetSpellXpInLevel(spell.Id);
            int xpNext = PlayerSpellData.XpForLevel(lvl);
            DrawDivider(ctx, x + 8, dy + 34, cw - 16);
            ctx.SetSourceRGBA(er * 0.65, eg * 0.65, eb * 0.65, 0.90);
            ctx.MoveTo(x + 8, dy + 48);
            ctx.ShowText($"Level {lvl}   {xpIn}/{xpNext} XP");
        }
    }

    // ---- Spells tab — right page ----

    private void DrawSpellsRightPage(Context ctx)
    {
        DrawElementTabs(ctx);
        double treeY = L.RPageY + ETabMaxH + 10;
        double treeH = L.RPageH - ETabMaxH - 18;
        DrawSpellTree(ctx, L.RPageX + 4, treeY, L.RPageW - 8, treeH);
    }

    // ---- Spell tree ----

    private void DrawSpellTree(Context ctx, double areaX, double areaY, double areaW, double areaH)
    {
        spellNodes.Clear();
        var spells = SpellRegistry.All.Values.Where(s => s.Element == SelectedElement).ToList();
        if (spells.Count == 0) return;

        int    maxCol  = spells.Max(s => s.TreePosition.col);
        double gridW   = (maxCol + 1) * L.NodeSpacingX;
        double originX = areaX + areaW / 2 - gridW / 2 + L.NodeSpacingX / 2;
        double originY = areaY + areaH - L.NodeSpacingY / 2 - L.NodeR - 8;

        var posMap = new Dictionary<string, (double cx, double cy)>();
        foreach (var spell in spells)
        {
            double cx = originX + spell.TreePosition.col * L.NodeSpacingX;
            double cy = originY - spell.TreePosition.row * L.NodeSpacingY;
            posMap[spell.Id] = (cx, cy);
            spellNodes.Add(new SpellNode(spell.Id, cx, cy, L.NodeR));
        }

        var entity = capi.World.Player?.Entity;
        var data   = entity != null ? PlayerSpellData.For(entity) : null;
        var (er, eg, eb) = ElementColor(SelectedElement);

        ctx.LineWidth = 1.5;
        foreach (var spell in spells)
        {
            if (!posMap.TryGetValue(spell.Id, out var to)) continue;
            foreach (var prereqId in spell.Prerequisites)
            {
                if (!posMap.TryGetValue(prereqId, out var from)) continue;
                bool pu = data?.IsUnlocked(prereqId) ?? false;
                bool tu = data?.IsUnlocked(spell.Id)  ?? false;
                ctx.SetSourceRGBA(er, eg, eb, pu && tu ? 0.50 : 0.12);
                ctx.MoveTo(from.cx, from.cy); ctx.LineTo(to.cx, to.cy); ctx.Stroke();
            }
        }

        foreach (var spell in spells)
        {
            if (!posMap.TryGetValue(spell.Id, out var pos)) continue;
            bool unlocked  = data?.IsUnlocked(spell.Id) ?? false;
            bool available = data != null && SpellTree.CanUnlock(spell.Id, data);
            DrawSpellNode(ctx, spell, pos.cx, pos.cy, unlocked, available,
                          hoveredSpellId == spell.Id, er, eg, eb);
        }
    }

    private void DrawSpellNode(Context ctx, Spell spell, double cx, double cy,
        bool unlocked, bool available, bool hovered, double er, double eg, double eb)
    {
        double r = L.NodeR;
        if (hovered) { ctx.Arc(cx, cy, r + 5, 0, 2 * Math.PI); ctx.SetSourceRGBA(er, eg, eb, 0.12); ctx.Fill(); }

        ctx.Arc(cx, cy, r, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(unlocked ? er * 0.18 : 0.84, unlocked ? eg * 0.18 : 0.80,
                          unlocked ? eb * 0.18 : 0.68, unlocked ? 0.80 : available ? 0.50 : 0.28);
        ctx.Fill();

        ctx.Arc(cx, cy, r, 0, 2 * Math.PI);
        if (unlocked)       ctx.SetSourceRGBA(er, eg, eb, hovered ? 1.0 : 0.75);
        else if (available) ctx.SetSourceRGBA(er, eg, eb, hovered ? 0.65 : 0.45);
        else                ctx.SetSourceRGBA(0.38, 0.30, 0.18, 0.45);
        ctx.LineWidth = unlocked ? 1.8 : 1.1; ctx.Stroke();

        if (!unlocked) DrawLockIcon(ctx, cx, cy, available ? 0.50 : 0.25);

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(8);
        ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], unlocked ? 0.85 : 0.50);
        var te = ctx.TextExtents(spell.Name);
        ctx.MoveTo(cx - te.Width / 2 - te.XBearing, cy + r + 11); ctx.ShowText(spell.Name);

        if (!unlocked)
        {
            string sp = $"{(int)spell.Tier} SP";
            ctx.SetFontSize(7);
            ctx.SetSourceRGBA(er, eg, eb, available ? 0.70 : 0.25);
            var te2 = ctx.TextExtents(sp);
            ctx.MoveTo(cx - te2.Width / 2 - te2.XBearing, cy + r + 20); ctx.ShowText(sp);
        }
    }

    private static void DrawLockIcon(Context ctx, double cx, double cy, double alpha)
    {
        ctx.SetSourceRGBA(0.38, 0.30, 0.18, alpha); ctx.LineWidth = 1.5;
        ctx.Arc(cx, cy - 3, 3.5, Math.PI, 0); ctx.Stroke();
        RoundedRect(ctx, cx - 4.5, cy - 1, 9, 6, 2);
        ctx.SetSourceRGBA(0.48, 0.38, 0.22, alpha); ctx.Fill();
    }

    // ---- Memorized — left page ----

    private void DrawMemorizedLeftPage(Context ctx)
    {
        double x = L.LContentX, cw = L.LContentW;
        double y = L.LPageY;

        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Bold);
        ctx.SetFontSize(13);
        ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.80);
        string title = "Memorized Spells";
        var te = ctx.TextExtents(title);
        ctx.MoveTo(x + cw / 2 - te.Width / 2 - te.XBearing, y + 26); ctx.ShowText(title);
        DrawDivider(ctx, x + 4, y + 33, cw - 8);

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(9);
        ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.60);
        string hint = "Drag spells from the right page";
        var hte = ctx.TextExtents(hint);
        ctx.MoveTo(x + cw / 2 - hte.Width / 2 - hte.XBearing, y + 50); ctx.ShowText(hint);

        double radCX = x + cw / 2;
        double radCY = y + L.LPageH / 2 + 10;
        double radR  = 80;

        ctx.Arc(radCX, radCY, radR, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.10);
        ctx.LineWidth = 1; ctx.Stroke();

        double[] angles     = { -Math.PI / 2, -Math.PI / 2 + 2 * Math.PI / 3, -Math.PI / 2 + 4 * Math.PI / 3 };
        string[] slotLabels = { "[1]", "[2]", "[3]" };

        var entity = capi.World.Player?.Entity;
        var data   = entity != null ? PlayerSpellData.For(entity) : null;

        ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.18);
        ctx.LineWidth = 1;
        for (int i = 0; i < 3; i++)
        {
            int j = (i + 1) % 3;
            ctx.MoveTo(radCX + radR * Math.Cos(angles[i]), radCY + radR * Math.Sin(angles[i]));
            ctx.LineTo(radCX + radR * Math.Cos(angles[j]), radCY + radR * Math.Sin(angles[j]));
            ctx.Stroke();
        }

        for (int i = 0; i < 3; i++)
        {
            double scx = radCX + radR * Math.Cos(angles[i]);
            double scy = radCY + radR * Math.Sin(angles[i]);
            hotbarSlotPos[i] = (scx, scy);

            bool isTarget  = isDragging && HitTestSlot(i, dragX, dragY);
            string? slotId = data?.GetHotbarSlot(i);

            ctx.Arc(scx, scy, L.SlotR, 0, 2 * Math.PI);
            ctx.SetSourceRGBA(0.80, 0.75, 0.60, 0.22); ctx.Fill();
            ctx.Arc(scx, scy, L.SlotR, 0, 2 * Math.PI);
            ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], isTarget ? 0.90 : 0.42);
            ctx.LineWidth = isTarget ? 2.5 : 1.5; ctx.Stroke();

            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(8);
            ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.40);
            var nle = ctx.TextExtents(slotLabels[i]);
            ctx.MoveTo(scx - nle.Width / 2 - nle.XBearing, scy + L.SlotR + 11); ctx.ShowText(slotLabels[i]);

            if (slotId != null)
            {
                var spell = SpellRegistry.Get(slotId);
                if (spell != null)
                {
                    var (er, eg, eb) = ElementColor(spell.Element);
                    DrawSpellIconInCircle(ctx, spell, scx, scy, L.SlotR - 3, er, eg, eb);
                    ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
                    ctx.SetFontSize(8);
                    ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], 0.75);
                    var snte = ctx.TextExtents(spell.Name);
                    ctx.MoveTo(scx - snte.Width / 2 - snte.XBearing, scy + L.SlotR + 22); ctx.ShowText(spell.Name);
                }
            }
            else
            {
                ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.16);
                ctx.LineWidth = 1.5;
                ctx.MoveTo(scx - 6, scy); ctx.LineTo(scx + 6, scy); ctx.Stroke();
                ctx.MoveTo(scx, scy - 6); ctx.LineTo(scx, scy + 6); ctx.Stroke();
            }
        }
    }

    // ---- Memorized — right page ----

    private void DrawMemorizedRightPage(Context ctx)
    {
        memorizedSpellCards.Clear();

        double x = L.RPageX + 8, y = L.RPageY + 10, w = L.RPageW - 16, h = L.RPageH - 18;

        var entity = capi.World.Player?.Entity;
        var data   = entity != null ? PlayerSpellData.For(entity) : null;

        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Bold);
        ctx.SetFontSize(13);
        ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.80);
        string title = "Unlocked Spells";
        var tte = ctx.TextExtents(title);
        ctx.MoveTo(x + w / 2 - tte.Width / 2 - tte.XBearing, y + 14); ctx.ShowText(title);
        DrawDivider(ctx, x + 4, y + 20, w - 8);

        var unlockedIds = data?.GetUnlockedIds().ToList() ?? new List<string>();
        double listY = y + 28;

        if (unlockedIds.Count == 0)
        {
            ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal);
            ctx.SetFontSize(12);
            ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.55);
            string empty = "No spells unlocked yet.";
            var ete = ctx.TextExtents(empty);
            ctx.MoveTo(x + w / 2 - ete.Width / 2 - ete.XBearing, listY + (h - 28) / 2);
            ctx.ShowText(empty);
            return;
        }

        int    cols   = Math.Max(1, (int)((w + 6) / (L.CardW + 6)));
        double gridW  = cols * L.CardW + (cols - 1) * 6;
        double startX = x + w / 2 - gridW / 2;

        for (int ci = 0; ci < unlockedIds.Count; ci++)
        {
            int    col   = ci % cols, row = ci / cols;
            double cx    = startX + col * (L.CardW + 6);
            double cardY = listY + row * (L.CardH + 6);
            if (cardY + L.CardH > y + h) break;

            var spell = SpellRegistry.Get(unlockedIds[ci]);
            if (spell == null) continue;

            bool beingDragged = isDragging && dragSpellId == spell.Id && dragFromSlot == -1;
            var (er, eg, eb)  = ElementColor(spell.Element);
            memorizedSpellCards.Add(new SpellCard(spell.Id, cx, cardY, L.CardW, L.CardH));

            RoundedRect(ctx, cx, cardY, L.CardW, L.CardH, 5);
            ctx.SetSourceRGBA(er * 0.10 + 0.74, eg * 0.10 + 0.70, eb * 0.10 + 0.54,
                              beingDragged ? 0.18 : 0.72);
            ctx.Fill();
            RoundedRect(ctx, cx, cardY, L.CardW, L.CardH, 5);
            ctx.SetSourceRGBA(er, eg, eb, beingDragged ? 0.12 : 0.42);
            ctx.LineWidth = 1.2; ctx.Stroke();

            if (!beingDragged)
            {
                ctx.Arc(cx + 11, cardY + L.CardH / 2, 4, 0, 2 * Math.PI);
                ctx.SetSourceRGBA(er, eg, eb, 0.75); ctx.Fill();
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                ctx.SetFontSize(10);
                ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], 0.88);
                ctx.MoveTo(cx + 21, cardY + L.CardH / 2 - 2); ctx.ShowText(spell.Name);
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
                ctx.SetFontSize(8);
                ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.65);
                ctx.MoveTo(cx + 21, cardY + L.CardH / 2 + 10);
                ctx.ShowText($"{spell.FluxCost} Flux · {spell.Element}");
            }
        }

        if (isDragging && dragSpellId != null)
        {
            var dspell = SpellRegistry.Get(dragSpellId);
            if (dspell != null)
            {
                var (er, eg, eb) = ElementColor(dspell.Element);
                double dx = dragX - L.CardW / 2, dy = dragY - L.CardH / 2;
                RoundedRect(ctx, dx, dy, L.CardW, L.CardH, 5);
                ctx.SetSourceRGBA(er * 0.10 + 0.74, eg * 0.10 + 0.70, eb * 0.10 + 0.54, 0.95); ctx.Fill();
                RoundedRect(ctx, dx, dy, L.CardW, L.CardH, 5);
                ctx.SetSourceRGBA(er, eg, eb, 0.80); ctx.LineWidth = 2; ctx.Stroke();
                ctx.Arc(dx + 11, dy + L.CardH / 2, 4, 0, 2 * Math.PI);
                ctx.SetSourceRGBA(er, eg, eb, 1.0); ctx.Fill();
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                ctx.SetFontSize(10);
                ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], 1.0);
                ctx.MoveTo(dx + 21, dy + L.CardH / 2 + 4); ctx.ShowText(dspell.Name);
            }
        }
    }

    private void DrawComingSoon(Context ctx)
    {
        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal);
        ctx.SetFontSize(14);
        ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.65);
        string ph = TabNames[currentTab] + " — coming soon";
        var te = ctx.TextExtents(ph);
        double midX = L.RPageX + L.RPageW / 2, midY = L.RPageY + L.RPageH / 2;
        ctx.MoveTo(midX - te.Width / 2 - te.XBearing, midY); ctx.ShowText(ph);
    }

    // ---- XP bar ----

    private void DrawElementXpBar(Context ctx, double x, double y, double maxW, SpellElement element)
    {
        var entity = capi.World.Player?.Entity;
        if (entity == null) return;
        var data   = PlayerSpellData.For(entity);
        int level  = data.GetElementLevel(element);
        int xpIn   = data.GetElementXpInLevel(element);
        int xpNext = PlayerSpellData.XpForLevel(level);
        int sp     = data.GetSkillPoints(element);
        var (er, eg, eb) = ElementColor(element);

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(11);
        ctx.SetSourceRGBA(er * 0.60, eg * 0.60, eb * 0.60, 1.0);
        ctx.MoveTo(x, y + 11); ctx.ShowText(element.ToString());

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(10);
        ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 1.0);
        ctx.MoveTo(x, y + 23); ctx.ShowText($"Lvl {level}   SP: {sp}");
        string xpLabel = $"{xpIn}/{xpNext} XP";
        var xpte = ctx.TextExtents(xpLabel);
        ctx.MoveTo(x + maxW - xpte.Width - xpte.XBearing, y + 23); ctx.ShowText(xpLabel);

        double barH = 9, barY = y + 29, barR = barH / 2;
        RoundedRect(ctx, x, barY, maxW, barH, barR);
        ctx.SetSourceRGBA(ColorXpBarBg[0], ColorXpBarBg[1], ColorXpBarBg[2], ColorXpBarBg[3]); ctx.Fill();

        double frac = xpNext > 0 ? Math.Clamp((double)xpIn / xpNext, 0, 1) : 1;
        if (frac > 0.01)
        {
            double fillW = Math.Max(barH, maxW * frac);
            RoundedRect(ctx, x, barY, fillW, barH, barR);
            using var grad = new LinearGradient(x, 0, x + fillW, 0);
            grad.AddColorStop(0, new Color(er * 0.35, eg * 0.35, eb * 0.35, 0.85));
            grad.AddColorStop(1, new Color(er * 0.75, eg * 0.75, eb * 0.75, 0.85));
            ctx.SetSource(grad); ctx.Fill();
            ctx.SetSourceRGBA(0, 0, 0, 0);
        }
        RoundedRect(ctx, x, barY, maxW, barH, barR);
        ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.35);
        ctx.LineWidth = 1; ctx.Stroke();
    }

    // ---- Helpers ----

    private static void DrawSpellIconInCircle(Context ctx, Spell spell, double cx, double cy, double r,
        double er, double eg, double eb)
    {
        ctx.Save();
        ctx.Arc(cx, cy, r, 0, 2 * Math.PI); ctx.Clip();
        ctx.Arc(cx, cy, r, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(er * 0.22, eg * 0.22, eb * 0.22, 0.80); ctx.Fill();
        ctx.SelectFontFace("Serif", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(r * 1.1);
        string letter = spell.Name.Length > 0 ? spell.Name[0].ToString() : "?";
        ctx.SetSourceRGBA(er * 0.75, eg * 0.75, eb * 0.75, 0.90);
        var te = ctx.TextExtents(letter);
        ctx.MoveTo(cx - te.Width / 2 - te.XBearing, cy + te.Height / 2 - te.YBearing - te.Height);
        ctx.ShowText(letter);
        ctx.Restore();
    }

    private static void DrawDivider(Context ctx, double x, double y, double w)
    {
        ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.28);
        ctx.LineWidth = 1; ctx.MoveTo(x, y); ctx.LineTo(x + w, y); ctx.Stroke();
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

        if (isDragging)
        { dragX = rx; dragY = ry; Redraw(); args.Handled = true; return; }

        int     prevMain  = hoveredMainTab;
        int     prevElem  = hoveredElemTab;
        bool    prevClose = closeHovered;
        string? prevSpell = hoveredSpellId;

        hoveredMainTab = -1; hoveredElemTab = -1; closeHovered = false; hoveredSpellId = null;

        if (rx >= L.CloseBookX && rx <= L.CloseBookX + L.CloseBookW &&
            ry >= L.CloseBookY && ry <= L.CloseBookY + L.CloseBookH)
        {
            closeHovered = true;
        }
        else
        {
            for (int i = 0; i < 4; i++)
                if (rx >= L.MTabX && rx <= L.MTabX + L.MTabW &&
                    ry >= mainTabY[i] && ry <= mainTabY[i] + mainTabH[i])
                { hoveredMainTab = i; break; }

            if (hoveredMainTab == -1 && currentTab == TabSpells)
            {
                double tw = ETabW;
                for (int i = 0; i < 4; i++)
                    if (rx >= elemTabX[i] && rx <= elemTabX[i] + tw &&
                        ry >= elemTabY[i] && ry <= elemTabY[i] + elemTabHHit[i])
                    { hoveredElemTab = i; break; }
            }

            if (hoveredMainTab == -1 && hoveredElemTab == -1 && currentTab == TabSpells)
            {
                foreach (var node in spellNodes)
                {
                    double dx = rx - node.Cx, dy = ry - node.Cy;
                    if (dx * dx + dy * dy <= node.R * node.R)
                    { hoveredSpellId = node.SpellId; break; }
                }
            }
        }

        if (hoveredMainTab != prevMain || hoveredElemTab != prevElem ||
            closeHovered != prevClose || hoveredSpellId != prevSpell)
            Redraw();
    }

    public override void OnMouseDown(MouseEvent args)
    {
        double rx = args.X - SingleComposer.Bounds.absX;
        double ry = args.Y - SingleComposer.Bounds.absY;

        if (rx >= L.CloseBookX && rx <= L.CloseBookX + L.CloseBookW &&
            ry >= L.CloseBookY && ry <= L.CloseBookY + L.CloseBookH)
        { TryClose(); args.Handled = true; return; }

        for (int i = 0; i < 4; i++)
        {
            if (rx >= L.MTabX && rx <= L.MTabX + L.MTabW &&
                ry >= mainTabY[i] && ry <= mainTabY[i] + mainTabH[i])
            { currentTab = i; Redraw(); args.Handled = true; return; }
        }

        if (currentTab == TabSpells)
        {
            double tw = ETabW;
            for (int i = 0; i < 4; i++)
            {
                if (rx >= elemTabX[i] && rx <= elemTabX[i] + tw &&
                    ry >= elemTabY[i] && ry <= elemTabY[i] + elemTabHHit[i])
                {
                    int idx = Array.IndexOf(Elements, ElemOrder[i]);
                    if (idx >= 0) { selectedElementIndex = idx; Redraw(); }
                    args.Handled = true; return;
                }
            }

            foreach (var node in spellNodes)
            {
                double dx = rx - node.Cx, dy = ry - node.Cy;
                if (dx * dx + dy * dy <= node.R * node.R)
                {
                    channel.SendPacket(new MsgUnlockSpell { SpellId = node.SpellId });
                    Redraw(); args.Handled = true; return;
                }
            }
        }

        if (currentTab == TabMemorized)
        {
            var entity = capi.World.Player?.Entity;
            var data   = entity != null ? PlayerSpellData.For(entity) : null;

            for (int i = 0; i < 3; i++)
            {
                if (HitTestSlot(i, rx, ry))
                {
                    string? ss = data?.GetHotbarSlot(i);
                    if (ss != null)
                    { isDragging = true; dragSpellId = ss; dragFromSlot = i; dragX = rx; dragY = ry; args.Handled = true; return; }
                }
            }

            foreach (var card in memorizedSpellCards)
            {
                if (rx >= card.X && rx <= card.X + card.W && ry >= card.Y && ry <= card.Y + card.H)
                { isDragging = true; dragSpellId = card.SpellId; dragFromSlot = -1; dragX = rx; dragY = ry; args.Handled = true; return; }
            }
        }

        if (rx >= 0 && rx <= DialogW && ry >= 0 && ry <= DialogH) args.Handled = true;
    }

    public override void OnMouseUp(MouseEvent args)
    {
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
        return dx * dx + dy * dy <= (L.SlotR + 8) * (L.SlotR + 8);
    }

    private int FindHotbarSlotAtPos(double rx, double ry)
    {
        for (int i = 0; i < 3; i++) if (HitTestSlot(i, rx, ry)) return i;
        return -1;
    }

    private record SpellNode(string SpellId, double Cx, double Cy, double R);
    private record SpellCard(string SpellId, double X, double Y, double W, double H);
}
