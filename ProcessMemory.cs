using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FiveMExternal;

public sealed class ProcessMemory : IDisposable
{
    private IntPtr handle;
    public nint BaseAddress   { get; private set; }
    public int   ModuleSize   { get; private set; }
    public int   ProcessId    { get; private set; }
    public string ModuleName  { get; private set; } = string.Empty;
    public IntPtr TargetWindow{ get; private set; }
    public bool IsAttached => handle != IntPtr.Zero
        && TargetWindow != IntPtr.Zero
        && NativeMethods.GetWindowThreadProcessId(TargetWindow, out _) != 0;

    // ──────────────────────────────────────────────────────────
    //  Attach / Detach
    // ──────────────────────────────────────────────────────────
    public bool AttachByWindowClass(string windowClass)
    {
        Detach();
        TargetWindow = NativeMethods.FindWindow(windowClass, null);
        if (TargetWindow == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(TargetWindow, out var pid);
        ProcessId = (int)pid;
        handle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_CREATE_THREAD | NativeMethods.PROCESS_VM_READ |
            NativeMethods.PROCESS_VM_WRITE | NativeMethods.PROCESS_VM_OPERATION |
            NativeMethods.PROCESS_QUERY_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) { TargetWindow = IntPtr.Zero; ProcessId = 0; return false; }
        using var process = Process.GetProcessById((int)pid);
        BaseAddress = process.MainModule?.BaseAddress ?? 0;
        ModuleSize  = process.MainModule?.ModuleMemorySize ?? 0;
        ModuleName  = process.MainModule?.ModuleName ?? string.Empty;
        return BaseAddress != 0;
    }

    public void Detach()
    {
        if (handle != IntPtr.Zero) NativeMethods.CloseHandle(handle);
        handle = IntPtr.Zero; BaseAddress = 0; ModuleSize = 0;
        TargetWindow = IntPtr.Zero; ProcessId = 0; ModuleName = string.Empty;
    }

    // ──────────────────────────────────────────────────────────
    //  Raw byte reads
    // ──────────────────────────────────────────────────────────
    public byte[] ReadBytes(nint address, int count)
    {
        var buffer = new byte[count];
        return handle != IntPtr.Zero
            && NativeMethods.ReadProcessMemory(handle, address, buffer, count, out var read)
            && read == count ? buffer : Array.Empty<byte>();
    }

    // ──────────────────────────────────────────────────────────
    //  Batch read — one syscall, multiple fields pulled out of a
    //  single buffer.  Massive win on tight ESP / aim loops.
    // ──────────────────────────────────────────────────────────
    /// <summary>
    /// Reads <paramref name="length"/> bytes starting at <paramref name="baseAddress"/>
    /// into a rented buffer and returns it.  The caller extracts fields via
    /// <see cref="BatchRead.Get{T}"/>.  Dispose the result to return the buffer.
    /// </summary>
    public BatchRead ReadBatch(nint baseAddress, int length)
    {
        var buf = new byte[length];
        if (handle == IntPtr.Zero ||
            !NativeMethods.ReadProcessMemory(handle, baseAddress, buf, length, out var read) ||
            read != length)
            return new BatchRead(Array.Empty<byte>(), baseAddress);
        return new BatchRead(buf, baseAddress);
    }

    // ──────────────────────────────────────────────────────────
    //  Generic typed read / write
    // ──────────────────────────────────────────────────────────
    public T Read<T>(nint address) where T : unmanaged
    {
        var size  = Marshal.SizeOf<T>();
        var bytes = ReadBytes(address, size);
        return bytes.Length == size ? MemoryMarshal.Read<T>(bytes) : default;
    }

    public bool Write<T>(nint address, T value) where T : unmanaged
    {
        if (handle == IntPtr.Zero || address == 0) return false;
        var bytes = new byte[Marshal.SizeOf<T>()];
        MemoryMarshal.Write(bytes, in value);
        return NativeMethods.WriteProcessMemory(handle, address, bytes, bytes.Length, out var written) && written == bytes.Length;
    }

