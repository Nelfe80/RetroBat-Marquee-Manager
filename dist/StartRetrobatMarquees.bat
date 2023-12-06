@echo off
setlocal enabledelayedexpansion
taskkill /IM ESEvents.exe /F
taskkill /IM mpv.exe /F
start ESEvents.exe
start ..\retrobat.exe
timeout /t 1 /nobreak >NUL
endlocal