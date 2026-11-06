# Hardware

Planned contents:

- `case/`: FreeCAD source, STL and STEP exports, and printing notes for a slim,
  header-free ESP32-S3-DevKitC-1 case.
- `BOM.md`: tested boards, capture dongles, cables and adapters.
- `labels/`: label text for the case and the HOST/TARGET cables.

## Tested so far

| Part | Notes |
|------|-------|
| ESP32-S3-DevKitC-1 clone, N8R2 | RGB LED on GPIO48. Leave the IN-OUT and USB-OTG solder pads open. |
| USB 2.0 HDMI capture dongle ("USB Video") | Needs Format = MJPG for usable 1080p. |
| USB 3.0 HDMI capture dongle | Works with uncompressed formats too. |
