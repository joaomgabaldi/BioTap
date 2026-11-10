# BioTap

[Português](README.pt-BR.md)

Turns your PC's fingerprint reader into a physical shortcut: **1, 2, N taps in a row on the sensor run the action you set up for N.** Any touch counts, even from a finger that is not enrolled, and Windows Hello keeps working as usual for sign-in and authentication.

## What it can do

Each action uses one of these presets:

| Category | Presets |
|---|---|
| Open and run | Open a program, file or URL · Run a command or script |
| Keyboard | Keyboard shortcut · Copy · Paste · Type text |
| Media and sound | Mute · Play/pause · Volume up / down · Set volume · Switch audio output |
| Windows | Always on top · Minimize the others · Lock Windows |
| Tools | Screenshot (monitor or window) · QR code of the copied text |
| Combine | Sequence (up to 20 steps, with waits) |

Besides taps, every action can also be run from the tray panel. A small confirmation shows above the taskbar without stealing focus. Mute and volume skip it, because Windows already shows its own indicator.

The interface is in Brazilian Portuguese.

## Requirements

- Windows 10 2004 (19041) or later, 64-bit.
- A fingerprint reader that works with Windows Hello.
- An administrator account: the app runs elevated and the service runs as SYSTEM.

## Installation

Download `BioTap-Setup-<version>.exe` from the [latest release](https://github.com/joaomgabaldi/BioTap/releases/latest) and run it. The installer registers the service, creates the logon task that starts the app without a UAC prompt, and opens the app. The installer is not signed, so Windows SmartScreen may warn you ("More info" → "Run anyway"). To uninstall, use Settings → Apps.

To build the installer from source you need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and [Inno Setup 7](https://jrsoftware.org/isinfo.php):

```powershell
.\install\build-installer.ps1
```

The output goes to `artifacts\installer\`.

Your actions are stored in `C:\ProgramData\BioTap\config.json` and the log in `C:\ProgramData\BioTap\logs\app.log`.

## How it works

```
sensor ─► BioTap.Service (SYSTEM) ─ named pipe (SYSTEM and Admins only) ─► BioTap.exe (WinUI 3, admin)
          counts taps and hands the                                         tray, panel, preferences,
          sensor back to Windows Hello                                      confirmation; runs the actions
```

- **The service only counts taps.** It never runs anything, and a test enforces that.
- **Programs, files and URLs open without elevation**, through Explorer. Only "Run a command" with the administrator option runs elevated.
- **Only administrators can write the configuration and the log.** The app checks their permissions before using them.
- **The log never records** typed text, clipboard contents or command arguments.

## Development

```powershell
dotnet build
dotnet test
```

| Project | Role |
|---|---|
| `src/BioTap.Core` | Testable logic: tap counting, presets, configuration, validation |
| `src/BioTap.Service` | Windows service that reads the sensor |
| `src/BioTap.App` | WinUI 3 app: tray, panel, preferences, confirmation |
| `src/BioTap.Probe` | Sensor diagnostics console (admin) |
| `tests/` | xUnit tests for Core and Service |

Stack: .NET 10, Windows App SDK, CsWin32, H.NotifyIcon, QRCoder.

## License

[GPL-3.0](LICENSE)
