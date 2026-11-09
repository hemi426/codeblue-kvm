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

- [ ] **Slim the viewer:** x64 only, an allowlist of the libVLC plugins we
      actually use, and perhaps a framework-dependent build.
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
- [ ] **USB product ID** from Espressif (free for open-source projects; it
      takes a while, so ask early).
- [ ] **Housekeeping:** CHANGELOG, LICENSE and credits check, a CI status
      badge in the README, then make the repository public.

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
      capture device, and a view of the modes a dongle offers.
- [ ] Someday: Media Foundation instead of libVLC (much smaller, Windows
      only), and a viewer for macOS or Linux.

## Case

A parametric FreeCAD model, driven by a spreadsheet, so variants share one
base design and fixing the fit fixes them all.

- [ ] Base case for the ESP32-S3-DevKitC-1, with no headers soldered.
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
- [ ] Decide the hardware licence (CERN-OHL-S or CC BY-SA 4.0).

## Kit and testing

- [ ] Video adapters for targets without HDMI: USB-C to HDMI (the port needs
      DisplayPort Alt Mode or Thunderbolt), DisplayPort to HDMI, Mini
      DisplayPort to HDMI, and a powered VGA to HDMI converter.
- [ ] A second ESP32-S3 board, so one can stay on known-good firmware.
- [ ] More targets for `hardware/README.md`: a Mac (an Intel Mac's Option-key
      boot picker), more Dells, servers.

## Done

- [x] Removed the legacy text protocol (the Arduino firmware's). The last
      commit that had it is tagged `legacy-protocol-final`.
