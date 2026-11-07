# Hardware

Planned contents:

- `case/`: FreeCAD source, STL and STEP exports, and printing notes for a slim,
  header-free ESP32-S3-DevKitC-1 case.
- `BOM.md`: tested boards, capture dongles, cables and adapters.
- `labels/`: label text for the case and the HOST/TARGET cables.
- **VBUS sense modification** (optional): a two-resistor divider from the
  native USB port's VBUS to a spare GPIO, so the board can tell a sleeping
  target from an unplugged one. See "VBUS sense" in
  [../docs/PROTOCOL.md](../docs/PROTOCOL.md). Instructions to follow.

## Tested so far

| Part | Notes |
|------|-------|
| ESP32-S3-DevKitC-1 clone, N8R2 | RGB LED on GPIO48. Leave the IN-OUT and USB-OTG solder pads open. |
| USB 2.0 HDMI capture dongle ("USB Video") | Needs Format = MJPG for usable 1080p. |
| "USB3.0 capture" dongle (sold as USB 3.0) | Behaves as a USB 2 device: YUY2 only reaches 1920x1080 at 10 fps, whatever the port. Use MJPG: 1080p at 30 fps, pixel-perfect at 1:1. |

**Use MJPG for capture.** YUY2 also shows text artifacts in the viewer: libVLC 3
hands packed YUY2 straight to its Direct3D 11 output, which distorts thin
strokes. Snapshots are unaffected, since they skip the display path. The
viewer's renderer can't be changed under libVLC 3 (see the comment in
`MainForm.cs`), so there is no workaround short of libVLC 4.

When testing a new dongle, check its real USB speed with USB Tree View
("SuperSpeed" is USB 3, "High-Speed" is USB 2), and list the modes it offers
from `%LOCALAPPDATA%\CodeBlue\libvlc.log` after connecting:

```powershell
Select-String -Path "$env:LOCALAPPDATA\CodeBlue\libvlc.log" -Pattern 'EnumDeviceCaps' | ForEach-Object Line
```

## Tested targets

What the target's firmware or OS accepted from the CodeBlue firmware
(`firmware/`, protocol v1). "Report" or "boot" is the protocol the target put
the keyboard in, as shown by `tools/cb_test.py`.

| Target | Keyboard | Absolute pointer | Relative mouse | Notes |
|--------|----------|------------------|----------------|-------|
| Dell OptiPlex 5080, BIOS setup | Works (report) | Works | Works | F2 during POST enters setup. Num Lock LED reported. |
| FreeBSD (console) | Works (report) | Enumerates (hms, "Tablet") | Enumerates (hms) | Caps Lock LED reported. |
