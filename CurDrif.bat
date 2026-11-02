@echo off
setlocal
cd /d "%~dp0"

if not exist "CurDrif.exe" (
    if exist "CursorMove.exe" (
        echo Renamed launcher: starting CurDrif.exe not found, trying CursorMove.exe...
        start "" "%~dp0CursorMove.exe"
        endlocal
        exit /b 0
    )
    echo CurDrif.exe is missing. Run build.bat first.
    pause
    exit /b 1
)

rem Stay in motion. All cursor movement is done inside CurDrif.exe.
start "" "%~dp0CurDrif.exe"
endlocal
