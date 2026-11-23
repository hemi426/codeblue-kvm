# Changelog

The firmware and the viewer are released separately, each with its own
version. Any firmware and viewer that speak the same protocol version work
together (the handshake checks it); every release so far speaks protocol 1.
Newer features need both sides to have them: Board settings need firmware
0.3.0 or later, and keep awake needs firmware 0.5.0.

## Viewer

### [0.5.0](https://github.com/hemi426/codeblue-kvm/releases/tag/viewer-v0.5.0) (2026-11-22)

- **Keep awake:** a toolbar button that keeps the target from sleeping or
  locking while it's idle, with its settings under the **▾** beside it and
  its state in the status bar (firmware 0.5.0 or later).

### [0.4.0](https://github.com/hemi426/codeblue-kvm/releases/tag/viewer-v0.4.0) (2026-11-19)

- **Board settings...:** set the board's LED pin, LED brightness, and VBUS
  sense pin from the viewer, starting from a board preset (firmware 0.3.0 or
  later; the brightness works with any firmware).
- **Viewer options...:** the video format, size, frame rate, and buffering,
  the mouse mode, and 1:1 pixels, moved off the toolbar.
- A new status bar: the dongle's frame rate (or **No video**), the mouse
  mode in use and why, and the target's Num, Caps, and Scroll Lock.
- **Mouse: Auto** also switches to relative when the target isn't reading
  the absolute pointer (firmware 0.3.0 or later).
- Fixed: a dongle that stops sending video no longer freezes the viewer on
  Disconnect or when closing. The status bar says to replug the dongle if
  it's stuck.
- Snapshot messages clear after a few seconds.
- **Cache ms** now defaults to 40 for new installs.
- The video device list leaves out unplugged devices.

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

### [0.5.0](https://github.com/hemi426/codeblue-kvm/releases/tag/firmware-v0.5.0) (2026-11-22)

- **Keep awake** (protocol 1.2): after a set time without input, the board
  makes a small, harmless input (a hidden key, a nudge, a glide, a scroll
  notch, or a mix) at random intervals, so the target doesn't sleep or lock.
  It runs on the board, with or without the viewer, never wakes a sleeping
  target, and is off whenever the board starts. Its settings are saved on
  the board. While it's on, the status LED shows a white heartbeat, faster
  while it's acting.
- `tools/cb_test.py`: `keep-awake on|off`, and the keep awake settings.

### [0.4.1](https://github.com/hemi426/codeblue-kvm/releases/tag/firmware-v0.4.1) (2026-11-21)

- Fixed: going straight from firmware setup screens (which don't read the
  absolute pointer) into an operating system left the board reporting
  `abs_unread`, so the viewer's **Mouse: Auto** stayed on relative. The
  board now starts afresh each time the target enumerates it.

### [0.4.0](https://github.com/hemi426/codeblue-kvm/releases/tag/firmware-v0.4.0) (2026-11-19)

- Updates keep the board's settings: releases now include the bootloader,
  partition table, and app as separate images, and the browser installer
  writes those, leaving the settings alone unless you choose to erase. That
  includes the update from 0.3.0. The whole image is still there for a
  clean install.

### [0.3.0](https://github.com/hemi426/codeblue-kvm/releases/tag/firmware-v0.3.0) (2026-11-19)

- Board settings (protocol 1.1): the status LED pin and the VBUS sense pin
  can be set from the viewer, or with `tools/cb_test.py`, and the board
  saves them. Pins the board can't use are refused.
- One release image for every supported board, replacing the two board
  presets.
- The target status reports when the target isn't reading the absolute
  pointer (as some firmware setup screens don't), so the viewer's automatic
  mouse mode can switch to relative.

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
