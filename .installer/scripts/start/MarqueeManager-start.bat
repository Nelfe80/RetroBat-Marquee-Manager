@echo off
setlocal EnableExtensions DisableDelayedExpansion

rem EmulationStation start hook for MarqueeManager.
rem This script is intended to be copied to:
rem   emulationstation\.emulationstation\scripts\start\MarqueeManager-start.bat
rem Pure batch on purpose: PowerShell one-liners with hidden windows are
rem flagged by antivirus heuristics (Trojan:Win32/ClickFix).

for %%I in ("%~dp0..\..\..\..\plugins\MarqueeManager") do set "PLUGIN_DIR=%%~fI"
set "MARQUEE_EXE=%PLUGIN_DIR%\MarqueeManager.exe"
set "MARQUEE_CONFIG=%PLUGIN_DIR%\config.ini"
set "LOG_DIR=%PLUGIN_DIR%\.log"
set "LOG_FILE=%LOG_DIR%\es-start-hook.log"

if not exist "%LOG_DIR%" mkdir "%LOG_DIR%" >nul 2>&1
echo %date% %time% ES start hook entered.>> "%LOG_FILE%"

if not exist "%MARQUEE_EXE%" (
  echo %date% %time% ERROR missing executable: %MARQUEE_EXE%>> "%LOG_FILE%"
  exit /b 1
)

if not exist "%MARQUEE_CONFIG%" (
  echo %date% %time% ERROR missing configuration: %MARQUEE_CONFIG%>> "%LOG_FILE%"
  exit /b 1
)

tasklist /FI "IMAGENAME eq MarqueeManager.exe" 2>nul | find /I "MarqueeManager.exe" >nul
if not errorlevel 1 (
  echo %date% %time% MarqueeManager already running.>> "%LOG_FILE%"
  exit /b 0
)

start "MarqueeManager" /D "%PLUGIN_DIR%" "%MARQUEE_EXE%"
echo %date% %time% MarqueeManager started.>> "%LOG_FILE%"
exit /b 0
