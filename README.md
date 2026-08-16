# NightLightTray

Minimal Windows tray app that follows Windows Night Light. Runs in the background, shows a tray icon only while Night Light is ON, and silently follows toggles from Action Center.

## Demo

> Screen capture does not record the night light filter, so you can't see a change in the video, but it works of course (it's the Windows-Settings. The program itself does not modify the night light strength):

https://github.com/user-attachments/assets/398b17c0-60a0-4df7-aef5-c4b98b8fdf6d

## Features

- Tray icon appears when Night Light is ON, disappears when OFF
- Click icon → Night light settings page opens small, directly above the tray
- Settings page closes itself when you click elsewhere
- Dark/light mode aware, "Always show in tray" option in context menu
- Tiny: ~0.7 MB idle, ~1.2 MB visible

## Install

- Download `NightLightTray.exe` and put it anywhere (e.g. `Program Files`).
- Right-click → `Create shortcut`, copy it, then `Win+R` → `shell:startup` → paste the shortcut there to autostart on boot.
- It only uses ~0.7 MB idle, so autostart is no problem.

**Fallback flag:** if the on/off detection ever breaks and the icon stays hidden, add `--always` to the shortcut target (e.g. `"C:\...\NightLightTray.exe" --always`) — the icon then stays visible no matter what.

## Usage

| Action | Effect |
|---|---|
| Turn Night Light ON (Action Center) | Tray icon appears |
| Turn Night Light OFF (Action Center) | Tray icon disappears |
| Left-click or double-click tray icon | Opens Night light settings, small, above the tray |
| Click elsewhere | Settings page closes again |
| Right-click tray icon | "Always show in tray", "Exit" |

## Limitations

- If the settings window is not fully initialized yet, click-outside-close does not work (known, accepted).
- The on/off detection reads the CloudStore registry blob and could break if its format changes with a Windows update.

## Build from source

Requirements: Windows 10/11, .NET Framework 4.8.1, Visual Studio 2022.

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\Msbuild\Current\Bin\MSBuild.exe" .\NightLightTray\NightLightTray.csproj /p:Configuration=Release
```

Output: `NightLightTray\bin\Release\NightLightTray.exe` — or open `NightLightTray.sln` in Visual Studio (F6).

## How it works

Windows has no public API for Night Light state or strength. The app reads the CloudStore registry blob to detect ON/OFF: `...\bluelightreductionstate\...` with `Data[18] == 0x15` meaning ON.

Writing strength directly is unreliable on recent builds (25H2), so the app just opens the system settings page instead.

## Tests

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\Msbuild\Current\Bin\MSBuild.exe" .\NightLightTests\NightLightTests.csproj /p:Configuration=Debug
.\NightLightTests\bin\Debug\NightLightTests.exe
```
