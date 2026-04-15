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
    [ProtoMember(2)] public int SpellLevel { get; set; } = 1;
}

[ProtoContract]
public class MsgChannelSpell
{
    [ProtoMember(1)] public string SpellId { get; set; } = "";
    [ProtoMember(2)] public bool IsActive { get; set; }
    [ProtoMember(3)] public int SpellLevel { get; set; } = 1;
}

/// <summary>Server → casting client: notify cast start (for HUD cast bar).</summary>
[ProtoContract]
public class MsgStartCast
{
    [ProtoMember(1)] public string SpellId { get; set; } = "";
    [ProtoMember(2)] public float CastTime { get; set; } = 1f;
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
    [ProtoMember(5)] public float LookDirY     { get; set; }
    [ProtoMember(6)] public bool  UseLookY     { get; set; }
}

/// <summary>Server → all nearby clients: play a spell animation on an entity.</summary>
[ProtoContract]
public class MsgPlayAnimation
{
    [ProtoMember(1)] public long   EntityId      { get; set; }
    [ProtoMember(2)] public string AnimationCode { get; set; } = "";
    [ProtoMember(3)] public bool   UpperBodyOnly { get; set; } = true;
    [ProtoMember(4)] public float  AnimationSpeed { get; set; } = 1f;
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
    [ProtoMember(8)] public int    SpellLevel { get; set; } = 1;
    [ProtoMember(9)] public float  RollDeg { get; set; } = 0f;
}

[ProtoContract]
public class MsgChickenKills
{
    [ProtoMember(1)] public int Count { get; set; }
}

[ProtoContract]
public class MsgCancelCast
{
    [ProtoMember(1)] public long EntityId { get; set; }
    [ProtoMember(2)] public string SpellId { get; set; } = "";
}
