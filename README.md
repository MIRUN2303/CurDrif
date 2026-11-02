# CurDrif

> **Stay in motion.**

CurDrif is a tiny, native Windows utility that gently drifts your mouse cursor inside a fixed box in its own window. Between drifts, it clicks in the middle of the wait — just enough activity to keep things moving without spamming keys or windows.

[![Download CurDrif](https://img.shields.io/badge/Download-CurDrif.zip-blue?style=for-the-badge)](https://github.com/MIRUN2303/CurDrif/releases)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6?style=for-the-badge&logo=windows)](https://github.com/MIRUN2303/CurDrif)
[![License](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)](LICENSE)
[![No Install](https://img.shields.io/badge/Install-Portable%20%7C%20Single%20Click-orange?style=for-the-badge)](#single-click-install)

## Why CurDrif?

- **Stay in motion** – Drifts your cursor at random intervals with natural easing.
- **Fixed box control** – Everything stays inside the **FIXED BOX** panel in CurDrif's own window.
- **Smart clicks** – A left click happens roughly halfway between drifts, inside the box.
- **Set it and forget it** – Daily schedule, sleep window, hotkeys (F8/F9/ESC).
- **Lightweight & native** – Pure C# WinForms, no browser, no SDK needed. Runs from a single folder.

**Keywords:** mouse mover, cursor jitter, keep alive, cursor drift, mouse activity, anti-idle, windows cursor utility, portable mouse automation

## Single-Click Install

**Easiest way (recommended):**

1. Go to the [CurDrif Releases](https://github.com/MIRUN2303/CurDrif/releases) page.
2. Download the latest `CurDrif.zip`.
3. Extract it to any folder (Desktop, Documents, etc.).
4. Double-click `CurDrif.bat` — that's it. No installer, no admin rights.

**That's all. One click to run.**

## Quick Start

1. Double-click `CurDrif.bat` or the `CurDrif` desktop shortcut.
2. Leave the window where you want the fixed box (maximize for a larger box).
3. Set radius and delay (random range or exact delay). Optionally enable Schedule/Sleep.
4. Click **START** (or press **F8**). The pointer jumps into the box and starts drifting.
5. Click **STOP** (or press **F9**/`ESC`) or run `StopCursorMove.bat` to stop.

The **FIXED BOX** is the real movement area. Moving/resizing/minimizing the window while armed will stop the session. Clicks happen in the middle of the wait between drifts, inside that box.

## Features

- **Native & portable** – No install, no registry, no admin rights.
- **Fixed in-window box** – Cursor never leaves the `FIXED BOX` panel. Radius is capped to fit the panel.
- **Drift between waits** – Random angle + distance, eased movement, click mid-wait.
- **Schedule & Sleep** – Run only during work hours or sleep at night (supports overnight windows).
- **Hotkeys** – `F8` Start, `F9` Stop, `ESC` Emergency stop (global when registered).
- **Activity nudge** – Sends a tiny 1px input every 60s while running to help with idle timers.
- **Status pill** – Clear STOPPED/RUNNING/SCHEDULED/SLEEPING states.
- **Last 20 events log** – See starts, drifts, mid-wait clicks, and stops.
- **Maximizable** – Bigger window = bigger box = larger allowed radius.

## Folder Contents

```text
CurDrif\
  CurDrif.exe      Main app
  CurDrif.bat      One-click launcher (use this)
  StopCursorMove.bat  Ask running app to stop
  cursermoveauto.bat  Compatibility launcher
  CursorMove.ico   Icon
  build.bat        Build from source
  README.md        This file
  src\
    CursorMove.Core.cs
    CursorMove.cs
    CursorMove.Tests.cs
```

## Build From Source

Requirements: Windows 10/11, no SDK needed (uses built-in .NET Framework csc).

```bat
build.bat
```

This compiles `CurDrif.exe`, builds and runs headless tests. All tests must pass.

## Notes & Limitations

- **Clicks are real** – Mid-wait clicks go inside the box. Keep the window visible and unobstructed.
- **No keyboard input** – Only mouse moves and left clicks.
- **Window must stay put** – Moving/resizing/minimizing while armed stops the session.
- **Secure desktop/lock screen** – Synthetic input won't work there.
- **Single box, single monitor** – Multi-monitor spanning not supported.

## Contributing

Issues and PRs are welcome at [github.com/MIRUN2303/CurDrif](https://github.com/MIRUN2303/CurDrif).

## SEO Metadata (for GitHub)

- **Repository name:** `CurDrif`
- **Short description:** `Stay in motion. A tiny native Windows app that drifts your cursor inside a fixed box with mid-wait clicks.`
- **Topics:** `cursor-mover`, `mouse-mover`, `anti-idle`, `keep-alive`, `windows`, `winforms`, `csharp`, `portable`, `automation`, `mouse-jitter`, `idle-prevention`
- **Website:** (optional) add later

---

**Stay in motion.**
