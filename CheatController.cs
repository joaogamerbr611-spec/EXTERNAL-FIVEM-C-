using System.Collections.Immutable;
using System.Numerics;

namespace FiveMExternal;

public sealed class CheatController : IDisposable
{
    private readonly ProcessMemory memory;
    private readonly GameSdk game;
    private CancellationTokenSource? workerCancellation;
    private bool silentHookInstalled;
    private bool silentPatchActive;
    private nint setPedInVehicle;
    private bool entrySignatureResolved;
    private int vehicleEntryInProgress;
    private int teleportInProgress;
    private ImmutableArray<Entity>  entities = ImmutableArray<Entity>.Empty;
    private ImmutableArray<Vehicle> vehicles = ImmutableArray<Vehicle>.Empty;
    private readonly Random rng = new();

    public Settings Settings { get; }
    public ImmutableArray<Entity>  Entities => entities;
    public ImmutableArray<Vehicle> Vehicles => vehicles;
    public bool TryGetViewMatrix(out Matrix4x4 m) => game.TryGetViewMatrix(out m);
    public bool WorldToScreen(Matrix4x4 m, Vector3 w, Size s, out PointF p) => game.WorldToScreen(m, w, s, out p);

    public Vector3 LocalPosition
    {
        get
        {
            var world    = game.World;
            var localPed = world == 0 ? 0 : memory.Read<nint>(world + 0x8);
            return localPed == 0 ? Vector3.Zero : memory.Read<Vector3>(localPed + Offsets.Position);
        }
    }

    // expose local ped address so overlay can read it directly
    public nint LocalPed
    {
        get
        {
            var world = game.World;
            return world == 0 ? 0 : memory.Read<nint>(world + 0x8);
        }
    }

