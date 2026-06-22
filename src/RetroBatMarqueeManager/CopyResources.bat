@echo off
REM %1 = OutDir (ex: bin\Release\net9.0-windows\)
REM %2 = ProjectDir (ex: F:\...\RetroBatMarqueeManager\src\RetroBatMarqueeManager\)

echo Copying Resources...

REM Copy icon.ico to output directory Resources folder
if exist "%~2Resources\icon.ico" (
    if not exist "%~1Resources\" mkdir "%~1Resources\"
    copy /Y "%~2Resources\icon.ico" "%~1Resources\icon.ico" > nul
    echo   - Copied icon.ico
)

echo Resources copied successfully!
exit /b 0