    public bool WriteProtected(nint address, ReadOnlySpan<byte> bytes)
    {
        if (handle == IntPtr.Zero || bytes.Length == 0) return false;
        if (!NativeMethods.VirtualProtectEx(handle, address, bytes.Length,
            NativeMethods.PAGE_EXECUTE_READWRITE, out var old)) return false;
        try
        {
            var copy = bytes.ToArray();
            return NativeMethods.WriteProcessMemory(handle, address, copy, copy.Length, out var w) && w == copy.Length;
        }
        finally { NativeMethods.VirtualProtectEx(handle, address, bytes.Length, old, out _); }
    }

    // ──────────────────────────────────────────────────────────
    //  String reads
    // ──────────────────────────────────────────────────────────
    public string ReadAscii(nint address, int maxLength)
    {
        var bytes  = ReadBytes(address, maxLength);
        var length = Array.IndexOf(bytes, (byte)0);
        return bytes.Length == 0 ? string.Empty
            : Encoding.ASCII.GetString(bytes, 0, length < 0 ? bytes.Length : length);
    }

    public nint GetModuleBase(string moduleName)
    {
        if (ProcessId == 0 || string.IsNullOrWhiteSpace(moduleName)) return 0;
        try
        {
            using var process = Process.GetProcessById(ProcessId);
            foreach (ProcessModule module in process.Modules)
                if (string.Equals(module.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
                    return module.BaseAddress;
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        return 0;
    }

    // ──────────────────────────────────────────────────────────
    //  Pattern scan — chunked, avoids loading whole module
    // ──────────────────────────────────────────────────────────
    public nint FindPattern(string signature)
    {
        if (BaseAddress == 0 || ModuleSize == 0) return 0;
        var pattern = signature.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Contains('?') ? -1 : Convert.ToInt32(t, 16)).ToArray();
        if (pattern.Length == 0) return 0;

        const int chunkSize = 0x400000;
        for (var offset = 0; offset < ModuleSize;)
        {
            var length = Math.Min(chunkSize, ModuleSize - offset);
            var bytes  = ReadBytes(BaseAddress + offset, length);
            for (var index = 0; index + pattern.Length <= bytes.Length; index++)
            {
                var found = true;
                for (var part = 0; part < pattern.Length; part++)
                    if (pattern[part] >= 0 && bytes[index + part] != pattern[part]) { found = false; break; }
                if (found) return BaseAddress + offset + index;
            }
            if (length == ModuleSize - offset) break;
            offset += length - pattern.Length + 1;
        }
        return 0;
    }

    // ──────────────────────────────────────────────────────────
    //  Remote thread execution (shellcode)
    // ──────────────────────────────────────────────────────────
    public bool TryExecuteRemote(ReadOnlySpan<byte> payload, uint timeoutMs = 3000)
    {
        if (handle == IntPtr.Zero || payload.Length == 0) return false;
        var remote = NativeMethods.VirtualAllocEx(handle, 0, (nuint)payload.Length,
            NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE, NativeMethods.PAGE_EXECUTE_READWRITE);
        if (remote == 0) return false;
        try
        {
            var bytes = payload.ToArray();
            if (!NativeMethods.WriteProcessMemory(handle, remote, bytes, bytes.Length, out var w) || w != bytes.Length)
                return false;
            var thread = NativeMethods.CreateRemoteThread(handle, 0, 0, remote, 0, 0, out _);
            if (thread == IntPtr.Zero) return false;
            try { return NativeMethods.WaitForSingleObject(thread, timeoutMs) == NativeMethods.WAIT_OBJECT_0; }
            finally { NativeMethods.CloseHandle(thread); }
        }
        finally { NativeMethods.VirtualFreeEx(handle, remote, 0, NativeMethods.MEM_RELEASE); }
    }

    public void Dispose() => Detach();
}

// ──────────────────────────────────────────────────────────────
//  BatchRead — zero-copy field extractor over a shared buffer
// ──────────────────────────────────────────────────────────────
public readonly struct BatchRead
{
    private readonly byte[] _buf;
    private readonly nint   _base;

    internal BatchRead(byte[] buf, nint baseAddress) { _buf = buf; _base = baseAddress; }

    public bool IsValid => _buf.Length > 0;

    /// <summary>Reads an unmanaged value at <paramref name="address"/> from the cached buffer.</summary>
    public T Get<T>(nint address) where T : unmanaged
    {
        var offset = (int)(address - _base);
        var size   = Marshal.SizeOf<T>();
        if (offset < 0 || offset + size > _buf.Length) return default;
        return MemoryMarshal.Read<T>(_buf.AsSpan(offset, size));
    }
}
