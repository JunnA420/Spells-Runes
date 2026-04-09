using System;
using System.IO;
using System.Text.Json;

namespace SpellsAndRunes.GUI;

/// <summary>
/// Live-editable layout config. Edit the JSON at ConfigPath and reopen the spellbook to apply.
/// </summary>
public class SpellbookLayout
{
    // Left page
    public double LPageX { get; set; } = 30;
    public double LPageY { get; set; } = 55;
    public double LPageW { get; set; } = 358;
    public double LPageH { get; set; } = 490;

    // Right page
    public double RPageX { get; set; } = 472;
    public double RPageY { get; set; } = 55;
    public double RPageW { get; set; } = 438;
    public double RPageH { get; set; } = 490;

    // Main tabs (left side, vertical)
    public double MTabX      { get; set; } = 8;
    public double MTabW      { get; set; } = 150;
    public double MTabStartY { get; set; } = 113;   // Y where first tab starts
    public double MTabGap    { get; set; } = 5;

    // Close bookmark
    public double CloseBookX { get; set; } = 8;
    public double CloseBookY { get; set; } = 47;
    public double CloseBookW { get; set; } = 40;
    public double CloseBookH { get; set; } = 52;

    // Element tabs (right page, horizontal)
    public double ETabGap { get; set; } = 4;

    // Spell tree nodes
    public double NodeR        { get; set; } = 22;
    public double NodeSpacingX { get; set; } = 88;
    public double NodeSpacingY { get; set; } = 80;

    // Memorized spell cards
    public double CardW { get; set; } = 195;
    public double CardH { get; set; } = 32;

    // Memorized hotbar slots
    public double SlotR { get; set; } = 26;

    // ---- Computed helpers ----

    public double LContentX => MTabX + MTabW + 8;
    public double LContentW => LPageX + LPageW - LContentX;
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
