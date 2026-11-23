# Roadmap

What's planned, roughly in order. Nothing here is promised; it's a list of
things worth doing, kept in one place so they don't get lost.

## Before outside testers

- [x] **CI:** build the firmware and the viewer on every
      push, and run the framing tests (`firmware/main/frame.c`'s test, moved
      into the repo, and `tools/cb_test.py selftest`).
- [x] **Parts list** (`docs/hardware/bom.md`): tested boards (with both USB
      ports), capture dongles, and the video adapters below.
- [x] **Issue template** for reporting a tested target (machine, BIOS or OS,
      keyboard, absolute and relative mouse, boot screens), feeding the table
      in `docs/hardware/index.md`.

## Before going public

- [x] **Slim the viewer:** x64 only and an allowlist of the libVLC plugins
      we actually use (338 MB of build output down to about 30).
- [x] **Viewer size:** releases offer both a framework-dependent zip (13.7 MB;
      install the .NET 10 Desktop Runtime once) and a portable one (62.7 MB;
      includes .NET).
- [x] **Release workflow** (`.github/workflows/release.yml`): `firmware-v*`
      and `viewer-v*` tags build the firmware images (per board preset) or
      the two viewer zips (small, 13.7 MB, and portable, 62.7 MB), and
      assemble a draft release. First used for firmware 0.1.1 and viewer
      0.2.0; the released firmware image was flashed and works.
- [x] **Docs site** on GitHub Pages (Jekyll, Just the Docs), built from
      `docs/`: home, getting started, viewer, firmware, hardware,
      troubleshooting, development, and experiments. Light and dark schemes in
      the logo's blue (`#1565C0`), following the system setting with a header
      override; logo and favicon from `assets/`. The READMEs are now short
      intros that link to it. Builds on every docs change; deploys once the
      repository is public.
- [x] **Experiments page** on the docs site, clearly marked "at your own
      risk" (`docs/experiments/`).
  - [x] MJPG at 1080p60 on the MS2130 stick's USB 3 mode: patched, flashed,
        and verified (`docs/experiments/ms2130-mjpg-usb3.md`).
  - [ ] Maybe: 1080p60 on the stick's USB 2 mode (bandwidth may not allow it).
- [x] **Install from the browser:** an "Install CodeBlue" button on the docs
      site using ESP Web Tools (the flasher ESPHome uses), with a manifest per
      board preset (one, since firmware 0.3.0). The firmware images have to be published on the Pages
      site itself, since browsers can't fetch GitHub release downloads from
      another site. Chrome and Edge only (Web Serial), so the esptool
      command stays in the docs as the fallback.
- [x] **Housekeeping:** the LICENSE file, a CHANGELOG, a copyright line in
      the README and on the docs site, credits (including what CodeBlue is
      built on), a CI status badge in the README, and `global.json` pinned to
      the current .NET 10 SDK.
- [x] **Make the repository public,** then set Settings → Pages → Source to
      GitHub Actions so the docs site deploys.
- [x] **CI:** skip builds for changes that only touch
      `.github/ISSUE_TEMPLATE/`, and leave version tags to the release workflow.

## Firmware

- [x] Test waking a sleeping target with a keypress (remote wakeup): works
      on an OptiPlex 5090 running Windows 11, once the BIOS allows USB wake.
- [x] Test a target that puts the mouse in boot protocol: the OptiPlex 5090's
      BIOS setup, One-Time Boot menu, and SupportAssist all do.
- [x] Tell the viewer when the target ignores the absolute pointer, so
      **Mouse: Auto** can switch to relative by itself: `abs_unread` in
      `TARGET_STATUS` (firmware 0.3.0, viewer 0.4.0), set when the absolute
      pointer's reports go uncollected. Verified on the OptiPlex 5090. A
      target that collects the reports and ignores them (SupportAssist on the
      OptiPlex 5080) can't be detected.
- [ ] Send `ERROR` code 3 when a USB report can't be sent.
- [x] Keep awake (firmware 0.5.0, viewer 0.5.0): after an idle delay, the
      board taps F15, nudges or glides the pointer, or scrolls a notch (or a
      mix), at random intervals, so the target doesn't sleep or lock. It runs
      on the board, so it carries on with no viewer attached; the viewer's
      **Keep awake** button turns it on and off and shows its state.
- [x] A status LED pattern while keep awake is on: a white heartbeat, every
      3 seconds while waiting and every 1.5 seconds while acting.
