using System.Numerics;
using System.Runtime.InteropServices;

namespace FiveMExternal;

public sealed class GameSdk
{
    private readonly ProcessMemory memory;
    private OffsetSet offsets = OffsetSet.Build3095;
    public GameSdk(ProcessMemory memory) => this.memory = memory;

    public void Initialize()
    {
        offsets = memory.ModuleName.Contains("3258", StringComparison.OrdinalIgnoreCase)
            ? OffsetSet.Build3258
            : OffsetSet.Build3095;
    }

    public nint World => memory.Read<nint>(memory.BaseAddress + offsets.World);
    public nint ReplayInterface => memory.Read<nint>(memory.BaseAddress + offsets.ReplayInterface);
    public nint ViewPort => memory.Read<nint>(memory.BaseAddress + offsets.ViewPort);
    public nint Camera => memory.Read<nint>(memory.BaseAddress + offsets.Camera);
    // Native VZX layout: base + BlipList is a 2,000-entry blip pointer table.
    public nint BlipList => memory.BaseAddress + 0x2023400;
    public bool IsBuild3258 => offsets.Equals(OffsetSet.Build3258);

    public bool TryGetViewMatrix(out Matrix4x4 matrix)
    {
        var viewport = ViewPort;
        matrix = viewport == 0 ? default : memory.Read<Matrix4x4>(viewport + 0x24C);
        return viewport != 0 && matrix != default;
    }

    public bool WorldToScreen(Matrix4x4 view, Vector3 world, Size screen, out PointF point)
    {
        var matrix = Matrix4x4.Transpose(view);
        var clipX = matrix.M21 * world.X + matrix.M22 * world.Y + matrix.M23 * world.Z + matrix.M24;
        var clipY = matrix.M31 * world.X + matrix.M32 * world.Y + matrix.M33 * world.Z + matrix.M34;
        var clipZ = matrix.M41 * world.X + matrix.M42 * world.Y + matrix.M43 * world.Z + matrix.M44;
        if (clipZ <= 0.1f) { point = default; return false; }
        point = new PointF(screen.Width * .5f + clipX / clipZ * screen.Width * .5f,
                           screen.Height * .5f - clipY / clipZ * screen.Height * .5f);
        return true;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct RemoteEntity { public nint Address; public nint Unused; }
