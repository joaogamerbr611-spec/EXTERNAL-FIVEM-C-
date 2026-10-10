namespace FiveMExternal;

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class Settings
{
    // ──────────────────────────────────────────────────────────
    //  Persistence
    // ──────────────────────────────────────────────────────────
    private static readonly string ConfigPath = Path.Combine(
        AppContext.BaseDirectory, "vzx_config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // Vector4 não é suportado nativamente — usamos surrogate
        Converters = { new Vector4Converter() }
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<Settings>(json, JsonOpts) ?? new Settings();
            }
        }
        catch { /* config corrompida → padrão */ }
        return new Settings();
    }

    public void Save()
    {
        try { File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOpts)); }
        catch { }
    }

    // ──────────────────────────────────────────────────────────
    //  Menu / HUD
    // ──────────────────────────────────────────────────────────
    public bool ShowMenu          { get; set; }
    public bool RememberLicenseKey { get; set; }
    public string SavedLicenseKey  { get; set; } = string.Empty;
    public bool ShowStatusBar     { get; set; } = false;
    public int  StatusBarPosition { get; set; } = 0; // 0=TopLeft, 1=BottomLeft, 2=BottomRight, 3=TopRight
    public bool ShowCrosshair     { get; set; }
    public bool Crosshair         { get; set; }
    public bool StreamMode        { get; set; }
    public int  MenuKey           { get; set; } = NativeMethods.VK_INSERT;

    // crosshair
    public int    CrosshairStyle     { get; set; }   // 0=plus, 1=dot, 2=circle, 3=cross+dot
    public float  CrosshairSize      { get; set; } = 10f;
    public float  CrosshairThickness { get; set; } = 1.5f;
    public float  CrosshairGap       { get; set; } = 4f;
    public Vector4 CrosshairColor    { get; set; } = new(0f, 1f, 0f, 1f);

    // ──────────────────────────────────────────────────────────
    //  ESP
    // ──────────────────────────────────────────────────────────
    public bool  EspEnabled         { get; set; } = true;
    public int   EspToggleKey       { get; set; }
    public bool  EspBox             { get; set; } = true;
    public bool  EspReferenceStyle  { get; set; } = true;
    public bool  EspHeadCross       { get; set; }
    public bool  EspName            { get; set; } = true;
    public bool  EspDistance        { get; set; } = true;
    public bool  EspWeapon          { get; set; } = true;
    public bool  EspHealth          { get; set; } = true;
    public bool  EspSkeleton        { get; set; } = true;
    public bool  EspVehicles        { get; set; } = true;
    public bool  EspVehicleName     { get; set; } = true;
    public bool  EspVehicleLock     { get; set; } = true;
    public bool  EspVehicleDistance { get; set; } = true;
    public float EspMaxDistance     { get; set; } = 1000f;
    public float EspTextSize        { get; set; } = 12f;
    public float EspBoxThickness    { get; set; } = 1.25f;
    public float EspSkeletonThickness { get; set; } = 1.0f;

    public float  VehicleMaxDistance   { get; set; } = 300f;
    public int    VehicleUnlockKey     { get; set; }
    public int    VehicleFixKey        { get; set; }
    public Vector4 VehicleNameColor    { get; set; } = Vector4.One;
    public Vector4 VehicleDistanceColor{ get; set; } = new(.9f, .9f, .9f, 1f);
    public Vector4 VehicleUnlockedColor{ get; set; } = new(.2f, 1f, .2f, 1f);
    public Vector4 VehicleLockedColor  { get; set; } = new(1f, .2f, .2f, 1f);
    public Vector4 VehicleInsideColor  { get; set; } = new(1f, .6f, 0f, 1f);
    public Vector4 PlayerColor         { get; set; } = Vector4.One;
    public Vector4 NpcColor            { get; set; } = Vector4.One;
    public Vector4 SkeletonColor       { get; set; } = Vector4.One;
    public Vector4 ReferenceHealthColor{ get; set; } = new(.35f, 1f, .47f, 1f);

    // ──────────────────────────────────────────────────────────
    //  Aim Assist
    // ──────────────────────────────────────────────────────────
    public bool  AimEnabled           { get; set; }
    public bool  AimShowFov           { get; set; } = true;
    public bool  AimOnlyPlayers       { get; set; } = true;
    public bool  AimIgnoreInvisible   { get; set; } = true;
    public bool  AimPrediction        { get; set; }
    public bool  AimAlwaysActive      { get; set; }
    public bool  AimPrioritizeDistance{ get; set; }
    public int   AimBone              { get; set; }
    public float AimFov               { get; set; } = 180f;
    public float AimSmooth            { get; set; } = 7f;
    public float AimMaxDistance       { get; set; } = 300f;
    public int   AimKey               { get; set; } = NativeMethods.VK_RBUTTON;

    // ──────────────────────────────────────────────────────────
    //  Silent Aim
    // ──────────────────────────────────────────────────────────
    public bool  SilentEnabled           { get; set; }
    public bool  SilentShowFov           { get; set; } = true;
    public bool  SilentOnlyPlayers       { get; set; } = true;
    public bool  SilentIgnoreInvisible   { get; set; } = true;
    public bool  SilentPrioritizeDistance{ get; set; }
    public int   SilentBone              { get; set; }
    public float SilentFov               { get; set; } = 180f;
    public float SilentMaxDistance       { get; set; } = 300f;
    public int   SilentKey               { get; set; } = NativeMethods.VK_LBUTTON;

    // ──────────────────────────────────────────────────────────
    //  Triggerbot
    // ──────────────────────────────────────────────────────────
    public bool  TriggerEnabled        { get; set; }
    public bool  TriggerOnlyPlayers    { get; set; } = true;
    public bool  TriggerIgnoreInvisible{ get; set; } = true;
    public float TriggerFov            { get; set; } = 15f;
    public float TriggerMaxDistance    { get; set; } = 200f;
    public int   TriggerDelayMin       { get; set; } = 30;   // ms
    public int   TriggerDelayMax       { get; set; } = 90;   // ms
    public int   TriggerKey            { get; set; } = NativeMethods.VK_RBUTTON;

    // ──────────────────────────────────────────────────────────
    //  Teleport
    // ──────────────────────────────────────────────────────────
    public bool GodMode { get; set; }
    public List<SavedTeleport> SavedTeleports { get; set; } = new();
}

public sealed class SavedTeleport
{
    public string Name { get; set; } = "Novo local";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

// ──────────────────────────────────────────────────────────────
//  JSON surrogate para System.Numerics.Vector4
// ──────────────────────────────────────────────────────────────
internal sealed class Vector4Converter : JsonConverter<Vector4>
{
    public override Vector4 Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions __)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        return new Vector4(
            root.GetProperty("r").GetSingle(),
            root.GetProperty("g").GetSingle(),
            root.GetProperty("b").GetSingle(),
            root.GetProperty("a").GetSingle());
    }

    public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions _)
    {
        writer.WriteStartObject();
        writer.WriteNumber("r", value.X);
        writer.WriteNumber("g", value.Y);
        writer.WriteNumber("b", value.Z);
        writer.WriteNumber("a", value.W);
        writer.WriteEndObject();
    }
}
