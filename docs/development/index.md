# Development

For working on CodeBlue itself: building the viewer and firmware from source,
and the protocol between them.

- **[Building](building.md):** the toolchains (with bootstrap scripts that set
  them up), building and publishing the viewer, building and flashing the
  firmware, and making a release.
- **[Protocol](protocol.md):** the binary protocol between the viewer and the
  board, for anyone writing another host or porting the firmware.

## Repository layout

| Path | Contents |
|------|----------|
| `viewer/` | Windows app (.NET 10, WinForms, LibVLCSharp) |
| `firmware/` | ESP32-S3 firmware (ESP-IDF, C) |
| `scripts/` | Bootstrap scripts that check for and install build tools |
| `tools/` | Test sender (`cb_test.py`), icon and photo scripts, and experiment scripts |
| `docs/` | This documentation, which is also the source of the docs site |
| `assets/` | The logo and icon |

Each code folder has a short README describing its source layout.
