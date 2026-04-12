using System;
using System.IO;
using System.Text.Json;

namespace SpellsAndRunes.GUI;

/// <summary>
/// Live-editable layout config. All properties in reference-space pixels (1128x720).
/// Call UpdateActualSize() each frame before reading any Sc* values.
/// Delete the JSON at ConfigPath to reset to defaults.
/// </summary>
public class SpellbookLayout
{
    // ── Dialog size (1.2x bigger than original 940x600 for readability) ───────
    public double GuiW { get; set; } = 1128;
    public double GuiH { get; set; } = 720;

    // ── Parchment page bounds (inside leather border, 1128x720 ref space) ────
    public double LPageX { get; set; } = 190;
    public double LPageY { get; set; } = 152;
    public double LPageW { get; set; } = 364;
    public double LPageH { get; set; } = 431;

    public double RPageX { get; set; } = 572;
    public double RPageY { get; set; } = 152;
    public double RPageW { get; set; } = 383;
    public double RPageH { get; set; } = 431;

    // ── Main tabs (right side, vertical) ─────────────────────────────────────
    // Sized so 4 tabs fit within page height (431px total, 3px gaps)
    // active 47x91, inactive 40x86 — preserves 55:106 and 45:97 asset ratios
    public double MTabRefW  { get; set; } = 47;
    public double MTabRefH  { get; set; } = 91;
    public double MTabRefWI { get; set; } = 40;
    public double MTabRefHI { get; set; } = 86;
    public double MTabGap   { get; set; } = 3;

    // ── Close button (bottom-left leather corner) ─────────────────────────────
    public double CloseBookX { get; set; } = 137;
    public double CloseBookY { get; set; } = 593;
    public double CloseBookW { get; set; } = 54;
    public double CloseBookH { get; set; } = 54;

    // ── Element tabs (above left page, horizontal) ────────────────────────────
    public double ETabGap   { get; set; } = 5;
    // Reference dimensions – actual display height computed from asset aspect ratio
    public double ETabRefW  { get; set; } = 160;
    public double ETabRefH  { get; set; } = 77;   // used as fallback only
    public double ETabRefHI { get; set; } = 62;   // used as fallback only

    // ── Spell tree nodes ──────────────────────────────────────────────────────
    public double NodeR        { get; set; } = 24;
    public double NodeSpacingX { get; set; } = 92;
    public double NodeSpacingY { get; set; } = 82;

    // ── Spell cards ───────────────────────────────────────────────────────────
    public double CardH { get; set; } = 44;

    // ── Wheel slots ───────────────────────────────────────────────────────────
    public double SlotR { get; set; } = 29;

    // ── Runtime scale (not serialized) ───────────────────────────────────────
    [System.Text.Json.Serialization.JsonIgnore] private double _sx = 1;
    [System.Text.Json.Serialization.JsonIgnore] private double _sy = 1;
    [System.Text.Json.Serialization.JsonIgnore] private double _s  = 1;

    public void UpdateActualSize(double actualW, double actualH)
    {
        _sx = actualW / GuiW;
        _sy = actualH / GuiH;
        _s  = Math.Min(_sx, _sy);
    }

    public double X(double v) => v * _sx;
    public double Y(double v) => v * _sy;
    public double D(double v) => v * _s;

    // ── Scaled page bounds ────────────────────────────────────────────────────
    [System.Text.Json.Serialization.JsonIgnore] public double ScLPageX => X(LPageX);
    [System.Text.Json.Serialization.JsonIgnore] public double ScLPageY => Y(LPageY);
    [System.Text.Json.Serialization.JsonIgnore] public double ScLPageW => X(LPageW);
    [System.Text.Json.Serialization.JsonIgnore] public double ScLPageH => Y(LPageH);
    [System.Text.Json.Serialization.JsonIgnore] public double ScRPageX => X(RPageX);
    [System.Text.Json.Serialization.JsonIgnore] public double ScRPageY => Y(RPageY);
    [System.Text.Json.Serialization.JsonIgnore] public double ScRPageW => X(RPageW);
    [System.Text.Json.Serialization.JsonIgnore] public double ScRPageH => Y(RPageH);

    // ── Scaled main tabs ──────────────────────────────────────────────────────
    [System.Text.Json.Serialization.JsonIgnore] public double ScMTabW  => X(MTabRefW);
    [System.Text.Json.Serialization.JsonIgnore] public double ScMTabH  => X(MTabRefW) * MTabRefH / MTabRefW;
    [System.Text.Json.Serialization.JsonIgnore] public double ScMTabWI => X(MTabRefWI);
    [System.Text.Json.Serialization.JsonIgnore] public double ScMTabHI => X(MTabRefWI) * MTabRefHI / MTabRefWI;
    [System.Text.Json.Serialization.JsonIgnore] public double ScMTabGap => Y(MTabGap);

    // ── Scaled close button ───────────────────────────────────────────────────
    [System.Text.Json.Serialization.JsonIgnore] public double ScCloseBookX => X(CloseBookX);
    [System.Text.Json.Serialization.JsonIgnore] public double ScCloseBookY => Y(CloseBookY);
    [System.Text.Json.Serialization.JsonIgnore] public double ScCloseBookW => X(CloseBookW);
    [System.Text.Json.Serialization.JsonIgnore] public double ScCloseBookH => Y(CloseBookH);

    // ── Scaled element tabs ───────────────────────────────────────────────────
    [System.Text.Json.Serialization.JsonIgnore] public double ScETabGap => X(ETabGap);

    // 4 tabs fit across left page width
    public double ETabW(int n = 4) => (ScLPageW - (n - 1) * ScETabGap) / n;

    // Fallback heights (actual height computed from asset in RecomputeSpriteSizes)
    [System.Text.Json.Serialization.JsonIgnore] public double ScETabH  => ETabW() * ETabRefH  / ETabRefW;
    [System.Text.Json.Serialization.JsonIgnore] public double ScETabHI => ETabW() * ETabRefHI / ETabRefW;

    // ── Scaled nodes ──────────────────────────────────────────────────────────
    [System.Text.Json.Serialization.JsonIgnore] public double ScNodeR        => D(NodeR);
    [System.Text.Json.Serialization.JsonIgnore] public double ScNodeSpacingX => D(NodeSpacingX);
    [System.Text.Json.Serialization.JsonIgnore] public double ScNodeSpacingY => D(NodeSpacingY);

    // ── Scaled cards / slots ──────────────────────────────────────────────────
    [System.Text.Json.Serialization.JsonIgnore] public double ScCardH => D(CardH);
    [System.Text.Json.Serialization.JsonIgnore] public double ScSlotR => D(SlotR);

    // ── Persistence ───────────────────────────────────────────────────────────
    private static readonly JsonSerializerOptions Opts = new()
    { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VintagestoryData", "ModConfig", "spellsandrunes_layout.json");

    public static SpellbookLayout Load()
    {
        string path = ConfigPath;
        if (!File.Exists(path)) { Save(new SpellbookLayout(), path); return new SpellbookLayout(); }
        try { return JsonSerializer.Deserialize<SpellbookLayout>(File.ReadAllText(path), Opts) ?? new SpellbookLayout(); }
        catch { return new SpellbookLayout(); }
    }

    private static void Save(SpellbookLayout layout, string path)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(layout, Opts)); }
        catch { }
    }
}