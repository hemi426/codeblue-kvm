# Troubleshooting

**Target not responding to keyboard or mouse?** Disconnect in the viewer,
re-plug the ESP32's native USB cable at the target, and reconnect. The status
LED turning green on Connect shows the viewer is reaching the ESP32; if it
does but the target ignores input, the problem is the ESP32-to-target USB
link (cable, port, or the target not having enumerated the device).

**"No CodeBlue firmware found on this port"?** Check you picked the board's
UART port, not its native USB port (the native one shows up on the target,
not on your computer), and that the board has been flashed with the CodeBlue
firmware (see [Flashing](firmware.md#flashing)).

**Black video?** Make sure no other program (OBS, VLC, the Camera app) has
the capture device open. **When in doubt, set FPS to Auto:** most dongles only
offer some combinations of format, size, and frame rate (YUY2 at 1080p is
often just 5 or 10 fps), and asking for one they lack gives a black screen.
The status bar says when that's the cause. Otherwise it names the problem,
and `%LOCALAPPDATA%\CodeBlue\libvlc.log` has the details.

**Mouse doesn't move on the target?** Some BIOS screens and diagnostics tools
(e.g., Dell SupportAssist On-board Diagnostics) ignore the absolute pointer,
and **Mouse: Auto** can't always tell; the status bar's Mouse field shows the
mode in use. Set **Mouse** to **Relative** in **Viewer options**. See
[Mouse](viewer.md#mouse).

**Status LED dark?** The board probably has its LED on another pin
(ESP32-S3-DevKitC-1 v1.1 boards use GPIO38). Set it in
[Board settings](firmware.md#board-settings).

**Video lag?** Keep **Format** on MJPG and **Cache ms** around 40 (both in
**Viewer options**). Many dongles can only deliver 1080p at full frame rate
as MJPEG.

**Blurry or broken-looking text?** Use MJPG rather than YUY2 (see
[What to buy](hardware/bom.md#capture-dongles)), and turn on **1:1 pixels**
(Ctrl+Alt+1) when the window or screen is larger than the capture. For a
record of exactly what the target showed, use **Snapshot** (F12): it saves
the captured frame itself, unscaled.

**Target stuck at 640x480 after its display sleeps?** Seen with the UGREEN
15389 dongle: when the target's display wakes, it can fall back to 640x480
(and may show up in Windows' display settings as "Wired Display" rather than
the dongle's own name). If the target's display settings still offer
1920x1080, choosing it may be enough; otherwise, replug the dongle. The
recommended MS2130 stick doesn't do this (see
[What to buy](hardware/bom.md#capture-dongles)).

**Video flickers, drops frames, or stops?** Try another dongle before blaming
the target or the viewer. The UGREEN 15389 does all three: it flickers to
black, drops below its frame rate, and can stop sending video altogether
without reporting an error (the frame rate in the status bar shows **No
video**). The recommended MS2130 stick hasn't done any of these.

**"Video stuck: unplug the dongle and plug it back in"?** The dongle stopped
sending video so badly that it couldn't be stopped, and it's holding on until
it's unplugged. Replug it, then **Connect** again. If it still won't open
(the Windows Camera app can't open it either, and it's still listed after
you unplug it), restart your computer. Viewers before 0.4.0 froze in this
situation instead; end **CodeBlue** in Task Manager if an older one does.

**Can't type in the viewer's own fields while capturing?** That's by design:
release capture first (Ctrl+Alt).
