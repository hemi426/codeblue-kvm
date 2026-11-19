# CodeBlue

A pocket crash cart: control a headless or misbehaving computer from your own,
BIOS screens included, using an ESP32-S3 dev board and a cheap HDMI-to-USB
capture dongle.

[Install the firmware](firmware.md#flashing){: .btn .btn-primary }
[Download the viewer](https://github.com/hemi426/codeblue-kvm/releases?q=viewer&expanded=true){: .btn }

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

## Why CodeBlue?

I needed something like this for setting up new machines and for
crash-carting misbehaving ones. I don't have much room for setting up a
separate monitor, keyboard, and mouse, so being able to drive a computer from
another computer I already have running seemed like a very useful tool for
me, and probably lots of other people.

I noticed that the ESP32-S3-DevKitC-1 boards I've been using for other
projects have two USB ports, and wondered whether one could work like
Hagstrom Electronics'
[USBtoUSB](https://www.hagstromelectronics.com/pdf_files/USBtoUSB_Manual.pdf)
box: a keyboard and mouse to one computer, controlled from another.

It turned out other projects had done something similar (see
[Credits](#credits)), but the ones I found had clunky viewers, too much lag,
and an ASCII protocol that caused problems. So (with a ton of help from
Claude) I built my own: a low-latency viewer that's a single download, a
binary protocol with checksums and heartbeats, a keyboard and mouse that work
in BIOS setup screens, a pointer that lands exactly where you click, and
nothing left held down when a cable comes out.

### How is it different from JetKVM or PiKVM?

They solve a different problem. JetKVM and PiKVM are KVM-over-IP devices: you
install one on a particular machine, put it on the network, and reach that
machine from anywhere through a browser. CodeBlue is a crash cart: you carry
it to whichever machine needs help, plug it into your laptop, and take it
with you when you're done. So CodeBlue:

- **Needs no network at all,** just USB between your laptop and the target.
  That helps when the network is the problem, when a new machine isn't on one
  yet, or when policy doesn't allow unapproved devices on it.
- **Serves many machines, one at a time,** rather than one device per machine.
- **Needs no setup:** no IP address, no accounts, and no web interface to
  secure.
- **Costs $20 to $30 in parts,** and it's open source down to the firmware.

If you want permanent remote access to one machine, a KVM-over-IP device is
the better tool. If you want a monitor, keyboard, and mouse in your pocket,
that's CodeBlue.

## What works

- **Viewer** (Windows): low-latency video, keyboard and mouse capture with an
  absolute or relative pointer, full screen, snapshots, Ctrl+Alt+Del, the
  target's status (connected, asleep, Caps Lock), and automatic
  reconnection if the board drops out.
- **Firmware** (ESP-IDF): a keyboard and a relative mouse that both work in
  BIOS setup screens (boot protocol), plus an absolute pointer, for the
  target; and a binary protocol with heartbeats both ways, so a crashed viewer
  or pulled cable never leaves a key held down.

## Where to go next

- **[Getting started](getting-started.md):** parts, flashing, the viewer, and
  your first capture.
- **[Viewer](viewer.md):** controls, mouse modes, keys, and when capture ends.
- **[Firmware](firmware.md):** flashing, board settings, the status LED, and
  testing a board.
- **[Hardware](hardware/index.md):** what to buy, tested targets, and the
  optional VBUS sense modification.
- **[Troubleshooting](troubleshooting.md):** when something doesn't work.
- **[Development](development/index.md):** building from source, and the
  protocol.

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

Software: GPL-3.0-or-later (see
[LICENSE](https://github.com/hemi426/codeblue-kvm/blob/main/LICENSE)).
Hardware design files: license to be decided (likely CERN-OHL-S or CC BY-SA
4.0) and noted alongside them when they arrive.
