# Hardware

- **[What to buy](bom.md):** boards, capture dongles, cables, and video
  adapters, which of them have been tested, and how to check a dongle before
  relying on it.
- **VBUS sense modification** (optional): two resistors, so the board can
  tell a sleeping target from an unplugged one. See
  [VBUS sense](vbus-sense.md).
- **Experiments** (unsupported, at your own risk): see
  [Experiments](../experiments/index.md), such as patching an MS2130 stick
  for MJPG on USB 3.

Planned: a slim, header-free case for the ESP32-S3-DevKitC-1 (FreeCAD source,
STL and STEP exports, and printing notes), and label text for the case and
the HOST/TARGET cables. Those are design files rather than docs, so they'll
live in a `hardware/` folder at the repo root.

## Tested targets

Tried CodeBlue on a machine that isn't listed, or found one that misbehaves?
Please report it with the
[Tested target](https://github.com/hemi426/codeblue-kvm/issues/new?template=tested-target.yml)
issue form on GitHub, so it can be added here.

What the target's firmware or OS accepted from the CodeBlue firmware
(`firmware/`, protocol v1). "Report" or "boot" is the protocol the target put
the keyboard in, as shown by `tools/cb_test.py`.

| Target | Keyboard | Absolute pointer | Relative mouse | Notes |
|--------|----------|------------------|----------------|-------|
| Dell OptiPlex 5080, BIOS setup | Works (report) | Works | Works | F2 during POST enters setup. Num Lock LED reported. |
| Dell OptiPlex 5080, SupportAssist On-board Diagnostics | Works (report) | Ignored | Works (report) | Leaves the mouse in report protocol rather than asking for the boot mouse, so **Mouse: Auto** stays on absolute and the pointer doesn't move; choose **Relative**. The mouse is off while a test runs (the tool says so). Num Lock LED reported. |
| FreeBSD (console) | Works (report) | Enumerates (hms, "Tablet") | Enumerates (hms) | Caps Lock LED reported. |
| Dell Precision 7680, Windows | Works | Works | Not tested | Video over USB-C (Thunderbolt 4) through a USB-C to HDMI adapter. Lock screen, sign-in, and desktop control all worked. The viewer's Ctrl+Alt+Del button needs viewer 0.3.2 or later (earlier versions sent it too fast to register). |
