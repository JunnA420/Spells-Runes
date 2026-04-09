using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using SpellsAndRunes.Spells;
using SpellsAndRunes.Network;

namespace SpellsAndRunes.GUI;

public class GuiDialogSpellbook : GuiDialog
{
    private readonly IClientNetworkChannel channel;
    private int currentTab = 0;
    private static readonly string[] TabNames = { "Spells", "Memorized Spells", "Lore", "Runes" };
    private const int TabMemorized = 1;

    private SpellElement[] Elements = Array.Empty<SpellElement>();
    private int selectedElementIndex = 0;
    private SpellElement SelectedElement => Elements.Length > 0 ? Elements[selectedElementIndex] : SpellElement.Air;

    // Element selector
    private double elemSelectorX, elemSelectorY;
    private const double ElemBtnW = 80, ElemBtnH = 26;
    private int hoveredElement = -1;

    // Spell tree nodes — computed on draw, used for hit testing
    private readonly List<SpellNode> spellNodes = new();
    private string? hoveredSpellId = null;

    // Tooltip
    private double tooltipX, tooltipY;

    // Memorized tab — spell card list (hit-testing)
    private readonly List<SpellCard> memorizedSpellCards = new();
    private const double CardW = 150, CardH = 38;
    private const double SlotR = 32; // hotbar slot radius

    // Hotbar slot positions (cx, cy)
    private readonly (double cx, double cy)[] hotbarSlotPos = new (double, double)[3];

    // Drag & drop state
    private string? dragSpellId = null;   // spell being dragged
    private int dragFromSlot = -1;        // -1 = from card list, 0-2 = from hotbar slot
    private double dragX, dragY;          // current drag cursor position
    private bool isDragging = false;

    // Colors
    private static readonly double[] ColorBg       = { 0.08, 0.08, 0.18, 0.97 };
    private static readonly double[] ColorBorder    = { 0.75, 0.62, 0.25, 1.00 };
    private static readonly double[] ColorTabActive = { 0.45, 0.20, 0.80, 0.90 };
    private static readonly double[] ColorTabHover  = { 0.30, 0.15, 0.55, 0.70 };
    private static readonly double[] ColorText      = { 0.90, 0.88, 1.00, 1.00 };
    private static readonly double[] ColorSubtext   = { 0.65, 0.60, 0.85, 0.70 };
    private static readonly double[] ColorXpBarBg   = { 0.10, 0.10, 0.25, 0.80 };

    private static (double r, double g, double b) ElementColor(SpellElement el) => el switch
    {
        SpellElement.Air   => (0.55, 0.85, 1.00),
        SpellElement.Fire  => (1.00, 0.45, 0.15),
        SpellElement.Water => (0.20, 0.60, 1.00),
        SpellElement.Earth => (0.45, 0.80, 0.30),
        _                  => (0.80, 0.80, 0.80),
    };

    private const double DialogW   = 820;
    private const double DialogH   = 560;
    private const double SidebarW  = 165;
    private const double HeaderH   = 50;
    private const double FooterH   = 56;
    private const double Pad       = 14;
    private const double BorderR   = 8;
    private const double NodeR     = 26;
    private const double NodeSpacingX = 100;
    private const double NodeSpacingY = 90;

    // Sidebar tab geometry (vertical list)
    private const double TabH     = 38;
    private const double TabFirst = HeaderH + 16;

    private double closeX, closeY, closeW = 80, closeH = 28;
    private readonly double[] tabX = new double[TabNames.Length];
    private readonly double[] tabY = new double[TabNames.Length];
    private readonly double[] tabW = new double[TabNames.Length];
    private readonly double[] tabHArr = new double[TabNames.Length];
    private int  hoveredTab   = -1;
    private bool closeHovered = false;

    // ---- Constructor ----

    public GuiDialogSpellbook(ICoreClientAPI capi, IClientNetworkChannel channel) : base(capi)
    {
        this.channel = channel;
        RebuildElements();
        ComposeDialog();
    }

    /// <summary>
    /// Builds the element list dynamically from spells registered in SpellRegistry.
    /// </summary>
    private void RebuildElements()
    {
        Elements = SpellRegistry.All.Values
            .Select(s => s.Element)
            .Distinct()
            .OrderBy(e => e)
            .ToArray();

        if (selectedElementIndex >= Elements.Length)
            selectedElementIndex = 0;
    }

    /// <summary>Called when WatchedAttributes arrive from server — triggers a redraw if open.</summary>
    public void ReloadData()
    {
        if (IsOpened())
            (SingleComposer?.GetElement("spellbookCanvas") as GuiElementCustomDraw)?.Redraw();
    }

    private void ComposeDialog()
    {
        var dialogBounds = ElementBounds.Fixed(EnumDialogArea.CenterMiddle, 0, 0, DialogW, DialogH);
        var canvasBounds = ElementBounds.Fixed(0, 0, DialogW, DialogH);

        SingleComposer = capi.Gui
            .CreateCompo("spellsandrunes:spellbook", dialogBounds)
            .AddDynamicCustomDraw(canvasBounds, DrawSpellbook, "spellbookCanvas")
            .Compose();
    }

    // ---- Main draw ----

    private void DrawSpellbook(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        double w = bounds.InnerWidth;
        double h = bounds.InnerHeight;

        // Outer background + border
        RoundedRect(ctx, 0, 0, w, h, BorderR);
        ctx.SetSourceRGBA(ColorBg[0], ColorBg[1], ColorBg[2], ColorBg[3]);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 1.0);
        ctx.LineWidth = 2; ctx.Stroke();

