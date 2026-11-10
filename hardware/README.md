# Hardware

**What to buy:** see [BOM.md](BOM.md) for boards, capture dongles, cables and
video adapters, which of them have been tested, and how to check a dongle
before relying on it.

Planned contents:

- `case/`: FreeCAD source, STL and STEP exports, and printing notes for a slim,
  header-free ESP32-S3-DevKitC-1 case.
- `labels/`: label text for the case and the HOST/TARGET cables.
- **VBUS sense modification** (optional): two resistors, so the board can
  tell a sleeping target from an unplugged one. See
  [VBUS-SENSE.md](VBUS-SENSE.md).

## Tested targets

Tried CodeBlue on a machine that isn't listed, or found one that misbehaves?
Please report it with the **Tested target** issue form on GitHub, so it can
be added here.

What the target's firmware or OS accepted from the CodeBlue firmware
(`firmware/`, protocol v1). "Report" or "boot" is the protocol the target put
the keyboard in, as shown by `tools/cb_test.py`.

| Target | Keyboard | Absolute pointer | Relative mouse | Notes |
|--------|----------|------------------|----------------|-------|
| Dell OptiPlex 5080, BIOS setup | Works (report) | Works | Works | F2 during POST enters setup. Num Lock LED reported. |
| FreeBSD (console) | Works (report) | Enumerates (hms, "Tablet") | Enumerates (hms) | Caps Lock LED reported. |
