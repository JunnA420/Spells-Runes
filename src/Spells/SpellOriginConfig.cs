namespace SpellsAndRunes.Spells;

/// <summary>
/// Configures where a spell originates relative to the caster.
/// All values are in blocks. Default = eye level, no offsets.
/// </summary>
public record struct SpellOriginConfig(
    float Forward = 0f,  // along look direction
    float Up      = 0f,  // relative to eye height (negative = below eyes)
    float Side    = 0f   // right of look direction (negative = left)
);