        // Sidebar background (slightly darker)
        RoundedRect(ctx, 0, 0, SidebarW, h, BorderR);
        ctx.SetSourceRGBA(0.04, 0.04, 0.12, 0.98);
        ctx.Fill();

        // Sidebar right border
        ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.35);
        ctx.LineWidth = 1;
        ctx.MoveTo(SidebarW, 8); ctx.LineTo(SidebarW, h - 8); ctx.Stroke();

        DrawSidebar(ctx, h);

        // Content area
        double cx = SidebarW + Pad;
        double cy = Pad;
        double cw = w - SidebarW - Pad * 2;
        double ch = h - Pad * 2;
        DrawContentArea(ctx, cx, cy, cw, ch);

        // Tooltip drawn last (on top)
        if (hoveredSpellId != null)
        {
            var spell = SpellRegistry.Get(hoveredSpellId);
            if (spell != null) DrawTooltip(ctx, spell, tooltipX, tooltipY, w, h);
        }
    }

    // ---- Sidebar ----

    private void DrawSidebar(Context ctx, double h)
    {
        // Title
        ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Bold);
        ctx.SetFontSize(13);
        ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 1.0);
        string title = "Spells & Runes";
        var te = ctx.TextExtents(title);
        ctx.MoveTo(SidebarW / 2 - te.Width / 2 - te.XBearing, 22);
        ctx.ShowText(title);

        // Ornament line under title
        ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.5);
        ctx.LineWidth = 1;
        ctx.MoveTo(Pad, 30); ctx.LineTo(SidebarW - Pad, 30); ctx.Stroke();

        // Tab buttons — vertical list
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(12);

        // Tab icons per tab
        string[] tabIcons = { "✦", "★", "📖", "◈" };

        for (int i = 0; i < TabNames.Length; i++)
        {
            double ty2 = TabFirst + i * (TabH + 4);
            tabX[i]    = 4;
            tabY[i]    = ty2;
            tabW[i]    = SidebarW - 8;
            tabHArr[i] = TabH;

            bool active  = currentTab == i;
            bool hovered = hoveredTab == i;

            // Tab background
            RoundedRect(ctx, tabX[i], ty2, tabW[i], TabH, 5);
            if (active)
                ctx.SetSourceRGBA(ColorTabActive[0], ColorTabActive[1], ColorTabActive[2], 0.85);
            else if (hovered)
                ctx.SetSourceRGBA(ColorTabHover[0], ColorTabHover[1], ColorTabHover[2], 0.60);
            else
                ctx.SetSourceRGBA(0.10, 0.10, 0.22, 0.50);
            ctx.Fill();

            // Active tab — gold left accent bar
            if (active)
            {
                RoundedRect(ctx, tabX[i], ty2 + 4, 3, TabH - 8, 2);
                ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 1.0);
                ctx.Fill();
            }

            // Tab label
            string label = TabNames[i];
            var lte = ctx.TextExtents(label);
            ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], active ? 1.0 : (hovered ? 0.85 : 0.55));
            ctx.MoveTo(tabX[i] + 16, ty2 + TabH / 2 + lte.Height / 2 - 1);
            ctx.ShowText(label);
        }

        // XP bar in sidebar (for current tab's element, only on Spells tab)
        if (currentTab == 0 && Elements.Length > 0)
        {
            double xpY = h - FooterH - 60;
            DrawDivider(ctx, Pad, xpY - 8, SidebarW - Pad * 2);
            DrawElementXpBar(ctx, Pad, xpY, SidebarW - Pad * 2, SelectedElement);
        }

        // Close button at very bottom of sidebar
        closeW = SidebarW - Pad * 2;
        closeX = Pad;
        closeY = h - Pad - closeH;

        RoundedRect(ctx, closeX, closeY, closeW, closeH, 5);
        ctx.SetSourceRGBA(closeHovered ? 0.65 : 0.25, 0.06, 0.06, 0.90);
        ctx.Fill();
        RoundedRect(ctx, closeX, closeY, closeW, closeH, 5);
        ctx.SetSourceRGBA(closeHovered ? 0.9 : 0.5, 0.15, 0.15, 0.70);
        ctx.LineWidth = 1; ctx.Stroke();

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(11);
        ctx.SetSourceRGBA(1, 0.7, 0.7, 0.9);
        var cte = ctx.TextExtents("Close");
        ctx.MoveTo(closeX + closeW / 2 - cte.Width / 2 - cte.XBearing,
                   closeY + closeH / 2 + cte.Height / 2);
        ctx.ShowText("Close");
    }

    // ---- Content area ----

    private void DrawContentArea(Context ctx, double x, double y, double w, double h)
    {
        RoundedRect(ctx, x, y, w, h, 4);
        ctx.SetSourceRGBA(0.05, 0.05, 0.12, 0.50);
        ctx.Fill();

        if (currentTab == 0)
        {
            DrawElementSelector(ctx, x, y, w);
            double treeY = y + ElemBtnH + 16;
            double treeH = h - ElemBtnH - 16;
            DrawSpellTree(ctx, x, treeY, w, treeH);
        }
        else if (currentTab == TabMemorized)
        {
            DrawMemorizedTab(ctx, x, y, w, h);
        }
        else
        {
            string ph = TabNames[currentTab] + " — coming soon";
            ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal);
            ctx.SetFontSize(16);
            ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], ColorSubtext[3]);
            var te = ctx.TextExtents(ph);
            ctx.MoveTo(x + w / 2 - te.Width / 2 - te.XBearing, y + h / 2 - te.Height / 2 - te.YBearing);
            ctx.ShowText(ph);
        }
    }

    // ---- Element selector ----

    private void DrawElementSelector(Context ctx, double contentX, double contentY, double contentW)
    {
        double spacing = 8;
        double totalW  = Elements.Length * ElemBtnW + (Elements.Length - 1) * spacing;
        elemSelectorX  = contentX + contentW / 2 - totalW / 2;
        elemSelectorY  = contentY + 8;

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(11);

        for (int i = 0; i < Elements.Length; i++)
        {
            var el = Elements[i];
            double bx = elemSelectorX + i * (ElemBtnW + spacing);
            double by = elemSelectorY;
            bool sel = selectedElementIndex == i, hov = hoveredElement == i;
            var (er, eg, eb) = ElementColor(el);

            RoundedRect(ctx, bx, by, ElemBtnW, ElemBtnH, 5);
            ctx.SetSourceRGBA(sel ? er * 0.30 : hov ? 0.18 : 0.08,
                              sel ? eg * 0.30 : hov ? 0.30 : 0.08,
                              sel ? eb * 0.30 : hov ? 0.55 : 0.20, 0.95);
            ctx.Fill();

            RoundedRect(ctx, bx, by, ElemBtnW, ElemBtnH, 5);
            ctx.SetSourceRGBA(er, eg, eb, sel ? 0.95 : 0.35);
            ctx.LineWidth = sel ? 1.5 : 1.0; ctx.Stroke();

            var te = ctx.TextExtents(el.ToString());
            ctx.SetSourceRGBA(er, eg, eb, sel ? 1.0 : 0.60);
            ctx.MoveTo(bx + ElemBtnW / 2 - te.Width / 2 - te.XBearing,
                       by + ElemBtnH / 2 + te.Height / 2);
            ctx.ShowText(el.ToString());
        }
    }

    // ---- Spell tree ----

    private void DrawSpellTree(Context ctx, double areaX, double areaY, double areaW, double areaH)
    {
        spellNodes.Clear();

        var spells = SpellRegistry.All.Values
            .Where(s => s.Element == SelectedElement)
            .ToList();

        if (spells.Count == 0) return;

        // Determine grid extents
        int maxCol = spells.Max(s => s.TreePosition.col);
        int maxRow = spells.Max(s => s.TreePosition.row);
        int cols   = maxCol + 1;
        int rows   = maxRow + 1;

        // Center grid within area
        double gridW = cols * NodeSpacingX;
        double gridH = rows * NodeSpacingY;
        double originX = areaX + areaW / 2 - gridW / 2 + NodeSpacingX / 2;
        // Rows grow upward: row 0 = bottom
        double originY = areaY + areaH - NodeSpacingY / 2 - NodeR;

        // Build node screen positions
        var posMap = new Dictionary<string, (double cx, double cy)>();
        foreach (var spell in spells)
        {
            double cx = originX + spell.TreePosition.col * NodeSpacingX;
            double cy = originY - spell.TreePosition.row * NodeSpacingY;
            posMap[spell.Id] = (cx, cy);
            spellNodes.Add(new SpellNode(spell.Id, cx, cy, NodeR));
        }

        var entity   = capi.World.Player?.Entity;
        var data     = entity != null ? PlayerSpellData.For(entity) : null;
        var (er, eg, eb) = ElementColor(SelectedElement);

        // Draw connection lines first (under nodes)
        ctx.LineWidth = 2;
        foreach (var spell in spells)
        {
            if (!posMap.TryGetValue(spell.Id, out var to)) continue;
            foreach (var prereqId in spell.Prerequisites)
            {
                if (!posMap.TryGetValue(prereqId, out var from)) continue;
                bool prereqUnlocked = data?.IsUnlocked(prereqId) ?? false;
                bool thisUnlocked   = data?.IsUnlocked(spell.Id) ?? false;

                ctx.SetSourceRGBA(er, eg, eb, prereqUnlocked && thisUnlocked ? 0.70 : 0.20);
                ctx.MoveTo(from.cx, from.cy);
                ctx.LineTo(to.cx, to.cy);
                ctx.Stroke();
            }
        }

        // Draw nodes
        foreach (var spell in spells)
        {
            if (!posMap.TryGetValue(spell.Id, out var pos)) continue;
            bool unlocked  = data?.IsUnlocked(spell.Id) ?? false;
            bool available = data != null && SpellTree.CanUnlock(spell.Id, data);
            bool hovered   = hoveredSpellId == spell.Id;
            DrawSpellNode(ctx, spell, pos.cx, pos.cy, unlocked, available, hovered, er, eg, eb);
        }
    }

    private void DrawSpellNode(Context ctx, Spell spell, double cx, double cy,
        bool unlocked, bool available, bool hovered,
        double er, double eg, double eb)
    {
        double r = NodeR;

        // Glow for hovered
        if (hovered)
        {
            ctx.Rectangle(cx - r - 5, cy - r - 5, (r + 5) * 2, (r + 5) * 2);
            ctx.SetSourceRGBA(er, eg, eb, 0.18);
            ctx.Fill();
        }

        // Node background
        ctx.Rectangle(cx - r, cy - r, r * 2, r * 2);
        if (unlocked)
            ctx.SetSourceRGBA(er * 0.25, eg * 0.25, eb * 0.25, 0.95);
        else if (available)
            ctx.SetSourceRGBA(0.14, 0.14, 0.28, 0.95);
        else
            ctx.SetSourceRGBA(0.12, 0.12, 0.22, 0.95);
        ctx.Fill();

        // Node border
        ctx.Rectangle(cx - r, cy - r, r * 2, r * 2);
        if (unlocked)
            ctx.SetSourceRGBA(er, eg, eb, hovered ? 1.0 : 0.90);
        else if (available)
            ctx.SetSourceRGBA(er, eg, eb, hovered ? 0.70 : 0.55);
        else
            ctx.SetSourceRGBA(0.50, 0.50, 0.65, hovered ? 0.80 : 0.55);
        ctx.LineWidth = unlocked ? 2.0 : 1.5;
        ctx.Stroke();

        // Lock icon for locked spells
        if (!unlocked && !available)
        {
            DrawLockIcon(ctx, cx, cy, 0.35);
        }
        else if (!unlocked && available)
        {
            // Available: dim lock
            DrawLockIcon(ctx, cx, cy, 0.60);
        }

        // Spell type indicator — small dot top-right
        var (tr, tg, tb) = spell.Type switch
        {
            SpellType.Offense     => (1.00, 0.35, 0.35),
            SpellType.Defense     => (0.35, 0.70, 1.00),
            SpellType.Enchantment => (0.80, 0.55, 1.00),
            _                     => (0.70, 0.70, 0.70),
        };
        ctx.Arc(cx + r - 4, cy - r + 4, 4, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(tr, tg, tb, unlocked ? 0.95 : 0.40);
        ctx.Fill();

        // Spell name below node
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(9);
        ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], unlocked ? 0.95 : 0.65);
        var te = ctx.TextExtents(spell.Name);
        ctx.MoveTo(cx - te.Width / 2 - te.XBearing, cy + r + 13);
        ctx.ShowText(spell.Name);

        // SP cost below name (Tier = cost in skill points)
        if (!unlocked)
        {
            string spStr = $"{(int)spell.Tier} SP";
            ctx.SetFontSize(8);
            ctx.SetSourceRGBA(er, eg, eb, available ? 0.80 : 0.30);
            var te2 = ctx.TextExtents(spStr);
            ctx.MoveTo(cx - te2.Width / 2 - te2.XBearing, cy + r + 24);
            ctx.ShowText(spStr);
        }
    }

    private static void DrawLockIcon(Context ctx, double cx, double cy, double alpha)
    {
        // Shackle arc
        ctx.SetSourceRGBA(0.85, 0.85, 0.85, alpha);
        ctx.LineWidth = 1.5;
        double lw = 7, lh = 6;
        ctx.Arc(cx, cy - 3, lw / 2, Math.PI, 0);
        ctx.Stroke();

        // Lock body
        RoundedRect(ctx, cx - lw / 2 - 1, cy - 1, lw + 2, lh, 2);
        ctx.SetSourceRGBA(0.60, 0.60, 0.60, alpha);
        ctx.Fill();
    }

    // ---- Tooltip ----

    private void DrawTooltip(Context ctx, Spell spell, double mx, double my, double dw, double dh)
    {
        const double tw = 190, pad = 10;

        // Spell level (only if unlocked)
        var entity = capi.World.Player?.Entity;
        var data   = entity != null ? PlayerSpellData.For(entity) : null;
        bool unlocked   = data?.IsUnlocked(spell.Id) ?? false;
        int  spellLevel = unlocked && data != null ? data.GetSpellLevel(spell.Id) : 0;
        int  spellXp    = unlocked && data != null ? data.GetSpellXpInLevel(spell.Id) : 0;
        int  spellXpNext = PlayerSpellData.XpForLevel(spellLevel);
        float misscast  = Spell.MisscastChanceForLevel(spellLevel);

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(11);

        // Wrap description
        var words   = spell.Description.Split(' ');
        var lines   = new List<string>();
        string cur  = "";
        foreach (var word in words)
        {
            string test = cur.Length == 0 ? word : cur + " " + word;
            if (ctx.TextExtents(test).Width > tw - pad * 2)
            { lines.Add(cur); cur = word; }
            else cur = test;
        }
        if (cur.Length > 0) lines.Add(cur);

        double lineH = 14;
        double levelRows = unlocked ? 2 : 0; // spell level + misscast rows
        double th    = pad * 2 + 18 + lines.Count * lineH + 20 + levelRows * 13;

        // Keep tooltip inside dialog
        double tx = mx + 14;
        double ty = my - th / 2;
        if (tx + tw > dw - 4) tx = mx - tw - 14;
        ty = Math.Clamp(ty, 4, dh - th - 4);

        // Background
        RoundedRect(ctx, tx, ty, tw, th, 6);
        ctx.SetSourceRGBA(0.05, 0.05, 0.15, 0.97);
        ctx.Fill();
        RoundedRect(ctx, tx, ty, tw, th, 6);
        var (er, eg, eb) = ElementColor(spell.Element);
        ctx.SetSourceRGBA(er, eg, eb, 0.60);
        ctx.LineWidth = 1.5; ctx.Stroke();

        double iy = ty + pad;

        // Spell name
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(13);
        ctx.SetSourceRGBA(er, eg, eb, 1.0);
        ctx.MoveTo(tx + pad, iy + 12); ctx.ShowText(spell.Name); iy += 18;

        // Divider
        ctx.SetSourceRGBA(er, eg, eb, 0.30);
        ctx.LineWidth = 1;
        ctx.MoveTo(tx + pad, iy); ctx.LineTo(tx + tw - pad, iy); ctx.Stroke();
        iy += 6;

        // Description lines
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(11);
        ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], 0.85);
        foreach (var line in lines)
        {
            ctx.MoveTo(tx + pad, iy + 11); ctx.ShowText(line); iy += lineH;
        }
        iy += 4;

        // Flux + SP cost
        ctx.SetFontSize(10);
        ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.90);
        ctx.MoveTo(tx + pad, iy + 10);
        ctx.ShowText($"Flux: {spell.FluxCost}   Cost: {(int)spell.Tier} SP");
        iy += 14;

        // Spell level block (only if unlocked)
        if (unlocked)
        {
            // Divider
            ctx.SetSourceRGBA(er, eg, eb, 0.20);
            ctx.LineWidth = 1;
            ctx.MoveTo(tx + pad, iy + 2); ctx.LineTo(tx + tw - pad, iy + 2); ctx.Stroke();
            iy += 6;

            // "Spell Lvl 3   47 / 125 XP"
            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(10);
            ctx.SetSourceRGBA(er, eg, eb, 0.90);
            ctx.MoveTo(tx + pad, iy + 10);
            ctx.ShowText($"Spell Lvl {spellLevel}");

            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
            ctx.SetFontSize(9);
            ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.80);
            string xpStr = spellLevel >= 10 ? "MAX" : $"{spellXp} / {spellXpNext} XP";
            var xpte = ctx.TextExtents(xpStr);
            ctx.MoveTo(tx + tw - pad - xpte.Width - xpte.XBearing, iy + 10);
            ctx.ShowText(xpStr);
            iy += 13;

            // Misscast chance
            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
            ctx.SetFontSize(9);
            string missStr = misscast <= 0f ? "No misscast" : $"Misscast: {(int)(misscast * 100)}%";
            ctx.SetSourceRGBA(misscast <= 0f ? 0.40 : 0.90,
                              misscast <= 0f ? 0.85 : 0.40,
                              misscast <= 0f ? 0.40 : 0.25, 0.85);
            ctx.MoveTo(tx + pad, iy + 10);
            ctx.ShowText(missStr);
            iy += 13;
        }

        // Tier + Type
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(10);
        ctx.SetSourceRGBA(er, eg, eb, 0.60);
        ctx.MoveTo(tx + pad, iy + 10);
        ctx.ShowText($"{spell.Tier}  ·  {spell.Type}");
    }

    // DrawFooter removed — Close button and XP bar are now in DrawSidebar

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

        // Line 1: element name
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(11);
        ctx.SetSourceRGBA(er, eg, eb, 1.0);
        ctx.MoveTo(x, y + 11);
        ctx.ShowText(element.ToString());

        // Line 2: "Lvl 7   SP: 3"
        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(10);
        ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 1.0);
        ctx.MoveTo(x, y + 23);
        ctx.ShowText($"Lvl {level}   SP: {sp}");

        // Line 3: XP fraction right-aligned
        string xpLabel = $"{xpIn} / {xpNext} XP";
        var xpte = ctx.TextExtents(xpLabel);
        ctx.MoveTo(x + maxW - xpte.Width - xpte.XBearing, y + 23);
        ctx.ShowText(xpLabel);

        // Progress bar
        double barH = 10, barY = y + 29, barR = barH / 2;

        // XP progress bar
        RoundedRect(ctx, x, barY, maxW, barH, barR);
        ctx.SetSourceRGBA(ColorXpBarBg[0], ColorXpBarBg[1], ColorXpBarBg[2], ColorXpBarBg[3]);
        ctx.Fill();

        double frac = xpNext > 0 ? Math.Clamp((double)xpIn / xpNext, 0.0, 1.0) : 1.0;

        if (frac > 0.01)
        {
            double fillW = Math.Max(barH, maxW * frac);
            RoundedRect(ctx, x, barY - 1, fillW, barH + 2, barR + 1);
            ctx.SetSourceRGBA(er * 0.5, eg * 0.5, eb * 0.5, 0.28); ctx.Fill();

            RoundedRect(ctx, x, barY, fillW, barH, barR);
            using (var grad = new LinearGradient(x, 0, x + fillW, 0))
            {
                grad.AddColorStop(0.0, new Color(er * 0.3, eg * 0.3, eb * 0.3, 0.95));
                grad.AddColorStop(1.0, new Color(er, eg, eb, 0.95));
                ctx.SetSource(grad);
                ctx.Fill();
            }
            ctx.SetSourceRGBA(er, eg, eb, 0); // reset source after gradient dispose
        }

        RoundedRect(ctx, x, barY, maxW, barH, barR);
        ctx.SetSourceRGBA(er, eg, eb, 0.45);
        ctx.LineWidth = 1; ctx.Stroke();
    }

    // ---- Helpers ----

    private static void DrawDivider(Context ctx, double x, double y, double w)
    {
        ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.4);
        ctx.LineWidth = 1;
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

        // Update drag position
        if (isDragging)
        {
            dragX = rx; dragY = ry;
            (SingleComposer.GetElement("spellbookCanvas") as GuiElementCustomDraw)?.Redraw();
            args.Handled = true;
            return;
        }

        int  prevTab   = hoveredTab;
        bool prevClose = closeHovered;
        int  prevElem  = hoveredElement;
        string? prevSpell = hoveredSpellId;

        hoveredTab    = -1;
        closeHovered  = false;
        hoveredElement = -1;
        hoveredSpellId = null;

        if (rx >= closeX && rx <= closeX + closeW && ry >= closeY && ry <= closeY + closeH)
        {
            closeHovered = true;
        }
        else if (currentTab == 0)
        {
            // Element selector
            for (int i = 0; i < Elements.Length; i++)
            {
                double bx = elemSelectorX + i * (ElemBtnW + 8);
                if (rx >= bx && rx <= bx + ElemBtnW && ry >= elemSelectorY && ry <= elemSelectorY + ElemBtnH)
                { hoveredElement = i; break; }
            }

            // Spell nodes
            if (hoveredElement == -1)
            {
                foreach (var node in spellNodes)
                {
                    double dx = rx - node.Cx, dy = ry - node.Cy;
                    if (dx * dx + dy * dy <= node.R * node.R)
                    {
                        hoveredSpellId = node.SpellId;
                        tooltipX = rx;
                        tooltipY = ry;
                        break;
                    }
                }
            }

            // Tabs
            if (hoveredElement == -1 && hoveredSpellId == null)
            {
                for (int i = 0; i < TabNames.Length; i++)
                    if (rx >= tabX[i] && rx <= tabX[i] + tabW[i] && ry >= tabY[i] && ry <= tabY[i] + tabHArr[i])
                    { hoveredTab = i; break; }
            }
        }
        else
        {
            for (int i = 0; i < TabNames.Length; i++)
                if (rx >= tabX[i] && rx <= tabX[i] + tabW[i] && ry >= tabY[i] && ry <= tabY[i] + tabHArr[i])
                { hoveredTab = i; break; }
        }

        if (hoveredTab != prevTab || closeHovered != prevClose ||
            hoveredElement != prevElem || hoveredSpellId != prevSpell)
            (SingleComposer.GetElement("spellbookCanvas") as GuiElementCustomDraw)?.Redraw();
    }

    public override void OnMouseDown(MouseEvent args)
    {
        double rx = args.X - SingleComposer.Bounds.absX;
        double ry = args.Y - SingleComposer.Bounds.absY;

        if (rx >= closeX && rx <= closeX + closeW && ry >= closeY && ry <= closeY + closeH)
        { TryClose(); args.Handled = true; return; }

        // Tab bar — always checked first, regardless of current tab
        for (int i = 0; i < TabNames.Length; i++)
        {
            if (rx >= tabX[i] && rx <= tabX[i] + tabW[i] && ry >= tabY[i] && ry <= tabY[i] + tabHArr[i])
            {
                currentTab = i;
                (SingleComposer.GetElement("spellbookCanvas") as GuiElementCustomDraw)?.Redraw();
                args.Handled = true; return;
            }
        }

        if (currentTab == 0)
        {
            // Element selector buttons
            for (int i = 0; i < Elements.Length; i++)
            {
                double bx = elemSelectorX + i * (ElemBtnW + 8);
                if (rx >= bx && rx <= bx + ElemBtnW && ry >= elemSelectorY && ry <= elemSelectorY + ElemBtnH)
                {
                    selectedElementIndex = i;
                    (SingleComposer.GetElement("spellbookCanvas") as GuiElementCustomDraw)?.Redraw();
                    args.Handled = true; return;
                }
            }

            // Spell node click — send unlock request to server
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

            // Consume remaining clicks in content area to prevent swing
            args.Handled = true; return;
        }

        if (currentTab == TabMemorized)
        {
            // Start drag from hotbar slot
            for (int i = 0; i < 3; i++)
            {
                if (HitTestSlot(i, rx, ry))
                {
                    var entity = capi.World.Player?.Entity;
                    var data   = entity != null ? PlayerSpellData.For(entity) : null;
                    string? slotSpell = data?.GetHotbarSlot(i);
                    if (slotSpell != null)
                    {
                        isDragging = true; dragSpellId = slotSpell;
                        dragFromSlot = i; dragX = rx; dragY = ry;
                        args.Handled = true; return;
                    }
                }
            }

            // Start drag from spell card
            foreach (var card in memorizedSpellCards)
            {
                if (rx >= card.X && rx <= card.X + card.W && ry >= card.Y && ry <= card.Y + card.H)
                {
                    isDragging = true; dragSpellId = card.SpellId;
                    dragFromSlot = -1; dragX = rx; dragY = ry;
                    args.Handled = true; return;
                }
            }
        }

        // Always consume clicks inside the dialog bounds to prevent game actions (swing etc.)
        if (rx >= 0 && rx <= DialogW && ry >= 0 && ry <= DialogH)
            args.Handled = true;
    }

    public override void OnMouseUp(MouseEvent args)
    {
        if (!isDragging) { base.OnMouseUp(args); return; }

        double rx = args.X - SingleComposer.Bounds.absX;
        double ry = args.Y - SingleComposer.Bounds.absY;

        int dropSlot = FindHotbarSlotAtPos(rx, ry);
        if (dropSlot >= 0 && dragSpellId != null)
        {
            // Clear source slot if dragging from another slot
            if (dragFromSlot >= 0 && dragFromSlot != dropSlot)
                channel.SendPacket(new MsgSetHotbarSlot { Slot = dragFromSlot, SpellId = "" });

            channel.SendPacket(new MsgSetHotbarSlot { Slot = dropSlot, SpellId = dragSpellId });
        }
        else if (dragFromSlot >= 0)
        {
            // Dropped outside — clear source slot
            channel.SendPacket(new MsgSetHotbarSlot { Slot = dragFromSlot, SpellId = "" });
        }

        isDragging = false; dragSpellId = null; dragFromSlot = -1;
        (SingleComposer.GetElement("spellbookCanvas") as GuiElementCustomDraw)?.Redraw();
        args.Handled = true;
    }

    public override string ToggleKeyCombinationCode => "spellsandrunes.spellbook";
    public override EnumDialogType DialogType => EnumDialogType.Dialog;

    // ---- Memorized Spells tab ----

    private void DrawMemorizedTab(Context ctx, double x, double y, double w, double h)
    {
        memorizedSpellCards.Clear();

        var entity = capi.World.Player?.Entity;
        var data   = entity != null ? PlayerSpellData.For(entity) : null;

        // --- 3 hotbar slots centered at top ---
        double slotAreaH = SlotR * 2 + 40;
        double slotSpacing = 20;
        double totalSlotW = 3 * (SlotR * 2) + 2 * slotSpacing;
        double slotStartX = x + w / 2 - totalSlotW / 2;
        double slotCY = y + slotAreaH / 2 + 10;

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(10);
        ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.7);
        string slotsLabel = "Memorized Spells (drag from list below)";
        var sle = ctx.TextExtents(slotsLabel);
        ctx.MoveTo(x + w / 2 - sle.Width / 2 - sle.XBearing, y + 14);
        ctx.ShowText(slotsLabel);

        for (int i = 0; i < 3; i++)
        {
            double cx = slotStartX + i * (SlotR * 2 + slotSpacing) + SlotR;
            hotbarSlotPos[i] = (cx, slotCY);

            bool isDropTarget = isDragging && HitTestSlot(i, dragX, dragY);
            string? slotSpellId = data?.GetHotbarSlot(i);

            // Slot background
            ctx.Arc(cx, slotCY, SlotR, 0, 2 * Math.PI);
            ctx.SetSourceRGBA(0.10, 0.10, 0.22, 0.90);
            ctx.Fill();

            // Border — highlight if drop target
            ctx.Arc(cx, slotCY, SlotR, 0, 2 * Math.PI);
            if (isDropTarget)
                ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 1.0);
            else
                ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.45);
            ctx.LineWidth = isDropTarget ? 2.5 : 1.5;
            ctx.Stroke();

            // Slot number label
            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(9);
            ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.5);
            string numLabel = $"[{i + 1}]";
            var nle = ctx.TextExtents(numLabel);
            ctx.MoveTo(cx - nle.Width / 2 - nle.XBearing, slotCY + SlotR + 13);
            ctx.ShowText(numLabel);

            if (slotSpellId != null)
            {
                var spell = SpellRegistry.Get(slotSpellId);
                if (spell != null)
                {
                    var (er, eg, eb) = ElementColor(spell.Element);
                    DrawSpellIconInCircle(ctx, spell, cx, slotCY, SlotR - 4, er, eg, eb);

                    // Spell name below slot
                    ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
                    ctx.SetFontSize(8);
                    ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], 0.85);
                    var snte = ctx.TextExtents(spell.Name);
                    double nameX = cx - snte.Width / 2 - snte.XBearing;
                    ctx.MoveTo(nameX, slotCY + SlotR + 24);
                    ctx.ShowText(spell.Name);
                }
            }
            else
            {
                // Empty slot — draw dim plus icon
                ctx.SetSourceRGBA(ColorBorder[0], ColorBorder[1], ColorBorder[2], 0.20);
                ctx.LineWidth = 1.5;
                ctx.MoveTo(cx - 8, slotCY); ctx.LineTo(cx + 8, slotCY); ctx.Stroke();
                ctx.MoveTo(cx, slotCY - 8); ctx.LineTo(cx, slotCY + 8); ctx.Stroke();
            }
        }

        // Divider between slots and list
        double listY = y + slotAreaH + 32;
        DrawDivider(ctx, x + 8, listY - 6, w - 16);

        // --- Unlocked spells list ---
        var unlockedIds = data?.GetUnlockedIds().ToList() ?? new List<string>();

        if (unlockedIds.Count == 0)
        {
            ctx.SelectFontFace("Serif", FontSlant.Italic, FontWeight.Normal);
            ctx.SetFontSize(13);
            ctx.SetSourceRGBA(ColorSubtext[0], ColorSubtext[1], ColorSubtext[2], 0.55);
            string emptyMsg = "No spells unlocked yet.";
            var ete = ctx.TextExtents(emptyMsg);
            ctx.MoveTo(x + w / 2 - ete.Width / 2 - ete.XBearing, listY + (h - listY) / 2);
            ctx.ShowText(emptyMsg);
            return;
        }

        // Arrange cards in centered rows
        int cols = Math.Max(1, (int)((w - 20) / (CardW + 10)));
        double colSpacing = 10;
        double rowSpacing = 10;
        double gridW = cols * CardW + (cols - 1) * colSpacing;
        double cardStartX = x + w / 2 - gridW / 2;
        double cy2 = listY + 4;

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);

        for (int ci = 0; ci < unlockedIds.Count; ci++)
        {
            int col = ci % cols;
            int row = ci / cols;
            double cx = cardStartX + col * (CardW + colSpacing);
            double cardY = cy2 + row * (CardH + rowSpacing);

            // Skip if out of visible area
            if (cardY + CardH > y + h) break;

            var spell = SpellRegistry.Get(unlockedIds[ci]);
            if (spell == null) continue;

            // Skip if this card is being dragged (render under cursor instead)
            bool beingDragged = isDragging && dragSpellId == spell.Id && dragFromSlot == -1;

            var (er, eg, eb) = ElementColor(spell.Element);
            memorizedSpellCards.Add(new SpellCard(spell.Id, cx, cardY, CardW, CardH));

            RoundedRect(ctx, cx, cardY, CardW, CardH, 5);
            ctx.SetSourceRGBA(beingDragged ? 0.05 : er * 0.12,
                              beingDragged ? 0.05 : eg * 0.12,
                              beingDragged ? 0.05 : eb * 0.12, beingDragged ? 0.3 : 0.85);
            ctx.Fill();
            RoundedRect(ctx, cx, cardY, CardW, CardH, 5);
            ctx.SetSourceRGBA(er, eg, eb, beingDragged ? 0.15 : 0.55);
            ctx.LineWidth = 1.2; ctx.Stroke();

            if (!beingDragged)
            {
                // Element color dot
                ctx.Arc(cx + 14, cardY + CardH / 2, 5, 0, 2 * Math.PI);
                ctx.SetSourceRGBA(er, eg, eb, 0.9);
                ctx.Fill();

                // Spell name
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                ctx.SetFontSize(11);
                ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], 0.95);
                ctx.MoveTo(cx + 26, cardY + CardH / 2 - 2);
                ctx.ShowText(spell.Name);

                // Flux cost
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
                ctx.SetFontSize(8);
                ctx.SetSourceRGBA(er, eg, eb, 0.65);
                ctx.MoveTo(cx + 26, cardY + CardH / 2 + 11);
                ctx.ShowText($"{spell.FluxCost} Flux");
            }
        }

        // Draw dragged spell card floating at cursor
        if (isDragging && dragSpellId != null)
        {
            var dspell = SpellRegistry.Get(dragSpellId);
            if (dspell != null)
            {
                var (er, eg, eb) = ElementColor(dspell.Element);
                double dw = CardW, dh = CardH;
                double dx = dragX - dw / 2, dy2 = dragY - dh / 2;

                RoundedRect(ctx, dx, dy2, dw, dh, 5);
                ctx.SetSourceRGBA(er * 0.18, eg * 0.18, eb * 0.18, 0.95);
                ctx.Fill();
                RoundedRect(ctx, dx, dy2, dw, dh, 5);
                ctx.SetSourceRGBA(er, eg, eb, 0.90);
                ctx.LineWidth = 2; ctx.Stroke();

                ctx.Arc(dx + 14, dy2 + dh / 2, 5, 0, 2 * Math.PI);
                ctx.SetSourceRGBA(er, eg, eb, 1.0);
                ctx.Fill();

                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                ctx.SetFontSize(11);
                ctx.SetSourceRGBA(ColorText[0], ColorText[1], ColorText[2], 1.0);
                ctx.MoveTo(dx + 26, dy2 + dh / 2 + 4);
                ctx.ShowText(dspell.Name);
            }
        }
    }

    /// <summary>Draw a minimal element-colored icon inside a circle (Cairo fallback).</summary>
    private static void DrawSpellIconInCircle(Context ctx, Spell spell, double cx, double cy, double r,
        double er, double eg, double eb)
    {
        // Clip to circle
        ctx.Save();
        ctx.Arc(cx, cy, r, 0, 2 * Math.PI);
        ctx.Clip();

        // Colored circle fill
        ctx.Arc(cx, cy, r, 0, 2 * Math.PI);
        ctx.SetSourceRGBA(er * 0.3, eg * 0.3, eb * 0.3, 0.9);
        ctx.Fill();

        // First letter of spell name, centered
        ctx.SelectFontFace("Serif", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(r * 1.1);
        string letter = spell.Name.Length > 0 ? spell.Name[0].ToString() : "?";
        ctx.SetSourceRGBA(er, eg, eb, 0.95);
        var te = ctx.TextExtents(letter);
        ctx.MoveTo(cx - te.Width / 2 - te.XBearing, cy + te.Height / 2 - te.YBearing - te.Height);
        ctx.ShowText(letter);

        ctx.Restore();
    }

    private bool HitTestSlot(int slot, double rx, double ry)
    {
        if (slot < 0 || slot >= 3) return false;
        var (cx, cy) = hotbarSlotPos[slot];
        double dx = rx - cx, dy = ry - cy;
        return dx * dx + dy * dy <= (SlotR + 8) * (SlotR + 8);
    }

    private int FindHotbarSlotAtPos(double rx, double ry)
    {
        for (int i = 0; i < 3; i++)
            if (HitTestSlot(i, rx, ry)) return i;
        return -1;
    }

    // ---- Inner types ----

    private record SpellNode(string SpellId, double Cx, double Cy, double R);
    private record SpellCard(string SpellId, double X, double Y, double W, double H);
}
