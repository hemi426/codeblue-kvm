# CodeBlue firmware

ESP-IDF (C) firmware for the ESP32-S3 is in progress. It will present a boot
protocol USB keyboard and an absolute USB mouse to the target, and speak the
binary protocol described in [../docs/PROTOCOL.md](../docs/PROTOCOL.md).

Until then, the viewer works with the Arduino firmware from
[alpharesearch/KeyboardAndMouseControl](https://github.com/alpharesearch/KeyboardAndMouseControl)
through its deprecated legacy protocol.
