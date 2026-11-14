#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 The CodeBlue Authors
"""Build an MJPG-on-USB3 image for MS2130 dongle A (345F:2130, code length 51625, code sum B559).

Changes, and nothing else:
  1. Append a new USB3 configuration descriptor at the end of the code area:
     the original USB3 config with an MJPG format (index 1) added after YUY2 (index 2),
     same order as UGREEN's working USB3 table. MJPG frames = A's USB3 YUY2 frames with
     subtype 0x05 -> 0x07 (MacroSilicon's own USB2 table does exactly this).
  2. Repoint the three code references to the USB3 config (one routine, file 0x545A-0x547C).
  3. Change the USB3-only serial string "20210623" -> "CBMJPG01" (same length) so a running
     patched image is recognisable in USB Tree View.
  4. Update header code length and both trailer checksums.

Usage: build_patch.py A_original.bin out.bin
"""
import sys, struct, hashlib

BASE = 0x30                       # file offset of code address 0x0000
EXPECT_SHA256 = '2be37a65dee892cea965dbab04e5d4a429937473c84e9256f768420313c662c7'

def die(msg):
    sys.exit('ABORT: ' + msg)

src, dst = sys.argv[1], sys.argv[2]
orig = open(src, 'rb').read()
if hashlib.sha256(orig).hexdigest() != EXPECT_SHA256:
    die('input is not the verified dump of dongle A')

d = bytearray(orig)
clen = struct.unpack('>H', d[2:4])[0]
assert d[0:2] == b'\x3c\xc3' and clen == 51625
code_end = BASE + clen

# ---- locate original USB3 config (file offsets) and check layout ----
CFG3 = 0xC347
assert d[CFG3:CFG3 + 2] == b'\x09\x02'
cfg3_len = struct.unpack('<H', d[CFG3 + 2:CFG3 + 4])[0]
assert cfg3_len == 796
cfg = bytes(d[CFG3:CFG3 + cfg3_len])

VSH = 0xC39D - CFG3               # VS input header offset within config
FMT_Y = 0xC3AB - CFG3             # YUY2 format descriptor
COLOR_Y = 0xC5C0 - CFG3           # colour matching after YUY2 frames
EP = 0xC5C6 - CFG3                # endpoint descriptor that follows
assert cfg[VSH:VSH + 4] == bytes([0x0E, 0x24, 0x01, 0x01])
assert cfg[FMT_Y:FMT_Y + 5] == bytes([0x1B, 0x24, 0x04, 0x02, 0x0B])
assert cfg[COLOR_Y:COLOR_Y + 3] == bytes([0x06, 0x24, 0x0D])
assert cfg[EP:EP + 2] == bytes([0x07, 0x05])

yuy2_block = cfg[FMT_Y:EP]        # format + 11 frames + colour = 27 + 506 + 6
assert len(yuy2_block) == 539

# ---- build MJPG block ----
mjpg_fmt = bytes([0x0B, 0x24, 0x06, 0x01, 0x0B, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00])
# sanity: identical to A's own USB2 MJPG format descriptor
assert d[0xC08F:0xC08F + 11] == mjpg_fmt, 'USB2 MJPG format descriptor differs from template'
frames = bytearray()
p = FMT_Y + 27
for n in range(11):
    fr = bytearray(cfg[p:p + cfg[p]])
    assert fr[1] == 0x24 and fr[2] == 0x05 and fr[3] == n + 1
    fr[2] = 0x07                  # Uncompressed Frame -> MJPEG Frame (same layout)
    frames += fr
    p += len(fr)
assert p == COLOR_Y
color = cfg[COLOR_Y:EP]
mjpg_block = mjpg_fmt + bytes(frames) + color

# ---- new VS input header: 2 formats, one bmaControls byte per format ----
old_vs_total = struct.unpack('<H', cfg[VSH + 4:VSH + 6])[0]
assert old_vs_total == 553
new_vsh = bytearray(cfg[VSH:VSH + 14]) + b'\x00'
new_vsh[0] = 0x0F
new_vsh[3] = 2
new_vs_total = 15 + len(yuy2_block) + len(mjpg_block)
new_vsh[4:6] = struct.pack('<H', new_vs_total)

new_cfg = bytearray(cfg[:VSH]) + new_vsh + yuy2_block + mjpg_block + cfg[EP:]
new_cfg[2:4] = struct.pack('<H', len(new_cfg))
assert len(new_cfg) == cfg3_len + 1 + len(mjpg_block)

# ---- append at end of code ----
new_addr = clen                    # code address where the new config starts
assert new_addr + len(new_cfg) <= 0xFC00, 'exceeds tool limit'
d = d[:code_end] + new_cfg          # drop old trailer; new trailer appended below
new_clen = clen + len(new_cfg)

# ---- repoint the three references (verify exact original bytes first) ----
def patch(off, old, new):
    assert bytes(d[off:off + len(old)]) == old, f'unexpected bytes at 0x{off:04x}'
    d[off:off + len(new)] = new

hi = lambda a: (a >> 8) & 0xFF
lo = lambda a: a & 0xFF
patch(0x545A, bytes([0x90, 0xC3, 0x1A]), bytes([0x90, hi(new_addr + 3), lo(new_addr + 3)]))   # wTotalLength hi
patch(0x545F, bytes([0x90, 0xC3, 0x19]), bytes([0x90, hi(new_addr + 2), lo(new_addr + 2)]))   # wTotalLength lo
patch(0x5476, bytes([0x74, 0x17, 0x2B, 0xF5, 0x82, 0x74, 0xC3, 0x3A]),
              bytes([0x74, lo(new_addr), 0x2B, 0xF5, 0x82, 0x74, hi(new_addr), 0x3A]))      # base + offset

# ---- USB3 serial marker ----
old_ser = '20210623'.encode('utf-16-le'); new_ser = 'CBMJPG01'.encode('utf-16-le')
assert d.count(old_ser) == 1
i = d.find(old_ser)
assert d[i - 2:i] == b'\x12\x03'   # string descriptor header (18 bytes, type 3)
d[i:i + len(new_ser)] = new_ser

# ---- header length + trailer ----
d[2:4] = struct.pack('>H', new_clen)
H = (sum(d[2:0x0C]) + sum(d[0x10:0x30])) & 0xFFFF
C = sum(d[BASE:BASE + new_clen]) & 0xFFFF
d += struct.pack('>HH', H, C)

open(dst, 'wb').write(d)
print(f'new config: code addr 0x{new_addr:04X} (file 0x{new_addr + BASE:04X}), {len(new_cfg)} bytes, VS total {new_vs_total}')
print(f'code length {clen} -> {new_clen}; trailer {H:04X} {C:04X}; file {len(d)} bytes')
print('sha256', hashlib.sha256(d).hexdigest())
