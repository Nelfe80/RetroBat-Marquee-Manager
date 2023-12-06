@echo off
SETLOCAL ENABLEDELAYEDEXPANSION
:: File in folder \RetroBat\system\scripts 
cd ..
cd roms
FOR /D %%d IN (*) DO (
    :: Boucler sur tous les fichiers -marquee.* dans chaque sous-dossier
    FOR %%f IN ("%%d\*-marquee.*") DO (
        SET "filename=%%~nf"        
        SET "extension=%%~xf"
        SET "newfilename=!filename!Screen!extension!"
        REN "%%f" "!newfilename!"
    )
)
ENDLOCAL