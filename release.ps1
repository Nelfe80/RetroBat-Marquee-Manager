# release.ps1 - Construit et publie une release MarqueeManager sur GitHub.
# Usage :
#   .\release.ps1                # construit les archives + release DRAFT
#   .\release.ps1 -Publish      # publie directement (sans draft)
#   .\release.ps1 -PackageOnly  # construit seulement les archives
param(
    [switch]$Publish,
    [switch]$PackageOnly
)
$ErrorActionPreference = 'Stop'
$sz = @('C:\Program Files\7-Zip\7z.exe','C:\Program Files (x86)\7-Zip\7z.exe') | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $sz) { throw '7-Zip introuvable.' }

$root = Split-Path $PSScriptRoot -Parent
$name = Split-Path $PSScriptRoot -Leaf
$exe  = Join-Path $PSScriptRoot 'MarqueeManager.exe'
$verFull = (Get-Item $exe).VersionInfo.ProductVersion
$ver = ($verFull -split '\+')[0]
Write-Host "Version detectee : $verFull (tag v$ver)"

$out = Join-Path $PSScriptRoot "artifacts\release\v$ver"
New-Item -ItemType Directory -Force $out | Out-Null

$ex = @(
    "-x!$name\.git", "-x!$name\.gitignore", "-x!$name\.github",
    "-x!$name\src", "-x!$name\docs",
    "-x!$name\.archive", "-x!$name\.cache", "-x!$name\.temp",
    "-x!$name\.versioning", "-x!$name\.log", "-x!$name\.graceful_exit",
    "-x!$name\artifacts", "-x!$name\wiki", "-x!$name\mkdocs.yml", "-x!$name\site",
    "-x!$name\build.bat", "-x!$name\build-Setup.bat", "-x!$name\release.ps1",
    "-x!$name\RetroBatMarqueeManager.sln", "-x!$name\Directory.Build.props",
    "-x!$name\MARQUEE_MANAGER_SETUP.md", "-x!$name\state", "-x!$name\media",
    '-xr!CAHIER_DES_CHARGES*', '-xr!*.log', '-xr!__pycache__', '-xr!*.pyc'
)

Set-Location $root
$full   = Join-Path $out "$name-$ver-full.7z"
$update = Join-Path $out "$name-$ver-update.7z"
Write-Host 'Construction full.7z...'
& $sz a -t7z $full "$name\" @ex -mx=5 -bsp1 -bso0
Write-Host 'Construction update.7z...'
& $sz a -t7z $update "$name\" @ex "-x!$name\Resources" "-x!$name\tools" -mx=5 -bsp0 -bso0

$listing = & $sz l $full
$leaks = $listing | Select-String '\\src\\|\\docs\\|CAHIER|\.git|crash|checkpoint'
if ($leaks) { throw "FUITE DETECTEE dans l'archive : $($leaks[0])" }
Write-Host 'Controle anti-fuite : OK'

$hashes = Get-FileHash "$out\*.7z" -Algorithm SHA256 | ForEach-Object { '{0}  {1}' -f $_.Hash, (Split-Path $_.Path -Leaf) }
$hashes | Set-Content (Join-Path $out 'SHA256SUMS.txt') -Encoding ascii
Write-Host ($hashes -join "`n")

if ($PackageOnly) { Write-Host 'PackageOnly : archives pretes, pas de release.'; exit 0 }

$notes = @"
Voir le wiki pour l'installation : https://nelfe80.github.io/RetroBat-Marquee-Manager/
Prerequis : APIExpose + runtime .NET 8 Desktop.

| Archive | Contenu |
|---|---|
| ``$name-$ver-full.7z`` | Programme + Resources + tools (premiere installation) |
| ``$name-$ver-update.7z`` | Programme seul (mise a jour) |

### SHA-256
``````
$($hashes -join "`n")
``````
"@
$notesFile = Join-Path $out 'notes.md'
$notes | Set-Content $notesFile -Encoding utf8
$draftFlag = if ($Publish) { @() } else { @('--draft') }
gh release create "v$ver" --repo Nelfe80/RetroBat-Marquee-Manager --target main @draftFlag --title "MarqueeManager $ver" --notes-file $notesFile $full $update
if ($LASTEXITCODE -ne 0) { throw "gh release create a echoue (exit $LASTEXITCODE)." }
Write-Host "Release v$ver creee$(if (-not $Publish) { ' (draft, a publier sur GitHub)' })."
