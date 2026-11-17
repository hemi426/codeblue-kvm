#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""
CodeBlue test sender: talks the binary protocol (docs/development/protocol.md)
to a board without the viewer.

Needs pyserial, which ESP-IDF's Python already has. After activating ESP-IDF:

    python tools/cb_test.py COM10 info            # handshake, print INFO and status
    python tools/cb_test.py COM10 watch           # print target status changes (Ctrl+C stops)
    python tools/cb_test.py COM10 type "hello"    # type text on the target (US layout)
    python tools/cb_test.py COM10 key ctrl+alt+del
    python tools/cb_test.py COM10 move 100 -50    # relative mouse
    python tools/cb_test.py COM10 point 16384 16384 [--click]   # absolute pointer
    python tools/cb_test.py COM10 scroll -3
    python tools/cb_test.py COM10 led 255 0 255
    python tools/cb_test.py COM10 brightness 20 [--persist]
    python tools/cb_test.py COM10 timeout-test    # holds Shift, goes silent, checks the board lets go
    python tools/cb_test.py selftest              # checks the framing code; no board needed
"""

from __future__ import annotations

import argparse
import struct
import sys
import threading
import time

# ---- Framing ----------------------------------------------------------------


def crc16(data: bytes) -> int:
    crc = 0xFFFF
    for b in data:
        crc ^= b << 8
        for _ in range(8):
            crc = ((crc << 1) ^ 0x1021) & 0xFFFF if crc & 0x8000 else (crc << 1) & 0xFFFF
    return crc


def cobs_encode(data: bytes) -> bytes:
    out = bytearray([0])
    code_at, code = 0, 1
    for b in data:
        if b == 0:
            out[code_at] = code
            code_at, code = len(out), 1
            out.append(0)
        else:
            out.append(b)
            code += 1
            if code == 0xFF:
                out[code_at] = code
                code_at, code = len(out), 1
                out.append(0)
    out[code_at] = code
    return bytes(out)


def cobs_decode(data: bytes) -> bytes | None:
    out = bytearray()
    i = 0
    while i < len(data):
        code = data[i]
        i += 1
        if code == 0 or i + code - 1 > len(data):
            return None
        out += data[i:i + code - 1]
        i += code - 1
        if code != 0xFF and i < len(data):
            out.append(0)
    return bytes(out)


def encode_frame(msg_type: int, payload: bytes = b"") -> bytes:
    raw = bytes([msg_type]) + payload
    return cobs_encode(raw + struct.pack("<H", crc16(raw))) + b"\x00"


def decode_frame(encoded: bytes):
    """Returns (type, payload) or None."""
    if not encoded or len(encoded) > 64:
        return None
    raw = cobs_decode(encoded)
    if raw is None or len(raw) < 3 or crc16(raw[:-2]) != struct.unpack("<H", raw[-2:])[0]:
        return None
    return raw[0], raw[1:-2]


# ---- Messages -----------------------------------------------------------------

HELLO, HEARTBEAT, SESSION_CONFIG = 0x01, 0x02, 0x03
KEYBOARD, MOUSE_ABS, MOUSE_REL = 0x10, 0x20, 0x21
LED, LED_BRIGHTNESS, RELEASE_ALL = 0x30, 0x31, 0x40
INFO, DEV_HEARTBEAT, TARGET_STATUS, ERROR = 0x81, 0x82, 0x90, 0xE0

STATUS_BITS = ["mounted", "suspended", "kbd_boot", "mouse_boot", "remote_wakeup", "vbus_present"]
LED_BITS = ["Num", "Caps", "Scroll"]
ERRORS = {1: "unknown message type", 2: "bad payload length", 3: "USB report failed"}


def describe_status(flags: int, leds: int) -> str:
    names = [n for i, n in enumerate(STATUS_BITS) if flags & (1 << i)] or ["(nothing)"]
    lit = [n for i, n in enumerate(LED_BITS) if leds & (1 << i)] or ["none"]
    return f"{' '.join(names)}; target LEDs: {' '.join(lit)}"


# HID usages for a US layout: char -> (usage, shift)
_KEYMAP = {}
for i, c in enumerate("abcdefghijklmnopqrstuvwxyz"):
    _KEYMAP[c] = (0x04 + i, False)
    _KEYMAP[c.upper()] = (0x04 + i, True)
for i, c in enumerate("1234567890"):
    _KEYMAP[c] = (0x1E + i, False)
for i, c in enumerate("!@#$%^&*()"):
    _KEYMAP[c] = (0x1E + i, True)
for usage, plain, shifted in [(0x2C, " ", None), (0x28, "\n", None), (0x2B, "\t", None),
                              (0x2D, "-", "_"), (0x2E, "=", "+"), (0x2F, "[", "{"), (0x30, "]", "}"),
                              (0x31, "\\", "|"), (0x33, ";", ":"), (0x34, "'", '"'), (0x35, "`", "~"),
                              (0x36, ",", "<"), (0x37, ".", ">"), (0x38, "/", "?")]:
    _KEYMAP[plain] = (usage, False)
    if shifted:
        _KEYMAP[shifted] = (usage, True)

MODIFIERS = {"ctrl": 0x01, "shift": 0x02, "alt": 0x04, "gui": 0x08, "win": 0x08,
             "rctrl": 0x10, "rshift": 0x20, "ralt": 0x40, "rgui": 0x80}
NAMED_KEYS = {"enter": 0x28, "esc": 0x29, "backspace": 0x2A, "tab": 0x2B, "space": 0x2C,
              "del": 0x4C, "delete": 0x4C, "ins": 0x49, "home": 0x4A, "end": 0x4D,
              "pgup": 0x4B, "pgdn": 0x4E, "right": 0x4F, "left": 0x50, "down": 0x51, "up": 0x52,
              "capslock": 0x39, "numlock": 0x53, "scrolllock": 0x47, "printscreen": 0x46,
              **{f"f{n}": 0x3A + n - 1 for n in range(1, 13)}}


def keyboard(modifiers: int, usages=()) -> bytes:
    return encode_frame(KEYBOARD, bytes([modifiers, len(usages), *usages]))


# ---- Link ---------------------------------------------------------------------


class Board:
    def __init__(self, port: str, verbose: bool):
        import serial  # pyserial; imported here so `selftest` works without it

        self.verbose = verbose
        self.ser = serial.Serial()
        self.ser.port, self.ser.baudrate, self.ser.timeout = port, 921600, 0.02
        self.ser.dtr = self.ser.rts = False  # set before open: they drive reset and boot
        self.ser.open()
        self.lock = threading.Lock()
        self.info = None
        self.status = None
        self.printed_status = None
        self.announced = False  # status changes print only after the board line
        self.last_rx = 0.0
        self.heartbeats = True
        self.got_info = threading.Event()
        self.stop = threading.Event()
        threading.Thread(target=self._reader, daemon=True).start()

    def send(self, frame: bytes):
        with self.lock:
            self.ser.write(frame)

    def _reader(self):
        buf = bytearray()
        while not self.stop.is_set():
            for b in self.ser.read(256):
                if b != 0:
                    buf.append(b)
                    continue
                msg = decode_frame(bytes(buf))
                buf.clear()
                if msg:
                    self.last_rx = time.monotonic()
                    self._on_message(*msg)

    def _on_message(self, t: int, p: bytes):
        if t == INFO and len(p) >= 8:
            caps = struct.unpack_from("<H", p, 5)[0]
            self.info = dict(protocol=f"{p[0]}.{p[1]}", firmware=f"{p[2]}.{p[3]}.{p[4]}",
                             capabilities=caps, led_brightness=p[7],
                             board=p[8:].decode("utf-8", "replace"))
            self.got_info.set()
        elif t == TARGET_STATUS and len(p) >= 2:
            self.status = (p[0], p[1])
            if self.announced:
                self._print_status()
        elif t == ERROR and len(p) >= 2:
            print(f"  board error: {ERRORS.get(p[0], p[0])} (detail 0x{p[1]:02X})")
        elif t == DEV_HEARTBEAT:
            if self.verbose:
                print("  (heartbeat)")
        else:
            print(f"  unexpected message 0x{t:02X}: {p.hex(' ')}")

    def _print_status(self):
        if self.status is not None and self.status != self.printed_status:
            self.printed_status = self.status
            print(f"  target: {describe_status(*self.status)}")

    def _heartbeat(self):
        while not self.stop.is_set():
            if self.heartbeats:
                self.send(encode_frame(HEARTBEAT))
            time.sleep(0.1)

    def connect(self) -> bool:
        hello = encode_frame(HELLO, b"CBLU" + bytes([1]))
        deadline = time.monotonic() + 2.0
        while time.monotonic() < deadline:
            self.send(hello)
            if self.got_info.wait(0.25):
                i = self.info
                print(f"Board: {i['board']}, firmware {i['firmware']}, protocol {i['protocol']}, "
                      f"capabilities 0x{i['capabilities']:04X}, LED {i['led_brightness']}%")
                if not i["protocol"].startswith("1."):
                    print("Unsupported protocol major version.")
                    return False
                threading.Thread(target=self._heartbeat, daemon=True).start()
                time.sleep(0.05)  # let TARGET_STATUS arrive
                self.announced = True
                self._print_status()
                return True
        print("No CodeBlue firmware found on this port.")
        return False

    def close(self):
        try:
            self.send(encode_frame(RELEASE_ALL))
            time.sleep(0.05)
        finally:
            self.stop.set()
            time.sleep(0.15)
            self.ser.close()


# ---- Commands -----------------------------------------------------------------


def cmd_type(b: Board, text: str, delay: float):
    for ch in text:
        if ch not in _KEYMAP:
            print(f"  skipping {ch!r}: not on the US layout map")
            continue
        usage, shift = _KEYMAP[ch]
        b.send(keyboard(0x02 if shift else 0, [usage]))
        time.sleep(delay)
        b.send(keyboard(0))
        time.sleep(delay)


def cmd_key(b: Board, combo: str, hold: float):
    mods, usages = 0, []
    for part in combo.lower().split("+"):
        if part in MODIFIERS:
            mods |= MODIFIERS[part]
        elif part in NAMED_KEYS:
            usages.append(NAMED_KEYS[part])
        elif part in _KEYMAP:
            usages.append(_KEYMAP[part][0])
        else:
            sys.exit(f"Unknown key '{part}'")
    b.send(keyboard(mods, usages[:6]))
    time.sleep(hold)
    b.send(keyboard(0))


def cmd_timeout_test(b: Board):
    print("Holding Left Shift, then going silent...")
    b.send(keyboard(0x02))
    time.sleep(0.2)
    b.heartbeats = False
    went_quiet = None
    start = time.monotonic()
    while time.monotonic() - start < 1.5:
        time.sleep(0.01)
        if went_quiet is None and time.monotonic() - b.last_rx > 0.25:
            went_quiet = b.last_rx - start
    if went_quiet is None:
        print("FAIL: the board kept talking after the host went silent.")
        return False
    print(f"OK: the board stopped {went_quiet * 1000:.0f} ms after the host did "
          f"(expect about 500), so it released Shift and its LED should be blue.")
    print("Check the target: Shift should not be stuck (type a letter on its own keyboard).")
    return True


def selftest() -> bool:
    ok = crc16(b"123456789") == 0x29B1
    vectors = [
        (KEYBOARD, bytes([1, 1, 6]), "07 10 01 01 06 a0 fb 00"),
        (KEYBOARD, bytes([0, 0]), "02 10 01 03 ff 8f 00"),
        (HELLO, b"CBLU\x01", "09 01 43 42 4c 55 01 59 be 00"),
    ]
    for t, p, want in vectors:
        got = encode_frame(t, p).hex(" ")
        ok &= got == want
        print(f"  {'ok  ' if got == want else 'FAIL'} {got}")
    for n in range(61):
        p = bytes((i * 7) & 0xFF if i % 3 else 0 for i in range(n))
        f = encode_frame(MOUSE_ABS, p)
        ok &= len(f) - 1 <= 64 and decode_frame(f[:-1]) == (MOUSE_ABS, p)
    print("selftest", "passed" if ok else "FAILED")
    return ok


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("port", help="serial port (COM10, /dev/ttyACM0), or 'selftest'")
    ap.add_argument("command", nargs="?", default="info")
    ap.add_argument("args", nargs="*")
    ap.add_argument("--persist", action="store_true", help="brightness: save on the board")
    ap.add_argument("--click", action="store_true", help="point: left-click there")
    ap.add_argument("--delay", type=float, default=0.02, help="type: seconds per key event")
    ap.add_argument("-v", "--verbose", action="store_true")
    a = ap.parse_args()

    if a.port == "selftest":
        sys.exit(0 if selftest() else 1)

    b = Board(a.port, a.verbose)
    ok = True
    try:
        if not b.connect():
            sys.exit(1)
        c, args = a.command, a.args
        if c == "info":
            pass
        elif c == "watch":
            print("Watching; Ctrl+C stops.")
            while True:
                time.sleep(0.5)
        elif c == "type":
            cmd_type(b, " ".join(args), a.delay)
        elif c == "key":
            cmd_key(b, args[0], 0.05)
        elif c == "move":
            b.send(encode_frame(MOUSE_REL, struct.pack("<hhBbb", int(args[0]), int(args[1]), 0, 0, 0)))
        elif c == "point":
            x, y = int(args[0]), int(args[1])
            b.send(encode_frame(MOUSE_ABS, struct.pack("<HHBbb", x, y, 0, 0, 0)))
            if a.click:
                time.sleep(0.05)
                b.send(encode_frame(MOUSE_ABS, struct.pack("<HHBbb", x, y, 1, 0, 0)))
                time.sleep(0.05)
                b.send(encode_frame(MOUSE_ABS, struct.pack("<HHBbb", x, y, 0, 0, 0)))
        elif c == "scroll":
            b.send(encode_frame(MOUSE_REL, struct.pack("<hhBbb", 0, 0, 0, int(args[0]), 0)))
        elif c == "led":
            b.send(encode_frame(LED, bytes(int(v) for v in args[:3])))
        elif c == "brightness":
            b.send(encode_frame(LED_BRIGHTNESS, bytes([int(args[0]), 1 if a.persist else 0])))
        elif c == "timeout-test":
            ok = cmd_timeout_test(b)
        else:
            sys.exit(f"Unknown command '{c}'")
        time.sleep(0.2)  # let late status changes print
    except KeyboardInterrupt:
        pass
    finally:
        b.close()
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
