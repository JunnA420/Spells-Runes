using System;
using System.IO;
using System.Text.Json;

namespace SpellsAndRunes.GUI;

/// <summary>
/// User sets only GuiW x GuiH. Everything else is computed by scaling from the
/// reference resolution (940x600 = native book_bg.png dimensions).
/// Edit the JSON at ConfigPath and reopen the spellbook to apply changes.
/// </summary>
public class SpellbookLayout
{
    // ---- User-facing config (only these two values matter) ----

    /// <summary>Width of the spellbook dialog in pixels.</summary>
    public double GuiW { get; set; } = 940;

    /// <summary>Height of the spellbook dialog in pixels.</summary>
    public double GuiH { get; set; } = 600;

    // ---- Reference resolution (native book_bg.png size) ----
    private const double RefW = 940;
    private const double RefH = 600;

    // ---- Scale helpers ----
    private double _sx = 1, _sy = 1, _s = 1;

    /// Called each frame with the actual canvas size so elements scale with the book.
    public void UpdateActualSize(double actualW, double actualH)
    {
        _sx = actualW / RefW;
        _sy = actualH / RefH;
        _s  = Math.Min(_sx, _sy);
    }

    public double X(double refVal) => refVal * _sx;
    public double Y(double refVal) => refVal * _sy;
    public double D(double refVal) => refVal * _s;  // uniform — for nodes, slots etc.

    // ---- Reference values (940x600 space) ----
    // These match the visual layout of book_bg.png exactly.
    // Change these only if you redesign the book background image.

    // Left page
    public double LPageX => X(30);
    public double LPageY => Y(55);
    public double LPageW => X(358);
    public double LPageH => Y(490);

    // Right page
    public double RPageX => X(472);
    public double RPageY => Y(55);
    public double RPageW => X(438);
    public double RPageH => Y(490);

    // Main tabs (left side, vertical — stick out left of book)
    public double MTabX      => X(8);
    public double MTabW      => X(150);
    public double MTabStartY => Y(113);
    public double MTabGap    => Y(5);

    // Close bookmark (bottom-left, sticks out below left page)
    public double CloseBookX => X(8);
    public double CloseBookY => Y(510);
    public double CloseBookW => X(30);
    public double CloseBookH => Y(38);

    // Element tabs (right page, horizontal)
    public double ETabGap => X(4);

    // Spell tree nodes
    public double NodeR        => D(22);
    public double NodeSpacingX => D(88);
    public double NodeSpacingY => D(80);

    // Memorized spell cards
    public double CardW => D(195);
    public double CardH => D(32);

    // Memorized hotbar slots
    public double SlotR => D(26);

    // ---- Computed helpers ----

    public double LContentX  => MTabX + MTabW + X(8);
    public double LContentW  => LPageX + LPageW - LContentX;
    public double ETabW(int numTabs = 4) => (RPageW - (numTabs - 1) * ETabGap) / numTabs;

    // ---- Load / Save ----

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
