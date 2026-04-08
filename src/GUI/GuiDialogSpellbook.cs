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

    private SpellElement[] Elements = Array.Empty<SpellElement>();
    private int selectedElementIndex = 0;
    private SpellElement SelectedElement => Elements.Length > 0 ? Elements[selectedElementIndex] : SpellElement.Air;

    // Spell tree
    private readonly List<SpellNode> spellNodes = new();
    private string? hoveredSpellId = null;
    private double tooltipX, tooltipY;

    // Memorized tab
    private readonly List<SpellCard> memorizedSpellCards = new();
    private const double CardW = 195, CardH = 32;
    private const double SlotR = 26;
    private readonly (double cx, double cy)[] hotbarSlotPos = new (double, double)[3];

    // Drag & drop
    private string? dragSpellId = null;
    private int    dragFromSlot = -1;
    private double dragX, dragY;
    private bool   isDragging = false;

    // Sprites
    private ImageSurface? bookBgSurface;
    private readonly ImageSurface?[] sectionTabActive   = new ImageSurface?[4];
    private readonly ImageSurface?[] sectionTabInactive = new ImageSurface?[4];
    private readonly double[]        sTabDispH          = new double[4];
    private readonly double[]        sTabDispHInact     = new double[4];
    private double STabMaxH => sTabDispH.Length > 0 ? sTabDispH.Max() : 35;

    // Colors (parchment style)
    private static readonly double[] ColorText    = { 0.18, 0.12, 0.06, 1.00 };
    private static readonly double[] ColorSubtext = { 0.40, 0.30, 0.18, 0.85 };
    private static readonly double[] ColorBorder  = { 0.55, 0.38, 0.14, 1.00 };
    private static readonly double[] ColorXpBarBg = { 0.60, 0.48, 0.30, 0.35 };

    private static (double r, double g, double b) ElementColor(SpellElement el) => el switch
    {
        SpellElement.Air   => (0.30, 0.55, 0.80),
        SpellElement.Fire  => (0.75, 0.25, 0.06),
        SpellElement.Water => (0.12, 0.40, 0.70),
        SpellElement.Earth => (0.25, 0.50, 0.15),
        _                  => (0.45, 0.38, 0.28),
    };

    // Dialog matches book_bg.png
    private const double DialogW = 940;
    private const double DialogH = 600;

    // Page layout
    private const double LPageX = 30,  LPageY = 55, LPageW = 358, LPageH = 490;
    private const double RPageX = 472, RPageY = 55, RPageW = 438, RPageH = 490;
    private const double STabDispW = RPageW / 4.0;

    // Node geometry
    private const double NodeR        = 22;
    private const double NodeSpacingX = 88;
    private const double NodeSpacingY = 80;

    // Arrow navigation
    private const double ArrowW = 28, ArrowH = 22;
    private double arrowPrevX, arrowPrevY, arrowNextX, arrowNextY;

    // Close button
    private double closeX, closeY, closeW = 120, closeH = 26;

    // Tab hit regions
    private readonly double[] tabX    = new double[4];
    private readonly double[] tabY    = new double[4];
    private readonly double[] tabW    = new double[4];
    private readonly double[] tabHArr = new double[4];
    private int  hoveredTab   = -1;
    private bool closeHovered = false;

    // ---- Constructor ----

    public GuiDialogSpellbook(ICoreClientAPI capi, IClientNetworkChannel channel) : base(capi)
    {
        this.channel = channel;
        RebuildElements();
        LoadTextures();
        ComposeDialog();
    }

    private void RebuildElements()
    {
        Elements = SpellRegistry.All.Values
            .Select(s => s.Element).Distinct().OrderBy(e => e).ToArray();
        if (selectedElementIndex >= Elements.Length) selectedElementIndex = 0;
    }

    public void ReloadData()
    {
        if (IsOpened())
            (SingleComposer?.GetElement("spellbookCanvas") as GuiElementCustomDraw)?.Redraw();
    }

    // ---- Textures ----

    private void LoadTextures()
    {
        bookBgSurface = LoadSurface("textures/gui/book_bg.png");

        string[] activeNames   = { "tab_spells_active",   "tab_memorized_active", "tab_lore_active",   "tab_runes_active"   };
        string?[] inactiveNames = { "tab_spells_inactive", null,                   null,                "tab_runes_inactive" };

        var fallback = LoadSurface("textures/gui/tab_spells_inactive.png");

        for (int i = 0; i < 4; i++)
        {
            sectionTabActive[i]   = LoadSurface($"textures/gui/{activeNames[i]}.png");
            sectionTabInactive[i] = inactiveNames[i] != null
                ? (LoadSurface($"textures/gui/{inactiveNames[i]}.png") ?? fallback)
                : fallback;

            var asurf = sectionTabActive[i];
            sTabDispH[i] = asurf != null ? STabDispW * asurf.Height / asurf.Width : 30;
            var isurf = sectionTabInactive[i];
            sTabDispHInact[i] = isurf != null ? STabDispW * isurf.Height / isurf.Width : 25;
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

        // Book background
        if (bookBgSurface != null)
        {
            ctx.Save();
            ctx.Scale(w / bookBgSurface.Width, h / bookBgSurface.Height);
            ctx.SetSourceSurface(bookBgSurface, 0, 0);
            ctx.Paint();
            ctx.Restore();
        }

        DrawSectionTabs(ctx);

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

        if (hoveredSpellId != null)
        {
            var spell = SpellRegistry.Get(hoveredSpellId);
            if (spell != null) DrawTooltip(ctx, spell, tooltipX, tooltipY, w, h);
        }
    }

    // ---- Section tabs ----

    private void DrawSectionTabs(Context ctx)
    {
        double maxH = STabMaxH;

        // Draw inactive tabs first, then active on top
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < TabNames.Length; i++)
            {
                bool active = currentTab == i;
                if ((pass == 0) == active) continue; // pass 0 = inactive, pass 1 = active

                double tx    = RPageX + i * STabDispW;
                double dispH = active ? sTabDispH[i] : sTabDispHInact[i];
                double ty    = RPageY + maxH - dispH; // align bottom edges
                var    surf  = active ? sectionTabActive[i] : sectionTabInactive[i];

                tabX[i] = tx; tabY[i] = ty; tabW[i] = STabDispW; tabHArr[i] = dispH;

                if (surf != null)
                {
                    ctx.Save();
                    // Clip to tab rectangle to prevent bleed
                    ctx.Rectangle(tx, ty, STabDispW, dispH);
                    ctx.Clip();
                    ctx.Translate(tx, ty);
                    ctx.Scale(STabDispW / surf.Width, dispH / surf.Height);
                    ctx.SetSourceSurface(surf, 0, 0);
                    ctx.Paint();
                    ctx.Restore();
                }
                else
                {
                    RoundedRect(ctx, tx + 1, ty, STabDispW - 2, dispH, 4);
                    ctx.SetSourceRGBA(active ? 0.68 : 0.48, active ? 0.52 : 0.38,
                                      active ? 0.22 : 0.18, active ? 0.85 : 0.50);
                    ctx.Fill();
                }

                // Label overlay (for fallback tabs that don't have baked label)
                bool isFallback = !active && (i == 1 || i == 2); // memorized/lore use spells_inactive fallback
                if (isFallback)
                {
                    ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                    ctx.SetFontSize(8);
                    ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], 0.70);
                    var te = ctx.TextExtents(TabNames[i]);
                    ctx.MoveTo(tx + STabDispW / 2 - te.Width / 2 - te.XBearing,
                               ty + dispH / 2 + te.Height / 2 - 1);
                    ctx.ShowText(TabNames[i]);
                }
            }
        }
    }

    // ---- Spells tab — left page ----

    private void DrawSpellsLeftPage(Context ctx)
    {
        double x = LPageX, y = LPageY;

        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Bold);
        ctx.SetFontSize(15);
        ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.85);
        string title = "Spells & Runes";
        var te = ctx.TextExtents(title);
        ctx.MoveTo(x + LPageW / 2 - te.Width / 2 - te.XBearing, y + 26);
        ctx.ShowText(title);
        DrawDivider(ctx, x + 10, y + 33, LPageW - 20);

        // Flavor text / element description placeholder
        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal);
        ctx.SetFontSize(11);
        ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.65);
        string flavor = "Select an element on the right page\nto view its spell tree.";
        double fy = y + 60;
        foreach (var line in flavor.Split('\n'))
        {
            var lte = ctx.TextExtents(line);
            ctx.MoveTo(x + LPageW / 2 - lte.Width / 2 - lte.XBearing, fy);
            ctx.ShowText(line);
            fy += 16;
        }

        closeW = LPageW - 40; closeX = x + 20; closeY = y + LPageH - 34;
        DrawCloseButton(ctx);
    }

    // ---- Spells tab — right page ----

    private void DrawSpellsRightPage(Context ctx)
    {
        double maxH  = STabMaxH;
        double cTop  = RPageY + maxH + 6;
        double footH = 58;
        double cBot  = RPageY + RPageH - footH;

        // Element name header
        var (er, eg, eb) = ElementColor(SelectedElement);
        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Bold);
        ctx.SetFontSize(20);
        ctx.SetSourceRGBA(er * 0.60, eg * 0.60, eb * 0.60, 0.88);
        string elName = SelectedElement.ToString();
        var te = ctx.TextExtents(elName);
        double hdrY = cTop + 18;
        ctx.MoveTo(RPageX + RPageW / 2 - te.Width / 2 - te.XBearing, hdrY);
        ctx.ShowText(elName);
        DrawDivider(ctx, RPageX + 20, hdrY + 6, RPageW - 40);

        // Spell tree
        double treeY = hdrY + 14;
        DrawSpellTree(ctx, RPageX + 4, treeY, RPageW - 8, cBot - treeY - 4);

        // Divider above footer
        DrawDivider(ctx, RPageX + 20, cBot + 2, RPageW - 40);

        // XP bar in footer
        DrawElementXpBar(ctx, RPageX + 20, cBot + 8, RPageW - 40, SelectedElement);

        // Navigation arrows (bottom corners)
        arrowPrevX = RPageX + 8;
        arrowNextX = RPageX + RPageW - 8 - ArrowW;
        arrowPrevY = arrowNextY = RPageY + RPageH - ArrowH - 6;

        DrawArrowBtn(ctx, arrowPrevX, arrowPrevY, false);
        DrawArrowBtn(ctx, arrowNextX, arrowNextY, true);
    }

    private void DrawArrowBtn(Context ctx, double x, double y, bool right)
    {
        RoundedRect(ctx, x, y, ArrowW, ArrowH, 4);
        ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.18); ctx.Fill();
        RoundedRect(ctx, x, y, ArrowW, ArrowH, 4);
        ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.38);
        ctx.LineWidth = 1; ctx.Stroke();

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(13);
        ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], 0.75);
        string arrow = right ? "▶" : "◀";
        var te = ctx.TextExtents(arrow);
        ctx.MoveTo(x + ArrowW / 2 - te.Width / 2 - te.XBearing,
                   y + ArrowH / 2 + te.Height / 2 - 1);
        ctx.ShowText(arrow);
    }

    // ---- Spell tree ----

    private void DrawSpellTree(Context ctx, double areaX, double areaY, double areaW, double areaH)
    {
        spellNodes.Clear();
        var spells = SpellRegistry.All.Values.Where(s => s.Element == SelectedElement).ToList();
        if (spells.Count == 0) return;

        int maxCol = spells.Max(s => s.TreePosition.col);
        double gridW  = (maxCol + 1) * NodeSpacingX;
        double originX = areaX + areaW / 2 - gridW / 2 + NodeSpacingX / 2;
        double originY = areaY + areaH - NodeSpacingY / 2 - NodeR - 8;

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
        var (er, eg, eb) = ElementColor(SelectedElement);

        ctx.LineWidth = 1.5;
        foreach (var spell in spells)
        {
            if (!posMap.TryGetValue(spell.Id, out var to)) continue;
            foreach (var prereqId in spell.Prerequisites)
            {
                if (!posMap.TryGetValue(prereqId, out var from)) continue;
                bool pu = data?.IsUnlocked(prereqId) ?? false;
                bool tu = data?.IsUnlocked(spell.Id) ?? false;
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
        bool unlocked, bool available, bool hovered,
        double er, double eg, double eb)
    {
        double r = NodeR;
        if (hovered) { ctx.Arc(cx, cy, r + 5, 0, 2 * Math.PI); ctx.SetSourceRGBA(er, eg, eb, 0.12); ctx.Fill(); }

        ctx.Arc(cx, cy, r, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(unlocked ? er * 0.18 : 0.84, unlocked ? eg * 0.18 : 0.80,
                          unlocked ? eb * 0.18 : 0.68, unlocked ? 0.80 : available ? 0.50 : 0.28);
        ctx.Fill();

        ctx.Arc(cx, cy, r, 0, 2 * Math.PI);
        if (unlocked)       ctx.SetSourceRGBA(er, eg, eb, hovered ? 1.0 : 0.75);
        else if (available)  ctx.SetSourceRGBA(er, eg, eb, hovered ? 0.65 : 0.45);
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

    // ---- Memorized — left page (radial slots) ----

    private void DrawMemorizedLeftPage(Context ctx)
    {
        double x = LPageX, y = LPageY;

        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Bold);
        ctx.SetFontSize(13);
        ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.80);
        string title = "Memorized Spells";
        var te = ctx.TextExtents(title);
        ctx.MoveTo(x + LPageW / 2 - te.Width / 2 - te.XBearing, y + 26);
        ctx.ShowText(title);
        DrawDivider(ctx, x + 10, y + 33, LPageW - 20);

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(9);
        ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.60);
        string hint = "Drag spells from the right page";
        var hte = ctx.TextExtents(hint);
        ctx.MoveTo(x + LPageW / 2 - hte.Width / 2 - hte.XBearing, y + 50);
        ctx.ShowText(hint);

        // Radial circle
        double radCX = x + LPageW / 2;
        double radCY = y + LPageH / 2 + 10;
        double radR  = 90;

        // Draw circle guide (faint)
        ctx.Arc(radCX, radCY, radR, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.10);
        ctx.LineWidth = 1; ctx.Stroke();

        // 3 slot positions: top, bottom-right, bottom-left
        double[] angles = { -Math.PI / 2, -Math.PI / 2 + 2 * Math.PI / 3, -Math.PI / 2 + 4 * Math.PI / 3 };
        string[] slotLabels = { "[1]", "[2]", "[3]" };

        var entity = capi.World.Player?.Entity;
        var data   = entity != null ? PlayerSpellData.For(entity) : null;

        // Connection lines
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

            bool isTarget = isDragging && HitTestSlot(i, dragX, dragY);
            string? slotSpellId = data?.GetHotbarSlot(i);

            ctx.Arc(scx, scy, SlotR, 0, 2 * Math.PI);
            ctx.SetSourceRGBA(0.80, 0.75, 0.60, 0.22); ctx.Fill();
            ctx.Arc(scx, scy, SlotR, 0, 2 * Math.PI);
            ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], isTarget ? 0.90 : 0.42);
            ctx.LineWidth = isTarget ? 2.5 : 1.5; ctx.Stroke();

            // Slot number
            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(8);
            ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.40);
            var nle = ctx.TextExtents(slotLabels[i]);
            ctx.MoveTo(scx - nle.Width / 2 - nle.XBearing, scy + SlotR + 11);
            ctx.ShowText(slotLabels[i]);

            if (slotSpellId != null)
            {
                var spell = SpellRegistry.Get(slotSpellId);
                if (spell != null)
                {
                    var (er, eg, eb) = ElementColor(spell.Element);
                    DrawSpellIconInCircle(ctx, spell, scx, scy, SlotR - 3, er, eg, eb);
                    ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
                    ctx.SetFontSize(8);
                    ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], 0.75);
                    var snte = ctx.TextExtents(spell.Name);
                    ctx.MoveTo(scx - snte.Width / 2 - snte.XBearing, scy + SlotR + 22);
                    ctx.ShowText(spell.Name);
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

        closeW = LPageW - 40; closeX = x + 20; closeY = y + LPageH - 34;
        DrawCloseButton(ctx);
    }

    // ---- Memorized — right page (spell list) ----

    private void DrawMemorizedRightPage(Context ctx)
    {
        memorizedSpellCards.Clear();

        double maxH  = STabMaxH;
        double x     = RPageX + 8;
        double y     = RPageY + maxH + 10;
        double w     = RPageW - 16;
        double h     = RPageH - maxH - 14;

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

        int    cols     = Math.Max(1, (int)((w + 6) / (CardW + 6)));
        double gridW    = cols * CardW + (cols - 1) * 6;
        double startX   = x + w / 2 - gridW / 2;

        for (int ci = 0; ci < unlockedIds.Count; ci++)
        {
            int    col   = ci % cols, row = ci / cols;
            double cx    = startX + col * (CardW + 6);
            double cardY = listY + row * (CardH + 6);
            if (cardY + CardH > y + h) break;

            var spell = SpellRegistry.Get(unlockedIds[ci]);
            if (spell == null) continue;

            bool beingDragged = isDragging && dragSpellId == spell.Id && dragFromSlot == -1;
            var (er, eg, eb) = ElementColor(spell.Element);
            memorizedSpellCards.Add(new SpellCard(spell.Id, cx, cardY, CardW, CardH));

            RoundedRect(ctx, cx, cardY, CardW, CardH, 5);
            ctx.SetSourceRGBA(er * 0.10 + 0.74, eg * 0.10 + 0.70, eb * 0.10 + 0.54,
                              beingDragged ? 0.18 : 0.72);
            ctx.Fill();
            RoundedRect(ctx, cx, cardY, CardW, CardH, 5);
            ctx.SetSourceRGBA(er, eg, eb, beingDragged ? 0.12 : 0.42);
            ctx.LineWidth = 1.2; ctx.Stroke();

            if (!beingDragged)
            {
                ctx.Arc(cx + 11, cardY + CardH / 2, 4, 0, 2 * Math.PI);
                ctx.SetSourceRGBA(er, eg, eb, 0.75); ctx.Fill();
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                ctx.SetFontSize(10);
                ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], 0.88);
                ctx.MoveTo(cx + 21, cardY + CardH / 2 - 2); ctx.ShowText(spell.Name);
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
                ctx.SetFontSize(8);
                ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.65);
                ctx.MoveTo(cx + 21, cardY + CardH / 2 + 10);
                ctx.ShowText($"{spell.FluxCost} Flux · {spell.Element}");
            }
        }

        // Floating drag card
        if (isDragging && dragSpellId != null)
        {
            var dspell = SpellRegistry.Get(dragSpellId);
            if (dspell != null)
            {
                var (er, eg, eb) = ElementColor(dspell.Element);
                double dx = dragX - CardW / 2, dy = dragY - CardH / 2;
                RoundedRect(ctx, dx, dy, CardW, CardH, 5);
                ctx.SetSourceRGBA(er * 0.10 + 0.74, eg * 0.10 + 0.70, eb * 0.10 + 0.54, 0.95); ctx.Fill();
                RoundedRect(ctx, dx, dy, CardW, CardH, 5);
                ctx.SetSourceRGBA(er, eg, eb, 0.80); ctx.LineWidth = 2; ctx.Stroke();
                ctx.Arc(dx + 11, dy + CardH / 2, 4, 0, 2 * Math.PI);
                ctx.SetSourceRGBA(er, eg, eb, 1.0); ctx.Fill();
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                ctx.SetFontSize(10);
                ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], 1.0);
                ctx.MoveTo(dx + 21, dy + CardH / 2 + 4); ctx.ShowText(dspell.Name);
            }
        }
    }

    private void DrawComingSoon(Context ctx)
    {
        double maxH = STabMaxH;
        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal);
        ctx.SetFontSize(14);
        ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.65);
        string ph = TabNames[currentTab] + " — coming soon";
        var te = ctx.TextExtents(ph);
        double midX = RPageX + RPageW / 2, midY = RPageY + maxH + (RPageH - maxH) / 2;
        ctx.MoveTo(midX - te.Width / 2 - te.XBearing, midY); ctx.ShowText(ph);
    }

    // ---- Tooltip ----

    private void DrawTooltip(Context ctx, Spell spell, double mx, double my, double dw, double dh)
    {
        const double tw = 185, pad = 10;
        var entity     = capi.World.Player?.Entity;
        var data       = entity != null ? PlayerSpellData.For(entity) : null;
        bool unlocked    = data?.IsUnlocked(spell.Id) ?? false;
        int  spellLevel  = unlocked && data != null ? data.GetSpellLevel(spell.Id) : 0;
        int  spellXp     = unlocked && data != null ? data.GetSpellXpInLevel(spell.Id) : 0;
        int  spellXpNext = PlayerSpellData.XpForLevel(spellLevel);
        float misscast   = Spell.MisscastChanceForLevel(spellLevel);

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(10);

        var words = spell.Description.Split(' ');
        var lines = new List<string>();
        string cur = "";
        foreach (var word in words)
        {
            string test = cur.Length == 0 ? word : cur + " " + word;
            if (ctx.TextExtents(test).Width > tw - pad * 2) { lines.Add(cur); cur = word; }
            else cur = test;
        }
        if (cur.Length > 0) lines.Add(cur);

        double lineH = 13, levelRows = unlocked ? 2 : 0;
        double th = pad * 2 + 16 + lines.Count * lineH + 18 + levelRows * 13;
        double tx = mx + 14, ty = my - th / 2;
        if (tx + tw > dw - 4) tx = mx - tw - 14;
        ty = Math.Clamp(ty, 4, dh - th - 4);

        RoundedRect(ctx, tx, ty, tw, th, 6);
        ctx.SetSourceRGBA(0.97, 0.93, 0.84, 0.97); ctx.Fill();
        RoundedRect(ctx, tx, ty, tw, th, 6);
        var (er, eg, eb) = ElementColor(spell.Element);
        ctx.SetSourceRGBA(er, eg, eb, 0.65); ctx.LineWidth = 1.5; ctx.Stroke();

        double iy = ty + pad;
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(12);
        ctx.SetSourceRGBA(er * 0.65, eg * 0.65, eb * 0.65, 1.0);
        ctx.MoveTo(tx + pad, iy + 11); ctx.ShowText(spell.Name); iy += 15;
        ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.35); ctx.LineWidth = 1;
        ctx.MoveTo(tx + pad, iy); ctx.LineTo(tx + tw - pad, iy); ctx.Stroke(); iy += 6;

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(10);
        ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], 0.88);
        foreach (var line in lines) { ctx.MoveTo(tx + pad, iy + 11); ctx.ShowText(line); iy += lineH; }
        iy += 4;

        ctx.SetFontSize(9); ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.88);
        ctx.MoveTo(tx + pad, iy + 10); ctx.ShowText($"Flux: {spell.FluxCost}   Cost: {(int)spell.Tier} SP"); iy += 14;

        if (unlocked)
        {
            ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.22); ctx.LineWidth = 1;
            ctx.MoveTo(tx + pad, iy + 2); ctx.LineTo(tx + tw - pad, iy + 2); ctx.Stroke(); iy += 6;
            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(9);
            ctx.SetSourceRGBA(er * 0.65, eg * 0.65, eb * 0.65, 0.90);
            ctx.MoveTo(tx + pad, iy + 10); ctx.ShowText($"Spell Lvl {spellLevel}");
            string xpStr = spellLevel >= 10 ? "MAX" : $"{spellXp}/{spellXpNext} XP";
            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(9);
            ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.80);
            var xpte = ctx.TextExtents(xpStr);
            ctx.MoveTo(tx + tw - pad - xpte.Width - xpte.XBearing, iy + 10); ctx.ShowText(xpStr); iy += 13;
            string missStr = misscast <= 0f ? "No misscast" : $"Misscast: {(int)(misscast * 100)}%";
            ctx.SetSourceRGBA(misscast <= 0f ? 0.20 : 0.60, misscast <= 0f ? 0.48 : 0.15,
                              misscast <= 0f ? 0.12 : 0.04, 0.85);
            ctx.MoveTo(tx + pad, iy + 10); ctx.ShowText(missStr);
        }
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

    // ---- Close button ----

    private void DrawCloseButton(Context ctx)
    {
        RoundedRect(ctx, closeX, closeY, closeW, closeH, 5);
        ctx.SetSourceRGBA(closeHovered ? 0.42 : 0.25, closeHovered ? 0.10 : 0.06, 0.03, 0.65); ctx.Fill();
        RoundedRect(ctx, closeX, closeY, closeW, closeH, 5);
        ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], closeHovered ? 0.75 : 0.38);
        ctx.LineWidth = 1; ctx.Stroke();
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(11);
        ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], 0.80);
        var cte = ctx.TextExtents("Close");
        ctx.MoveTo(closeX + closeW / 2 - cte.Width / 2 - cte.XBearing,
                   closeY + closeH / 2 + cte.Height / 2 - 1);
        ctx.ShowText("Close");
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
        {
            dragX = rx; dragY = ry;
            (SingleComposer.GetElement("spellbookCanvas") as GuiElementCustomDraw)?.Redraw();
            args.Handled = true; return;
        }

        int prevTab = hoveredTab; bool prevClose = closeHovered; string? prevSpell = hoveredSpellId;
        hoveredTab = -1; closeHovered = false; hoveredSpellId = null;

        if (rx >= closeX && rx <= closeX + closeW && ry >= closeY && ry <= closeY + closeH)
        { closeHovered = true; }
        else
        {
            for (int i = 0; i < TabNames.Length; i++)
                if (rx >= tabX[i] && rx <= tabX[i] + tabW[i] && ry >= tabY[i] && ry <= tabY[i] + tabHArr[i])
                { hoveredTab = i; break; }

            if (hoveredTab == -1 && currentTab == TabSpells)
            {
                foreach (var node in spellNodes)
                {
                    double dx = rx - node.Cx, dy = ry - node.Cy;
                    if (dx * dx + dy * dy <= node.R * node.R)
                    { hoveredSpellId = node.SpellId; tooltipX = rx; tooltipY = ry; break; }
                }
            }
        }

        if (hoveredTab != prevTab || closeHovered != prevClose || hoveredSpellId != prevSpell)
            (SingleComposer.GetElement("spellbookCanvas") as GuiElementCustomDraw)?.Redraw();
    }

    public override void OnMouseDown(MouseEvent args)
    {
        double rx = args.X - SingleComposer.Bounds.absX;
        double ry = args.Y - SingleComposer.Bounds.absY;

        // Close
        if (rx >= closeX && rx <= closeX + closeW && ry >= closeY && ry <= closeY + closeH)
        { TryClose(); args.Handled = true; return; }

        // Section tabs
        for (int i = 0; i < TabNames.Length; i++)
        {
            if (rx >= tabX[i] && rx <= tabX[i] + tabW[i] && ry >= tabY[i] && ry <= tabY[i] + tabHArr[i])
            {
                currentTab = i;
                (SingleComposer.GetElement("spellbookCanvas") as GuiElementCustomDraw)?.Redraw();
                args.Handled = true; return;
            }
        }

        if (currentTab == TabSpells)
        {
            // Prev/Next arrows
            if (Elements.Length > 1)
            {
                if (rx >= arrowPrevX && rx <= arrowPrevX + ArrowW && ry >= arrowPrevY && ry <= arrowPrevY + ArrowH)
                {
                    selectedElementIndex = (selectedElementIndex - 1 + Elements.Length) % Elements.Length;
                    (SingleComposer.GetElement("spellbookCanvas") as GuiElementCustomDraw)?.Redraw();
                    args.Handled = true; return;
                }
                if (rx >= arrowNextX && rx <= arrowNextX + ArrowW && ry >= arrowNextY && ry <= arrowNextY + ArrowH)
                {
                    selectedElementIndex = (selectedElementIndex + 1) % Elements.Length;
                    (SingleComposer.GetElement("spellbookCanvas") as GuiElementCustomDraw)?.Redraw();
                    args.Handled = true; return;
                }
            }

            // Spell node unlock
            foreach (var node in spellNodes)
            {
                double dx = rx - node.Cx, dy = ry - node.Cy;
                if (dx * dx + dy * dy <= node.R * node.R)
                {
                    channel.SendPacket(new MsgUnlockSpell { SpellId = node.SpellId });
                    (SingleComposer.GetElement("spellbookCanvas") as GuiElementCustomDraw)?.Redraw();
                    args.Handled = true; return;
                }
            }
        }

        if (currentTab == TabMemorized)
        {
            var entity = capi.World.Player?.Entity;
            var data   = entity != null ? PlayerSpellData.For(entity) : null;

            // Drag from slot
            for (int i = 0; i < 3; i++)
            {
                if (HitTestSlot(i, rx, ry))
                {
                    string? ss = data?.GetHotbarSlot(i);
                    if (ss != null)
                    {
                        isDragging = true; dragSpellId = ss; dragFromSlot = i; dragX = rx; dragY = ry;
                        args.Handled = true; return;
                    }
                }
            }

            // Drag from card
            foreach (var card in memorizedSpellCards)
            {
                if (rx >= card.X && rx <= card.X + card.W && ry >= card.Y && ry <= card.Y + card.H)
                {
                    isDragging = true; dragSpellId = card.SpellId; dragFromSlot = -1; dragX = rx; dragY = ry;
                    args.Handled = true; return;
                }
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
        (SingleComposer.GetElement("spellbookCanvas") as GuiElementCustomDraw)?.Redraw();
        args.Handled = true;
    }

    public override string ToggleKeyCombinationCode => "spellsandrunes.spellbook";
    public override EnumDialogType DialogType => EnumDialogType.Dialog;

    private bool HitTestSlot(int slot, double rx, double ry)
    {
        if (slot < 0 || slot >= 3) return false;
        var (cx, cy) = hotbarSlotPos[slot];
        double dx = rx - cx, dy = ry - cy;
        return dx * dx + dy * dy <= (SlotR + 8) * (SlotR + 8);
    }

    private int FindHotbarSlotAtPos(double rx, double ry)
    {
        for (int i = 0; i < 3; i++) if (HitTestSlot(i, rx, ry)) return i;
        return -1;
    }

    private record SpellNode(string SpellId, double Cx, double Cy, double R);
    private record SpellCard(string SpellId, double X, double Y, double W, double H);
}
