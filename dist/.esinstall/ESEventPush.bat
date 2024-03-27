@echo off
:: Obtenir le nom du dossier courant
for %%i in ("%~dp0.") do set "currentDir=%%~nxi"
:: Capture tous les arguments dans args0
set args0=%*
echo %args0%
set args0=%args0:""="%
set args0=%args0:&=|A%
set args0=%args0:,=|v%
set args0=%args0:+=|p%
set args0=%args0:!=|%
echo %args0%
setlocal enabledelayedexpansion
echo !args0!
:: Initialiser la chaîne des paramètres
set "params=event=!currentDir!"

:: Découper args0 et ajouter chaque argument à la chaîne des paramètres
set "counter=1"
for %%a in (%args0%) do (
    :: Supprimer les guillemets externes
    set "param=%%~a"
	echo !param!
    :: Ajouter des guillemets autour de chaque paramètre et échapper correctement les guillemets internes
    set "paramEscaped=!param:"=""!"
    set "params=!params!&param!counter!="!paramEscaped!""
    set /a counter+=1
)

set "outputFile=..\..\..\..\plugins\MarqueeManager\ESEvent.arg"
:: Écrire dans le fichier game-selected.arg
echo !params! > !outputFile!