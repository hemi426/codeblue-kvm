# Firmware

ESP-IDF (C) firmware for the ESP32-S3. It presents a boot-protocol USB
keyboard, a relative mouse, and an absolute pointer to the target, and talks to
the viewer over the board's UART port with the
[binary protocol](development/protocol.md). Tested targets are listed in
[Hardware](hardware/index.md#tested-targets).

## Flashing

Each firmware release has one image per board preset, holding the
bootloader, partition table, and app. Not sure which preset your board needs?
See [Boards](#boards); `esp32-s3-devkitc-1-v1.0` suits most boards, and
guessing wrong only leaves the LED dark.

### From your browser

The easiest way, with nothing to install. It needs Chrome or Edge on a
desktop computer (they're the browsers that can talk to serial ports).

1. Plug the board's **UART** port into your computer.
2. Click the button for your board, pick the board's port, and follow the
   prompts. Erasing the board first is optional.

<p>
<esp-web-install-button manifest="/codeblue-kvm/install/esp32-s3-devkitc-1-v1.0.json">
  <button slot="activate" class="btn btn-primary">Install for v1.0 (most boards)</button>
</esp-web-install-button>
<esp-web-install-button manifest="/codeblue-kvm/install/esp32-s3-devkitc-1-v1.1.json">
  <button slot="activate" class="btn">Install for v1.1</button>
</esp-web-install-button>
</p>
<script type="module" src="https://unpkg.com/esp-web-tools@10/dist/web/install-button.js?module"></script>

The buttons always install the latest firmware release.

### With esptool

For other browsers, or without the site:

1. Download the image for your board from the latest firmware release
   ([Releases](https://github.com/hemi426/codeblue-kvm/releases?q=firmware&expanded=true)).
2. Plug the board's **UART** port into your computer.
3. Write the image with esptool (it comes with ESP-IDF, or
   `pip install esptool`), using your board's port in place of COM10:

   ```sh
   python -m esptool --chip esp32s3 -p COM10 write-flash 0x0 codeblue-firmware-<version>-esp32-s3-devkitc-1-v1.0.bin
   ```

`SHA256SUMS.txt` in each release lists every file's checksum.

To build the firmware yourself instead, see
[Building](development/building.md#firmware-esp32-s3).

## Boards

The release images come in two board presets:

- **`esp32-s3-devkitc-1-v1.0`:** the original ESP32-S3-DevKitC-1 and most
  clones (RGB LED on GPIO48).
- **`esp32-s3-devkitc-1-v1.1`:** v1.1 boards (LED on GPIO38).

Not sure which you have? Check the listing or the maker's page for your
board: it usually says which GPIO drives the RGB LED (look for GPIO48 or
GPIO38). Guessing wrong is harmless. If the LED stays dark after flashing but
everything else works, it's the other kind: flash the other image. If your
board's LED is on some other pin, set it in your own build (see
[Build options](#build-options)). Tested boards are in
[What to buy](hardware/bom.md#boards).

When building, pick the preset with `-DBOARD=`; the default is
`esp32-s3-devkitc-1-v1.0`:

```sh
idf.py -DBOARD=esp32-s3-devkitc-1-v1.1 build
```

Board settings only take effect when `sdkconfig` is created, so when
switching boards, or after pulling changes to `sdkconfig.defaults` or
`boards/`, delete `firmware/sdkconfig` and run `idf.py fullclean build`. The
build stops with that instruction if it notices a mismatch. To add a board,
copy a file in `firmware/boards/`.

Boards with the [VBUS sense modification](hardware/vbus-sense.md) need their
own build, with the sense pin set (see [Build options](#build-options)); the
release images don't read it.

## Build options

Most people never need these: the release images suit the tested boards. For
anything else, the firmware has its own **CodeBlue** menu in
`idf.py menuconfig`, and [Building](development/building.md#firmware-esp32-s3)
covers setting up ESP-IDF and building your own image.

| Option | Default | What it's for |
|--------|---------|---------------|
| **Board name** | Set by the board preset | The name the viewer shows for the board ("ESP32-S3-DevKitC-1 v1.0", say). |
| **Status LED GPIO (WS2812)** | 48 (38 with the v1.1 preset) | The pin driving the RGB LED. Change it for a board with its LED on another pin. |
| **Default status LED brightness** | 5% | The brightness until the viewer sets one. **LED %** in the viewer overrides it, and the board saves that. |
| **USB vendor ID** and **USB product ID** | `0x303A` and `0x4004` | What the target sees. Espressif's vendor ID with TinyUSB's default HID product ID, which Espressif allows for devices using standard USB classes, as CodeBlue does. Change them only for a product with its own allocated IDs. |
| **VBUS sense GPIO** | -1 (none) | For boards with the [VBUS sense modification](hardware/vbus-sense.md): the pin the divider feeds (21 on the tested clones). Leave it at -1 on an unmodified board; a floating pin reports USB power at random. |
| **Keep logging on the UART** | Off | For bring-up with `idf.py monitor` only (see [Logs and testing](#logs-and-testing)); the viewer won't work reliably while it's on. |

The settings live in `firmware/sdkconfig`, so set them again after deleting
it (which switching boards needs).

## Status LED

The RGB LED shows CodeBlue's status. Blue: waiting for the viewer. Green:
the viewer is connected. Set its brightness with **LED %** in the viewer; each
board saves its own setting.

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
python tools/cb_test.py -h                  # everything else
```

The framing code (`firmware/main/frame.c`) has its own test, which runs on a
PC with any C compiler. From the repo root:

```sh
gcc -std=c11 -Wall -Wextra -Werror -Ifirmware/main firmware/test/test_frame.c firmware/main/frame.c -o firmware/build/test_frame
firmware/build/test_frame
```

CI (`.github/workflows/ci.yml`) runs it on every push, along with builds for
both board presets. Each build is kept as a zip on the run's page; its
`merged-binary.bin` flashes the same way as a release image:

```sh
python -m esptool --chip esp32s3 -p COM10 write-flash 0x0 merged-binary.bin
```
