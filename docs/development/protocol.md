# Serial protocol

The viewer talks to the ESP32 over its UART bridge port using the CodeBlue
protocol below. In the viewer, the protocol sits behind one interface
(`viewer/CodeBlue.Viewer/Link/IKvmLink.cs`), which works in HID terms:
keyboard usage IDs, mouse buttons and deltas.

## CodeBlue protocol, version 1 (draft)

**Status: draft.** Implemented by the ESP-IDF firmware, the viewer and
`tools/cb_test.py`. Until it's released, details may change without a version
bump.

### Requirements

These come from real failure modes and are not optional:

- **Bidirectional heartbeat.** Both ends send a heartbeat every 100 ms. If
  either side hears nothing for the session timeout (500 ms by default), it
  treats the link as down.
  - Viewer: releases capture immediately and says why.
  - Firmware: releases every key and button on the target, so nothing is left
    held down when the viewer crashes or the cable is pulled.
- **Target status.** The firmware reports whether the target has enumerated
  its USB keyboard and mouse, whether it is asleep, and its keyboard LEDs, and
  reports changes as they happen. The viewer shows it.
- **Firmware identity on connect.** Protocol version and firmware version, so
  the viewer can refuse an incompatible board with a clear message instead of
  sending into the void.

The text protocol of the Arduino firmware CodeBlue started from had none of
these: that firmware never sends anything back, so a viewer can't tell when
the ESP32 reboots or the target is unplugged.

### Design choices

- **State, not events.** Keyboard and mouse messages carry the complete
  current state (every held key, every held button), never "key X went down".
  A lost or corrupted frame is corrected by the next one, and a key can never
  stay stuck because one release went missing.
- **Self-synchronizing framing.** Frames are COBS-encoded and end in `0x00`,
  which never appears inside a frame. After any corruption, the receiver
  resynchronizes at the next `0x00`.
- **Errors are dropped, not repaired.** A frame with a bad CRC, an unknown
  type, or too few bytes is discarded. State messages make retries
  unnecessary.

### Link

