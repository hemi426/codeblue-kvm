# CodeBlue

A pocket crash cart: control a headless or misbehaving PC from your laptop, BIOS
screens included, using an ESP32-S3 dev board and a cheap HDMI-to-USB capture
dongle.

```
 your laptop                                   target PC
 ┌─────────────┐   USB (serial)   ┌──────────┐   USB (keyboard + mouse)
 │  CodeBlue   │◄────────────────►│ ESP32-S3 │──────────────────────────►┐
 │   viewer    │                  └──────────┘                           │
 │             │   USB (video)    ┌──────────┐   HDMI                    │
 │             │◄─────────────────│ capture  │◄──────────────────────────┤
 └─────────────┘                  │  dongle  │                           │
                                  └──────────┘                     ┌─────┴─────┐
                                                                   │  target   │
                                                                   └───────────┘
```

The target sees an ordinary USB keyboard and mouse and an ordinary monitor; it
needs no software and no network.

## Status

Early. What works today:

- **Viewer** (Windows): low-latency video, keyboard and mouse capture, full
  screen, snapshots, Ctrl+Alt+Del. Talks to the original Arduino firmware
  through the deprecated legacy protocol.
- **Firmware**: an ESP-IDF rewrite with a binary protocol is in progress. Until
  it lands, use the Arduino sketch from the project that inspired this one
  (see Credits).

## Repository layout

| Path        | Contents |
|-------------|----------|
| `viewer/`   | Windows app (.NET 10, WinForms, LibVLCSharp) |
| `firmware/` | ESP32-S3 firmware (ESP-IDF, C), in progress |
| `scripts/`  | Bootstrap scripts that check for and install build tools |
| `docs/`     | Build instructions, protocol spec, hardware notes |
| `hardware/` | Case design and BOM (planned) |

## Quick start (viewer)

```powershell
git clone https://github.com/<you>/codeblue-kvm.git
cd codeblue-kvm
.\scripts\bootstrap-windows.ps1          # checks first, asks before installing
dotnet run --project viewer\CodeBlue.Viewer
```

See [docs/BUILDING.md](docs/BUILDING.md) for details.

## Using the viewer

1. Plug the ESP32's **UART** port into your laptop and its **native USB** port
   into the target. Plug the capture dongle's HDMI into the target and its USB
   into your laptop (a USB 3 port if the dongle supports it).
2. Pick the serial port, the protocol, and the video device, then **Connect**.
3. Click **Capture** to send keyboard and mouse to the target. An amber
   banner shows while they are captured.

| Key | Action |
|-----|--------|
| Ctrl+Alt (press and release, nothing else) | Release keyboard and mouse |
| Menu key (next to right Ctrl) | Release keyboard and mouse |
| Ctrl+Alt+Enter (while captured) | Toggle full screen |
| Ctrl+Alt+1 | Toggle 1:1 pixels (sharpest text; no scaling) |
| F11 (not captured) | Toggle full screen |
| Esc (full screen, not captured) | Leave full screen |
| F12 (not captured) | Save a snapshot to Pictures\CodeBlue |

Ctrl+Alt+Del can't be captured (Windows reserves it), so it has its own button.

**Video lag?** Keep **Format** on MJPG and **Cache ms** around 50. Most USB 2.0
dongles can only deliver 1080p at full frame rate as MJPEG.

## Credits

Inspired by [KeyboardAndMouseControl](https://github.com/alpharesearch/KeyboardAndMouseControl)
by Markus Schulz, whose serial protocol the viewer still speaks for
compatibility. CodeBlue is an independent implementation.

## License

Software: GPL-3.0-or-later. Hardware design files: license to be decided
(likely CERN-OHL-S or CC BY-SA 4.0) and noted in `hardware/` when they arrive.
