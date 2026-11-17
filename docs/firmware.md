# Firmware

ESP-IDF (C) firmware for the ESP32-S3. It presents a boot-protocol USB
keyboard, a relative mouse and an absolute pointer to the target, and talks to
the viewer over the board's UART port with the
[binary protocol](development/protocol.md). Tested targets are listed in
[Hardware](hardware/index.md#tested-targets).

## Flashing

Each firmware release has one image per board preset, holding the
bootloader, partition table and app, so flashing takes one command.

1. Download the image for your board from the latest firmware release
   ([Releases](https://github.com/hemi426/codeblue-kvm/releases?q=firmware&expanded=true)).
   `esp32-s3-devkitc-1-v1.0` suits most boards, including the common clones;
   see [Boards](#boards) if your board's LED stays dark.
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

If a board's LED stays dark after flashing but everything else works, it's
the other kind: flash the other image. Tested boards are in
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
own build, with the sense pin set in `idf.py menuconfig`; the release images
don't read it.

## Status LED

Blue: waiting for the viewer. Green: the viewer is connected (the viewer can
set any colour). Set its brightness with **LED %** in the viewer; each board
saves its own setting.

## Logs and testing

The UART port carries the host protocol, so the firmware logs only while it
starts up (at 115200 baud, which `idf.py -p COM10 monitor` uses by default),
then switches the port to 921600 for the protocol and goes quiet. To keep
logging for bring-up, turn on **CodeBlue > Keep logging on the UART** in
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
