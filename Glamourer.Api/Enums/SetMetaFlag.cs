namespace Glamourer.Api.Enums;

/// <summary> Application flags for setting the meta state of an actor. </summary>
[Flags]
public enum MetaFlag : ulong
{
    Wetness = 0x01,
    HatState = 0x02,
    VisorState = 0x04,
    WeaponState = 0x08,
}
