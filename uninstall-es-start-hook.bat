@echo off
setlocal EnableExtensions DisableDelayedExpansion

rem Removes only the MarqueeManager EmulationStation start hook.
rem RetroBat's updatestores.bat is never modified.

set "PLUGIN_DIR=%~dp0"
for %%I in ("%PLUGIN_DIR%..\..") do set "RETROBAT_ROOT=%%~fI"

set "TARGET=%RETROBAT_ROOT%\emulationstation\.emulationstation\scripts\start\MarqueeManager-start.bat"

if exist "%TARGET%" (
  del "%TARGET%"
  echo Removed MarqueeManager ES start hook:
  echo   %TARGET%
) else (
  echo MarqueeManager ES start hook was not installed:
  echo   %TARGET%
)

echo.
echo RetroBat's updatestores.bat was not modified.
pause
exit /b 0
