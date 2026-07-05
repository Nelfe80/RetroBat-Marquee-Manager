@echo off
setlocal EnableExtensions DisableDelayedExpansion

rem EmulationStation start hook for MarqueeManager.
rem This script is intended to be copied to:
rem   emulationstation\.emulationstation\scripts\start\MarqueeManager-start.bat

for %%I in ("%~dp0..\..\..\..\plugins\MarqueeManager") do set "PLUGIN_DIR=%%~fI"
set "MARQUEE_EXE=%PLUGIN_DIR%\MarqueeManager.exe"
set "MARQUEE_CONFIG=%PLUGIN_DIR%\config.ini"
set "LOG_DIR=%PLUGIN_DIR%\.log"
set "LOG_FILE=%LOG_DIR%\es-start-hook.log"

if not exist "%LOG_DIR%" mkdir "%LOG_DIR%" >nul 2>&1

if not exist "%MARQUEE_EXE%" (
  echo MarqueeManager executable not found:
  echo   %MARQUEE_EXE%
  exit /b 1
)

if not exist "%MARQUEE_CONFIG%" (
  echo MarqueeManager configuration not found:
  echo   %MARQUEE_CONFIG%
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
"$ErrorActionPreference='SilentlyContinue'; ^
 $exe=[System.IO.Path]::GetFullPath('%MARQUEE_EXE%'); ^
 $wd=[System.IO.Path]::GetFullPath('%PLUGIN_DIR%'); ^
 $log='%LOG_FILE%'; ^
 function Log([string]$m){ $stamp=(Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff'); Add-Content -LiteralPath $log -Value ($stamp + ' ' + $m) -Encoding UTF8 }; ^
 Log 'ES start hook entered.'; ^
 $running=@(Get-Process -Name 'MarqueeManager' -ErrorAction SilentlyContinue).Where({ try { [System.IO.Path]::GetFullPath($_.Path) -eq $exe } catch { $false } }); ^
 if ($running) { Log ('MarqueeManager already running PID ' + $running[0].Id); exit 0 }; ^
 Unblock-File -LiteralPath $exe -ErrorAction SilentlyContinue; ^
 try { ^
   $proc=Start-Process -FilePath $exe -WorkingDirectory $wd -PassThru -ErrorAction Stop; ^
   if ($null -eq $proc) { throw 'Start-Process returned no process.' } ^
   Log ('MarqueeManager started PID ' + $proc.Id); ^
   exit 0; ^
 } catch { ^
   Log ('ERROR failed to start MarqueeManager: ' + $_.Exception.Message); ^
   exit 1; ^
 }"

exit /b %ERRORLEVEL%
