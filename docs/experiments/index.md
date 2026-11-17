# Experiments

Unsupported hacks that go beyond the tested setup: things that worked for
someone, written up so others can reproduce them. They may void warranties,
depend on one exact firmware revision, or brick hardware.

Before trying one:

- **Back up first.** Dump the original firmware or settings and keep the dump
  somewhere safe; it's your way back.
- **Check the hashes.** Each write-up gives the hashes it was tested against.
  If yours differ, stop: you have a different revision.
- **Don't open an issue for these** unless the write-up asks for reports.

| Experiment | What it does | Status |
|------------|--------------|--------|
| [MS2130 MJPG on USB 3](ms2130-mjpg-usb3.md) | Patches a $9 MS2130 capture stick to offer MJPG at 1080p60 on USB 3, where it otherwise offers only YUY2. | Working |
