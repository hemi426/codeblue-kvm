# CodeBlue firmware

ESP-IDF (C) firmware for the ESP32-S3. It presents a boot-protocol USB
keyboard, a relative mouse and an absolute pointer to the target, and speaks
the binary protocol in [../docs/PROTOCOL.md](../docs/PROTOCOL.md).

**Status:** skeleton. It builds, and lights the status LED dim blue. The USB
HID device and the protocol come next. Until then, the viewer works with the
Arduino firmware from
[alpharesearch/KeyboardAndMouseControl](https://github.com/alpharesearch/KeyboardAndMouseControl)
through its deprecated legacy protocol.

## Building

Set up ESP-IDF once with the bootstrap script, then, in each new terminal,
from the repo root:

```sh
. scripts/activate-idf.sh          # Windows: . .\scripts\activate-idf.ps1
cd firmware
idf.py build
idf.py -p COM10 flash monitor      # the board's UART port; Ctrl+] exits the monitor
```

The first build downloads the managed components listed in
`main/idf_component.yml`. See [../docs/BUILDING.md](../docs/BUILDING.md).

## Layout

| Path | Contents |
|------|----------|
| `IDF_VERSION` | The tested ESP-IDF release |
| `sdkconfig.defaults` | Build defaults (the generated `sdkconfig` is not committed) |
| `main/Kconfig.projbuild` | CodeBlue options in `idf.py menuconfig` (LED pin, default brightness) |
| `main/idf_component.yml` | Managed component dependencies |
| `dependencies.lock` | Resolved component versions (generated; commit it) |
