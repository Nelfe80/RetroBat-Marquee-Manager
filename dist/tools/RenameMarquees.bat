@ECHO OFF
SETLOCAL ENABLEDELAYEDEXPANSION

CD /D "C:\RetroBat\roms"
IF %ERRORLEVEL% NEQ 0 (
    ECHO Directory C:\RetroBat\roms not found
    EXIT /B 1
)
ECHO RENAMING in /roms folder, all files -marquee to -marqueescrapped, are you ready?
PAUSE
:: Boucle sur chaque sous-dossier du répertoire roms
FOR /D %%d IN (*) DO (
    :: Changer de répertoire dans le dossier images du système
    IF EXIST "%%d\images" (
        CD "%%d\images"
        :: Boucle sur chaque fichier marquee et renommer
        FOR %%f IN (*-marquee.*) DO (
            SET "filename=%%~nf"
            SET "extension=%%~xf"
            :: Créer le nouveau nom de fichier
            SET "newfilename=!filename:-marquee=-marqueescrapped!!extension!"
            :: Renommer le fichier
            REN "%%f" "!newfilename!"
            ECHO In %%d : renamed %%f to !newfilename!
        )
        :: Retour au répertoire roms
        CD /D "C:\RetroBat\roms"
    ) ELSE (
        ECHO Images directory not found in %%d
    )
)
ECHO RENAMING FINISH
PAUSE

ENDLOCAL