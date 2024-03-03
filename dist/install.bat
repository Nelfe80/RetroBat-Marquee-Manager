@echo off
echo Ce script va copier le fichier ESEventPush dans les dossiers script d'EmulationStation,
echo pour permettre d'ecouter les evenements d'ES et de mettre a jour le marquee.
echo.
echo This script will copy the ESEventPush file into EmulationStation's script folders,
echo to enable listening to ES events and update the marquee.
echo.
pause

SET source=.\.esinstall\ESEventPush.exe
SET dest1=..\..\emulationstation\.emulationstation\scripts\game-start
SET dest2=..\..\emulationstation\.emulationstation\scripts\game-end
SET dest3=..\..\emulationstation\.emulationstation\scripts\game-selected
SET dest4=..\..\emulationstation\.emulationstation\scripts\system-selected

echo Work in progress...

IF EXIST "%source%" (
    copy "%source%" "%dest1%"
    copy "%source%" "%dest2%"
    copy "%source%" "%dest3%"
    copy "%source%" "%dest4%"
    echo Nice! Parfait!
) ELSE (
    echo Files not found - Fichiers on trouves
)

pause