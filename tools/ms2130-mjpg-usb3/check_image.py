#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Josh Tolbert
"""Independent checker for a patched MS2130 flash image against the original dump.
Derives everything from the patched bytes (decodes the 8051 pointer instructions itself)
rather than trusting the builder. Usage: check_image.py original.bin patched.bin"""
import sys, struct

BASE = 0x30
ok = True
def check(cond, msg):
    global ok
    print(('  PASS ' if cond else '  FAIL ') + msg)
    ok &= bool(cond)

o = open(sys.argv[1], 'rb').read(); p = open(sys.argv[2], 'rb').read()
ocl = struct.unpack('>H', o[2:4])[0]; pcl = struct.unpack('>H', p[2:4])[0]

print('[header and trailer]')
check(p[0:2] == b'\x3c\xc3', 'flash marker 3C C3')
check(p[4:0x30] == o[4:0x30], 'header bytes 0x04-0x2F unchanged (flags, version, overrides)')
check(len(p) == BASE + pcl + 4, f'file size = 0x30 + code length ({pcl}) + 4')
check(pcl <= 0xFC00, 'code length within tool limit 0xFC00')
H = (sum(p[2:0x0C]) + sum(p[0x10:0x30])) & 0xFFFF
C = sum(p[BASE:BASE + pcl]) & 0xFFFF
check(p[BASE + pcl:] == struct.pack('>HH', H, C), f'trailer = {H:04X} {C:04X} (header sum, code sum)')

print('[pointer patches, decoded from 8051 code]')
check(p[0x545A] == 0x90 and p[0x545D] == 0x93, 'MOV DPTR,#hiaddr ; MOVC at 0x545A')
check(p[0x545F] == 0x90 and p[0x5462:0x5464] == b'\xe4\x93', 'MOV DPTR,#loaddr ; CLR A ; MOVC at 0x545F')
a_hi = (p[0x545B] << 8) | p[0x545C]; a_lo = (p[0x5460] << 8) | p[0x5461]
base = (p[0x547C] << 8) | p[0x5477]
check(p[0x5476] == 0x74 and p[0x5478:0x547B] == b'\x2b\xf5\x82' and p[0x547B] == 0x74 and p[0x547D] == 0x3a,
      'MOV A,#lo ; ADD A,R3 ; MOV DPL,A ; MOV A,#hi ; ADDC A,R2 shape intact')
check(a_lo == base + 2 and a_hi == base + 3, f'length pointers 0x{a_lo:04X}/0x{a_hi:04X} = base 0x{base:04X} + 2/+3')
cfg_off = base + BASE
check(cfg_off + 9 <= BASE + pcl, f'config base 0x{base:04X} (file 0x{cfg_off:04X}) inside code area')

print('[new USB3 configuration descriptor]')
c = p[cfg_off:]
total = struct.unpack('<H', c[2:4])[0]
check(c[0:2] == b'\x09\x02', 'starts with CONFIGURATION descriptor')
check(cfg_off + total <= BASE + pcl, f'wTotalLength {total} fits in code area')
c = c[:total]
pos, ifaces, cur_cls, vs = 0, set(), None, {}
fmt_idx, frames, colors, eps, ss_comp = [], {}, 0, [], 0
cur_fmt = None
while pos < total:
    bl, bt = c[pos], c[pos + 1]
    if bl < 2 or pos + bl > total:
        check(False, f'descriptor chain broken at +{pos}'); break
    if bt == 4:
        ifaces.add(c[pos + 2]); cur_cls = (c[pos + 5], c[pos + 6])
    elif bt == 5:
        eps.append(c[pos + 2])
    elif bt == 0x30:
        ss_comp += 1
    elif bt == 0x24 and cur_cls == (0x0E, 0x02):
        st = c[pos + 2]
        if st == 1:
            vs = dict(off=pos, nfmt=c[pos + 3], tot=struct.unpack('<H', c[pos+4:pos+6])[0], bl=bl, ctl=c[pos+12], ep=c[pos+6])
        elif st in (4, 6):
            cur_fmt = (c[pos + 3], 'YUY2' if st == 4 else 'MJPG'); fmt_idx.append(cur_fmt)
            frames[cur_fmt] = []; nfr = c[pos + 4]; vs.setdefault('nfr', {})[cur_fmt] = nfr
        elif st in (5, 7):
            exp = 5 if cur_fmt[1] == 'YUY2' else 7
            check(st == exp, f'{cur_fmt[1]} frame subtype 0x{st:02x}') if st != exp else None
            w, h = struct.unpack('<HH', c[pos+5:pos+9])
            n = c[pos + 25]
            check(bl == 26 + 4 * n, f'frame {cur_fmt[1]}#{c[pos+3]} length matches interval count') if bl != 26 + 4*n else None
            fps = [round(1e7 / struct.unpack('<I', c[pos+26+4*i:pos+30+4*i])[0]) for i in range(n)]
            frames[cur_fmt].append((c[pos + 3], w, h, fps))
        elif st == 0x0D:
            colors += 1
    pos += bl
