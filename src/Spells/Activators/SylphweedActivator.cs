using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common;

namespace SpellsAndRunes.Spells.Activators;

/// <summary>
/// Triggered when the player interacts with a Sylphweed flower.
/// Unlocks the Sylphweed spell branch (spells with RequiredActivator = "sylphweed").
/// TODO: hook into item interaction event in SpellsAndRunesMod.StartServerSide
/// </summary>
public class SylphweedActivator : SpellActivator
{
    public override string Id          => "sylphweed";
    public override string Name        => "Sylphweed";
    public override string Description => "A rare mountain flower that resonates with air energy.";

    protected override void OnTriggered(EntityAgent player, IWorldAccessor world)
    {
        // TODO: play particle effect + send chat message to player
        // e.g. world.Api.SendMessageToPlayer(..., "You sense the air around the Sylphweed hum with arcane energy...");
    }
}
