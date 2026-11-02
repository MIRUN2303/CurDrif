@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Could not find the .NET Framework C# compiler.
    echo Windows 10/11 normally includes it at %%WINDIR%%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
    pause
    exit /b 1
)

if not exist src\CursorMove.Core.cs goto missing
if not exist src\CursorMove.cs goto missing
if not exist src\CursorMove.Tests.cs goto missing
if not exist build mkdir build

set "ICON="
if exist CurDrif.ico set "ICON=/win32icon:CurDrif.ico"
if not exist "%ICON%" if exist CursorMove.ico set "ICON=/win32icon:CursorMove.ico"

echo Building CurDrif.exe ...
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ %ICON% /out:CurDrif.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll src\CursorMove.Core.cs src\CursorMove.cs
if errorlevel 1 goto fail

echo Building the headless tests ...
"%CSC%" /nologo /codepage:65001 /target:exe /optimize+ /out:build\CurDrif.Tests.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll src\CursorMove.Core.cs src\CursorMove.Tests.cs
if errorlevel 1 goto fail

echo Running tests ...
build\CurDrif.Tests.exe
if errorlevel 1 goto fail

echo.
echo Build and tests succeeded: CurDrif.exe
exit /b 0

:missing
echo Source files are missing. Keep the src folder next to build.bat.
pause
exit /b 1

:fail
echo.
echo Build or tests failed.
pause
exit /b 1
