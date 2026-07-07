@echo off
setlocal enabledelayedexpansion

echo ===================================================
echo  RetroBat Marquee Manager - Build Release
echo ===================================================
echo.

where dotnet >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] .NET SDK introuvable dans le PATH.
    pause
    exit /b 1
)

echo [0/4] Arret de l'instance en cours...
taskkill /IM MarqueeManager.exe /F >nul 2>nul
if %ERRORLEVEL% equ 0 (
    echo   - Instance arretee. Attente...
    timeout /t 2 /nobreak >nul
) else (
    echo   - Aucune instance active.
)

echo.
echo [1/4] Nettoyage des artefacts precedents...
dotnet clean src\RetroBatMarqueeManager\RetroBatMarqueeManager.csproj -c Release -v quiet >nul 2>nul
echo   - Clean OK.

set "BUILD_TMP=%~dp0.temp\publish"
if exist "%BUILD_TMP%" rmdir /S /Q "%BUILD_TMP%"
if not exist "%BUILD_TMP%" mkdir "%BUILD_TMP%"

echo.
echo [2/4] Publication Release win-x64...
echo.
dotnet publish src\RetroBatMarqueeManager\RetroBatMarqueeManager.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained false ^
    -p:PublishSingleFile=true ^
    -p:PublishReadyToRun=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o "%BUILD_TMP%"

if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] Echec de la compilation.
    if exist "%BUILD_TMP%" rmdir /S /Q "%BUILD_TMP%"
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [3/4] Copie de la publication a la racine...
if exist "%BUILD_TMP%\MarqueeManager.exe" (
    copy /Y "%BUILD_TMP%\MarqueeManager.exe" "%~dp0MarqueeManager.exe" >nul
    if %ERRORLEVEL% neq 0 (
        echo [ERROR] Copie impossible. Le fichier est-il encore verrouille ?
        rmdir /S /Q "%BUILD_TMP%"
        pause
        exit /b 1
    )
    if exist "%BUILD_TMP%\Resources" (
        xcopy /E /I /Y "%BUILD_TMP%\Resources" "%~dp0Resources" >nul
        if %ERRORLEVEL% geq 4 (
            echo [ERROR] Copie du dossier Resources impossible.
            rmdir /S /Q "%BUILD_TMP%"
            pause
            exit /b 1
        )
    )
    echo   - MarqueeManager.exe et ressources mis a jour a la racine.
) else (
    echo [ERROR] Executable introuvable dans le dossier de publication.
    dir "%BUILD_TMP%"
    rmdir /S /Q "%BUILD_TMP%"
    pause
    exit /b 1
)

echo.
echo [4/4] Nettoyage temporaire...
rmdir /S /Q "%BUILD_TMP%"
echo   - Done.

echo.
echo [5/5] Build de MarqueeManagerSetup.exe...
call "%~dp0build-Setup.bat" --no-pause
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Echec du build MarqueeManagerSetup.
    pause
    exit /b 1
)

echo.
echo ===================================================
echo  Build termine avec succes !
echo  Lancez MarqueeManager.exe depuis la racine.
echo ===================================================
echo.
pause
