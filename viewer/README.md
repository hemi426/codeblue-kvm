# CodeBlue viewer

The Windows app (.NET 10, WinForms, LibVLCSharp) that shows the target's
screen and sends it your keyboard and mouse.

- Using it: [docs/viewer.md](../docs/viewer.md)
- Building and publishing: [docs/development/building.md](../docs/development/building.md)
- The protocol it speaks: [docs/development/protocol.md](../docs/development/protocol.md)

Quick build, from the repo root:

```powershell
.\scripts\bootstrap-windows.ps1 -Component viewer   # checks first, asks before installing
dotnet run --project viewer\CodeBlue.Viewer
```

## Source layout

| Path | Contents |
|------|----------|
| `Directory.Packages.props` | NuGet versions for the whole viewer (Central Package Management) |
| `CodeBlue.Viewer/MainForm.cs` | The window: toolbar, video, capture, status |
| `CodeBlue.Viewer/ViewerSettings.cs` | Saved choices (`%APPDATA%\CodeBlue\viewer.json`) |
| `CodeBlue.Viewer/Input/` | Keyboard hook, raw mouse input, and the key-to-HID-usage map |
| `CodeBlue.Viewer/Link/` | The board link: framing, the binary protocol, the serial port |
| `CodeBlue.Viewer/Video/` | Capture device discovery and libVLC options |
| `CodeBlue.Viewer/Native/` | Win32 declarations |
