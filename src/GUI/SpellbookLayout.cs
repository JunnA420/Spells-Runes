using System;
using System.IO;
using System.Text.Json;

namespace SpellsAndRunes.GUI;

/// <summary>
/// Live-editable layout config. All public properties are in reference-space pixels (940×600).
/// Call UpdateActualSize() each frame before reading any scaled values.
/// Edit the JSON at ConfigPath and reopen the spellbook to apply changes.
/// </summary>
public class SpellbookLayout
{
    // ── Reference resolution (matches book_bg.png native size) ───────────────
    [System.Text.Json.Serialization.JsonIgnore]
    public const double RefW = 940;
    [System.Text.Json.Serialization.JsonIgnore]
    public const double RefW2 = 600;

    // ── User-facing config (only GuiW / GuiH matter for dialog size) ─────────
    public double GuiW { get; set; } = 940;
    public double GuiH { get; set; } = 600;

    // ── Page bounds (reference pixels, measured from book_bg.png 1536x1024 → 940x600) ──
    public double LPageX { get; set; } = 154;  // left edge of left page
    public double LPageY { get; set; } = 111;  // top edge of pages
    public double LPageW { get; set; } = 315;  // width of left page
    public double LPageH { get; set; } = 378;  // height of pages

    public double RPageX { get; set; } = 469;  // right page starts at spine
    public double RPageY { get; set; } = 111;
    public double RPageW { get; set; } = 330;  // width of right page
    public double RPageH { get; set; } = 378;

    // ── Main tabs (left side, vertical — stick out left of book) ─────────────
    public double MTabX      { get; set; } = 8;
    public double MTabW      { get; set; } = 140;
    public double MTabStartY { get; set; } = 130;
    public double MTabGap    { get; set; } = 5;

    // ── Close bookmark ────────────────────────────────────────────────────────
    public double CloseBookX { get; set; } = 8;
    public double CloseBookY { get; set; } = 80;
    public double CloseBookW { get; set; } = 40;
    public double CloseBookH { get; set; } = 48;

    // ── Element tabs (right page, horizontal) ────────────────────────────────
    public double ETabGap { get; set; } = 4;

    // ── Spell tree nodes ─────────────────────────────────────────────────────
    public double NodeR        { get; set; } = 22;
    public double NodeSpacingX { get; set; } = 88;
    public double NodeSpacingY { get; set; } = 80;

    // ── Memorized spell cards ─────────────────────────────────────────────────
    public double CardW { get; set; } = 195;
    public double CardH { get; set; } = 32;

    // ── Memorized hotbar slots ────────────────────────────────────────────────
    public double SlotR { get; set; } = 26;

    // ── Runtime scale (NOT serialized, set each frame) ───────────────────────
    [System.Text.Json.Serialization.JsonIgnore]
    private double _sx = 1;
    [System.Text.Json.Serialization.JsonIgnore]
    private double _sy = 1;
    [System.Text.Json.Serialization.JsonIgnore]
    private double _s  = 1;

    /// <summary>
    /// Call at the start of every OnDraw with the actual canvas size.
    /// All X() / Y() / D() calls after this will return scaled values.
    /// </summary>
    public void UpdateActualSize(double actualW, double actualH)
    {
        _sx = actualW / GuiW;
        _sy = actualH / GuiH;
        _s  = Math.Min(_sx, _sy);
    }

    // ── Scale helpers ─────────────────────────────────────────────────────────
    /// <summary>Scale a horizontal reference value.</summary>
    public double X(double refVal) => refVal * _sx;

    /// <summary>Scale a vertical reference value.</summary>
    public double Y(double refVal) => refVal * _sy;

    /// <summary>Uniform scale — use for radii, font sizes, node spacing etc.</summary>
    public double D(double refVal) => refVal * _s;

    // ── Scaled computed properties ────────────────────────────────────────────
    // These all go through X() / Y() so they're always correct after UpdateActualSize().

    [System.Text.Json.Serialization.JsonIgnore]
    public double ScLPageX => X(LPageX);
    [System.Text.Json.Serialization.JsonIgnore]
    public double ScLPageY => Y(LPageY);
    [System.Text.Json.Serialization.JsonIgnore]
    public double ScLPageW => X(LPageW);
    [System.Text.Json.Serialization.JsonIgnore]
    public double ScLPageH => Y(LPageH);

    [System.Text.Json.Serialization.JsonIgnore]
    public double ScRPageX => X(RPageX);
    [System.Text.Json.Serialization.JsonIgnore]
    public double ScRPageY => Y(RPageY);
    [System.Text.Json.Serialization.JsonIgnore]
    public double ScRPageW => X(RPageW);
    [System.Text.Json.Serialization.JsonIgnore]
    public double ScRPageH => Y(RPageH);

    [System.Text.Json.Serialization.JsonIgnore]
    public double ScMTabX      => X(MTabX);
    [System.Text.Json.Serialization.JsonIgnore]
    public double ScMTabW      => X(MTabW);
    [System.Text.Json.Serialization.JsonIgnore]
    public double ScMTabStartY => Y(MTabStartY);
    [System.Text.Json.Serialization.JsonIgnore]
    public double ScMTabGap    => Y(MTabGap);

    [System.Text.Json.Serialization.JsonIgnore]
    public double ScCloseBookX => X(CloseBookX);
    [System.Text.Json.Serialization.JsonIgnore]
    public double ScCloseBookY => Y(CloseBookY);
    [System.Text.Json.Serialization.JsonIgnore]
    public double ScCloseBookW => X(CloseBookW);
    [System.Text.Json.Serialization.JsonIgnore]
    public double ScCloseBookH => Y(CloseBookH);

    [System.Text.Json.Serialization.JsonIgnore]
    public double ScETabGap => X(ETabGap);

    [System.Text.Json.Serialization.JsonIgnore]
    public double ScNodeR        => D(NodeR);
    [System.Text.Json.Serialization.JsonIgnore]
    public double ScNodeSpacingX => D(NodeSpacingX);
    [System.Text.Json.Serialization.JsonIgnore]
    public double ScNodeSpacingY => D(NodeSpacingY);

    [System.Text.Json.Serialization.JsonIgnore]
    public double ScCardW => D(CardW);
    [System.Text.Json.Serialization.JsonIgnore]
    public double ScCardH => D(CardH);

    [System.Text.Json.Serialization.JsonIgnore]
    public double ScSlotR => D(SlotR);

    // ── Derived layout values (scaled) ────────────────────────────────────────
    [System.Text.Json.Serialization.JsonIgnore]
    public double LContentX => ScMTabX + ScMTabW + X(8);
    [System.Text.Json.Serialization.JsonIgnore]
    public double LContentW => ScLPageX + ScLPageW - LContentX;

    public double ETabW(int numTabs = 4) => (ScRPageW - (numTabs - 1) * ScETabGap) / numTabs;

    // ── Persistence ───────────────────────────────────────────────────────────
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VintagestoryData", "ModConfig", "spellsandrunes_layout.json");

    public static SpellbookLayout Load()
    {
        string path = ConfigPath;
        if (!File.Exists(path))
        {
            Save(new SpellbookLayout(), path);
            return new SpellbookLayout();
        }
        try
        {
            return JsonSerializer.Deserialize<SpellbookLayout>(File.ReadAllText(path), Opts)
                   ?? new SpellbookLayout();
        }
        catch
        {
            return new SpellbookLayout();
        }
    }

    private static void Save(SpellbookLayout layout, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(layout, Opts));
        }
        catch { }
    }
}