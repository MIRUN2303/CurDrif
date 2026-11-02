@echo off
setlocal
cd /d "%~dp0"

if not exist "CursorMove.exe" (
    echo CursorMove.exe is missing. Run build.bat first.
    pause
    exit /b 1
)

rem The BAT only launches the GUI. All cursor movement is done inside CursorMove.exe.
start "" "%~dp0CursorMove.exe"
endlocal
