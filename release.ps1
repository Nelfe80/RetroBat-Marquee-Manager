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
    "-x!$name\scripts\optimize-sprite-gifs.ps1",
    "-x!$name\Resources\sprites\master",
    "-x!$name\tests",
    # dist = installeur Inno compile (~90 Mo) ; installer = sources .iss. Jamais dans
    # le pack runtime (sinon full/update explosent). Idem APIExpose.
    "-x!$name\dist", "-x!$name\installer",
    '-xr!obj', '-xr!bin',
    '-xr!CAHIER_DES_CHARGES*', '-xr!*.log', '-xr!__pycache__', '-xr!*.pyc'
)

Set-Location $root
$full   = Join-Path $out "$name-$ver-full.7z"
$update = Join-Path $out "$name-$ver-update.7z"
$expectedRuntimeSpriteCount = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Resources\sprites') -File -Filter '*.gif').Count
if ($expectedRuntimeSpriteCount -eq 0) { throw 'Aucun sprite runtime a empaqueter.' }
foreach ($archive in @($full, $update)) {
    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }
}
Write-Host 'Construction full.7z...'
& $sz a -t7z $full "$name\" @ex -mx=5 -bsp1 -bso0
if ($LASTEXITCODE -ne 0) { throw "Construction de full.7z echouee (exit $LASTEXITCODE)." }
Write-Host 'Construction update.7z...'
& $sz a -t7z $update "$name\" @ex "-x!$name\Resources" "-x!$name\tools" -mx=5 -bsp0 -bso0
if ($LASTEXITCODE -ne 0) { throw "Construction de update.7z echouee (exit $LASTEXITCODE)." }
# Les sprites optimises sont des ressources runtime et doivent aussi atteindre
# les installations existantes. Les masters restent reserves au depot Git.
& $sz a -t7z $update "$name\Resources\sprites\" "-x!$name\Resources\sprites\master" -mx=5 -bsp0 -bso0
if ($LASTEXITCODE -ne 0) { throw "Ajout des sprites a update.7z echoue (exit $LASTEXITCODE)." }

# L'archive fraichement ecrite peut etre brievement verrouillee (scan antivirus /
# flush disque) : "7z l" renvoie alors une liste vide. On relit avec quelques essais.
function Get-ArchiveListing {
    param([string]$SevenZip, [string]$Archive)
    # Le listing de 7-Zip contient des LIGNES VIDES (autour de l'en-tete et de la table).
    # On les retire ici, car un parametre [string[]] declare Mandatory valide CHAQUE
    # element : une seule chaine vide dans le tableau et la liaison echoue avec
    # "il s'agit d'une chaine vide". Le symptome ressemblait a un listing vide - d'ou le
    # diagnostic historique d'archive verrouillee - alors que 7-Zip repondait
    # parfaitement.
    #
    # On exige aussi un listing PLEIN : une reponse partielle passerait sinon le controle
    # anti-fuite pour "rien de suspect".
    for ($i = 0; $i -lt 12; $i++) {
        if ($i -gt 0) { Start-Sleep -Milliseconds 1000 }
        $listing = @(& $SevenZip l $Archive | Where-Object { $_ -ne '' })
        if ($listing.Count -ge 10 -and ($listing -join '') -match '7-Zip') { return $listing }
    }
    throw "Listing de $Archive incomplet apres plusieurs tentatives (fichier verrouille ?)."
}
$fullListing = Get-ArchiveListing $sz $full
$updateListing = Get-ArchiveListing $sz $update
function Assert-ArchiveContent {
    param(
        [Parameter(Mandatory = $true)][string]$ArchiveName,
        [Parameter(Mandatory = $true)][string[]]$Listing,
        [Parameter(Mandatory = $true)][int]$ExpectedSpriteCount
    )
    $leaks = $Listing | Select-String '\\src\\|\\docs\\|CAHIER|\.git|crash|checkpoint|EmbeddedSecretDefaults|\.env|\\Resources\\sprites\\master(?:\\|$)'
    if ($leaks) { throw "FUITE DETECTEE dans $ArchiveName : $($leaks[0])" }
    $runtimeSprites = @($Listing | Select-String '\\Resources\\sprites\\[^\\]+\.gif$')
    if ($runtimeSprites.Count -ne $ExpectedSpriteCount) {
        throw "$ArchiveName incomplet : $($runtimeSprites.Count) sprites runtime sur $ExpectedSpriteCount."
    }
}
Assert-ArchiveContent -ArchiveName (Split-Path $full -Leaf) -Listing $fullListing -ExpectedSpriteCount $expectedRuntimeSpriteCount
Assert-ArchiveContent -ArchiveName (Split-Path $update -Leaf) -Listing $updateListing -ExpectedSpriteCount $expectedRuntimeSpriteCount
$tracked = git -C $PSScriptRoot ls-files | Select-String 'EmbeddedSecretDefaults'
if ($tracked) { throw "FUITE DETECTEE dans git : $($tracked[0])" }
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
# Arguments assembles en TABLEAU puis passes en une fois, comme le fait APIExpose :
# un splat au milieu d'une ligne de commande native fait interpreter le drapeau par
# l'expansion de gh ("no matches found for `-`") au lieu d'etre transmis tel quel.
$ghArgs = @('release', 'create', "v$ver",
    '--repo', 'Nelfe80/RetroBat-Marquee-Manager', '--target', 'main',
    '--title', "MarqueeManager $ver", '--notes-file', $notesFile)
if (-not $Publish) { $ghArgs += '--draft' }
$ghArgs += @($full, $update)
& gh @ghArgs
if ($LASTEXITCODE -ne 0) { throw "gh release create a echoue (exit $LASTEXITCODE)." }
Write-Host "Release v$ver creee$(if (-not $Publish) { ' (draft, a publier sur GitHub)' })."
