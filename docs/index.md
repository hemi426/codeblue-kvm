# CodeBlue

A pocket crash cart: control a headless or misbehaving PC from your computer,
BIOS screens included, using an ESP32-S3 dev board and a cheap HDMI-to-USB
capture dongle.

[Install the firmware](firmware.md#flashing){: .btn .btn-primary }
[Download the viewer](https://github.com/hemi426/codeblue-kvm/releases?q=viewer&expanded=true){: .btn }

```
 your computer                                                       target PC
┌─────────────┐  USB (serial)  ┌──────────┐  USB (keyboard + mouse) ┌──────────┐
│   CodeBlue  │◄──────────────►│ ESP32-S3 │────────────────────────►│ USB port │
│    viewer   │                └──────────┘                         │          │
│             │  USB (video)   ┌──────────┐  HDMI                   │          │
│             │◄───────────────│ capture  │◄────────────────────────│ HDMI out │
└─────────────┘                │  dongle  │                         └──────────┘
                               └──────────┘
```

The target sees an ordinary USB keyboard and mouse and an ordinary monitor; it
needs no software and no network.

## What works

- **Viewer** (Windows): low-latency video, keyboard and mouse capture with an
  absolute or relative pointer, full screen, snapshots, Ctrl+Alt+Del, the
  target's status (connected, asleep, Caps Lock), and automatic
  reconnection if the board drops out.
- **Firmware** (ESP-IDF): a keyboard and a relative mouse that both work in
  BIOS setup screens (boot protocol), plus an absolute pointer, for the
  target; and a binary protocol with heartbeats both ways, so a crashed viewer
  or pulled cable never leaves a key held down.

## Where to go next

- **[Getting started](getting-started.md):** parts, flashing, the viewer, and
  your first capture.
- **[Viewer](viewer.md):** controls, mouse modes, keys, and when capture ends.
- **[Firmware](firmware.md):** flashing, board presets, the status LED, and
  testing a board.
- **[Hardware](hardware/index.md):** what to buy, tested targets, and the
  optional VBUS sense modification.
- **[Troubleshooting](troubleshooting.md):** when something doesn't work.
- **[Development](development/index.md):** building from source, and the
  protocol.

## Credits

Inspired by [KeyboardAndMouseControl](https://github.com/alpharesearch/KeyboardAndMouseControl)
by Markus Schulz. CodeBlue is an independent implementation with its own
firmware and protocol.

## License

Software: GPL-3.0-or-later. Hardware design files: license to be decided
(likely CERN-OHL-S or CC BY-SA 4.0) and noted alongside them when they arrive.
