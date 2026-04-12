using System.Collections.Generic;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Air;

public class TripleWindSlash : Spell
{
    public override string Id          => "air_triple_wind_slash";
    public override string Name        => "Triple Wind Slash";
    public override string Description => "Shapes three wind blades in quick succession and launches them forward, each following the last.";

    public override SpellTier    Tier    => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 45f;
    public override float CastTime => 1.2f;

    public override IReadOnlyList<string> Prerequisites => ["air_wind_slash"];

    public override (int col, int row) TreePosition => (2, 4);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        var api = world.Api;
        if (api == null) return;

        var lookDir = caster.SidedPos.GetViewVector().ToVec3d().Normalize();
        var up = new Vec3d(0, 1, 0);
        var right = lookDir.Cross(up).Normalize();
        var baseOrigin = caster.SidedPos.XYZ.Add(0, caster.LocalEyePos.Y - 0.1, 0);
        float range = WindSlash.Range * GetRangeMultiplier(spellLevel) * 1.1f;
        float damage = WindSlash.Damage * GetDamageMultiplier(spellLevel) * 0.8f;
        double[] offsets = { -0.7, 0.0, 0.7 };

        for (int i = 0; i < offsets.Length; i++)
        {
            int index = i;
            api.Event.RegisterCallback(_ =>
            {
                if (!caster.Alive) return;
                var origin = baseOrigin + right * offsets[index];
                WindSlash.HitAlongLine(caster, world, origin, lookDir, range, WindSlash.HitRadius, damage);
                WindSlash.SpawnFx(world, origin, lookDir, spellLevel, range);
            }, i * 120);
        }
    }
}
