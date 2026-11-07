# CodeBlue firmware

ESP-IDF (C) firmware for the ESP32-S3. It presents a boot-protocol USB
keyboard, a relative mouse and an absolute pointer to the target, and speaks
the binary protocol in [../docs/PROTOCOL.md](../docs/PROTOCOL.md).

**Status:** first working version, not yet supported by the viewer (which
still speaks only the legacy protocol). Test it with `tools/cb_test.py`.
Until the viewer catches up, the Arduino firmware from
[alpharesearch/KeyboardAndMouseControl](https://github.com/alpharesearch/KeyboardAndMouseControl)
remains the way to use CodeBlue day to day.

| Module | Job |
|--------|-----|
| `main/usb_hid.c` | The USB device the target sees: boot keyboard, boot mouse, absolute pointer |
| `main/session.c` | The host protocol on UART0: handshake, heartbeats, messages |
| `main/frame.c` | COBS and CRC framing (plain C; testable on a PC) |
| `main/status_led.c` | The RGB LED and its saved brightness |

## Building

Set up ESP-IDF once with the bootstrap script, then, in each new terminal,
from the repo root:

```sh
. scripts/activate-idf.sh          # Windows: . .\scripts\activate-idf.ps1
cd firmware
idf.py build
idf.py -p COM10 flash              # the board's UART port
```

### Boards

The build defaults to `devkitc-v1.0`, which suits the original
ESP32-S3-DevKitC-1 and most clones (RGB LED on GPIO48). For a v1.1 board
(LED on GPIO38):

```sh
idf.py -DBOARD=devkitc-v1.1 build
```

Board settings only take effect when `sdkconfig` is created, so when
switching boards, or after pulling changes to `sdkconfig.defaults` or
`boards/`, delete `firmware/sdkconfig` and run `idf.py fullclean build`. The
build stops with that instruction if it notices a mismatch. To add a board,
copy a file in `boards/`.

### Logs and testing

The UART port carries the host protocol, so the firmware logs only while it
starts up (at 115200 baud, which `idf.py -p COM10 monitor` uses by default),
then switches the port to 921600 for the protocol and goes quiet. To keep logging for bring-up, turn on
**CodeBlue > Keep logging on the UART** in `idf.py menuconfig`; the viewer
won't work reliably while it's on.

To exercise the board without the viewer, from the repo root (ESP-IDF's
Python has pyserial):

```sh
python tools/cb_test.py COM10 info          # handshake: firmware version and target status
python tools/cb_test.py COM10 type "hello"  # types on the target
python tools/cb_test.py COM10 timeout-test  # checks the board lets go when the host goes quiet
python tools/cb_test.py -h                  # everything else
```

LED colours: blue, waiting for a host; green, host connected (the host can
set any colour).

The first build downloads the managed components listed in
`main/idf_component.yml`. See [../docs/BUILDING.md](../docs/BUILDING.md).

## Layout

| Path | Contents |
|------|----------|
| `IDF_VERSION` | The tested ESP-IDF release |
| `sdkconfig.defaults` | Build defaults (the generated `sdkconfig` is not committed) |
| `boards/` | Per-board defaults, chosen with `-DBOARD=` |
| `main/Kconfig.projbuild` | CodeBlue options in `idf.py menuconfig` (board name, LED pin, default brightness, USB IDs, logging) |
| `main/idf_component.yml` | Managed component dependencies |
| `dependencies.lock` | Resolved component versions (generated; commit it) |
