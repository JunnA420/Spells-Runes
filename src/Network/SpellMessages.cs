using ProtoBuf;

namespace SpellsAndRunes.Network;

[ProtoContract]
public class MsgUnlockSpell
{
    [ProtoMember(1)] public string SpellId { get; set; } = "";
}

[ProtoContract]
public class MsgSetHotbarSlot
{
    [ProtoMember(1)] public int    Slot    { get; set; }
    [ProtoMember(2)] public string SpellId { get; set; } = ""; // empty = clear
}

[ProtoContract]
public class MsgCastSpell
{
    [ProtoMember(1)] public string SpellId { get; set; } = "";
}

/// <summary>Server → casting client only: kill your momentum immediately.</summary>
[ProtoContract]
public class MsgFreezeMotion
{
    [ProtoMember(1)] public float NudgeY { get; set; } = 0.06f;
}

/// <summary>Server → casting client only: launch with upward burst then forward dash.</summary>
[ProtoContract]
public class MsgLaunchPlayer
{
    [ProtoMember(1)] public float UpForce      { get; set; }
    [ProtoMember(2)] public float ForwardForce { get; set; }
    [ProtoMember(3)] public float LookDirX     { get; set; }
    [ProtoMember(4)] public float LookDirZ     { get; set; }
}

/// <summary>Server → all nearby clients: a spell visual effect fired at this position/direction.</summary>
[ProtoContract]
public class MsgSpellFx
{
    [ProtoMember(1)] public string SpellId  { get; set; } = "";
    [ProtoMember(2)] public double OriginX  { get; set; }
    [ProtoMember(3)] public double OriginY  { get; set; }
    [ProtoMember(4)] public double OriginZ  { get; set; }
    [ProtoMember(5)] public float  LookDirX { get; set; }
    [ProtoMember(6)] public float  LookDirY { get; set; }
    [ProtoMember(7)] public float  LookDirZ { get; set; }
}
