@echo off
setlocal
cd /d "%~dp0"

if exist "CurDrif.exe" (
    "%~dp0CurDrif.exe" --stop
) else if exist "CursorMove.exe" (
    "%~dp0CursorMove.exe" --stop
) else (
    exit /b 1
)
if errorlevel 1 (
    echo CurDrif is not running.
    rem ping is used instead of timeout because timeout fails when input is redirected.
    ping -n 3 127.0.0.1 >nul
    exit /b 1
)

exit /b 0
