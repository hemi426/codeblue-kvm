<img src="assets/codeblue.svg" alt="" width="72" align="right">

# CodeBlue

A pocket crash cart: control a headless or misbehaving PC from your computer, BIOS
screens included, using an ESP32-S3 dev board and a cheap HDMI-to-USB capture
dongle.

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

## Status

Early. What works today:

- **Viewer** (Windows): low-latency video, keyboard and mouse capture with an
  absolute or relative pointer (see **Mouse** below), full screen, snapshots,
  Ctrl+Alt+Del, the target's status (connected, asleep, Caps Lock), and
  automatic reconnection if the board drops out.
- **Firmware** (ESP-IDF): a keyboard and a relative mouse that both work in
  BIOS setup screens (boot protocol), plus an absolute pointer, for the
  target; and a binary protocol with heartbeats both ways, so a crashed viewer
  or pulled cable never leaves a key held down. See
  [firmware/README.md](firmware/README.md).

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
git clone https://github.com/hemi426/codeblue-kvm.git
cd codeblue-kvm
.\scripts\bootstrap-windows.ps1          # checks first, asks before installing
dotnet run --project viewer\CodeBlue.Viewer
```

See [docs/BUILDING.md](docs/BUILDING.md) for details.

## Using the viewer

1. Plug the ESP32's **UART** port into your computer and its **native USB** port
   into the target. Plug the capture dongle's HDMI into the target and its USB
   into your computer (a USB 3 port if the dongle supports it).
2. Pick the board's serial port and the video device, then **Connect**. The
   picture appears straight away, but nothing you type or click reaches the
   target yet, so it's safe to just watch. (Tick **Video only** to watch a
   screen without a board.)
3. Click **Capture** (or press Ctrl+Alt+C) to send keyboard and mouse to the
   target. An amber banner shows while they are captured.

**Mouse** sets how the pointer reaches the target (CodeBlue protocol only):

- **Auto** (default): absolute, switching to relative when the target asks
  for a basic "boot" mouse, as some BIOS setup screens do.
- **Absolute:** the target's pointer goes exactly where yours is on the
  picture, with no fighting the target's mouse acceleration.
- **Relative:** an ordinary mouse. Use it for targets with more than one
  monitor, where absolute positions span the whole desktop.

If the board's cable is pulled or the board stops answering, the viewer
releases capture, keeps the video running, and reconnects by itself when the
board is back.

| Key | Action |
|-----|--------|
| Ctrl+Alt+C (not captured) | Capture keyboard and mouse |
| Ctrl+Alt (press and release, nothing else) | Release keyboard and mouse |
| Menu key, if your keyboard has one | Also releases keyboard and mouse |
| Ctrl+Alt+Enter (while captured) | Toggle full screen |
| Ctrl+Alt+1 | Toggle 1:1 pixels (sharpest text; no scaling) |
| F11 (not captured) | Toggle full screen |
| Esc (full screen, not captured) | Leave full screen |
| Ctrl+Alt+S | Save a snapshot to Pictures\CodeBlue (works while captured) |
| F12 (not captured) | Save a snapshot (F12 goes to the target while captured) |

The viewer's own Ctrl+Alt shortcuts (Enter, 1, S, and Ctrl+Alt alone) never
reach the target. Ctrl+Alt+Del can't be captured (Windows reserves it), so it
has its own button.

Compact keyboards (many Keychron layouts, for example) have no Menu key, which
is why Ctrl+Alt is the main release. To use the Menu key anyway, map a spare
key to the App/Menu key code (`KC_APP`) in your keyboard's configuration tool.

### When capture ends

Capture ends only when you ask, or when your keystrokes might go somewhere
you can't see:

- **You release it:** Ctrl+Alt (or the Menu key), **Disconnect**, or closing
  the viewer.
- **The viewer loses focus:** a UAC prompt, a notification you click, Win+L,
  or another program taking the foreground.
- **The board is lost:** it stops answering, or its cable is pulled. The
  viewer reconnects by itself, but waits for you to capture again.

Two safety nets cover a viewer that crashes or hangs. If its window stops
responding for three seconds, your mouse pointer is freed. And if the board
stops hearing from the viewer for half a second, it releases every key and
button on the target, so nothing stays held down.

The target sleeping, rebooting, or dropping the board does **not** end
capture: the status bar shows what the target is doing, and keys you're
holding are sent again when it's back. So you can capture before rebooting
and press the target's setup key (F2, Del, Esc...) at its logo screen.

## Troubleshooting

**Target not responding to keyboard or mouse?** Disconnect in the viewer,
re-plug the ESP32's native USB cable at the target, and reconnect. The status
LED turning green on Connect shows the viewer is reaching the ESP32; if it
does but the target ignores input, the problem is the ESP32-to-target USB
link (cable, port, or the target not having enumerated the device).

**"No CodeBlue firmware found on this port"?** Check you picked the board's
UART port, not its native USB port (the native one shows up on the target,
not on your computer), and that the board has been flashed with the CodeBlue
firmware (see [firmware/README.md](firmware/README.md)).

**Black video?** Make sure no other program (OBS, VLC, the Camera app) has
the capture device open. If it still fails, the status bar names the problem,
and `%LOCALAPPDATA%\CodeBlue\libvlc.log` has the details.

**Video lag?** Keep **Format** on MJPG and **Cache ms** around 50. Many
dongles can only deliver 1080p at full frame rate as MJPEG.

**Blurry or broken-looking text?** Use MJPG rather than YUY2 (see
[hardware/BOM.md](hardware/BOM.md)), and turn on **1:1 pixels**
(Ctrl+Alt+1) when the window or screen is larger than the capture. For a
record of exactly what the target showed, use **Snapshot** (F12): it saves
the captured frame itself, unscaled.

**Can't type in the viewer's own fields while capturing?** That's by design:
release capture first (Ctrl+Alt).

## Credits

Inspired by [KeyboardAndMouseControl](https://github.com/alpharesearch/KeyboardAndMouseControl)
by Markus Schulz. CodeBlue is an independent implementation with its own
firmware and protocol.

## License

Software: GPL-3.0-or-later. Hardware design files: license to be decided
(likely CERN-OHL-S or CC BY-SA 4.0) and noted in `hardware/` when they arrive.
