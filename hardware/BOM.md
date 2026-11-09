# What to buy

Everything CodeBlue needs, and what has actually been tested. "Tested" means
used with CodeBlue on real targets; everything else is a reasonable choice
that nobody has confirmed yet. If you try something new, please report it
(see "Tested targets" in [README.md](README.md)).

## Required

| Part | Notes | Status |
|------|-------|--------|
| **ESP32-S3-DevKitC-1, or a clone** | Must have **two** USB ports, usually labelled USB (or OTG) and UART (or COM). Any flash/PSRAM size works; the firmware fits easily in 4 MB. Leave the IN-OUT and USB-OTG solder pads open. | N8R2 clone (LED on GPIO48): tested. Espressif v1.1 (LED on GPIO38): untested; build with `-DBOARD=devkitc-v1.1`. |
| **HDMI capture dongle** | Must offer MJPG at 1920x1080 and 30 fps; that's what makes 1080p usable over USB 2. Plain "USB Video" class devices work without drivers. | See the dongles below. |
| **Two USB cables for the board** | One to your laptop (UART port), one to the target (USB port). Must carry data: charge-only cables are a common cause of "nothing happens". Match the ends to your laptop and target (USB-C or USB-A). | Any data cable. |
| **HDMI cable** | Target to dongle. | Any. |
| **A Windows PC** | Windows 10 or 11, 64-bit (x64), for the viewer. | Windows 11: tested. |

Avoid boards with a single USB port (many "S3 Mini", "S3 Zero" and
"Super Mini" boards): CodeBlue needs one port for the target and one for
your laptop.

## Capture dongles

| Dongle | Notes | Status |
|--------|-------|--------|
| USB 2.0 HDMI capture ("USB Video") | Needs Format = MJPG for usable 1080p. | Tested |
| "USB3.0 capture" (sold as USB 3.0) | Actually a USB 2 device. MJPG gives 1080p at 30 fps, pixel-perfect at 1:1. YUY2 only manages 1080p at 10 fps. | Tested |

Before relying on a new dongle, check what it offers: see "When testing a
new dongle" in [README.md](README.md).

## Video adapters (optional)

For targets without HDMI out. The dongle needs an HDMI signal coming in, so
these all convert *to* HDMI.

| Adapter | For | Status |
|---------|-----|--------|
| DisplayPort to HDMI (cable or adapter) | Most business desktops of the last decade, many with DisplayPort only | Untested with CodeBlue |
| USB-C to HDMI | Newer laptops and small desktops. The port must carry video: look for a DisplayPort (D) or Thunderbolt (lightning bolt) symbol. | Untested |
| Mini DisplayPort to HDMI | Older Macs (about 2010 to 2015), some small PCs | Untested |
| DVI to HDMI (passive cable or adapter) | Older desktops and workstations with DVI-D or DVI-I ports; same digital signal as HDMI, so no conversion needed | Untested |
| VGA to HDMI, **powered** | Old machines with VGA only. Must be an active converter, since VGA is analogue. | Untested |

The usual cheap DisplayPort-to-HDMI adapters only work in that direction
(DisplayPort computer to HDMI screen), which is the direction CodeBlue needs.
