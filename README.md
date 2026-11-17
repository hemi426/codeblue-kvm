<img src="assets/codeblue.svg" alt="" width="72" align="right">

# CodeBlue

[![CI](https://github.com/hemi426/codeblue-kvm/actions/workflows/ci.yml/badge.svg)](https://github.com/hemi426/codeblue-kvm/actions/workflows/ci.yml)

A pocket crash cart: control a headless or misbehaving computer from your own,
BIOS screens included, using an ESP32-S3 dev board and a cheap HDMI-to-USB
capture dongle.

```
 your computer                                                 target computer
┌─────────────┐  USB (serial)  ┌──────────┐  USB (keyboard + mouse) ┌──────────┐
│   CodeBlue  │◄──────────────►│ ESP32-S3 │────────────────────────►│ USB port │
│    viewer   │                └──────────┘                         │          │
│             │  USB (video)   ┌──────────┐  HDMI                   │          │
│             │◄───────────────│ capture  │◄────────────────────────│ HDMI out │
└─────────────┘                │  dongle  │                         └──────────┘
                               └──────────┘
```

The target sees an ordinary USB keyboard and mouse and an ordinary monitor; it
needs no software and no network.

## Documentation

The full documentation is at **https://hemi426.github.io/codeblue-kvm/**, and
its source is in [`docs/`](docs/index.md):

- [Getting started](docs/getting-started.md): parts, flashing, the viewer, and
  your first capture
- [Viewer](docs/viewer.md), [Firmware](docs/firmware.md), and
  [Hardware](docs/hardware/index.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Development](docs/development/index.md): building from source, and the
  protocol
- [Roadmap](docs/roadmap.md)

Downloads are on the [Releases](https://github.com/hemi426/codeblue-kvm/releases)
page: the firmware and the viewer are released separately, and
[CHANGELOG.md](CHANGELOG.md) lists what changed in each version.

## Credits

Inspired by [KeyboardAndMouseControl](https://github.com/alpharesearch/KeyboardAndMouseControl)
by Markus Schulz. CodeBlue is an independent implementation with its own
firmware and protocol.

Written with a lot of help from [Claude](https://claude.ai), Anthropic's AI
model.

Built on:

- **Firmware:** [ESP-IDF](https://github.com/espressif/esp-idf) (Apache-2.0);
  Espressif's `esp_tinyusb` component (Apache-2.0), which wraps
  [TinyUSB](https://github.com/hathach/tinyusb) (MIT); and Espressif's
  `led_strip` component (Apache-2.0).
- **Viewer:** [.NET](https://github.com/dotnet/runtime) (MIT),
  [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp)
  (LGPL-2.1-or-later), and [libVLC](https://www.videolan.org/vlc/libvlc.html)
  (LGPL-2.1-or-later, with GPL-licensed plugins, including DirectShow
  capture).
- **Docs site:** [Jekyll](https://jekyllrb.com/) and
  [Just the Docs](https://github.com/just-the-docs/just-the-docs) (both MIT).

## License

Copyright (C) 2026 Josh Tolbert.

Software: GPL-3.0-or-later (see [LICENSE](LICENSE)). Hardware design files:
license to be decided (likely CERN-OHL-S or CC BY-SA 4.0) and noted in
`hardware/` when they arrive.
