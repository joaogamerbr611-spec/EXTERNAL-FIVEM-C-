using System.Numerics;
using System.Collections.Concurrent;

namespace FiveMExternal;

public sealed class Entity
{
    private readonly record struct CachedBone(Vector3 Position, long UpdatedAt);
    private readonly record struct CachedName(string Value, long UpdatedAt);
    private static readonly ConcurrentDictionary<(nint Address, int Mask), CachedBone> ComplexBoneCache = new();
    private static readonly ConcurrentDictionary<int, CachedName> PlayerNameCache = new();
    private static nint playerNamesModule;
    private static long playerNamesModuleCheckedAt;
    private const nint PlayerNamesHeadOffset = 0x31D98;
    private static readonly int[] BoxBones = { 0, 1, 2, 7 };
    private static readonly int[] SkeletonOnlyBones = { 3, 4, 5, 6, 8 };
    private readonly ProcessMemory memory;
    private readonly Vector3[] simpleBones = new Vector3[9];
    public nint Address { get; }
    public nint PlayerInfo { get; private set; }
    public nint CurrentWeapon { get; private set; }
    public float Health { get; private set; }
    public float MaxHealth { get; private set; }
    public float Armor { get; private set; }
    public Vector3 Velocity { get; private set; }
    public Vector3 Position { get; private set; }
    public Matrix4x4 Matrix { get; private set; }
    public bool IsInvisible { get; private set; }
    public long UpdatedAt { get; private set; }
    private nint skeletonData;
    private readonly Dictionary<int, uint> boneIds = new();

    public Entity(ProcessMemory memory, nint address) { this.memory = memory; Address = address; RefreshStatic(); }
    public bool IsPlayer => PlayerInfo != 0;
    public bool IsDead => Health <= 0 || Position == Vector3.Zero;

    public void RefreshStatic()
    {
        PlayerInfo = memory.Read<nint>(Address + Offsets.PlayerInfo);
        var weaponManager = memory.Read<nint>(Address + Offsets.WeaponManager);
        CurrentWeapon = weaponManager == 0 ? 0 : memory.Read<nint>(weaponManager + 0x20);
        MaxHealth = memory.Read<float>(Address + Offsets.MaxHealth);
        skeletonData = 0;
        boneIds.Clear();
        var fragment = memory.Read<nint>(Address + Offsets.FragInstance);
        var instance = fragment == 0 ? 0 : memory.Read<nint>(fragment + 0x68);
        skeletonData = instance == 0 ? 0 : memory.Read<nint>(instance + 0x178);
    }

    public bool Refresh(bool includeSimpleSkeleton)
    {
        Health = memory.Read<float>(Address + Offsets.Health);
        Position = memory.Read<Vector3>(Address + Offsets.Position);
        if (IsDead) return false;
        Armor = memory.Read<float>(Address + Offsets.Armor);
        Velocity = memory.Read<Vector3>(Address + Offsets.Velocity);
        Matrix = memory.Read<Matrix4x4>(Address + Offsets.Matrix);

        // Check GTA V Ped invisibility:
        // 0x2C byte (0x01 / 0x2B flag for invisible) or Alpha at 0xA8 (0 or < 50 for full/high transparency)
        var invisFlag = memory.Read<byte>(Address + Offsets.Invisibility);
        var alphaByte = memory.Read<byte>(Address + Offsets.Alpha);
        IsInvisible = invisFlag == 0x01 || (alphaByte is < 30 and > 0);

        SnapshotBones(BoxBones);
        if (includeSimpleSkeleton) SnapshotBones(SkeletonOnlyBones);
        UpdatedAt = Environment.TickCount64;
        return true;
    }

    private void SnapshotBones(IEnumerable<int> indices)
    {
        var baseAddress = Address + Offsets.BoneList;
        foreach (var index in indices)
            simpleBones[index] = memory.Read<Vector3>(baseAddress + index * 0x10);
    }

    public string Name
    {
        get
        {
            if (!IsPlayer) return "NPC";
            var playerId = ReadPlayerId();
            if (playerId > 0 && playerId < 65535)
            {
                var now = Environment.TickCount64;
                if (PlayerNameCache.TryGetValue(playerId, out var cached) && now - cached.UpdatedAt < 1000)
                    return cached.Value;

                var resolved = ResolvePlayerNameFromCitizenTable(playerId);
                if (!string.IsNullOrEmpty(resolved))
                {
                    PlayerNameCache[playerId] = new CachedName(resolved, now);
                    return resolved;
                }
            }

            // Same inline fallbacks used by the reference project when the
            // citizen-playernames table has not streamed the name yet.
            var inlineName = ValidName(memory.ReadAscii(PlayerInfo + 0xE0, 64));
            if (inlineName.Length == 0) inlineName = ValidName(memory.ReadAscii(PlayerInfo + 0xFC, 64));
            if (inlineName.Length != 0) return inlineName;
            return playerId > 0 && playerId < 65535 ? $"ID: {playerId}" : "Player";
        }
    }

    /// <summary>
    /// Resolves the selected CWeaponInfo name across the common FiveM layouts.
    /// A non-null weapon object also exists while a ped is unarmed, so unknown
    /// data deliberately resolves to Unarmed instead of producing false labels.
    /// </summary>
    public string WeaponName
    {
        get
        {
            if (CurrentWeapon == 0) return "Unarmed";

            foreach (var offset in new nint[] { 0x10, 0x18, 0x20 })
            {
                if (WeaponLabel(memory.Read<uint>(CurrentWeapon + offset)) is { } label)
                    return label;
            }
            return "Unarmed";
        }
    }

