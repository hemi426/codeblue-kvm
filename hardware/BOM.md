# What to buy

Everything CodeBlue needs, and what has actually been tested. "Tested" means
used with CodeBlue on real targets; everything else is a reasonable choice
that nobody has confirmed yet. If you try something new, please report it
(see "Tested targets" in [README.md](README.md)).

## Required

| Part | Notes | Status |
|------|-------|--------|
| **ESP32-S3-DevKitC-1, or a clone** | Must have **two** USB ports, usually labelled USB (or OTG) and UART (or COM). Any flash/PSRAM size works; the firmware fits easily in 4 MB. Leave the IN-OUT and USB-OTG solder pads open. | See the boards below. |
| **HDMI capture dongle** | Must offer MJPG at 1920x1080 and 30 fps; that's what makes 1080p usable over USB 2. Plain "USB Video" class devices work without drivers. | See the dongles below. |
| **Two USB cables for the board** | One to your computer (UART port), one to the target (USB port). Must carry data: charge-only cables are a common cause of "nothing happens". Match the ends to your computer and target (USB-C or USB-A); adapters and hubs are fine (see below). | Any data cable. |
| **HDMI cable** | Target to dongle. | Any. |
| **A Windows PC** | Windows 10 or 11, 64-bit (x64), for the viewer. | Windows 11: tested. |

Avoid boards with a single USB port (many "S3 Mini", "S3 Zero" and
"Super Mini" boards): CodeBlue needs one port for the target and one for
your computer.

## Boards

| Board | Notes | Status |
|-------|-------|--------|
| AITRIP ESP32-S3-DevKitC-1 clone, N8R2 ([Amazon](https://www.amazon.com/dp/B0D62QGMWR)) | 8 MB flash, 2 MB PSRAM. RGB LED on GPIO48: the default build. | Tested |
| DORHEA ESP32-S3-DevKitC-1 clone, N8R2 ([Amazon](https://www.amazon.com/dp/B0C9H7Y66W)) | Same layout as the AITRIP. RGB LED on GPIO48: the default build. | Tested |
| DORHEA ESP32-S3-DevKitC-1 clone, N16R8 (same listing) | 16 MB flash, 8 MB octal PSRAM (CodeBlue uses neither the extra flash nor the PSRAM). Expected to need no changes. | Not yet tested |
| Espressif ESP32-S3-DevKitC-1 v1.1 | RGB LED on GPIO38: build with `-DBOARD=devkitc-v1.1`. | Untested |

If a board's LED stays dark after flashing but everything else works, it's
probably wired like the v1.1: rebuild with `-DBOARD=devkitc-v1.1`.

## Capture dongles

| Dongle (as Windows names it) | USB ID | Chip | Notes | Status |
|------------------------------|--------|------|-------|--------|
| USB Video | `534D:2109` | MacroSilicon MS2109 (USB 2) | MJPG: 1080p at 30 fps, 720p at 60. YUY2: 1080p at 5 fps, 720p at 10, 640x480 at 30. | Tested |
| USB3.0 capture, sold as USB 3.0 ([Amazon](https://www.amazon.com/dp/B091NWB4RJ)) | `345F:2109` | MS2109 family (USB 2), despite the label | MJPG: 1080p at 60 fps, pixel-perfect at 1:1. YUY2: 1080p at 10 fps, 720p at 25, 640x480 at 60. The better of the two. | Tested |

**Use MJPG, and when in doubt, set FPS to Auto.** At 1080p, USB 2 dongles
only reach full frame rate with MJPG, and asking for a frame rate a format
doesn't offer (YUY2 at 1080p and 30 fps, say) gives a black screen.
YUY2 also shows text artifacts in the viewer, even when the frame rate is
fine: libVLC 3 hands packed YUY2 straight to its Direct3D 11 output, which
distorts thin strokes. Snapshots are unaffected, since they skip the display
path, and there is no workaround short of libVLC 4.

### Checking a dongle

Cheap dongles are often not what their listings claim, so before relying on
one (or reporting it), check three things: its USB ID, its real USB speed,
and the modes it offers.

**The easy way: USB Tree View.** Uwe Sieber's free USB Device Tree Viewer
(`winget install UweSieber.UsbTreeView`, or from his website) shows all
three. Plug the dongle in, find it in the tree, and read:

- **Vendor ID and Product ID** at the top: the USB ID, which identifies the
  hardware even when listings change. MacroSilicon's chips are the usual ones
  inside cheap dongles: a product ID of `2109` means the MS2109 (USB 2),
  while the MS2130 (`345F:2130`) supports real USB 3.
- **Device maximum Speed:** "SuperSpeed" means the dongle can do USB 3;
  "High-Speed" means it's a USB 2 device, whatever the box says. (Device
  Connection Speed is what it got from this port; plug a USB 3 dongle into a
  USB 3 port, or it falls back to USB 2.)
- **The mode summary** just below: every size and frame rate it offers, for
  MJPEG and YUY2.

**Without extra tools**, PowerShell shows the USB ID:

```powershell
Get-PnpDevice -Class Camera,Image -PresentOnly | Select-Object FriendlyName, InstanceId | Format-List
```

The ID is the `VID_xxxx&PID_xxxx` part of `InstanceId`. FFmpeg, if you have
it, lists the modes (use the name from the command above):

```powershell
ffmpeg -hide_banner -list_options true -f dshow -i video="USB Video"
```

It ends with "Error opening input file", which is normal: it lists the modes
and stops.

Then try MJPG and YUY2 at 1920x1080 in the viewer, and watch the frame rate
and how text looks.

## Video adapters (optional)

For targets without HDMI out. The dongle needs an HDMI signal coming in, so
these all convert *to* HDMI.

| Adapter | For | Status |
|---------|-----|--------|
| DisplayPort to HDMI (cable or adapter) | Most business desktops of the last decade, many with DisplayPort only. Tested: a 6 ft cable, and the UGREEN 4K DisplayPort to HDMI adapter ([Amazon](https://www.amazon.com/dp/B0FCLXJHTX)). | Tested |
| USB-C to HDMI | Newer laptops and small desktops. The port must carry video: look for a DisplayPort (D) or Thunderbolt (lightning bolt) symbol. | Untested |
| Mini DisplayPort to HDMI | Older Macs (about 2010 to 2015), some small PCs and graphics cards. Tested: the Mini-DP to DP adapter that comes with an NVIDIA T400 graphics card, followed by the DP to HDMI adapter above. | Tested (as a chain) |
| DVI to HDMI (passive cable or adapter) | Older desktops and workstations with DVI-D or DVI-I ports; same digital signal as HDMI, so no conversion needed | Untested |
| VGA to HDMI, **powered** | Old machines with VGA only. Must be an active converter, since VGA is analogue. | Untested |

The usual cheap DisplayPort-to-HDMI adapters only work in that direction
(DisplayPort computer to HDMI screen), which is the direction CodeBlue needs.

## USB adapters and hubs

Adapters and hubs on either end work. Tested: the board's UART end through a
0.5 ft USB-C to USB-A cable ([Amazon](https://www.amazon.com/dp/B08W2129Z8))
into a USB hub on the viewer PC, and its USB end through a 6 ft USB-C cable and a
UGREEN USB-A to USB-C adapter (from the
[4-pack](https://www.amazon.com/dp/B0F9YFLVJN)) into the target's USB-A port.
Plug the target end straight into the target where you can, though: a BIOS
usually handles a keyboard on a hub, but not always.
