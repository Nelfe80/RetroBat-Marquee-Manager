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

:: Vérifier et fermer ESRetroAchievements.exe si en cours d'exécution
tasklist | find /I "retroarch.exe" > NUL
if not errorlevel 1 taskkill /IM retroarch.exe /F

:: Vérifier et fermer mpv.exe si en cours d'exécution
tasklist | find /I "mpv.exe" > NUL
if not errorlevel 1 taskkill /IM mpv.exe /F

:: Vérifier et fermer dmd.exe si en cours d'exécution
tasklist | find /I "dmd.exe" > NUL
if not errorlevel 1 taskkill /IM dmd.exe /F

:: Vérifier et fermer emulationstation.exe si en cours d'exécution
tasklist | find /I "emulationstation.exe" > NUL
if not errorlevel 1 taskkill /IM emulationstation.exe /F

:: Démarrer ESEvents.exe
start ESEvents.exe
timeout /t 1 /nobreak >NUL
tasklist | find /I "ESEvents.exe" > NUL
timeout /t 1 /nobreak >NUL

:: Démarrer retrobat.exe
start ..\..\retrobat.exe
timeout /t 2 /nobreak >NUL

:: Création d'un fichier VBScript temporaire pour changer le focus sur la fenêtre EmulationStation.
:: Créez le fichier VBScript pour modifier le focus de la fenêtre.
echo Set WshShell = CreateObject("WScript.Shell") > "%temp%\focus.vbs"
echo WshShell.AppActivate "EmulationStation" >> "%temp%\focus.vbs"
:: Lancer le script VBScript en mode asynchrone, de sorte que nous n'attendons pas sa conclusion.
start "" /B "cscript" "//nologo" "%temp%\focus.vbs"
:: Exécution du script VBScript et suppression du fichier temporaire.
timeout /t 1 /nobreak >NUL
del "%temp%\focus.vbs"

endlocal
