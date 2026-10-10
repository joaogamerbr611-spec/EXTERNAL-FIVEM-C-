using System.Runtime.InteropServices;

namespace FiveMExternal;

internal static class NativeMethods
{
    internal const uint PROCESS_VM_OPERATION    = 0x0008;
    internal const uint PROCESS_CREATE_THREAD   = 0x0002;
    internal const uint PROCESS_VM_READ         = 0x0010;
    internal const uint PROCESS_VM_WRITE        = 0x0020;
    internal const uint PROCESS_QUERY_INFORMATION = 0x0400;

    internal const int VK_INSERT  = 0x2D;
    internal const int VK_RBUTTON = 0x02;
    internal const int VK_LBUTTON = 0x01;
    internal const int VK_SHIFT   = 0x10;
    internal const int VK_F1      = 0x70;
    internal const int VK_F2      = 0x71;
    internal const int VK_F3      = 0x72;
    internal const int VK_F4      = 0x73;
    internal const int VK_DELETE  = 0x2E;
    internal const int VK_END     = 0x23;
    internal const int VK_HOME    = 0x24;
    internal const int VK_PRIOR   = 0x21;  // Page Up
    internal const int VK_NEXT    = 0x22;  // Page Down

    internal const uint PAGE_EXECUTE_READWRITE = 0x40;
    internal const uint MEM_COMMIT   = 0x1000;
    internal const uint MEM_RESERVE  = 0x2000;
    internal const uint MEM_RELEASE  = 0x8000;
    internal const uint WAIT_OBJECT_0 = 0;

    internal const int GWL_EXSTYLE      = -20;
    internal const int WS_EX_TRANSPARENT = 0x20;
    internal const int WS_EX_LAYERED     = 0x80000;
    internal const int WS_EX_TOOLWINDOW  = 0x80;
    internal const int WS_EX_TOPMOST     = 0x8;
    internal const int WM_NCHITTEST      = 0x84;
    internal const int HTTRANSPARENT     = -1;
    internal const uint WDA_NONE                 = 0x00000000;
    internal const uint WDA_EXCLUDEFROMCAPTURE   = 0x00000011;

    // mouse_event flags
    internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    internal const uint MOUSEEVENTF_LEFTUP   = 0x0004;

    [DllImport("user32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    internal static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    internal static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    internal static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool ReadProcessMemory(IntPtr process, nint address, byte[] buffer, nint size, out nint read);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool WriteProcessMemory(IntPtr process, nint address, byte[] buffer, nint size, out nint written);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool VirtualProtectEx(IntPtr process, nint address, nint size, uint newProtect, out uint oldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint VirtualAllocEx(IntPtr process, nint address, nuint size, uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool VirtualFreeEx(IntPtr process, nint address, nuint size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr CreateRemoteThread(IntPtr process, nint attributes, nuint stackSize, nint startAddress, nint parameter, uint creationFlags, out uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    internal static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    internal static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

    internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    internal static void SimulateMouseEvent(uint flags) =>
        mouse_event(flags, 0, 0, 0, UIntPtr.Zero);

    internal static bool IsKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT  { public int Left; public int Top; public int Right; public int Bottom; }
}