    private static string? WeaponLabel(uint hash) => hash switch
    {
        0xA2719263 => "Unarmed",
        0x1B06D571 => "Pistol",
        0x5EF9FEC4 => "Combat Pistol",
        0x22D8FE39 => "AP Pistol",
        0x99AEEB3B => "Pistol .50",
        0x13532244 => "Micro SMG",
        0x2BE6766B => "SMG",
        0xBFEFFF6D => "Assault Rifle",
        0x83BF0278 => "Carbine Rifle",
        0xAF113F99 => "Advanced Rifle",
        0x9D07F764 => "MG",
        0x7FD62962 => "Combat MG",
        0x1D073A89 => "Pump Shotgun",
        0xE284C527 => "Assault Shotgun",
        0x05FC3C11 => "Sniper Rifle",
        0x0C472FE2 => "Heavy Sniper",
        0xB1CA77B1 => "RPG",
        0x3656C8C1 => "Stun Gun",
        0x99B507EA => "Knife",
        0x958A4A8F => "Bat",
        _ => null
    };

    private int ReadPlayerId()
    {
        foreach (var offset in new nint[] { 0xE8, 0x88, 0x68, 0xEC })
        {
            var id = memory.Read<int>(PlayerInfo + offset);
            if (id > 0 && id < 65535) return id;
        }
        return 0;
    }

    private string ResolvePlayerNameFromCitizenTable(int playerId)
    {
        var now = Environment.TickCount64;
        if (playerNamesModule == 0 || now - playerNamesModuleCheckedAt > 3000)
        {
            playerNamesModuleCheckedAt = now;
            playerNamesModule = memory.GetModuleBase("citizen-playernames-five.dll");
        }
        if (playerNamesModule == 0) return string.Empty;

        var sentinel = memory.Read<nint>(playerNamesModule + PlayerNamesHeadOffset);
        var count = memory.Read<int>(playerNamesModule + PlayerNamesHeadOffset + 0x8);
        if (sentinel == 0 || count is < 1 or > 2048) return string.Empty;

        var node = memory.Read<nint>(sentinel + 0x8);
        for (var index = 0; index < count && node != 0 && node != sentinel; index++, node = memory.Read<nint>(node + 0x8))
        {
            if (memory.Read<int>(node + 0x10) != playerId) continue;
            var capacity = memory.Read<nint>(node + 0x30).ToInt64();
            var name = capacity >= 16 ? memory.ReadAscii(memory.Read<nint>(node + 0x18), 64) : string.Empty;
            if (string.IsNullOrEmpty(name)) name = memory.ReadAscii(node + 0x18, 64);
            return ValidName(name);
        }
        return string.Empty;
    }

    private static string ValidName(string value)
    {
        var name = value.Trim();
        if (name.Length == 0 || name.Length > 64 || !name.All(character => character is >= ' ' and <= '~')) return string.Empty;
        return name.Equals("Player", StringComparison.OrdinalIgnoreCase) || name.Equals("NPC", StringComparison.OrdinalIgnoreCase) ? string.Empty : name;
    }

    public Vector3 Bone(int index)
    {
        return Bone(index, Matrix);
    }

    public Vector3 Bone(int index, Matrix4x4 matrix)
    {
        return (uint)index < simpleBones.Length
            ? Vector3.Transform(simpleBones[index], matrix)
            : Position;
    }

    /// <summary>Reads a GTA animation bone using the same crSkeletonData transform as CPed.cpp.</summary>
    public Vector3 ComplexBone(int boneMask)
    {
        var cacheKey = (Address, boneMask);
        var now = Environment.TickCount64;
        if (ComplexBoneCache.TryGetValue(cacheKey, out var cached) && now - cached.UpdatedAt < 33)
            return cached.Position;
        if (skeletonData == 0) return Vector3.Zero;
        if (!boneIds.TryGetValue(boneMask, out var boneId))
        {
            var table = memory.Read<nint>(skeletonData);
            if (table == 0 || memory.Read<ushort>(table + 0x1A) == 0) return Vector3.Zero;
            var bucketCount = memory.Read<ushort>(table + 0x18);
            var buckets = memory.Read<nint>(table + 0x10);
            if (bucketCount == 0 || buckets == 0) return Vector3.Zero;
            var node = memory.Read<nint>(buckets + 8 * (boneMask % bucketCount));
            for (var attempts = 0; node != 0 && attempts < 64; attempts++, node = memory.Read<nint>(node + 0x8))
                if (memory.Read<int>(node) == boneMask)
                {
                    boneId = unchecked((uint)memory.Read<int>(node + 0x4));
                    if (boneId == 0) return Vector3.Zero;
                    boneIds[boneMask] = boneId;
                    break;
                }
            if (boneId == 0) return Vector3.Zero;
        }

        var parent = memory.Read<nint>(skeletonData + 0x8);
        var matrices = memory.Read<nint>(skeletonData + 0x18);
        if (parent == 0 || matrices == 0) return Vector3.Zero;
        var col0 = memory.Read<Vector4>(parent);
        var col1 = memory.Read<Vector4>(parent + 0x10);
        var col2 = memory.Read<Vector4>(parent + 0x20);
        var col3 = memory.Read<Vector4>(parent + 0x30);
        var localPosition = memory.Read<Vector4>(matrices + (nint)boneId * 0x40 + 0x30);
        var world = col0 * localPosition.X + col1 * localPosition.Y + col2 * localPosition.Z + col3;
        var result = new Vector3(world.X, world.Y, world.Z);
        ComplexBoneCache[cacheKey] = new CachedBone(result, now);
        return result;
    }
}