| Setting | Value |
|---------|-------|
| Port | The board's UART bridge (the DevKitC "UART" USB port) |
| Speed | 921600 baud, 8N1, no flow control |
| DTR/RTS | Held low by the host (they drive the DevKitC's reset and boot pins) |
| Byte order | Little-endian for all multi-byte fields |

### Frame format

Before encoding, a frame is:

```
+--------+-----------------+-----------+
| type   | payload         | crc16     |
| 1 byte | 0 to 60 bytes   | 2 bytes   |
+--------+-----------------+-----------+
```

- `crc16` is CRC-16/CCITT-FALSE (polynomial `0x1021`, initial value `0xFFFF`,
  no reflection, no final XOR) over `type` and `payload`, stored
  little-endian. Check value: `"123456789"` gives `0x29B1`.
- The whole thing is COBS-encoded, then followed by a single `0x00` delimiter.
- Receivers discard any encoded frame longer than 64 bytes (not counting the
  delimiter) and resume at the next `0x00`.
- Receivers discard a frame that is too short for its message's fields, and
  ignore any bytes after those fields. That lets a later minor version append
  fields without breaking older receivers.

### Session

1. The host opens the port and sends `HELLO`, repeating it every 250 ms until
   the device answers with `INFO`, for up to 2 seconds.
2. No answer: the host reports "No CodeBlue firmware found on this port" and
   closes it. (Old Arduino firmware never answers; see the safety note
   below.)
3. The device answers `INFO`, then sends `TARGET_STATUS`.
4. The host may send `SESSION_CONFIG` to change the timeout.
5. Both sides start sending `HEARTBEAT`s. The host may now send input.
6. A `HELLO` received mid-session (for example after the viewer restarts)
   resets the session: the device releases all input, returns to the default
   timeout, and answers `INFO` and `TARGET_STATUS` again.

**Liveness.** Each side sends a `HEARTBEAT` at least every 100 ms; any valid
frame also counts. If either side hears nothing valid for the session timeout
(500 ms unless changed by `SESSION_CONFIG`):

- Device: releases every key and button on the target, sets the LED to the
  "no host" colour, and waits for a new `HELLO`.
- Host: releases capture, shows "CodeBlue board not responding", and treats
  the link as down.

A 500 ms timeout usually releases a key held during a crash before the
target's own key repeat starts (typically after 250 to 500 ms). Real stalls
on a working host are tens of milliseconds, so false trips are rare; a
debugger breakpoint is the exception, which is what `SESSION_CONFIG` is for.

The viewer sends its heartbeats from its UI thread, not a background thread.
A frozen viewer therefore stops heartbeating, and the board releases
everything, rather than a still-running serial thread keeping a key held on
the target indefinitely.

### Messages: host to device

| Type | Name | Payload |
|------|------|---------|
| `0x01` | `HELLO` | `magic[4]` = `"CBLU"`, `protocol_major` u8 (= 1) |
| `0x02` | `HEARTBEAT` | none |
| `0x03` | `SESSION_CONFIG` | `timeout_ms` u16 |
| `0x10` | `KEYBOARD` | `modifiers` u8, `count` u8, `usages[count]` (count 0 to 6) |
| `0x20` | `MOUSE_ABS` | `x` u16, `y` u16, `buttons` u8, `wheel` i8, `pan` i8 |
| `0x21` | `MOUSE_REL` | `dx` i16, `dy` i16, `buttons` u8, `wheel` i8, `pan` i8 |
| `0x30` | `LED` | `r` u8, `g` u8, `b` u8 |
| `0x31` | `LED_BRIGHTNESS` | `percent` u8 (0 to 100), `persist` u8 (0 or 1) |
| `0x40` | `RELEASE_ALL` | none |

#### `SESSION_CONFIG`

Sets the liveness timeout for this session, on both sides. The device clamps
it to 100 to 60000 ms. It lasts until the next `HELLO` or timeout. The viewer
asks for a long timeout (30000 ms) when a debugger is attached, so
breakpoints don't end the session, and otherwise leaves the default.

#### `KEYBOARD`

The complete set of held keys. `usages` are HID Keyboard/Keypad page usages
for every held key other than the modifiers, in any order. With more than six
held, the host sends six and the device reports nothing extra. An empty set
(`count` 0, `modifiers` 0) releases everything.

`modifiers` is the standard HID modifier byte:

| Bit | Key | Bit | Key |
|-----|-----|-----|-----|
| 0 | Left Ctrl | 4 | Right Ctrl |
| 1 | Left Shift | 5 | Right Shift |
| 2 | Left Alt | 6 | Right Alt |
| 3 | Left GUI (Windows, Cmd) | 7 | Right GUI |

#### `MOUSE_ABS` and `MOUSE_REL`

`MOUSE_ABS` places the pointer: `x` and `y` run from 0 to 32767 across the
target's whole screen. `MOUSE_REL` moves it by `dx`, `dy` since the last
message; the device splits large deltas into as many HID reports as needed.
See "Mouse modes" below for which one the viewer sends.

Both carry the complete button state, and `wheel` and `pan` as one-shot
detents (applied once, positive is away from the user and to the right):

| Bit | Button |
|-----|--------|
| 0 | Left |
| 1 | Right |
| 2 | Middle |
| 3 | Back |
| 4 | Forward |

Buttons belong to whichever message type was sent last: switching from one to
the other releases any buttons held on the other device.

#### `LED_BRIGHTNESS`

Scales every colour the device shows, including its own status colours. With
`persist` 1, the device saves the value in flash, so it also applies from
power-on; it skips the write if the stored value is already the same.

The board is the source of truth: `INFO` reports the current value, and the
viewer shows that rather than a remembered one. While the user adjusts it,
the viewer sends `persist` 0 previews, then one `persist` 1 about a second
after the last change, so flash is written once per adjustment. The
firmware's built-in default (a build setting) is 5%.

#### `RELEASE_ALL`

Releases every key and button immediately. The host sends it whenever
capture ends.

### Messages: device to host

| Type | Name | Payload |
|------|------|---------|
| `0x81` | `INFO` | `protocol_major` u8, `protocol_minor` u8, `fw_major` u8, `fw_minor` u8, `fw_patch` u8, `capabilities` u16, `led_brightness` u8, `board[]` (UTF-8, rest of payload) |
| `0x82` | `HEARTBEAT` | none |
| `0x90` | `TARGET_STATUS` | `flags` u8, `kbd_leds` u8 |
| `0xE0` | `ERROR` | `code` u8, `detail` u8 |

#### `INFO`

The device's answer to `HELLO`. The host refuses a `protocol_major` it
doesn't support, naming both versions in the message. `led_brightness` is
the current brightness percentage. `board` is a short human-readable name
such as `ESP32-S3-DevKitC-1 v1.0`.

`capabilities`:

| Bit | Meaning |
|-----|---------|
| 0 | Absolute pointer (`MOUSE_ABS`) |
| 1 | Relative mouse (`MOUSE_REL`) |
| 2 | Persistent settings (`LED_BRIGHTNESS` with `persist`) |
| 3 | VBUS sense (see "VBUS sense" below) |

#### `TARGET_STATUS`

Sent after `INFO`, whenever anything in it changes, and at least once a
second.

`flags`:

| Bit | Name | Meaning |
|-----|------|---------|
| 0 | `mounted` | The target has enumerated the device. |
| 1 | `suspended` | The USB bus is suspended: the target is asleep or unplugged (see below). |
| 2 | `kbd_boot` | The keyboard is in boot protocol. |
| 3 | `mouse_boot` | The mouse is in boot protocol (see "Mouse modes"). |
| 4 | `remote_wakeup` | The target has enabled remote wakeup, so a keypress can wake it. |
| 5 | `vbus_present` | The target is supplying USB power. Always 0 unless `INFO` reports VBUS sense. |

`kbd_leds`, the target's own lock-key state from its HID output report, so
the viewer can show the target's Caps Lock rather than the host's:

| Bit | LED |
|-----|-----|
| 0 | Num Lock |
| 1 | Caps Lock |
| 2 | Scroll Lock |

#### `ERROR`

For diagnostics only; the host logs it and carries on.

| Code | Meaning | `detail` |
|------|---------|----------|
| 1 | Unknown message type | The type |
| 2 | Payload too short | The type |
| 3 | USB report could not be sent (not yet sent by the firmware) | 0 |

### Asleep or unplugged

In USB the target does all the talking: a device speaks only when polled. A
sleeping target suspends the bus (no polling at all), and a pulled cable
leaves the bus silent too, so the board sees both as `suspended`. The only
physical difference is USB power: a sleeping PC usually keeps it on, a
pulled cable doesn't. A board with VBUS sense can tell them apart; without
it, the viewer says "asleep or disconnected". Even VBUS isn't conclusive,
since some PCs cut USB power when fully off or hibernating.

What each side does:

- **The host keeps capture** through both `suspended` and `mounted` 0, and
  shows the target's state. A rebooting target resets the bus and enumerates
  the device again several times (BIOS, bootloader, OS), and "capture, hold
  F2, reboot" has to work. The usual capture exits still work.
- **The device keeps the host's state.** Only the host changes which keys and
  buttons are held (and the device releases everything if the host goes
  quiet). While the target can't receive reports, the device holds on to
  that state and drops queued movement and wheel detents. When the target
  enumerates the device again, or resumes, the device sends whatever is
  still held.
- **Wake on input:** while `suspended` and `remote_wakeup` are both 1, the
  first `KEYBOARD` or `MOUSE_*` message with a key or button pressed makes
  the device signal remote wakeup, as a real keyboard would.

### USB side (informative)

The device presents one composite USB device to the target:

- A **keyboard** supporting the boot protocol, so BIOS and UEFI setup screens
  work.
- A **relative mouse** supporting the boot protocol, for the same reason.
- An **absolute pointer** (0 to 32767 on both axes), so clicks land exactly
  where they are aimed in the viewer.

#### Mouse modes

Boot-protocol mice can only report relative movement, and some firmware setup
screens ignore absolute pointers entirely. The viewer has a mouse mode
setting:

- **Auto** (default): `MOUSE_ABS`, switching to `MOUSE_REL` whenever
  `TARGET_STATUS` reports `mouse_boot`.
- **Absolute:** always `MOUSE_ABS`.
- **Relative:** always `MOUSE_REL`. Use this for targets with more than one
  monitor: an absolute pointer spans the whole desktop, so on a multi-monitor
  target, clicks land in the wrong place relative to the one captured screen.

Whether a given BIOS accepts the absolute pointer varies by machine; tested
results belong in [the tested-targets table](../hardware/index.md#tested-targets).

**VBUS sense (optional hardware).** Telling a sleeping target from an
unplugged one needs the board to sense the target's 5 V USB power: a
two-resistor voltage divider from the native port's VBUS (before its diode)
to a spare GPIO. The ESP32-S3-DevKitC-1 doesn't have this; see
[VBUS sense modification](../hardware/vbus-sense.md) for how to add it.
Firmware built with a sense pin (`CONFIG_CODEBLUE_VBUS_GPIO`) sets
capability bit 3 and reports `vbus_present`. The viewer then says "asleep"
when the bus is suspended with power present, and "unplugged or powered off"
when there's no power.

### Safety with the old Arduino firmware

The viewer might be pointed at a board still running the Arduino firmware
CodeBlue started from. Its parser runs a command only when it reads a
newline after three numbers, so the encoded `HELLO` frame is chosen to contain
no newline (`0x0A`), no ASCII digit and no minus sign. The exact bytes are:

```
09 01 43 42 4C 55 01 59 BE 00
```

That is type `0x01`, magic `"CBLU"`, protocol major 1, CRC `0xBE59`, COBS
encoded, then the delimiter. The old firmware reads it, times out, and does
nothing. Hosts must send nothing else until the device has answered with
`INFO`.

### Worked examples

Holding Left Ctrl+C: `KEYBOARD`, modifiers `0x01`, one usage (`0x06`, C):

| Stage | Bytes |
|-------|-------|
| Type and payload | `10 01 01 06` |
| Plus CRC (`0xFBA0`) | `10 01 01 06 A0 FB` |
| On the wire (COBS, delimiter) | `07 10 01 01 06 A0 FB 00` |

Releasing everything: `KEYBOARD`, modifiers `0x00`, no usages. The zeros in
the payload show COBS at work:

| Stage | Bytes |
|-------|-------|
| Type and payload | `10 00 00` |
| Plus CRC (`0x8FFF`) | `10 00 00 FF 8F` |
| On the wire (COBS, delimiter) | `02 10 01 03 FF 8F 00` |
