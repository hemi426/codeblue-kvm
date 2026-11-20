# Firmware

ESP-IDF (C) firmware for the ESP32-S3. It presents a boot-protocol USB
keyboard, a relative mouse, and an absolute pointer to the target, and talks to
the viewer over the board's UART port with the
[binary protocol](development/protocol.md). Tested targets are listed in
[Hardware](hardware/index.md#tested-targets).

## Flashing

The same firmware suits every supported board. Board differences (which pin
drives the LED, and whether the board has the VBUS sense modification) are
set from the viewer afterwards; see [Board settings](#board-settings).
Updating keeps those settings (firmware 0.4.0 and later), unless you choose
to erase the board.

### From your browser

The easiest way, with nothing to install. It needs Chrome or Edge on a
desktop computer (they're the browsers that can talk to serial ports).

1. Plug the board's **UART** port into your computer.
2. Click the button, pick the board's port, and follow the prompts. Leave
   **Erase device** unticked to keep the board's settings; tick it for a
   clean start.

<p>
<!-- The build time in the URL gives each site build its own manifest
     address, so browsers never install from a cached, older manifest. -->
<esp-web-install-button manifest="/codeblue-kvm/install/codeblue.json?build={{ site.time | date: '%s' }}">
  <button slot="activate" class="btn btn-primary">Install CodeBlue firmware</button>
</esp-web-install-button>
</p>
<script type="module" src="https://unpkg.com/esp-web-tools@10/dist/web/install-button.js?module"></script>

The button always installs the latest firmware release.

### With esptool

For other browsers, or without the site:

1. Download the images from the latest firmware release
   ([Releases](https://github.com/hemi426/codeblue-kvm/releases?q=firmware&expanded=true)).
2. Plug the board's **UART** port into your computer.
3. Write them with esptool (it comes with ESP-IDF, or `pip install esptool`),
   using your board's port in place of COM10. To update, keeping the board's
   settings, write the three parts:

   ```sh
   python -m esptool --chip esp32s3 -p COM10 write-flash 0x0 codeblue-firmware-<version>-bootloader.bin 0x8000 codeblue-firmware-<version>-partition-table.bin 0x10000 codeblue-firmware-<version>-app.bin
   ```

   For a clean install, which resets the board's settings, write the whole
   image instead:

   ```sh
   python -m esptool --chip esp32s3 -p COM10 write-flash 0x0 codeblue-firmware-<version>.bin
   ```

Releases before 0.4.0 have only the whole image.

`SHA256SUMS.txt` in each release lists every file's checksum.

To build the firmware yourself instead, see
[Building](development/building.md#firmware-esp32-s3).

## Board settings

The firmware starts out set up for the original ESP32-S3-DevKitC-1 (v1.0):
the RGB LED on GPIO48, and no VBUS sense. To change that,
connect with the viewer and click **Board settings...** (firmware 0.3.0 or
later):

- **Board:** pick your board to fill in its pins. **ESP32-S3-DevKitC-1 v1.1**
  boards have the LED on GPIO38. **Custom** is for boards that aren't listed.
- **Status LED:** the LED's pin and brightness. If the LED stays dark after
  flashing but everything else works, the board probably has it on another
  pin: check the listing or the maker's page (look for GPIO48 or GPIO38).
- **VBUS sense mod:** for boards with the
  [VBUS sense modification](hardware/vbus-sense.md), choose **Yes** and the
  pin the divider feeds (21 on the tested boards).

The board applies each change at once and keeps it across resets and
firmware updates (0.4.0 and later). Erasing the board, or writing the whole
image, resets them. It refuses pins it can't use (the strapping, USB, UART,
and flash or PSRAM pins), and the viewer says why. Tested boards are in
[What to buy](hardware/bom.md#boards). `tools/cb_test.py` can set the same
settings without the viewer (see [Logs and testing](#logs-and-testing)).

## Build options

Most people never need these: the release image suits the tested boards, and
the LED and VBUS sense pins are set from the viewer. For anything else, the
firmware has its own **CodeBlue** menu in `idf.py menuconfig`, and
[Building](development/building.md#firmware-esp32-s3) covers setting up
ESP-IDF and building your own image.

| Option | Default | What it's for |
|--------|---------|---------------|
| **Board name** | ESP32-S3-DevKitC-1 | The name the viewer shows for the board. |
| **Status LED GPIO (WS2812)** | 48 | The LED's pin until one is set in **Board settings**. |
| **Default status LED brightness** | 5% | The brightness until one is set in **Board settings**. |
| **USB vendor ID** and **USB product ID** | `0x303A` and `0x4004` | What the target sees. Espressif's vendor ID with TinyUSB's default HID product ID, which Espressif allows for devices using standard USB classes, as CodeBlue does. Change them only for a product with its own allocated IDs. |
| **VBUS sense GPIO** | -1 (none) | The [VBUS sense](hardware/vbus-sense.md) pin until one is set in **Board settings**. Leave it at -1 for an image that might go on an unmodified board; a floating pin reports USB power at random. |
| **Keep logging on the UART** | Off | For bring-up with `idf.py monitor` only (see [Logs and testing](#logs-and-testing)); the viewer won't work reliably while it's on. |

The settings live in `firmware/sdkconfig`, so set them again after deleting
it. Board presets in `firmware/boards/` hold a board's defaults (pick one
with `idf.py -DBOARD=<preset> build`); there's one for now,
`esp32-s3-devkitc-1`. Defaults only apply when `sdkconfig` is created, so
after changing presets or pulling changes to `sdkconfig.defaults` or
`boards/`, delete `firmware/sdkconfig` and run `idf.py fullclean build`. The
build stops with that instruction if it notices a mismatch.

## Status LED

The RGB LED shows CodeBlue's status. Blue: waiting for the viewer. Green:
the viewer is connected. Set its brightness in the viewer's
**Board settings**; each board saves its own setting.

Most boards have other LEDs too, which CodeBlue doesn't control:

- **Power** (usually red): lit whenever the board has power.
- **TX and RX** (on many clones), driven by the USB-to-serial chip: while the
  viewer is connected, they flicker constantly, since the viewer and the
  board exchange heartbeats many times a second in each direction. Typing or
  moving the mouse adds to it. When nothing is connected, they stay dark.

## Logs and testing

The UART port carries the host protocol, so the firmware logs only while it
starts up (at 115200 baud, which `idf.py -p COM10 monitor` uses by default),
then switches the port to 921600 for the protocol and goes quiet. To keep
logging for bring-up, turn on **CodeBlue → Keep logging on the UART** in
`idf.py menuconfig`; the viewer won't work reliably while it's on.

To exercise the board without the viewer, from the repo root (ESP-IDF's
Python has pyserial):

```sh
python tools/cb_test.py COM10 info          # handshake: firmware version and target status
python tools/cb_test.py COM10 type "hello"  # types on the target
python tools/cb_test.py COM10 timeout-test  # checks the board lets go when the host goes quiet
python tools/cb_test.py COM10 get           # board settings: LED and VBUS sense pins
python tools/cb_test.py COM10 set vbus-gpio 21  # or a GPIO, none, or default; led-gpio too
python tools/cb_test.py -h                  # everything else
```

The framing code (`firmware/main/frame.c`) has its own test, which runs on a
PC with any C compiler. From the repo root:

```sh
gcc -std=c11 -Wall -Wextra -Werror -Ifirmware/main firmware/test/test_frame.c firmware/main/frame.c -o firmware/build/test_frame
firmware/build/test_frame
```

CI (`.github/workflows/ci.yml`) runs it on every push, along with a firmware
build. The build is kept as a zip on the run's page; its
`merged-binary.bin` flashes the same way as a release's whole image (which
resets the board's settings):

```sh
python -m esptool --chip esp32s3 -p COM10 write-flash 0x0 merged-binary.bin
```
