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

**Video lag?** Keep **Format** on MJPG and **Cache ms** around 50. Many
dongles can only deliver 1080p at full frame rate as MJPEG.

**Blurry or broken-looking text?** Use MJPG rather than YUY2 (see
[What to buy](hardware/bom.md#capture-dongles)), and turn on **1:1 pixels**
(Ctrl+Alt+1) when the window or screen is larger than the capture. For a
record of exactly what the target showed, use **Snapshot** (F12): it saves
the captured frame itself, unscaled.

**Target stuck at 640x480 after its display sleeps?** Seen with the UGREEN
15389 dongle: when the target's display wakes, it can fall back to 640x480
until the dongle is replugged. The recommended MS2130 stick doesn't do this
(see [What to buy](hardware/bom.md#capture-dongles)).

**Viewer freezes on Disconnect or when closing?** Seen with the UGREEN 15389:
after the target's display sleeps, the dongle can stop sending video without
reporting an error, and the viewer then hangs waiting for it to stop. End
**CodeBlue** in Task Manager and start it again. The recommended MS2130 stick
hasn't done this, and a fix in the viewer is planned.

**Can't type in the viewer's own fields while capturing?** That's by design:
release capture first (Ctrl+Alt).
