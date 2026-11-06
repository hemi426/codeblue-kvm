namespace CodeBlue.Viewer.Input;

/// <summary>
/// Maps physical keys (PC/AT set 1 scan codes, as reported by the low-level hook)
/// to USB HID Keyboard/Keypad page (0x07) usage IDs.
///
/// Mapping from scan codes rather than virtual-key codes makes this layout
/// independent: we send which physical key was pressed, and the target's own
/// keyboard layout decides what character it produces, exactly as with a real
/// keyboard. Reference: Microsoft "Keyboard Scan Code Specification" and the
/// USB HID Usage Tables, section 10.
/// </summary>
internal static class HidUsageMap
{
    // Modifier usages
    public const byte LeftCtrl = 0xE0, LeftShift = 0xE1, LeftAlt = 0xE2, LeftGui = 0xE3;
    public const byte RightCtrl = 0xE4, RightShift = 0xE5, RightAlt = 0xE6, RightGui = 0xE7;
    public const byte Delete = 0x4C, Enter = 0x28;

    private const int Ext = 0x100; // marks an E0-prefixed (extended) scan code

    private static readonly Dictionary<int, byte> ScanToUsage = new()
    {
        [0x01] = 0x29, // Esc
        [0x02] = 0x1E, [0x03] = 0x1F, [0x04] = 0x20, [0x05] = 0x21, [0x06] = 0x22, // 1-5
        [0x07] = 0x23, [0x08] = 0x24, [0x09] = 0x25, [0x0A] = 0x26, [0x0B] = 0x27, // 6-0
        [0x0C] = 0x2D, // -
        [0x0D] = 0x2E, // =
        [0x0E] = 0x2A, // Backspace
        [0x0F] = 0x2B, // Tab
        [0x10] = 0x14, [0x11] = 0x1A, [0x12] = 0x08, [0x13] = 0x15, [0x14] = 0x17, // Q W E R T
        [0x15] = 0x1C, [0x16] = 0x18, [0x17] = 0x0C, [0x18] = 0x12, [0x19] = 0x13, // Y U I O P
        [0x1A] = 0x2F, // [
        [0x1B] = 0x30, // ]
        [0x1C] = 0x28, // Enter
        [0x1D] = LeftCtrl,
        [0x1E] = 0x04, [0x1F] = 0x16, [0x20] = 0x07, [0x21] = 0x09, [0x22] = 0x0A, // A S D F G
        [0x23] = 0x0B, [0x24] = 0x0D, [0x25] = 0x0E, [0x26] = 0x0F,               // H J K L
        [0x27] = 0x33, // ;
        [0x28] = 0x34, // '
        [0x29] = 0x35, // `
        [0x2A] = LeftShift,
        [0x2B] = 0x31, // backslash
        [0x2C] = 0x1D, [0x2D] = 0x1B, [0x2E] = 0x06, [0x2F] = 0x19, [0x30] = 0x05, // Z X C V B
        [0x31] = 0x11, [0x32] = 0x10,                                             // N M
        [0x33] = 0x36, // ,
        [0x34] = 0x37, // .
        [0x35] = 0x38, // /
        [0x36] = RightShift,
        [0x37] = 0x55, // Keypad *
        [0x38] = LeftAlt,
        [0x39] = 0x2C, // Space
        [0x3A] = 0x39, // Caps Lock
        [0x3B] = 0x3A, [0x3C] = 0x3B, [0x3D] = 0x3C, [0x3E] = 0x3D, [0x3F] = 0x3E, // F1-F5
        [0x40] = 0x3F, [0x41] = 0x40, [0x42] = 0x41, [0x43] = 0x42, [0x44] = 0x43, // F6-F10
        [0x46] = 0x47, // Scroll Lock
        [0x47] = 0x5F, [0x48] = 0x60, [0x49] = 0x61, // Keypad 7 8 9
        [0x4A] = 0x56, // Keypad -
        [0x4B] = 0x5C, [0x4C] = 0x5D, [0x4D] = 0x5E, // Keypad 4 5 6
        [0x4E] = 0x57, // Keypad +
        [0x4F] = 0x59, [0x50] = 0x5A, [0x51] = 0x5B, // Keypad 1 2 3
        [0x52] = 0x62, // Keypad 0
        [0x53] = 0x63, // Keypad .
        [0x56] = 0x64, // Non-US backslash (ISO key left of Z)
        [0x57] = 0x44, // F11
        [0x58] = 0x45, // F12
        [0x64] = 0x68, [0x65] = 0x69, [0x66] = 0x6A, [0x67] = 0x6B, // F13-F16
        [0x68] = 0x6C, [0x69] = 0x6D, [0x6A] = 0x6E, [0x6B] = 0x6F, // F17-F20
        [0x6C] = 0x70, [0x6D] = 0x71, [0x6E] = 0x72, [0x76] = 0x73, // F21-F24
        [0x70] = 0x88, // Katakana/Hiragana
        [0x73] = 0x87, // Ro
        [0x79] = 0x8A, // Henkan
        [0x7B] = 0x8B, // Muhenkan
        [0x7D] = 0x89, // Yen

        [Ext | 0x1C] = 0x58, // Keypad Enter
        [Ext | 0x1D] = RightCtrl,
        [Ext | 0x35] = 0x54, // Keypad /
        [Ext | 0x37] = 0x46, // Print Screen
        [Ext | 0x38] = RightAlt,
        [Ext | 0x46] = 0x48, // Ctrl+Pause (Break)
        [Ext | 0x47] = 0x4A, // Home
        [Ext | 0x48] = 0x52, // Up
        [Ext | 0x49] = 0x4B, // Page Up
        [Ext | 0x4B] = 0x50, // Left
        [Ext | 0x4D] = 0x4F, // Right
        [Ext | 0x4F] = 0x4D, // End
        [Ext | 0x50] = 0x51, // Down
        [Ext | 0x51] = 0x4E, // Page Down
        [Ext | 0x52] = 0x49, // Insert
        [Ext | 0x53] = 0x4C, // Delete
        [Ext | 0x5B] = LeftGui,
        [Ext | 0x5C] = RightGui,
        [Ext | 0x5D] = 0x65, // Application (Menu)
    };

