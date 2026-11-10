# VBUS sense modification (optional)

Two resistors let the board sense the target's USB power, so the viewer can
tell a sleeping target ("asleep; a keypress may wake it") from an unplugged
or powered-off one ("unplugged or powered off"). Without it, both look the
same over USB, and the viewer says "asleep or disconnected". See "Asleep or
unplugged" in [../docs/PROTOCOL.md](../docs/PROTOCOL.md) for why.

Even with the mod, "no USB power" isn't conclusive: some PCs cut USB power
when fully off or hibernating, and some keep it on.

## What you need

- A **10 kΩ** and a **15 kΩ** resistor (through-hole, 1/4 W or smaller; 5%
  is fine). **100 kΩ and 150 kΩ** work too: only the ratio matters, and on
  the tested clone the pad's leakage put just 26 mV across the 150 kΩ with
  no target (the chip reads a low below about 0.8 V). Use the 10x values in
  place of 10 kΩ and 15 kΩ below.
- A soldering iron and a multimeter.

## Where it goes

These notes are for the ESP32-S3-DevKitC-1 clones in [BOM.md](BOM.md)
(AITRIP and DORHEA). Check your own board with a multimeter before
soldering: clones differ, and the solder pads in particular don't always
match Espressif's board.

On the underside, near the USB ports, are two pairs of solder pads:

- **IN-OUT** bypasses the diode on the **5V header pin**, so that pin can
  supply power out. Not the one we want.
- **USB-OTG** bypasses the diode on the **native USB port** (the one that
  goes to the target). One of its pads carries the target's 5 V: that's the
  sense point.

To find the right pad, plug both ports in (your computer to UART, the target
to USB, target on) and measure each USB-OTG pad to GND:

| | Target plugged in | Target unplugged |
|---|---|---|
| Target-side pad (the sense point) | about 5 V | floating (a meter may show 1 to 2 V) |
| Board-side pad | about 4.6 to 4.8 V | about 4.6 V (fed from the UART port) |

On the tested clones, the sense point is the USB-OTG pad nearer the USB
ports.

**Leave the USB-OTG pads unbridged.** Bridging them connects the target's
5 V straight to your computer's.

## Wiring

```
sense pad ──[10 kΩ]──┬── GPIO21
                     │
                  [15 kΩ]
                     │
                    GND
```

- **GPIO21** is the closest usable pin to the USB end, on the header row
  beside the two GND holes (GPIO19 and 20 next to it are the USB data lines:
  don't use those).
- The 10 kΩ runs from the sense pad to the GPIO21 hole; the 15 kΩ from the
  GPIO21 hole to a neighbouring GND hole. Everything stays on the underside.

The divider turns 5.12 V into 3.07 V at the pin (3.15 V at USB's maximum
5.25 V), safely under the chip's 3.3 V, and well above the 2.5 V it needs to
read a high. With nothing plugged into the target port, the 15 kΩ holds the
pin at 0 V.

If you use another GPIO, avoid 0, 3, 45 and 46 (boot mode), 19 and 20 (USB),
43 and 44 (the UART), the LED pin (48 or 38), 33 to 37 on boards with octal
PSRAM (N16R8 and other "R8" boards), and 47 (1.8 V on some modules).

## Firmware

The release images don't read the pin, since on an unmodified board it would
float. Build the firmware with the pin set (see
[../docs/BUILDING.md](../docs/BUILDING.md)):

```sh
idf.py menuconfig          # CodeBlue → VBUS sense GPIO: 21
idf.py build
idf.py -p COM10 flash      # use your board's UART port
```

Then check it: `python tools/cb_test.py COM10 info` should show capabilities
`0x000F`, and `vbus_present` in the target status while the target is
plugged in and on.
