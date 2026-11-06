using System.ComponentModel;
using System.Runtime.InteropServices;
using static CodeBlue.Viewer.Native.NativeMethods;

namespace CodeBlue.Viewer.Input;

internal enum TargetMouseButton { Left, Right, Middle }

/// <summary>One decoded raw mouse packet.</summary>
internal readonly record struct RawMouseEvent(int DeltaX, int DeltaY, ushort ButtonFlags, short WheelDelta);

/// <summary>
/// Raw Input registration for the mouse. Raw input reports device-level motion
/// (no Windows pointer acceleration), which is what a relative HID mouse on the
/// target should receive: the target applies its own acceleration.
/// </summary>
internal static class RawMouse
{
    private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    private const ushort HID_USAGE_GENERIC_MOUSE = 0x02;

    public static void Register(IntPtr hwnd) => Set(0, hwnd);

    public static void Unregister() => Set(RIDEV_REMOVE, IntPtr.Zero);

    private static void Set(uint flags, IntPtr hwnd)
    {
        var dev = new[]
        {
            new RAWINPUTDEVICE
            {
                usUsagePage = HID_USAGE_PAGE_GENERIC,
                usUsage = HID_USAGE_GENERIC_MOUSE,
                dwFlags = flags,
                hwndTarget = hwnd,
            },
        };
        if (!RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Raw input registration failed.");
    }

    /// <summary>
    /// Decodes a WM_INPUT message. Returns null for non-mouse input and for
    /// absolute-mode devices (pen tablets, remote desktop), which v1 ignores.
    /// </summary>
    public static RawMouseEvent? Decode(IntPtr lParam)
    {
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0) return null;

        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buf, ref size, headerSize) != size) return null;

            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buf);
            if (header.dwType != RIM_TYPEMOUSE) return null;

            // RAWMOUSE layout, offsets relative to the end of the header:
            //   0  usFlags        (USHORT)
            //   4  usButtonFlags  (USHORT, low half of the ulButtons union)
            //   6  usButtonData   (USHORT, wheel delta)
            //   8  ulRawButtons   (ULONG)
            //  12  lLastX         (LONG)
            //  16  lLastY         (LONG)
            int o = (int)headerSize;
            ushort usFlags = (ushort)Marshal.ReadInt16(buf, o + 0);
            if ((usFlags & MOUSE_MOVE_ABSOLUTE) != 0) return null;

            ushort buttonFlags = (ushort)Marshal.ReadInt16(buf, o + 4);
            short buttonData = Marshal.ReadInt16(buf, o + 6);
            int dx = Marshal.ReadInt32(buf, o + 12);
            int dy = Marshal.ReadInt32(buf, o + 16);

            short wheel = (buttonFlags & RI_MOUSE_WHEEL) != 0 ? buttonData : (short)0;
            return new RawMouseEvent(dx, dy, buttonFlags, wheel);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }
}
