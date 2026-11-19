# Getting started

From a bag of parts to controlling a target. Each step links to the full
details.

## 1. Get the parts

You need four things (see [What to buy](hardware/bom.md) for tested models
and links):

- **An ESP32-S3-DevKitC-1, or a clone,** with two USB ports.
- **An HDMI capture dongle.** The recommended one is the $9 MS2130 stick on a
  short USB 2.0 extension.
- **Two USB data cables** for the board: one to your computer, one to the
  target.
- **An HDMI cable,** plus an adapter if the target has only DisplayPort,
  USB-C, or another video output.

The viewer runs on Windows 10 or 11 (64-bit).

## 2. Flash the firmware

Plug the board's **UART** port into your computer, open
[Flashing](firmware.md#from-your-browser) in Chrome or Edge, and click the
install button. For other browsers, the same page shows how to flash with
esptool instead. You only do this once per board (and again when you want a
newer version).

## 3. Get the viewer

Download the latest viewer release
([Releases](https://github.com/hemi426/codeblue-kvm/releases?q=viewer&expanded=true)).
The portable zip is the easy choice: unzip it anywhere (a USB stick works) and
run `CodeBlue.exe`. Nothing to install, and no admin rights needed.

The viewer isn't code-signed yet, so Windows SmartScreen may warn the first
time: choose **More info**, then **Run anyway**.

## 4. Connect and capture

Plug everything in, pick the board's port and the capture dongle in the
viewer, and click **Connect**: the target's screen appears. Click
**Capture** (or press Ctrl+Alt+C) to send it your keyboard and mouse, and
press Ctrl+Alt to let go. [Connecting](viewer.md#connecting) has the details.

If the board's RGB LED stays dark, or the board has the
[VBUS sense modification](hardware/vbus-sense.md), set it up once in
**Board settings...** (see [Board settings](firmware.md#board-settings)).

From here:

- [Viewer](viewer.md) covers the mouse modes, every shortcut, and what ends
  capture.
- If something doesn't work, see [Troubleshooting](troubleshooting.md).
- Tried CodeBlue on a machine that isn't in
  [Tested targets](hardware/index.md#tested-targets)? Please
  [report it](https://github.com/hemi426/codeblue-kvm/issues/new?template=tested-target.yml).
