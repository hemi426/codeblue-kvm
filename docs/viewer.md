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

## Toolbar

| Control | What it does |
|---------|--------------|
| **Port** | The board's serial port (its UART port). **Refresh** rescans ports and video devices. |
| **Video only** | Shows the video without a board, to just watch a screen. |
| **Video** | The capture dongle. |
| **Capture** | Sends keyboard and mouse to the target. |
| **Ctrl+Alt+Del** | Sends Ctrl+Alt+Del to the target (you can't type it; see below). |
| **Snapshot** | Saves the current frame to Pictures\CodeBlue. |
| **Full screen** | Fills the screen with the target. |
| **Board settings...** | Settings kept on the board (see below). Needs a connected board. |
| **Viewer options...** | The viewer's own settings (see below). |

The viewer remembers your choices in `%APPDATA%\CodeBlue\viewer.json`.

## Viewer options

| Option | What it does |
|--------|--------------|
| **Format, Size, FPS** | The mode to ask the dongle for. Use MJPG; when in doubt, set FPS to **Auto** (see [Troubleshooting](troubleshooting.md)). |
| **Cache ms** | How much video libVLC buffers. The default, 40, keeps lag low. |
| **Mouse** | How the pointer reaches the target (see [Mouse](#mouse)). |
| **1:1 pixels** | Shows the capture unscaled, for the sharpest text (also Ctrl+Alt+1). |

Video changes take effect the next time you connect; the mouse and 1:1
pixels apply at once.

## Board settings

These live on the board, so they follow it to any computer. They need
firmware 0.3.0 or later, except the LED brightness.

| Setting | What it does |
|---------|--------------|
| **Board** | Your board, to fill in its pins. **Custom** is for a board that isn't listed. |
| **Status LED: GPIO** | The pin driving the board's RGB LED. |
| **Status LED: Brightness %** | The LED's brightness. It changes as you adjust it; **Cancel** puts it back. |
| **VBUS sense mod: Present** | **Yes** for a board with the [VBUS sense modification](hardware/vbus-sense.md). |
| **VBUS sense mod: GPIO** | The pin the divider feeds. |

Choosing a board fills in its pins, and each GPIO label shows that board's
default (e.g., "GPIO (default: 48)"), so you can see when you've strayed
from it. Changing a pin keeps the board you chose; choose it again to put
its pins back.

The board applies each change at once and keeps it across resets. It refuses
pins it can't use, and the dialog says why. Firmware updates keep these
settings (0.4.0 and later); erasing the board resets them (see
[Firmware](firmware.md#board-settings)).

The board keeps only its pins, not which board you chose; the viewer
remembers that. Until you've chosen one, it shows the board whose pins
match, or **Custom**. If you switch between boards of different kinds, it
shows the one you chose last until you change it (the pins are always read
from the board).

## Status bar

The status bar shows the connection and the target's state on the left, and
on the right:

- **Frame rate:** frames per second from the dongle; **No video** when frames
  stop arriving, and **Video off** when not connected.
- **Mouse:** the mode in use, with **(Auto)** when Auto chose it. Hover over
  it for the reason.
- **NUM, CAPS, SCRL:** the target's lock keys, bold when on.

If a dongle stalls badly enough that libVLC can't stop it, the status bar
says **Video stuck: unplug the dongle and plug it back in.** The viewer
carries on meanwhile; only the dongle needs a replug.

## Mouse

**Mouse** sets how the pointer reaches the target:

- **Auto** (default): absolute, switching to relative when the target asks
  for a basic "boot" mouse, as some BIOS setup screens do, or (with firmware
  0.3.0 or later) when it isn't reading the absolute pointer at all.
- **Absolute:** the target's pointer goes exactly where yours is on the
  picture, with no fighting the target's mouse acceleration.
- **Relative:** an ordinary mouse. Use it for targets with more than one
  monitor, where absolute positions span the whole desktop.

If the pointer doesn't move in a BIOS screen or a diagnostics tool, choose
**Relative**. A few tools (e.g., Dell SupportAssist On-board Diagnostics on
some models) read the absolute pointer and then ignore it, which no device
can detect, so **Auto** never gets its cue to switch. The status bar's Mouse
field shows which mode is in use.

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

- **You release it:** Ctrl+Alt (or the Menu key).
- **The viewer loses focus:** a UAC prompt, Ctrl+Alt+Del or Win+L, or
  another program taking the foreground.
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
