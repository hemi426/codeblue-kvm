namespace CodeBlue.Viewer;

/// <summary>A board revision's pins, for the Board settings dialog.</summary>
internal sealed record BoardPreset(string Name, short LedGpio, short VbusGpio)
{
    public override string ToString() => Name;
}

internal static class BoardPresets
{
    /// <summary>The boards CodeBlue knows. Add a row to support another; VbusGpio is the suggested sense pin.</summary>
    public static readonly BoardPreset[] All =
    [
        new("ESP32-S3-DevKitC-1 v1.0", LedGpio: 48, VbusGpio: 21),
        new("ESP32-S3-DevKitC-1 v1.1", LedGpio: 38, VbusGpio: 21),
    ];

    public const string Custom = "Custom";

    /// <summary>The preset these pins match, or null. With no VBUS pin, only the LED has to match.</summary>
    public static BoardPreset? Match(short ledGpio, short vbusGpio) =>
        All.FirstOrDefault(p => p.LedGpio == ledGpio && (vbusGpio < 0 || p.VbusGpio == vbusGpio));
}
