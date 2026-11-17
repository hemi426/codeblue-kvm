# Changelog

The firmware and the viewer are released separately, each with its own
version. Any firmware and viewer that speak the same protocol version work
together (the handshake checks it); every release so far speaks protocol 1.

## Viewer

### [0.3.3](https://github.com/hemi426/codeblue-kvm/releases/tag/viewer-v0.3.3) (2026-11-16)

- Fixed: Ctrl and Alt could stay stuck on your own computer after capturing
  with Ctrl+Alt+C.

### [0.3.2](https://github.com/hemi426/codeblue-kvm/releases/tag/viewer-v0.3.2) (2026-11-16)

- Fixed: the **Ctrl+Alt+Del** button now reaches the target reliably
  (earlier versions sent it too fast for some targets to register).
- The version is shown in the title bar.

### [0.3.1](https://github.com/hemi426/codeblue-kvm/releases/tag/viewer-v0.3.1) (2026-11-11)

- Fixed: pressing Ctrl+Alt+Del or Win+L on your own computer no longer
  reaches the target. Either one now ends capture.

### [0.3.0](https://github.com/hemi426/codeblue-kvm/releases/tag/viewer-v0.3.0) (2026-11-10)

- With a board that has the VBUS sense modification (firmware 0.2.0 or
  later), the status bar tells a sleeping target from an unplugged or
  powered-off one.

### [0.2.0](https://github.com/hemi426/codeblue-kvm/releases/tag/viewer-v0.2.0) (2026-11-09)

The first release:

- Low-latency video from a UVC capture dongle (libVLC over DirectShow), with
  format, size, frame rate, and buffering settings, square pixels, a 1:1
  view, full screen, and snapshots.
- Keyboard and mouse capture over the CodeBlue binary protocol, with
  absolute, relative, and automatic mouse modes, and a Ctrl+Alt+Del button.
- The target's status in the status bar: connected, asleep, and Caps Lock.
- Capture survives target reboots, and ends safely when the viewer loses
  focus or the board is lost; the viewer reconnects to the board by itself.
- **Video only**, to watch a screen without a board.
- Two downloads: a portable zip that includes .NET, and a small one that
  needs the .NET 10 Desktop Runtime. Both ship only the parts of libVLC the
  viewer uses.

## Firmware

### [0.2.0](https://github.com/hemi426/codeblue-kvm/releases/tag/firmware-v0.2.0) (2026-11-10)

- Optional VBUS sense: on a board with the modification, the firmware
  reports whether the target's USB port has power, so the viewer can tell a
  sleeping target from an unplugged one. Set the sense pin with
  **CodeBlue → VBUS sense GPIO** in `idf.py menuconfig`; the release images
  leave it off.

### [0.1.1](https://github.com/hemi426/codeblue-kvm/releases/tag/firmware-v0.1.1) (2026-11-09)

The first release:

- A composite USB device for the target: a keyboard and a relative mouse that
  both support the boot protocol (so BIOS setup screens work), plus an
  absolute pointer.
- The CodeBlue binary protocol, version 1: framed and checksummed messages,
  heartbeats both ways, and every key and button released if the viewer goes
  quiet.
- Target status reports: connected, suspended, boot protocol in use, remote
  wakeup, and the keyboard LEDs.
- An RGB status LED whose brightness the viewer can set and the board saves.
- Images for two board presets: `esp32-s3-devkitc-1-v1.0` (most boards) and
  `esp32-s3-devkitc-1-v1.1`.