- [x] VBUS sense: the firmware reports `vbus_present` (capability bit 3)
      when built with a sense pin, and the viewer tells "asleep" from
      "unplugged or powered off"; the modification is in
      `docs/hardware/vbus-sense.md`. Released in firmware 0.2.0 and viewer
      0.3.0, and tested on a modified N8R2 clone (100k/150k on GPIO21).
- [x] VBUS sense photo in `docs/hardware/vbus-sense.md`: the finished mod on
      an N16R8, with the sense pad marked (labels drawn by
      `tools/label_vbus_photo.py`).
- [x] Board settings the viewer can save on the board (the LED and VBUS
      sense pins), so one release image suits every board, the mod
      included. Firmware 0.3.0, viewer 0.4.0.
- [ ] Maybe: esp_tinyusb's self-powered mode (`vbus_monitor_io`), which
      would turn an unplug into a proper USB disconnect.
- [x] Keep board settings across firmware updates (firmware 0.4.0): releases
      include the bootloader, partition table, and app as separate images,
      and the installer writes those, leaving the settings alone.

## Viewer

- [x] **Don't hang on a stalled dongle** (viewer 0.4.0). A dongle that stops
      sending frames without an error (the UGREEN) left libVLC waiting, and
      `Stop()` on the window's thread froze the viewer. Each session now gets
      its own player, stopped off the window's thread and abandoned if it
      hangs; the status bar says to replug the dongle, and exit doesn't wait.
      An fps field shows **No video** when frames stop. Verified against the
      UGREEN.
- [ ] Horizontal scrolling (`pan`) from tilt wheels and touchpads.
- [ ] Check for updates: an option in **Viewer options**, off by default.
      When on, the viewer checks GitHub's releases about once a day and
      shows a quiet note in the status bar when a newer viewer, or newer
      firmware than the connected board's, is out, linking to the docs
      site's home page (which has both installers). Notify only: no pop-ups, no downloads, no installs.
      Offline, it fails silently and never slows anything down.
- [ ] Polish: dropdown widths, better DirectShow device listing, settings per
      capture device, and a view of the modes a dongle offers. Ideally the
      Size and FPS lists come from the dongle itself: the fixed list offers
      sizes some dongles don't support (the MS2109 has no 1600x900). With
      that list, an Auto setting could pick MJPG at 1920x1080 at the highest
      frame rate the dongle offers there (60 on the newer MS2109, 30 on the
      older). Until then, the default stays 30 fps.
- [ ] Optional audio from the dongle: an Audio checkbox (off by default)
      that pairs the dongle's USB audio input with its video device. Needs a
      real audio output, the uncompressed-audio decoder, and perhaps a
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
- [ ] Room for the VBUS sense resistors (`docs/hardware/vbus-sense.md`), all
      on the underside at the USB end: the 10 kΩ from the USB-OTG pad to the
      GPIO21 hole, the 15 kΩ from GPIO21 to the GND hole beside it. A
      shallow pocket in the base should do.
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

- [x] Video adapters for targets without HDMI: USB-C to HDMI (the port needs
      DisplayPort Alt Mode or Thunderbolt), DisplayPort to HDMI, and Mini
      DisplayPort to HDMI, all tested. VGA (a powered converter) and DVI stay
      untested in the parts list, for anyone with such a machine to report.
- [x] A second ESP32-S3 board, so one can stay on known-good firmware: ten
      DORHEA N16R8 clones arrived, and the first runs the release image fine.
- [x] BOM: the DORHEA N16R8 marked tested, and a genuine MS2130 dongle added
      (MJPG only on USB 2, YUY2 only on USB 3).
- [x] BOM: a USB 2.0 extension (C2G 52119) puts the MS2130 in its MJPG mode
      on a USB 3 port.
- [ ] More targets for `docs/hardware/index.md`: a Mac (an Intel Mac's
      Option-key boot picker), more Dells, servers.

## Done

- [x] Removed the legacy text protocol (the Arduino firmware's). The last
      commit that had it is tagged `legacy-protocol-final`.
- [x] USB product ID: none needed. Espressif allows its vendor ID with
      TinyUSB's default product IDs for devices using standard USB classes,
      and CodeBlue is plain HID. A dedicated ID (free, via a pull request to
      espressif/usb-pids) only matters for a product that wants its own
      identity.
