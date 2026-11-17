# Viewer

The CodeBlue viewer is a Windows app (Windows 10 or 11, 64-bit) that shows the
target's screen and sends it your keyboard and mouse. To get it, see
[Getting started](getting-started.md).

## Connecting

1. Plug the ESP32's **UART** port into your computer and its **native USB** port
   into the target. Plug the capture dongle's HDMI into the target and its USB
   into your computer.
2. Pick the board's serial port and the video device, then **Connect**. The
   picture appears straight away, but nothing you type or click reaches the
   target yet, so it's safe to just watch. (Tick **Video only** to watch a
   screen without a board.)
3. Click **Capture** (or press Ctrl+Alt+C) to send keyboard and mouse to the
   target. An amber banner shows while they are captured.

If the board's cable is pulled or the board stops answering, the viewer
releases capture, keeps the video running, and reconnects by itself when the
board is back.

## Controls

| Control | What it does |
|---------|--------------|
| **Port** | The board's serial port (its UART port). **Refresh** rescans ports and video devices. |
| **Video only** | Shows the video without a board, to just watch a screen. |
| **Video** | The capture dongle. |
| **Format, Size, FPS** | The mode to ask the dongle for. Use MJPG; when in doubt, set FPS to **Auto** (see [Troubleshooting](troubleshooting.md)). |
| **Cache ms** | How much video libVLC buffers. Around 50 keeps lag low. |
| **LED %** | The board's status LED brightness, saved on the board itself. |
| **Mouse** | How the pointer reaches the target (see below). |
| **1:1 pixels** | Shows the capture unscaled, for the sharpest text. |
| **Capture** | Sends keyboard and mouse to the target. |
| **Ctrl+Alt+Del** | Sends Ctrl+Alt+Del to the target (you can't type it; see below). |
| **Snapshot** | Saves the current frame to Pictures\CodeBlue. |
| **Full screen** | Fills the screen with the target. |

The video settings are locked while connected; disconnect to change them.
The viewer remembers your choices in `%APPDATA%\CodeBlue\viewer.json`.

## Mouse

**Mouse** sets how the pointer reaches the target:

- **Auto** (default): absolute, switching to relative when the target asks
  for a basic "boot" mouse, as some BIOS setup screens do.
- **Absolute:** the target's pointer goes exactly where yours is on the
  picture, with no fighting the target's mouse acceleration.
- **Relative:** an ordinary mouse. Use it for targets with more than one
  monitor, where absolute positions span the whole desktop.

## Keys

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
reach the target. Ctrl+Alt+Del and Win+L can't be captured (Windows reserves
them): pressing either one ends capture, and neither reaches the target. Use
the **Ctrl+Alt+Del** button to send one to the target.

Compact keyboards (many Keychron layouts, for example) have no Menu key, which
is why Ctrl+Alt is the main release. To use the Menu key anyway, map a spare
key to the App/Menu key code (`KC_APP`) in your keyboard's configuration tool.

## When capture ends

Capture ends only when you ask, or when your keystrokes might go somewhere
you can't see:

- **You release it:** Ctrl+Alt (or the Menu key), **Disconnect**, or closing
  the viewer.
- **The viewer loses focus:** a UAC prompt, a notification you click,
  Ctrl+Alt+Del or Win+L, or another program taking the foreground.
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