    // Keys whose scan code and extended flag are unreliable through the
    // low-level hook (Pause and Num Lock share scan 0x45, for example), so
    // the virtual-key code decides instead.
    private static readonly Dictionary<uint, byte> VirtualKeyOverrides = new()
    {
        [0x13] = 0x48,       // VK_PAUSE
        [0x90] = 0x53,       // VK_NUMLOCK
        [0x2C] = 0x46,       // VK_SNAPSHOT (Print Screen)
        [0xA0] = LeftShift,  // VK_LSHIFT
        [0xA1] = RightShift, // VK_RSHIFT
        [0xA2] = LeftCtrl,   // VK_LCONTROL
        [0xA3] = RightCtrl,  // VK_RCONTROL
        [0xA4] = LeftAlt,    // VK_LMENU
        [0xA5] = RightAlt,   // VK_RMENU
        [0x5B] = LeftGui,    // VK_LWIN
        [0x5C] = RightGui,   // VK_RWIN
        [0x5D] = 0x65,       // VK_APPS
    };

    /// <summary>Returns the HID usage for a key event, or 0 if it should be ignored.</summary>
    public static byte Map(KeyEvent ev)
    {
        // AltGr on European layouts makes Windows synthesize a fake Left Ctrl
        // with scan code 0x21D. The real Right Alt still arrives; drop the fake.
        if ((ev.ScanCode & 0xFFFFFF00) != 0) return 0;

        if (VirtualKeyOverrides.TryGetValue(ev.VirtualKey, out byte vkUsage))
            return vkUsage;

        int key = (int)ev.ScanCode | (ev.Extended ? Ext : 0);
        return ScanToUsage.TryGetValue(key, out byte usage) ? usage : (byte)0;
    }

    public static bool IsCtrl(byte usage) => usage is LeftCtrl or RightCtrl;
    public static bool IsAlt(byte usage) => usage is LeftAlt or RightAlt;
}
