using System.ComponentModel;
using System.Runtime.InteropServices;
using static CodeBlue.Viewer.Native.NativeMethods;

namespace CodeBlue.Viewer.Input;

/// <summary>A raw keyboard event as seen by the low-level hook.</summary>
internal readonly record struct KeyEvent(uint VirtualKey, uint ScanCode, bool Extended, bool Up, bool Injected);

/// <summary>
/// Global low-level keyboard hook. Installed only while input is captured, so
/// keys like Win, Alt+Tab and Alt+F4 go to the target instead of the local desktop.
/// The handler runs on the UI thread and must return quickly: return true to
/// swallow the key locally, false to let Windows handle it.
/// Ctrl+Alt+Del can never be intercepted; Windows reserves it.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private readonly Func<KeyEvent, bool> _handler;
    private readonly LowLevelKeyboardProc _proc; // held so the GC can't collect the delegate
    private IntPtr _hook;

    public KeyboardHook(Func<KeyEvent, bool> handler)
    {
        _handler = handler;
        _proc = HookProc;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the keyboard hook.");
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HC_ACTION)
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var ev = new KeyEvent(
                k.vkCode,
                k.scanCode,
                (k.flags & LLKHF_EXTENDED) != 0,
                (k.flags & LLKHF_UP) != 0,
                (k.flags & LLKHF_INJECTED) != 0);

            bool swallow;
            try { swallow = _handler(ev); }
            catch { swallow = false; } // never let an exception escape into the hook chain

            if (swallow) return 1;
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