    public CheatController(ProcessMemory memory, GameSdk game, Settings settings)
    {
        this.memory = memory;
        this.game   = game;
        Settings    = settings;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Vehicle ops
    // ──────────────────────────────────────────────────────────────────────────
    public bool TryUnlockNearestVehicle(float maxDist = 50f)
    {
        if (!TryGetNearestVehicle(maxDist, out var v)) return false;
        return TryUnlockVehicle(v.Address);
    }

    public bool TryUnlockVehicle(nint vehicle)
    {
        if (vehicle == 0) return false;
        memory.Write(vehicle + Offsets.LockState, 1u);
        return true;
    }

    public bool TryFixNearestVehicle(float maxDist = 50f)
    {
        var world    = game.World;
        var localPed = world == 0 ? 0 : memory.Read<nint>(world + 0x8);
        if (localPed == 0) return false;

        var vehicle = memory.Read<nint>(localPed + 0xD10);
        if (vehicle == 0) vehicle = memory.Read<nint>(localPed + 0xD30);
        if (vehicle == 0) vehicle = memory.Read<nint>(localPed + 0xD28);
        if (vehicle == 0)
        {
            if (!TryGetNearestVehicle(maxDist, out var nearest)) return false;
            vehicle = nearest.Address;
        }
        return TryFixVehicle(vehicle);
    }

    public bool TryFixVehicle(nint vehicle)
    {
        if (vehicle == 0) return false;
        var world    = game.World;
        var localPed = world == 0 ? 0 : memory.Read<nint>(world + 0x8);
        if (localPed == 0) return false;
        RequestVehicleNetworkControl(vehicle, localPed);
        memory.Write(vehicle + 0x972, (byte)0x17);
        return true;
    }

    public bool TryPullVehicleToPlayer(nint vehicle)
    {
        if (vehicle == 0) return false;
        var world    = game.World;
        var localPed = world == 0 ? 0 : memory.Read<nint>(world + 0x8);
        if (localPed == 0 || localPed == vehicle) return false;
        var localPosition = memory.Read<Vector3>(localPed + Offsets.Position);
        if (!IsFinite(localPosition)) return false;
        var forward = memory.Read<Vector3>(localPed + 0x70);
        forward.Z = 0;
        forward = forward.LengthSquared() > .001f ? Vector3.Normalize(forward) : Vector3.UnitX;
        var destination = localPosition + forward * 4.5f + new Vector3(0, 0, .25f);
        var zero = Vector3.Zero;
        RequestVehicleNetworkControl(vehicle, localPed);
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 4 && memory.IsAttached; attempt++)
            {
                var nav = memory.Read<nint>(vehicle + 0x30);
                if (nav != 0) memory.Write(nav + 0x50, destination);
                memory.Write(vehicle + Offsets.Position, destination);
                memory.Write(vehicle + Offsets.Velocity, zero);
                await Task.Delay(50).ConfigureAwait(false);
            }
        });
        return true;
    }

    public bool TryEnterVehicle(nint vehicle)
    {
        if (vehicle == 0 || Interlocked.CompareExchange(ref vehicleEntryInProgress, 1, 0) != 0) return false;
        var world    = game.World;
        var localPed = world == 0 ? 0 : memory.Read<nint>(world + 0x8);
        if (localPed == 0 || localPed == vehicle || !TryResolveSetPedInVehicle())
        {
            Volatile.Write(ref vehicleEntryInProgress, 0);
            return false;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                RequestVehicleNetworkControl(vehicle, localPed);
                await Task.Delay(100).ConfigureAwait(false);
                memory.TryExecuteRemote(BuildSetPedInVehicleShellcode(localPed, vehicle, setPedInVehicle));
            }
            finally { Volatile.Write(ref vehicleEntryInProgress, 0); }
        });
        return true;
    }

    private bool TryResolveSetPedInVehicle()
    {
        if (setPedInVehicle != 0) return true;
        if (entrySignatureResolved) return false;
        entrySignatureResolved = true;
        setPedInVehicle = memory.FindPattern("48 8B C4 44 89 48 ? 44 89 40 ? 48 89 50 ? 48 89 48 ? 55 53 56 57 41 54 41 55 41 56 41 57 48 8D A8 ? ? ? ? 48 81 EC ? ? ? ? 83 BA");
        return setPedInVehicle != 0;
    }

    private static byte[] BuildSetPedInVehicleShellcode(nint ped, nint vehicle, nint function)
    {
        var sc = new byte[]
        {
            0x48,0x83,0xEC,0x28,
            0x48,0xB9,0,0,0,0,0,0,0,0,
            0x48,0xBA,0,0,0,0,0,0,0,0,
            0x41,0xB8,0,0,0,0,
            0x41,0xB9,0,0,0,0,
            0x48,0xB8,0,0,0,0,0,0,0,0,
            0xFF,0xD0,
            0x48,0x83,0xC4,0x28,
            0x33,0xC0,0xC3
        };
        BitConverter.GetBytes(ped.ToInt64()).CopyTo(sc, 6);
        BitConverter.GetBytes(vehicle.ToInt64()).CopyTo(sc, 16);
        BitConverter.GetBytes(function.ToInt64()).CopyTo(sc, 38);
        return sc;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Worker lifecycle
    // ──────────────────────────────────────────────────────────────────────────
    public void Start(CancellationToken applicationToken)
    {
        Stop();
        workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(applicationToken);
        _ = UpdateEntitiesAsync(workerCancellation.Token);
        _ = UpdateVehiclesAsync(workerCancellation.Token);
        _ = ApplyMiscAsync(workerCancellation.Token);
        _ = ApplyAimAsync(workerCancellation.Token);
        _ = ApplySilentAimAsync(workerCancellation.Token);
        _ = ApplyTriggerBotAsync(workerCancellation.Token);
    }

    public void Stop()
    {
        RestoreSilentPatch();
        workerCancellation?.Cancel();
        workerCancellation?.Dispose();
        workerCancellation = null;
        entities = ImmutableArray<Entity>.Empty;
        vehicles = ImmutableArray<Vehicle>.Empty;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Entity / vehicle snapshots  (batch-read optimised)
    // ──────────────────────────────────────────────────────────────────────────
    private async Task UpdateEntitiesAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && memory.IsAttached)
        {
            var discovered = ImmutableArray.CreateBuilder<Entity>();
            var replay     = game.ReplayInterface;
            var local      = game.World == 0 ? 0 : memory.Read<nint>(game.World + 0x8);
            var entityList = replay == 0 ? 0 : memory.Read<nint>(memory.Read<nint>(replay + 0x18) + 0x100);
            if (entityList != 0)
            {
                for (var i = 0; i < 256; i++)
                {
                    var address = memory.Read<nint>(entityList + i * 0x10);
                    if (address == 0 || address == local) continue;
                    var entity = new Entity(memory, address);
                    if (entity.Refresh(Settings.EspSkeleton || Settings.AimEnabled || Settings.TriggerEnabled))
                        discovered.Add(entity);
                }
            }
            entities = discovered.ToImmutable();
            await Task.Delay(50, token).ConfigureAwait(false);
        }
    }

    private async Task UpdateVehiclesAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && memory.IsAttached)
        {
            var discovered     = ImmutableArray.CreateBuilder<Vehicle>();
            var replay         = game.ReplayInterface;
            var vehicleIface   = replay == 0 ? 0 : memory.Read<nint>(replay + 0x10);
            var list           = vehicleIface == 0 ? 0 : memory.Read<nint>(vehicleIface + 0x180);
            var count          = vehicleIface == 0 ? 0 : memory.Read<int>(vehicleIface + 0x188);
            if (list != 0 && count is > 0 and <= 1024)
                for (var i = 0; i < count; i++)
                {
                    var addr = memory.Read<nint>(list + i * 0x10);
                    if (addr == 0) continue;
                    var pos = memory.Read<Vector3>(addr + Offsets.Position);
                    if (pos != Vector3.Zero)
                        discovered.Add(new Vehicle(
                            addr, pos,
                            memory.Read<Vector3>(addr + Offsets.Velocity),
                            memory.Read<uint>(addr + Offsets.LockState),
                            memory.ReadAscii(memory.Read<nint>(addr + 0x20) + 0x298, 64),
                            Environment.TickCount64));
                }
            vehicles = discovered.ToImmutable();
            await Task.Delay(33, token).ConfigureAwait(false);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Misc (god mode)
    // ──────────────────────────────────────────────────────────────────────────
    private async Task ApplyMiscAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && memory.IsAttached)
        {
            if (Settings.GodMode && game.World is var world && world != 0)
            {
                var local = memory.Read<nint>(world + 0x8);
                if (local > 0x10000)
                {
                    memory.Write(local + Offsets.GodMode, true);
                    memory.Write(local + Offsets.Health, 200f);
                }
            }
            await Task.Delay(16, token).ConfigureAwait(false);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Aim assist
    // ──────────────────────────────────────────────────────────────────────────
    private async Task ApplyAimAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && memory.IsAttached)
        {
            if (Settings.AimEnabled && (Settings.AimAlwaysActive || NativeMethods.IsKeyDown(Settings.AimKey)))
                AimAtBestTarget();
            await Task.Delay(8, token).ConfigureAwait(false);
        }
    }

    private void AimAtBestTarget()
    {
        if (!game.TryGetViewMatrix(out var view)) return;
        var world    = game.World;
        var localPed = world == 0 ? 0 : memory.Read<nint>(world + 0x8);
        if (localPed == 0) return;
        var localPos = memory.Read<Vector3>(localPed + Offsets.Position);
        var screen   = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Size ?? new Size(1920, 1080);
        var center   = new Vector2(screen.Width * .5f, screen.Height * .5f);
        Entity? best = null;
        var bestPoint = Vector3.Zero;
        var bestScore = float.MaxValue;

        foreach (var entity in entities)
        {
            if (Settings.AimOnlyPlayers && !entity.IsPlayer) continue;
            if (Settings.AimIgnoreInvisible && entity.IsInvisible) continue;
            var worldDist = Vector3.Distance(entity.Position, localPos);
            if (worldDist > Settings.AimMaxDistance) continue;
            var point = GetBone(entity, Settings.AimBone);
            if (Settings.AimPrediction) point += entity.Velocity * .075f;
            if (!game.WorldToScreen(view, point, screen, out var proj)) continue;
            var cursorDist = Vector2.Distance(center, new Vector2(proj.X, proj.Y));
            var score      = Settings.AimPrioritizeDistance ? worldDist : cursorDist;
            if (cursorDist > Settings.AimFov || score >= bestScore) continue;
            best = entity; bestPoint = point; bestScore = score;
        }
        if (best is not null) SetCameraAim(bestPoint);
    }

    private static Vector3 GetBone(Entity entity, int bone) => bone switch
    {
        1 => entity.Bone(7), 2 => entity.Bone(8), 3 => entity.Bone(5),
        4 => entity.Bone(6), 5 => entity.Bone(1), 6 => entity.Bone(2),
        _ => entity.Bone(0)
    };

    // ──────────────────────────────────────────────────────────────────────────
    //  Triggerbot — dispara quando um inimigo cruza o crosshair
    // ──────────────────────────────────────────────────────────────────────────
    private async Task ApplyTriggerBotAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && memory.IsAttached)
        {
            if (Settings.TriggerEnabled && NativeMethods.IsKeyDown(Settings.TriggerKey))
                await TryTriggerAsync(token);
            await Task.Delay(8, token).ConfigureAwait(false);
        }
    }

    private async Task TryTriggerAsync(CancellationToken token)
    {
        if (!game.TryGetViewMatrix(out var view)) return;
        var world    = game.World;
        var localPed = world == 0 ? 0 : memory.Read<nint>(world + 0x8);
        if (localPed == 0) return;
        var localPos = memory.Read<Vector3>(localPed + Offsets.Position);
        var screen   = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Size ?? new Size(1920, 1080);
        var center   = new Vector2(screen.Width * .5f, screen.Height * .5f);

        foreach (var entity in entities)
        {
            if (Settings.TriggerOnlyPlayers && !entity.IsPlayer) continue;
            if (Settings.TriggerIgnoreInvisible && entity.IsInvisible) continue;
            var dist = Vector3.Distance(entity.Position, localPos);
            if (dist > Settings.TriggerMaxDistance) continue;

            // check head and torso — any bone inside FOV fires
            foreach (var boneIndex in new[] { 0, 7 })
            {
                var point = entity.Bone(boneIndex);
                if (!game.WorldToScreen(view, point, screen, out var proj)) continue;
                if (Vector2.Distance(center, new Vector2(proj.X, proj.Y)) > Settings.TriggerFov) continue;

                // humanised delay — anti-pattern detection
                var delay = rng.Next(Settings.TriggerDelayMin, Settings.TriggerDelayMax);
                await Task.Delay(delay, token).ConfigureAwait(false);
                SimulateLeftClick();
                return;
            }
        }
    }

    private static void SimulateLeftClick()
    {
        NativeMethods.SimulateMouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN);
        Thread.Sleep(12);
        NativeMethods.SimulateMouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Teleport
    // ──────────────────────────────────────────────────────────────────────────
    public bool TryTeleportTo(Vector3 destination)
    {
        var world = game.World;
        var localPed = world == 0 ? 0 : memory.Read<nint>(world + 0x8);
        if (localPed <= 0x10000 || !float.IsFinite(destination.X) || !float.IsFinite(destination.Y) || !float.IsFinite(destination.Z))
            return false;
        if (Interlocked.CompareExchange(ref teleportInProgress, 1, 0) != 0)
            return false;

        // Write once on the caller thread first.  This makes the button report
        // an actual failed memory write instead of always claiming success.
        if (!WriteTeleportPosition(localPed, destination))
        {
            Volatile.Write(ref teleportInProgress, 0);
            return false;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                for (var attempt = 0; attempt < 10 && memory.IsAttached; attempt++)
                {
                    WriteTeleportPosition(localPed, destination);
                    await Task.Delay(50).ConfigureAwait(false);
                }
            }
            finally { Volatile.Write(ref teleportInProgress, 0); }
        });
        return true;
    }

    private bool WriteTeleportPosition(nint localPed, Vector3 destination)
    {
        var target = GetTeleportTarget(localPed);
        var nav = memory.Read<nint>(target + 0x30);
        var wrote = true;
        if (nav > 0x10000)
            wrote &= memory.Write(nav + 0x30, Vector3.Zero);
        wrote &= memory.Write(target + Offsets.Position, destination);
        wrote &= memory.Write(target + Offsets.Velocity, Vector3.Zero);
        return wrote;
    }

    private nint GetTeleportTarget(nint localPed)
    {
        // Match the native executor: use the vehicle only when the ped is
        // actually driving or riding in one. A random non-null field must not
        // redirect the position write away from the player.
        var task = memory.Read<byte>(localPed + 0x144B);
        if ((task & 0x10) != 0 || (task & 0x60) == 0)
            return localPed;

        foreach (var offset in new nint[] { 0xD10, 0xD30, 0xD28 })
        {
            var vehicle = memory.Read<nint>(localPed + offset);
            if (vehicle > 0x10000) return vehicle;
        }
        return localPed;
    }

    public bool TryTeleportToWaypoint()
    {
        var blipList = game.BlipList;
        if (blipList == 0) return false;
        for (var index = 0; index < 2000; index++)
        {
            var blip = memory.Read<nint>(blipList + index * 8);
            if (blip == 0 || memory.Read<int>(blip + 0x40) != 8) continue;
            var x = memory.Read<float>(blip + 0x10);
            var y = memory.Read<float>(blip + 0x14);
            return float.IsFinite(x) && float.IsFinite(y) && TryTeleportTo(new Vector3(x, y, -210f));
        }
        return false;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Silent aim
    // ──────────────────────────────────────────────────────────────────────────
    private async Task ApplySilentAimAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && memory.IsAttached)
        {
            if (Settings.SilentEnabled) UpdateSilentAim();
            else RestoreSilentPatch();
            await Task.Delay(4, token).ConfigureAwait(false);
        }
    }

    private void UpdateSilentAim()
    {
        if (!NativeMethods.IsKeyDown(Settings.SilentKey) || !TrySelectSilentTarget(out var target))
        { RestoreSilentPatch(); return; }
        if (!TryInstallSilentHook()) return;
        memory.Write(memory.BaseAddress + GetSilentBulletHandler(), GetSilentJumpValue());
        silentPatchActive = true;
        memory.Write(memory.BaseAddress + 0xEF8, target);
    }

    private bool TrySelectSilentTarget(out Vector3 target)
    {
        target = default;
        if (!game.TryGetViewMatrix(out var view)) return false;
        var world    = game.World;
        var localPed = world == 0 ? 0 : memory.Read<nint>(world + 0x8);
        if (localPed == 0) return false;
        var localPos = memory.Read<Vector3>(localPed + Offsets.Position);
        var screen   = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Size ?? new Size(1920, 1080);
        var center   = new Vector2(screen.Width * .5f, screen.Height * .5f);
        var bestScore = float.MaxValue;

        foreach (var entity in entities)
        {
            if (Settings.SilentOnlyPlayers && !entity.IsPlayer) continue;
            if (Settings.SilentIgnoreInvisible && entity.IsInvisible) continue;
            var dist = Vector3.Distance(entity.Position, localPos);
            if (dist > Settings.SilentMaxDistance) continue;
            var point = GetSilentBone(entity);
            if (!game.WorldToScreen(view, point, screen, out var proj)) continue;
            var score       = Vector2.Distance(center, new Vector2(proj.X, proj.Y));
            var targetScore = Settings.SilentPrioritizeDistance ? dist : score;
            if (score > Settings.SilentFov || targetScore >= bestScore) continue;
            bestScore = targetScore;
            target    = point;
        }
        return bestScore != float.MaxValue;
    }

    private nint  GetSilentBulletHandler() => game.IsBuild3258 ? 0x101A660 : 0x100F5A4;
    private ulong GetSilentJumpValue()     => game.IsBuild3258 ? 0x0F41F3FEFE5D73E9UL : 0x0F41F3FEFF0E2FE9UL;

    private Vector3 GetSilentBone(Entity entity) => Settings.SilentBone switch
    {
        1 => entity.Bone(8), 2 => entity.Bone(2), 3 => entity.Bone(1),
        4 => entity.Bone(6), 5 => entity.Bone(5), 6 => entity.Bone(7),
        _ => entity.Bone(0)
    };

    private bool TryInstallSilentHook()
    {
        if (silentHookInstalled) return true;
        var bulletHandler  = GetSilentBulletHandler();
        var shellcodeBase  = memory.BaseAddress + 0x3D8;
        var jumpAddress    = memory.BaseAddress + bulletHandler + 0x11;
        var shellcode = new byte[]
        {
            0xF3,0x0F,0x10,0x1D,0x18,0x0B,0x00,0x00,
            0xF3,0x0F,0x10,0x05,0x14,0x0B,0x00,0x00,
            0xF3,0x0F,0x10,0x15,0x10,0x0B,0x00,0x00,
            0xE9,0x00,0x00,0x00,0x00
        };
        BitConverter.GetBytes((int)(jumpAddress - (shellcodeBase + 29))).CopyTo(shellcode, 25);
        silentHookInstalled = memory.WriteProtected(shellcodeBase, shellcode);
        return silentHookInstalled;
    }

    private void RestoreSilentPatch()
    {
        if (!silentPatchActive || memory.BaseAddress == 0) return;
        memory.Write(memory.BaseAddress + GetSilentBulletHandler(), 0x0F41F319100F41F3UL);
        silentPatchActive = false;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Camera aim
    // ──────────────────────────────────────────────────────────────────────────
    private void SetCameraAim(Vector3 target)
    {
        var director     = game.Camera;
        var followCamera = director == 0 ? 0 : memory.Read<nint>(director + 0x2C0);
        if (followCamera == 0) return;
        var cameraPos = memory.Read<Vector3>(followCamera + 0x60);
        var desired   = target - cameraPos;
        if (desired.LengthSquared() < .0001f) return;
        desired = Vector3.Normalize(desired);
        var current = memory.Read<Vector3>(followCamera + 0x40);
        if (current.LengthSquared() < .0001f) current = desired;
        else current = Vector3.Normalize(current);
        var factor = 1f / Math.Clamp(Settings.AimSmooth, 1f, 50f);
        var result = Vector3.Normalize(Vector3.Lerp(current, desired, factor));
        memory.Write(followCamera + 0x40, result);
        memory.Write(followCamera + 0x3D0, result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────────
    private bool TryGetNearestVehicle(float maxDist, out Vehicle nearest)
    {
        nearest = default;
        var localPos = LocalPosition;
        if (localPos == Vector3.Zero) return false;
        var bestDist     = maxDist;
        var replay       = game.ReplayInterface;
        var vehicleIface = replay == 0 ? 0 : memory.Read<nint>(replay + 0x10);
        var list         = vehicleIface == 0 ? 0 : memory.Read<nint>(vehicleIface + 0x180);
        var count        = vehicleIface == 0 ? 0 : memory.Read<int>(vehicleIface + 0x188);
        if (list == 0 || count is < 1 or > 1024) return false;
        for (var index = 0; index < count; index++)
        {
            var addr = memory.Read<nint>(list + index * 0x10);
            if (addr == 0) continue;
            var pos  = memory.Read<Vector3>(addr + Offsets.Position);
            if (pos == Vector3.Zero) continue;
            var dist = Vector3.Distance(localPos, pos);
            if (dist > bestDist) continue;
            bestDist = dist;
            nearest  = new Vehicle(addr, pos, memory.Read<Vector3>(addr + Offsets.Velocity), memory.Read<uint>(addr + Offsets.LockState), string.Empty, Environment.TickCount64);
        }
        return nearest.Address != 0;
    }

    private static bool IsFinite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private void RequestVehicleNetworkControl(nint vehicle, nint localPed)
    {
        var vNet = memory.Read<nint>(vehicle + 0xD0);
        var lNet = memory.Read<nint>(localPed + 0xD0);
        if (vNet == 0 || lNet == 0) return;
        var flags = memory.Read<ushort>(vNet + 0x4E);
        flags = (ushort)((flags & ~1) | 8);
        memory.Write(vNet + 0x4E, flags);
        memory.Write(vNet + 0x49, memory.Read<byte>(lNet + 0x49));
        memory.Write(vNet + 0x4A, (byte)0xFF);
        memory.Write(vNet + 0x4B, (byte)0);
    }

    public void Dispose() => Stop();
}

public readonly record struct Vehicle(nint Address, Vector3 Position, Vector3 Velocity, uint LockState, string Name, long UpdatedAt);