check(pos == total, 'descriptor chain ends exactly at wTotalLength')
check(c[4] == len(ifaces) == 5, f'bNumInterfaces {c[4]} = {len(ifaces)} distinct interfaces')
check(vs.get('nfmt') == 2 and vs.get('bl') == 15, 'VS input header: 2 formats, 15 bytes (2 bmaControls)')
# VS total = header + everything up to the endpoint
vs_end = vs['off']; q = vs['off']
while not (c[q + 1] == 5): q += c[q]
check(vs['tot'] == q - vs['off'], f'VS wTotalLength {vs["tot"]} = bytes from header to endpoint ({q - vs["off"]})')
check(sorted(i for i, _ in fmt_idx) == [1, 2] and dict(fmt_idx) == {1: 'MJPG', 2: 'YUY2'}, f'formats {fmt_idx}')
for f in fmt_idx:
    fr = frames[f]
    check(len(fr) == vs['nfr'][f] == 11 and [x[0] for x in fr] == list(range(1, 12)), f'{f[1]} has 11 frames indexed 1..11')
check([x[1:] for x in frames[(1, 'MJPG')]] == [x[1:] for x in frames[(2, 'YUY2')]], 'MJPG frame list identical to YUY2 (sizes and rates)')
check(colors == 2, 'one colour-matching descriptor per format')
check(eps == [0x83, 0x82, 0x84] and ss_comp == 3, 'endpoints 83/82/84 each with SuperSpeed companion')

print('[everything else unchanged]')
diffs = [i for i in range(min(BASE + ocl, len(p))) if o[i] != p[i]]
ranges = []
for i in diffs:
    if ranges and i == ranges[-1][1] + 1: ranges[-1][1] = i
    else: ranges.append([i, i])
expected = {(0x02, 0x03), (0x545B, 0x545C), (0x5460, 0x5461), (0x5477, 0x5477), (0x547C, 0x547C)}
ser = o.find('20210623'.encode('utf-16-le'))
allowed = lambda r: tuple(r) in expected or (ser <= r[0] and r[1] < ser + 16)
for r in ranges:
    print(f'    changed 0x{r[0]:04X}-0x{r[1]:04X}: {o[r[0]:r[1]+1].hex(" ")} -> {p[r[0]:r[1]+1].hex(" ")}')
check(all(allowed(r) for r in ranges), 'only header length, 3 pointer sites and USB3 serial changed inside original code')
check(p[BASE + ocl:BASE + pcl] == p[cfg_off:cfg_off + total] and cfg_off == BASE + ocl, 'appended bytes are exactly the new config')
for name, off, n in (('USB2 config', 0xC02B, 762), ('USB2 device desc', 0x17B8, 18), ('EDID', 0xBE30, 256), ('old USB3 config (now unused)', 0xC347, 796)):
    check(o[off:off+n] == p[off:off+n], f'{name} byte-identical')

print('\nRESULT:', 'ALL CHECKS PASSED' if ok else 'FAILURES PRESENT')
print('\nMJPG frames advertised on USB3:')
for idx, w, h, fps in frames[(1, 'MJPG')]: print(f'    #{idx:2d} {w}x{h} {fps}')
sys.exit(0 if ok else 1)
