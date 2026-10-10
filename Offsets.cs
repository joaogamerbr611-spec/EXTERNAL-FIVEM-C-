namespace FiveMExternal;

public readonly record struct OffsetSet(nint World, nint ReplayInterface, nint ViewPort, nint Camera)
{
    public static readonly OffsetSet Build3095 = new(0x2593320, 0x1F58B58, 0x20019E0, 0x2002888);
    public static readonly OffsetSet Build3258 = new(0x25B14B0, 0x1FBD4F0, 0x201DBA0, 0x201ED50);
}

public static class Offsets
{
    public const nint FragInstance = 0x1430;
    public const nint BoneList = 0x410;
    public const nint PlayerInfo = 0x10A8;
    public const nint WeaponManager = 0x10B8;
    public const nint Matrix = 0x60;
    public const nint Position = 0x90;
    public const nint GodMode = 0x189;
    public const nint Health = 0x280;
    public const nint MaxHealth = 0x284;
    public const nint Velocity = 0x300;
    public const nint Armor = 0x150C;
    public const nint LockState = 0x13C0;
    public const nint VehicleEngineHealth = 0x280;
    public const nint VehicleBodyHealth = 0x820;
    public const nint VehiclePetrolTankHealth = 0x824;
    public const nint VehicleDirtLevel = 0x9E4;
    public const nint Invisibility = 0x2C;
    public const nint Alpha = 0xA8;
}
