; ─────────────────────────────────────────────────────────────────────────────
; RetroBat Marquee Manager — installeur de BORNE (Inno Setup)
; Installe le plugin dans <RetroBat>\plugins\MarqueeManager, branche son hook
; EmulationStation, et — via apiexpose-bootstrap.iss — installe APIExpose dans le
; dossier frère plugins\APIExpose s'il manque (APIExpose déjà présent = intact).
; Build préalable : release des exes (MarqueeManager.exe + MarqueeManagerSetup.exe
; à la racine du plugin) ; APIExpose buildé (plugins\APIExpose\RetroBat.Api.exe).
; Compilation : ISCC.exe installer\MarqueeManager.iss
; ─────────────────────────────────────────────────────────────────────────────

#define AppName "RetroBat Marquee Manager"
#define AppVersion "2.5.0"
#define AppExe "MarqueeManager.exe"

[Setup]
AppId={{7C2F0A10-1D3B-4E55-9AA1-MARQUEEMGR001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=NelfeTech
AppPublisherURL=https://www.nelfetech.com
DefaultDirName=C:\RetroBat\plugins\MarqueeManager
DirExistsWarning=no
AppendDefaultDirName=no
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=MarqueeManager-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
DisableProgramGroupPage=yes
CloseApplications=yes
WizardStyle=modern

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
french.SelectDirDesc=Choisissez le dossier plugins\MarqueeManager de VOTRE RetroBat (ex. D:\RetroBat\plugins\MarqueeManager).

[Files]
; Tout l'arbre runtime du plugin, moins le dev/build/état/docs (calqué sur release.ps1)
Source: "..\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; \
    Excludes: "\src\*,\docs\*,\wiki\*,\media\*,\state\*,\artifacts\*,\dist\*,\installer\*,\tests\*,\.git\*,\.github\*,\.log\*,\.cache\*,\.versioning\*,\.archive\*,\.temp\*,\.graceful_exit\*,\obj\*,\bin\*,\site\*,\.gitignore,\.gitattributes,\mkdocs.yml,\RetroBatMarqueeManager.sln,\Directory.Build.props,\build.bat,\build-Setup.bat,\release.ps1,\config.ini,\config.ini.bak,\DmdDevice.log,\MARQUEE_MANAGER_SETUP.md,\RetroBat-Marquee-Manager-Plan-Developpement-UX-UI.md,\scripts\optimize-sprite-gifs.ps1,\tools\rbmarquee-gen\obj\*,\tools\rbmarquee-gen\bin\*,\Resources\sprites\master\*,CAHIER*,*.log,*.pdb,*.lib,__pycache__\*,*.pyc"

; Dépendance APIExpose (dossier frère) — installée seulement si absente
#include "..\..\APIExpose\installer\apiexpose-bootstrap.iss"

[Dirs]
Name: "{app}\state"; Flags: uninsneveruninstall

[Run]
Filename: "{app}\install-es-start-hook.bat"; WorkingDir: "{app}"; Description: "Démarrer Marquee Manager avec RetroBat (hook EmulationStation)"; Flags: postinstall skipifsilent
Filename: "{app}\MarqueeManagerSetup.exe"; WorkingDir: "{app}"; Description: "Ouvrir Marquee Manager Setup maintenant"; Flags: postinstall nowait skipifsilent unchecked

[UninstallRun]
Filename: "taskkill"; Parameters: "/f /im {#AppExe}"; Flags: runhidden; RunOnceId: "StopMarquee"
Filename: "{app}\uninstall-es-start-hook.bat"; WorkingDir: "{app}"; Flags: runhidden; RunOnceId: "UnhookMarquee"
