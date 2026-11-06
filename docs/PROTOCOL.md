# Serial protocol

The viewer talks to the ESP32 over its UART bridge port. In the viewer, every
protocol sits behind one interface (`viewer/CodeBlue.Viewer/Link/IKvmLink.cs`),
which works in HID terms: keyboard usage IDs, mouse buttons and deltas.

## Binary protocol (planned)

The ESP-IDF firmware will use a framed binary protocol carrying HID usage codes
and absolute pointer coordinates. It will be specified here before it is
implemented.

### Requirements

These come from real failure modes and are not optional:

- **Bidirectional heartbeat.** Both ends send a heartbeat at a fixed interval
  (target: every 250 ms). If either side misses several in a row, it treats
  the link as down.
  - Viewer: releases capture immediately and says why.
  - Firmware: releases every key and button on the target, so nothing is left
    held down when the viewer crashes or the cable is pulled.
- **Target status.** The firmware reports whether the target has enumerated
  its USB keyboard and mouse (TinyUSB's mounted state), and reports changes as
  they happen. The viewer shows it, and releases capture if the target goes
  away (unplugged, rebooting, powered off).
- **Firmware identity on connect.** Protocol version and firmware version, so
  the viewer can refuse an incompatible board with a clear message instead of
  sending into the void.

The legacy protocol has none of these: its firmware never sends anything back,
so the viewer cannot tell when the ESP32 reboots or the target is unplugged.
The viewer can only detect the COM port itself disappearing.

## Legacy text protocol (deprecated)

Compatible with the Arduino firmware from
[alpharesearch/KeyboardAndMouseControl](https://github.com/alpharesearch/KeyboardAndMouseControl).
Kept only until the new firmware ships; see `CodeBlueLegacyProtocol` in
[BUILDING.md](BUILDING.md).

**Link:** 921600 baud, 8N1, no flow control. The viewer holds DTR and RTS low,
because on ESP32 dev boards they drive the reset and boot pins.

**Format:** one ASCII line per command: a command letter, then three
space-separated integers, then `\n`. Unused values are sent as `0`.

```
<cmd> <v0> <v1> <v2>\n
```

The firmware reads numbers with Arduino's `Serial.parseInt()`, which gives up
after one second of silence. Always send a whole line in one write; typing
commands by hand into a terminal can lose them.

| Cmd | Values | Action |
|-----|--------|--------|
| `a` | dx dy - | Move the mouse by dx, dy (each -127..127) |
| `m` | n - - | Mouse buttons: 0/1/2 click L/R/M; 3/4/5 press L/R/M; 6/7/8 release L/R/M; 9 release all buttons **and** all keys |
| `p` | key - - | Press a key (held until released) |
| `e` | key - - | Release a key |
| `k` | key - - | Press and immediately release a key |
| `s` | r g b - | Set the status LED colour (0..255 each) |

### Key values

`key` is interpreted by the Arduino `Keyboard` library:

| Value | Meaning |
|-------|---------|
| 0x00-0x7F | An ASCII character, translated through a US-layout table (Shift added for capitals) |
| 0x80-0x87 | A modifier: L-Ctrl, L-Shift, L-Alt, L-GUI, R-Ctrl, R-Shift, R-Alt, R-GUI |
| 0x88-0xFF | Raw HID usage `value - 0x88` |

The CodeBlue viewer sends **only** the last two forms: modifiers as
`0x80 + (usage - 0xE0)` and every other key as `0x88 + usage`. That makes the
legacy protocol carry physical keys (layout independent), with true press and
release, for HID usages 0x00 to 0x77, which covers every standard key.

### Limitations

- Mouse wheel is not supported (`a` ignores its third value).
- The mouse is relative only.
- International keys with usages above 0x77 (Japanese Kana and similar) cannot
  be sent.
