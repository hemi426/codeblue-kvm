<img src="assets/codeblue.svg" alt="" width="72" align="right">

# CodeBlue

A pocket crash cart: control a headless or misbehaving PC from your computer, BIOS
screens included, using an ESP32-S3 dev board and a cheap HDMI-to-USB capture
dongle.

```
 your computer                                                       target PC
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
- [Viewer](docs/viewer.md), [Firmware](docs/firmware.md) and
  [Hardware](docs/hardware/index.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Development](docs/development/index.md): building from source, and the
  protocol
- [Roadmap](docs/roadmap.md)

Downloads are on the [Releases](https://github.com/hemi426/codeblue-kvm/releases)
page: the firmware and the viewer are released separately.

## Credits

Inspired by [KeyboardAndMouseControl](https://github.com/alpharesearch/KeyboardAndMouseControl)
by Markus Schulz. CodeBlue is an independent implementation with its own
firmware and protocol.

## License

Software: GPL-3.0-or-later. Hardware design files: license to be decided
(likely CERN-OHL-S or CC BY-SA 4.0) and noted in `hardware/` when they arrive.
