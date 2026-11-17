# CodeBlue firmware

ESP-IDF (C) firmware for the ESP32-S3: a USB keyboard, mouse and absolute
pointer for the target, driven by the viewer over the board's UART port.

- Flashing, board presets, the status LED, logs and testing:
  [docs/firmware.md](../docs/firmware.md)
- Setting up ESP-IDF and building: [docs/development/building.md](../docs/development/building.md)
- The protocol: [docs/development/protocol.md](../docs/development/protocol.md)

Quick build, after setting up ESP-IDF, from the repo root:

```sh
. scripts/activate-idf.sh          # Windows: . .\scripts\activate-idf.ps1
cd firmware
idf.py build
idf.py -p COM10 flash              # the board's UART port
```

## Source layout

| Module | Job |
|--------|-----|
| `main/usb_hid.c` | The USB device the target sees: boot keyboard, boot mouse, absolute pointer |
| `main/session.c` | The host protocol on UART0: handshake, heartbeats, messages |
| `main/frame.c` | COBS and CRC framing (plain C; testable on a PC) |
| `main/status_led.c` | The RGB LED and its saved brightness |

| Path | Contents |
|------|----------|
| `IDF_VERSION` | The tested ESP-IDF release |
| `sdkconfig.defaults` | Build defaults (the generated `sdkconfig` is not committed) |
| `boards/` | Per-board defaults, chosen with `-DBOARD=` |
| `test/` | Tests that run on a PC |
| `main/Kconfig.projbuild` | CodeBlue options in `idf.py menuconfig` (board name, LED pin, default brightness, USB IDs, logging) |
| `main/idf_component.yml` | Managed component dependencies |
| `dependencies.lock` | Resolved component versions (generated; commit it) |
