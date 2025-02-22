@echo off
setlocal enabledelayedexpansion

:: Vérifier et fermer ESEvents.exe si en cours d'exécution
tasklist | find /I "ESEvents.exe" > NUL
if not errorlevel 1 taskkill /IM ESEvents.exe /F

:: Vérifier et fermer ESEventsScrapTopper.exe si en cours d'exécution
tasklist | find /I "ESEventsScrapTopper.exe" > NUL
if not errorlevel 1 taskkill /IM ESEventsScrapTopper.exe /F

:: Vérifier et fermer ESRetroAchievements.exe si en cours d'exécution
tasklist | find /I "ESRetroAchievements.exe" > NUL
if not errorlevel 1 taskkill /IM ESRetroAchievements.exe /F

:: Vérifier et fermer VPListenerWS.exe si en cours d'exécution
tasklist | find /I "VPListenerWS.exe" > NUL
if not errorlevel 1 taskkill /IM VPListenerWS.exe /F

:: Vérifier et fermer MAMEListenerWS.exe si en cours d'exécution
tasklist | find /I "MAMEListenerWS.exe" > NUL
if not errorlevel 1 taskkill /IM MAMEListenerWS.exe /F

:: Vérifier et fermer mpv.exe si en cours d'exécution
tasklist | find /I "mpv.exe" > NUL
if not errorlevel 1 taskkill /IM mpv.exe /F

:: Vérifier et fermer dmd.exe si en cours d'exécution
tasklist | find /I "dmd.exe" > NUL
if not errorlevel 1 taskkill /IM dmd.exe /F

:: Vérifier et fermer emulationstation.exe si en cours d'exécution
tasklist | find /I "emulationstation.exe" > NUL
if not errorlevel 1 taskkill /IM emulationstation.exe /F

endlocal
