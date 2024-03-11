@echo off
echo Ce script va copier le fichier ESEventPush dans les dossiers script d'EmulationStation,
echo pour permettre d'ecouter les evenements d'ES et de mettre a jour le marquee.
echo.
echo This script will copy the ESEventPush file into EmulationStation's script folders,
echo to enable listening to ES events and update the marquee.
echo.
pause

SET source=.\.esinstall\ESEventPush.bat
SET dest1=..\..\emulationstation\.emulationstation\scripts\game-start
SET dest2=..\..\emulationstation\.emulationstation\scripts\game-end
SET dest3=..\..\emulationstation\.emulationstation\scripts\game-selected
SET dest4=..\..\emulationstation\.emulationstation\scripts\system-selected

echo Work in progress...

IF NOT EXIST "%dest1%" mkdir "%dest1%"
IF NOT EXIST "%dest2%" mkdir "%dest2%"
IF NOT EXIST "%dest3%" mkdir "%dest3%"
IF NOT EXIST "%dest4%" mkdir "%dest4%"

:: Supprimer ESEventPush.exe dans chaque destination s'il existe
IF EXIST "%dest1%\ESEventPush.exe" del "%dest1%\ESEventPush.exe"
IF EXIST "%dest2%\ESEventPush.exe" del "%dest2%\ESEventPush.exe"
IF EXIST "%dest3%\ESEventPush.exe" del "%dest3%\ESEventPush.exe"
IF EXIST "%dest4%\ESEventPush.exe" del "%dest4%\ESEventPush.exe"

IF EXIST "%source%" (
    copy "%source%" "%dest1%"
    copy "%source%" "%dest2%"
    copy "%source%" "%dest3%"
    copy "%source%" "%dest4%"
    echo Nice! Parfait!
) ELSE (
    echo Files not found - Fichiers non trouves
)

pause
