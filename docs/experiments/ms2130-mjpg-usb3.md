# MS2130 capture stick: enabling MJPG over USB 3

**Status:** working. A $9 MS2130 HDMI capture stick now streams MJPG at 1920×1080 @ 60 fps over USB 3, verified with ffmpeg and VLC.

> **Experimental and unsupported.** This rewrites the dongle's own firmware with a third-party tool. It worked on one firmware revision of one stick; yours may differ, and a bad write can brick it. Keep your original dump. See [the experiments overview](index.md).

## Why

CodeBlue's viewer uses libVLC 3, which shows soft, blurry text when fed YUY2. The cheap stick we prefer (better colours, no startup logo) only offers YUY2 on USB 3 and only offers MJPG on USB 2, where it tops out at 1080p50. A UGREEN card built on the same MS2130 chip offers MJPG at 1080p60 over USB 3, which proved the silicon can do it.

## Hardware

| | Dongle A (patched) | Dongle B (reference, untouched) |
|---|---|---|
| Product | no-name stick, [Amazon B0BJ6XLK45](https://www.amazon.com/dp/B0BJ6XLK45) | UGREEN 15389, [Amazon B0DT9DY312](https://www.amazon.com/dp/B0DT9DY312) |
| USB ID | `345F:2130` | `2B89:5389` |
| Product string | "USB3.0 UHD" | "UGREEN 15389" |
| USB 3 formats (stock) | YUY2 only | MJPG + YUY2, up to 1440p30 |
| USB 2 formats (stock) | MJPG only, 1080p50 | MJPG + YUY2 |
| Quirks | none | UGREEN logo for ~10 s when capture starts |

Both expose a small vendor HID interface (interface 4, 64-byte interrupt IN endpoint) that the configuration tool uses.

## Tooling

- **MS21XX_MS91XX_DownloadTool V1.7.0** (build 20221024), Windows; SHA-256 `9ae4b86d00100aa29d73500c402d0bcc5656f6274a7f94c7caeb2af98d578e68`. The UI is partly in Chinese.
- **USB Tree View** for descriptor dumps.
- **ffmpeg / ffprobe** for stream verification.
- Python scripts in [`tools/ms2130-mjpg-usb3/`](https://github.com/hemi426/codeblue-kvm/blob/main/tools/ms2130-mjpg-usb3): `build_patch.py` (builds the patched image) and `check_image.py` (independently re-parses and verifies it).

### Download tool settings and gotchas

| Setting | Value / note |
|---|---|
| 芯片型号 (chip model) | `MS2130/31` |
| Memory type | **Flash** radio button. The default EEPROM options read a 52-byte all-zero file from this chip. |
| Connect | Needed only for non-default VID/PID (dongle B: `2B89` / `5389`). |
| 连接自动下载 | "Auto-download on connect." **Keep unticked**; it writes flash as soon as a device appears. |
| Modify Length | Leave unticked. |
| Version box | Editable; leave it alone. |
| **Download** | This is the **write** button. Read + Save To BIN are the safe operations. |

Status bar messages: 读取完成 read complete; 打开文件成功 file opened; 校验成功，烧录成功 verify OK, programming OK; 下载失败 download failed.

The tool reads only header + code + trailer, not the whole flash chip.

## Dumps

Each dongle was read four times (twice on USB 3, twice through a USB 2-only extension). All reads of a dongle hashed identically.

| | Size | SHA-256 |
|---|---|---|
| A original | 51,677 bytes | `2be37a65dee892cea965dbab04e5d4a429937473c84e9256f768420313c662c7` |
| B original | 59,488 bytes | `b09526e9a1759482fb94b1e9f18accf48deb80fa69b660ecb4a873bfe646abd6` |

## Image format

File = 48-byte header + code + 4-byte trailer. The MCU is an 8051; code address = file offset − `0x30` (the reset vector `LJMP` is at file `0x30`).

### Header

| Offset | Meaning | A | B |
|---|---|---|---|
| `00-01` | Type marker (`3C C3` = SPI flash; `5A A5` / `69 96` = EEPROM sizes) | `3C C3` | `3C C3` |
| `02-03` | Code length, big-endian | `C9 A9` (51625) | `E8 2C` (59436) |
| `04-07` | VID/PID override (unused in flash mode) | `FF…` | `FF…` |
| `08` | Common flags; bit 7 = "Flash code" | `80` | `80` |
| `0C-0F` | Version (shown in the tool; not checksummed) | `FF FF FF FF` | `19 02 1A 14` |
| `10-2F` | Video/audio name overrides (unused in flash mode) | `FF…` | `FF…` |

In flash mode both vendors left the header overrides blank and edited the descriptors inside the code area instead.

### Trailer checksum

Two big-endian 16-bit wrapping sums, immediately after the code:

```
H = sum(header[0x02..0x0B]) + sum(header[0x10..0x2F])   # bytes 0x0C-0x0F skipped
C = sum(code bytes)
trailer = H(2 bytes BE) + C(2 bytes BE)
```

Verified against both dumps (A `25CE B559`, B `2570 0183`) and against the download tool's own code. The tool displays only `C` as "Check Sum". It rejects code longer than `0xFC00` bytes.

### Contents of the code area (file offsets, dongle A)

| Offset | Contents |
|---|---|
| `0x0030` | 8051 code |
| `0x1709` | String descriptors |
| `0x17B8` / `0x17CD` | Device descriptors: USB 2 (`bcdDevice 2100`), USB 3 (`3100`) |
| `0xBE30` | EDID, 256 bytes ("HDMI TO USB", maker `HJW`, preferred 1920×1080@60) |
| `0xBF2F` | Four resolution tables (speed × format), 11 entries each |
| `0xBFE6` | LANGID, BOS, device qualifiers, HID report descriptor |
| `0xC02B` | USB 2 configuration, 762 bytes: MJPG only |
| `0xC325` | Full-speed configuration, 34 bytes: HID only |
| `0xC347` | USB 3 configuration, 796 bytes: YUY2 only, `bFormatIndex = 2` |
| `0xC663` | JPEG quantisation tables, four quality levels |
| `0xC863` | Timing / clock tables |

Dongle B follows the same pattern: strings `0x10E2`, device descriptors `0x119B`/`0x11B0`, EDID `0xD8DC` ("UGREEN-UHD"), USB 2 config `0xDB28` (1162 bytes), USB 3 config `0xDFD4` (1304 bytes).

### Key observations

- A's USB 3 table has a single format numbered **2**, a sign that MJPG (format 1) was stripped from a shared template.
- A already carries a USB 3 MJPG resolution table and JPEG quality tables; only the advertised descriptor was missing.
- B's USB 3 table is A's with an MJPG block appended after YUY2 (and 640×480 swapped for 1440p30).
- The firmware reads each config descriptor's length from its own `wTotalLength` field. The USB 3 config is referenced at exactly three code sites, all in one routine:

```
file 0x545A  90 C3 1A   MOV  DPTR,#0xC31A   ; wTotalLength high byte
file 0x545F  90 C3 19   MOV  DPTR,#0xC319   ; wTotalLength low byte
file 0x5476  74 17 2B F5 82 74 C3 3A          ; DPTR = 0xC317 + offset
```

## The patch

`build_patch.py` makes exactly these changes to A's image:

1. **New USB 3 configuration (1320 bytes) appended at code address `0xC9A9`:** the original config plus an MJPG format (index 1) after YUY2 (index 2), each with its own colour-matching descriptor. The VS input header grows to 15 bytes with `bNumFormats = 2` and `wTotalLength = 1077`. MJPG frames are A's USB 3 YUY2 frames with the subtype changed `05 → 07`: 11 sizes, 60/50/30/20/10 fps.
2. **Pointer sites repointed** (6 bytes): `C3 1A → C9 AC`, `C3 19 → C9 AB`, `17 → A9`, `C3 → C9`.
3. **USB 3 serial changed** from "20210623" to "CBMJPG01", so a running patched image is identifiable at a glance.
4. **Code length** 51625 → 52945; trailer recalculated to `25FB 5922`.

The USB 2 descriptors, EDID and the old USB 3 config are byte-identical to the original. The builder refuses any input whose hash differs from A's verified dump.

| | Size | SHA-256 |
|---|---|---|
| `A_mjpg_usb3_v1.bin` | 52,997 bytes | `85dda69f786547584cf7253f897a2d889ca4cd3dc26e997a1fac2095960ebe26` |

### Flashing

1. Plug A into a USB 3 port; MS2130/31, Flash, Connect.
2. Open File → `A_mjpg_usb3_v1.bin`. Confirm Code length **52945** and Check Sum **5922**.
3. Download; wait for 校验成功，烧录成功.
4. Read → Save To BIN; the read-back must hash to `85dda69f…be26`.
5. Unplug, replug.

### Recovery

Only the USB 3 path changed. If the patched image misbehaves on USB 3, connect through a USB 2-only port or extension: the stick enumerates with its untouched USB 2 tables, including the HID interface, and the original dump can be written back with Open File + Download.

## Results

**USB Tree View (USB 3):** serial `CBMJPG01`, config `wTotalLength 0x0528`, two formats, MJPEG and YUY2 at all 11 sizes up to 1920×1080@60.

**ffmpeg, raw stream copy:**

```
ffmpeg -f dshow -vcodec mjpeg -video_size 1920x1080 -framerate 60 -i video="USB3.0 UHD" -t 10 -c copy mjpg_test.mkv
```

- `mjpeg (Baseline)`, `yuvj422p`, 1920×1080
- 600 frames in 10.03 s, `fps=60`, `speed=1x`
- ~43 Mbit/s (≈88 KB/frame; uncompressed YUY2 would be ≈995 Mbit/s)

**VLC (CodeBlue viewer):** sharp text, no visible JPEG artifacts.

```
"C:\Program Files\VideoLAN\VLC\vlc" dshow:// :dshow-vdev="USB3.0 UHD" :dshow-adev=none :dshow-size=1920x1080 :dshow-chroma=MJPG :dshow-fps=60 :live-caching=50
```

`:dshow-chroma=MJPG` matters: the descriptor lists YUY2 first, so a viewer that doesn't specify a chroma may still get YUY2. In the CodeBlue viewer, set **Format** to MJPG, which requests it explicitly.

**Cross-check against a published image:** an MS2130 firmware image published alongside the format documentation in [Forest178/ms21xx-firmware-research](https://github.com/Forest178/ms21xx-firmware-research) (original provenance unknown; manufacturer string "UltraSemi", 64,416 bytes of code, SHA-256 `b9a097f11db196fba8bca36c4a7d089e71de47c50edea691c3261ce4192d5f7a`) contains a USB 3 configuration descriptor that is **byte-identical** to the one this patch builds: all 1320 bytes. It also shares A's EDID and serial strings byte for byte, and its trailer (`25F7 726C`) follows the same checksum formula. Its code is a different build, so it's useful as a reference, not as something to flash onto A.

## Notes and open items

- **EDID:** A's preferred mode is 1920×1080@60 (ideal), but its extension block also lists 4K30 modes (CEA VIC 95 and 100). A source that picks "highest resolution" instead of "preferred" would send 4K30, which the stick downscales. Fix if it ever bites: drop those VICs and recompute the extension checksum.
- **B's startup logo:** no JPEG (`FF D8 FF`) in B's image; stored some other way. Not yet located.
- **HID protocol (from the download tool):** 9-byte feature reports via `HidD_SetFeature` / `HidD_GetFeature`; command `0xB5` reads chip registers; the tool identifies the chip by ID bytes (`A7`/`B7`, `13`, `0A`). A starting point for a read-only dumper on FreeBSD/Linux.
- **Windows USB stack:** heavy plug/unplug cycling with same-VID/PID devices carrying different descriptors (through a VIA Labs `2109:2817` hub) left the USB stack wedged. A full restart fixed it. Plug the stick directly into the host for CodeBlue.
- **Case:** no visible fasteners; likely glued or welded. Not opened, so there is no CH341A full-chip backup.

## Files

The scripts are GPL-3.0-or-later. The firmware images are MacroSilicon's code and aren't distributed here: dump your own stick and compare it against the hashes above.

| File | Purpose |
|---|---|
| [`build_patch.py`](https://github.com/hemi426/codeblue-kvm/blob/main/tools/ms2130-mjpg-usb3/build_patch.py) | Builds the patched image from your own dump of dongle A. Refuses any input that doesn't match A's SHA-256, which also protects sticks with a different firmware revision. |
| [`check_image.py`](https://github.com/hemi426/codeblue-kvm/blob/main/tools/ms2130-mjpg-usb3/check_image.py) | Independent verifier: header, trailer, pointer decode, descriptor walk, byte diff against the original. |

To reproduce, from the repo root:

```
python3 tools/ms2130-mjpg-usb3/build_patch.py my_dump.bin A_mjpg_usb3_v1.bin
python3 tools/ms2130-mjpg-usb3/check_image.py my_dump.bin A_mjpg_usb3_v1.bin    # must end with ALL CHECKS PASSED
```

The output should hash to `85dda69f…be26`. Keep your original dump: it's your restore image.
