# NightLightTray

Minimal Windows tray app that follows Windows Night Light.

- Runs in the background (no window)
- Shows a tray icon while Night Light is ON
- Hides completely when Night Light is OFF
- Silently follows toggles from Windows Control Center / Action Center
- Click tray icon → opens the Windows Night light settings page, small, directly above the tray
- Settings page closes itself as soon as you click elsewhere (event-driven, no wastefull polling)
- Dark/light mode aware (follows Windows app theme)
- "Always show in tray" option in context menu
- **Tiny:** ~1.2 MB RAM while visible, <1 MB when idle in the tray, can
 slightly rise while settings Window is open

## Requirements

- Windows 10 or Windows 11
- .NET Framework 4.8.1 (ships with Windows 10 1903+, Windows 11)
- Visual Studio 2022 (or Build Tools) to compile

## Build

With Visual Studio 2022 Community:

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\Msbuild\Current\Bin\MSBuild.exe" .\NightLightTray\NightLightTray.csproj /p:Configuration=Release
```

Output: `NightLightTray\bin\Release\NightLightTray.exe`

Or open `NightLightTray\NightLightTray.sln` in Visual Studio and build (F6).

## Run

Double-click the exe, or from PowerShell:

```powershell
.\NightLightTray\bin\Release\NightLightTray.exe
```

## Usage

| Action | Effect |
|---|---|
| Turn Night Light ON (Action Center) | Tray icon appears |
| Turn Night Light OFF (Action Center) | Tray icon disappears |
| Left-click or double-click tray icon | Opens Windows Night light settings page, resized and placed above the tray |
| Click elsewhere | Settings page closes again |
| Right-click tray icon | Context menu: "Always show in tray", "Exit" |

"Always show in tray" keeps the icon visible even while Night Light is off (clicking still opens the settings page).

## Memory footprint

Measured working set (Task Manager), Release build:

| State | RAM |
|---|---|
| Idle in tray (Night Light off) | 0.7 MB, usually under 1 MB |
| Visible (Night Light on) | ~1.2 MB |

Memory is collected and trimmed automatically after the settings page closes.

## Autostart (start with Windows)

Simplest: place a shortcut to the exe in the Startup folder.

```powershell
$ws = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\NightLightTray.lnk")
$lnk.TargetPath = "$PWD\NightLightTray\bin\Release\NightLightTray.exe"
$lnk.Save()
```

Or press `Win+R`, type `shell:startup`, and drop a shortcut there.

To remove autostart, delete `NightLightTray.lnk` from the Startup folder.

## How it works / limitations

Windows has no public API for Night Light state or strength. The app reads the CloudStore
registry blob (reverse-engineered format) to detect ON/OFF:

- State: `HKCU\...\CloudStore\...\default$windows.data.bluelightreduction.bluelightreductionstate\...` — `Data[18] == 0x15` means ON

Directly writing the strength setting to the registry is unreliable on recent Windows builds
(25H2): the display pipeline only accepts strength changes while the Settings page is open.
The app therefore opens the system Night light settings page (`ms-settings:nightlight`),
resized and placed above the tray, instead of writing strength itself.

This format is undocumented and can change with Windows updates. If the icon stops
working after an update, the byte layout needs to be re-checked.

## Tests

Small console test harness (includes a live on/off round-trip; toggles your Night Light briefly):

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\Msbuild\Current\Bin\MSBuild.exe" .\NightLightTests\NightLightTests.csproj /p:Configuration=Debug
.\NightLightTests\bin\Debug\NightLightTests.exe
```
