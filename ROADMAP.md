# Roadmap

What's planned, roughly in order. Nothing here is promised; it's a list of
things worth doing, kept in one place so they don't get lost.

## Before outside testers

- [x] **CI:** build the firmware (both board presets) and the viewer on every
      push, and run the framing tests (`firmware/main/frame.c`'s test, moved
      into the repo, and `tools/cb_test.py selftest`).
- [x] **`hardware/BOM.md`:** tested boards (with both USB ports), capture
      dongles, and the video adapters below.
- [x] **Issue template** for reporting a tested target (machine, BIOS or OS,
      keyboard, absolute and relative mouse, boot screens), feeding the table
      in `hardware/README.md`.

## Before going public

- [x] **Slim the viewer:** x64 only and an allowlist of the libVLC plugins
      we actually use (338 MB of build output down to about 30).
- [ ] **Viewer size, optional:** a framework-dependent build (testers install
      the .NET Desktop Runtime once) would leave out the .NET runtime too.
- [ ] **Release workflow:** a `v*` tag builds the merged firmware image (per
      board preset) and the viewer zip, and attaches them to a GitHub release
      with flashing instructions and a note about the SmartScreen warning.
- [ ] **Docs site** on GitHub Pages (Jekyll, like DarkStage): home, getting
      started, user guide, tested hardware, troubleshooting, building from
      source, protocol reference, about. Accent colour `#1565C0`, heartbeat
      logo and favicon from `assets/`. The site becomes the docs; the READMEs
      shrink to a short intro that links to it.
- [ ] **Install from the browser:** an "Install CodeBlue" button on the docs
      site using ESP Web Tools (the flasher ESPHome uses), with a manifest per
      board preset. The firmware images have to be published on the Pages
      site itself, since browsers can't fetch GitHub release downloads from
      another site. Chrome and Edge only (Web Serial), so the esptool
      command stays in the docs as the fallback.
- [ ] **Housekeeping:** CHANGELOG, a copyright line (full name) in the README
      and LICENSE, credits check, a CI status badge in the README, then make
      the repository public.
- [ ] **CI:** skip builds for changes that only touch
      `.github/ISSUE_TEMPLATE/` (with the release workflow's `ci.yml` changes).

## Firmware

- [ ] Test waking a sleeping target with a keypress (remote wakeup).
- [ ] Test a target that puts the mouse in boot protocol.
- [ ] Send `ERROR` code 3 when a USB report can't be sent.
- [ ] VBUS sense: document the two-resistor modification, then report
      `vbus_present` (capability bit 3) so "asleep" and "unplugged" can be
      told apart.

## Viewer

- [ ] Horizontal scrolling (`pan`) from tilt wheels and touchpads.
- [ ] Polish: dropdown widths, better DirectShow device listing, settings per
      capture device, and a view of the modes a dongle offers. Ideally the
      Size and FPS lists come from the dongle itself: the fixed list offers
      sizes some dongles don't support (the MS2109 has no 1600x900). With
      that list, an Auto setting could pick MJPG at 1920x1080 at the highest
      frame rate the dongle offers there (60 on the newer MS2109, 30 on the
      older). Until then, the default stays 30 fps.
- [ ] Optional audio from the dongle: an Audio checkbox (off by default)
      that pairs the dongle's USB audio input with its video device. Needs a
      real audio output, the uncompressed-audio decoder and perhaps a
      resampler back in the allowlist (under 2 MB). Favour low latency over
      lip sync. (Today, Windows' "Listen to this device" on the dongle's audio
      input works as a stopgap.)
- [ ] libVLC 4, on a separate branch, once 4.0 ships as a stable release with
      NuGet packages: mainly for its reworked video output (which should fix
      the YUY2 text artifacts) and asynchronous stop.
- [ ] Someday: Media Foundation instead of libVLC (much smaller, Windows
      only). It would also drop avcodec's 19 MB: Windows has its own MJPEG
      decoder, but libVLC 3's mft plugin fails to set it up, so only a native
      Media Foundation viewer could use it. And a viewer for macOS or Linux.

## Case

A parametric FreeCAD model, driven by a spreadsheet, so variants share one
base design and fixing the fit fixes them all.

- [ ] Base case for the ESP32-S3-DevKitC-1, with no headers soldered.
- [ ] Room for the VBUS sense resistors, which use component leads in the
      plated header holes (suggested pin GPIO14, next to GND; confirm on the
      silkscreen): a shallow pocket under the USB end of the header row for
      the 15 kΩ (GPIO14 to GND), and lid clearance on top for the 10 kΩ, which
      runs from the IN-OUT pad (top side) to the GPIO14 hole.
- [ ] Options:
  - [ ] Logo: debossed, embossed, or none (single-colour friendly).
  - [ ] Tactile port markers so HOST and TARGET can be told apart by feel
        (for example one raised dot and two), with optional text labels.
  - [ ] LED: open window, thin light-pipe layer, or closed.
  - [ ] Holes for the BOOT and EN (reset) buttons, or covered.
  - [ ] Lanyard or cable-tie loop.
- [ ] CAD-ready logo: `tools/make_icon.py` also writes an SVG with the
      heartbeat as an outline, since FreeCAD imports strokes as bare lines.
- [ ] Script the STL export for the common combinations, ship them in
      releases, and regenerate them in CI.
- [ ] Labels for the HOST and TARGET cables.
- [ ] Hardware licence: leaning CERN-OHL-S (strongly reciprocal, matching
      the software's GPL), over CC BY-SA 4.0.

## Kit and testing

- [ ] Video adapters for targets without HDMI: USB-C to HDMI (the port needs
      DisplayPort Alt Mode or Thunderbolt), DisplayPort to HDMI, Mini
      DisplayPort to HDMI, and a powered VGA to HDMI converter.
- [ ] A second ESP32-S3 board, so one can stay on known-good firmware
      (ordered: N16R8 DevKitC-1 clones, from the same vendor as the N8R2).
- [ ] BOM: mark the DORHEA N16R8 tested once one has been flashed, and add
      a genuine MS2130 (USB 3) dongle once one has been checked.
- [ ] More targets for `hardware/README.md`: a Mac (an Intel Mac's Option-key
      boot picker), more Dells, servers.

## Done

- [x] Removed the legacy text protocol (the Arduino firmware's). The last
      commit that had it is tagged `legacy-protocol-final`.
- [x] USB product ID: none needed. Espressif allows its vendor ID with
      TinyUSB's default product IDs for devices using standard USB classes,
      and CodeBlue is plain HID. A dedicated ID (free, via a pull request to
      espressif/usb-pids) only matters for a product that wants its own
      identity.
