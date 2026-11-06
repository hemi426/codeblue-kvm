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
| USB 3.0 HDMI capture dongle | Works with uncompressed formats too. |
