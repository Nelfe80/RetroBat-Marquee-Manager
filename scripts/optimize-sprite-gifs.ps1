[CmdletBinding()]
param(
    [string]$ConvertPath = "",
    [string]$PluginRoot = "",
    [int]$TargetFps = 24
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($PluginRoot)) {
    $PluginRoot = Split-Path -Parent $PSScriptRoot
}
$PluginRoot = [System.IO.Path]::GetFullPath($PluginRoot)
$RuntimeDirectory = Join-Path $PluginRoot "Resources\sprites"
$MasterDirectory = Join-Path $RuntimeDirectory "master"
$MasterManifestPath = Join-Path $MasterDirectory "manifest.json"
$RuntimeManifestPath = Join-Path $RuntimeDirectory "optimization-manifest.json"

if (-not (Test-Path -LiteralPath $RuntimeDirectory -PathType Container)) {
    throw "Sprite directory not found: $RuntimeDirectory"
}
if ($TargetFps -lt 1 -or $TargetFps -gt 50) {
    throw "TargetFps must be between 1 and 50 so every GIF frame remains at least 20 ms."
}

if ([string]::IsNullOrWhiteSpace($ConvertPath)) {
    $ConvertPath = Join-Path (Split-Path -Parent $PluginRoot) "APIExpose\tools\imagemagick\convert.exe"
}
$ConvertPath = [System.IO.Path]::GetFullPath($ConvertPath)
if (-not (Test-Path -LiteralPath $ConvertPath -PathType Leaf)) {
    throw "ImageMagick convert.exe not found: $ConvertPath"
}

Add-Type -AssemblyName System.Drawing

function Get-GifMetadata {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = [System.IO.Path]::GetFullPath($Path)
    $image = [System.Drawing.Image]::FromFile($resolved)
    try {
        $frameCount = $image.GetFrameCount([System.Drawing.Imaging.FrameDimension]::Time)
        $delays = @()
        try {
            $delayProperty = $image.GetPropertyItem(0x5100)
            for ($index = 0; $index -lt $frameCount; $index++) {
                $offset = $index * 4
                $delay = if ($offset + 4 -le $delayProperty.Value.Length) {
                    [BitConverter]::ToInt32($delayProperty.Value, $offset)
                }
                else {
                    0
                }
                $delays += [int]$delay
            }
        }
        catch {
            for ($index = 0; $index -lt $frameCount; $index++) {
                $delays += 0
            }
        }

        $loop = $null
        try {
            $loopProperty = $image.GetPropertyItem(0x5101)
            if ($loopProperty.Value.Length -ge 2) {
                $loop = [int][BitConverter]::ToUInt16($loopProperty.Value, 0)
            }
        }
        catch {
            $loop = $null
        }

        $normalizedDelays = @($delays | ForEach-Object { [Math]::Max(2, [int]$_) })
        $rawDurationCs = 0
        $runtimeDurationCs = 0
        foreach ($delay in $delays) { $rawDurationCs += [int]$delay }
        foreach ($delay in $normalizedDelays) { $runtimeDurationCs += [int]$delay }

        return [pscustomobject]@{
            Path = $resolved
            Width = [int]$image.Width
            Height = [int]$image.Height
            FrameCount = [int]$frameCount
            RawDelaysCs = @($delays)
            NormalizedDelaysCs = $normalizedDelays
            RawDurationMs = [int64]$rawDurationCs * 10
            RuntimeDurationMs = [int64]$runtimeDurationCs * 10
            Loop = $loop
            HasAlpha = (($image.Flags -band [int][System.Drawing.Imaging.ImageFlags]::HasAlpha) -ne 0)
            Bytes = [int64](Get-Item -LiteralPath $resolved).Length
            Sha256 = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash
        }
    }
    finally {
        $image.Dispose()
    }
}

function Get-ResamplePlan {
    param(
        [Parameter(Mandatory = $true)][int[]]$DelaysCs,
        [Parameter(Mandatory = $true)][int]$FramesPerSecond
    )

    $frameCount = $DelaysCs.Count
    $totalCs = 0
    foreach ($delay in $DelaysCs) { $totalCs += [int]$delay }
    if ($frameCount -eq 0 -or $totalCs -le 0) {
        throw "Cannot build a resampling plan for an empty animation."
    }

    $maximumFrames = [Math]::Max(1, [int][Math]::Floor($totalCs * $FramesPerSecond / 100.0))
    if ($frameCount -le $maximumFrames) {
        $unchanged = @()
        for ($index = 0; $index -lt $frameCount; $index++) {
            $unchanged += [pscustomobject]@{
                SourceIndex = $index
                DelayCs = [int]$DelaysCs[$index]
            }
        }
        return $unchanged
    }

    $sourceEnds = @()
    $sourceTime = 0
    foreach ($delay in $DelaysCs) {
        $sourceTime += [int]$delay
        $sourceEnds += $sourceTime
    }

    $plan = @()
    $sourceIndex = 0
    for ($targetIndex = 0; $targetIndex -lt $maximumFrames; $targetIndex++) {
        $startCs = [int][Math]::Floor($targetIndex * $totalCs / [double]$maximumFrames)
        $endCs = [int][Math]::Floor(($targetIndex + 1) * $totalCs / [double]$maximumFrames)
        $delayCs = $endCs - $startCs
        $midpointCs = ($startCs + $endCs) / 2.0
        while ($sourceIndex -lt $sourceEnds.Count - 1 -and $sourceEnds[$sourceIndex] -le $midpointCs) {
            $sourceIndex++
        }
        $plan += [pscustomobject]@{
            SourceIndex = $sourceIndex
            DelayCs = $delayCs
        }
    }
    return $plan
}

function Assert-MasterIntegrity {
    if (-not (Test-Path -LiteralPath $MasterManifestPath -PathType Leaf)) {
        return
    }

    $manifest = Get-Content -LiteralPath $MasterManifestPath -Raw | ConvertFrom-Json
    if ([string]$manifest.schema -ne "marqueemanager.sprite-masters.v1") {
        throw "Unsupported master manifest schema: $($manifest.schema)"
    }

    $entries = @($manifest.sprites)
    if ($entries.Count -eq 0) {
        throw "Master manifest contains no sprite. Refusing to bypass immutable originals."
    }

    $declaredNames = @()
    $seenNames = @{}
    foreach ($entry in $entries) {
        $name = [string]$entry.name
        if ([string]::IsNullOrWhiteSpace($name) -or
            [System.IO.Path]::GetFileName($name) -ne $name -or
            [System.IO.Path]::GetExtension($name) -ne ".gif") {
            throw "Invalid sprite name in master manifest: $name"
        }
        if ($seenNames.ContainsKey($name)) {
            throw "Duplicate sprite in master manifest: $name"
        }
        $seenNames[$name] = $true
        $declaredNames += $name

        $expectedHash = [string]$entry.sha256
        if ($expectedHash -notmatch "^[0-9A-Fa-f]{64}$") {
            throw "Invalid SHA-256 in master manifest for $name"
        }

        $path = Join-Path $MasterDirectory $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Master declared by manifest is missing: $path"
        }
        $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if (-not $actualHash.Equals($expectedHash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Master hash mismatch for $name. Refusing to overwrite immutable originals."
        }
    }

    $actualNames = @(Get-ChildItem -LiteralPath $MasterDirectory -File -Filter "*.gif" |
        ForEach-Object { $_.Name } | Sort-Object)
    $nameDifferences = @(Compare-Object ($declaredNames | Sort-Object) $actualNames)
    if ($nameDifferences.Count -gt 0) {
        throw "Master manifest does not describe the exact GIF set: $($nameDifferences.InputObject -join ', ')"
    }
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Value
    )
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Value, $utf8)
}

function Assert-FullCanvasFrames {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$ExpectedWidth,
        [Parameter(Mandatory = $true)][int]$ExpectedHeight
    )

    $expected = "$ExpectedWidth|$ExpectedHeight|$ExpectedWidth|$ExpectedHeight|+0|+0"
    $rows = @(& $ConvertPath $Path "-format" "%w|%h|%W|%H|%X|%Y\n" "info:")
    if ($LASTEXITCODE -ne 0) {
        throw "ImageMagick could not inspect frame geometry for $Path"
    }
    $invalidRows = @($rows | Where-Object { ([string]$_).Trim() -ne $expected })
    if ($rows.Count -eq 0 -or $invalidRows.Count -gt 0) {
        throw "GIF contains a partial canvas frame or non-zero frame offset: $Path"
    }
}

New-Item -ItemType Directory -Path $MasterDirectory -Force | Out-Null
Assert-MasterIntegrity

$runtimeGifs = @(Get-ChildItem -LiteralPath $RuntimeDirectory -File -Filter "*.gif" | Sort-Object Name)
if ($runtimeGifs.Count -eq 0) {
    throw "No runtime GIF found in $RuntimeDirectory"
}

foreach ($runtimeGif in $runtimeGifs) {
    $masterPath = Join-Path $MasterDirectory $runtimeGif.Name
    if (-not (Test-Path -LiteralPath $masterPath -PathType Leaf)) {
        Copy-Item -LiteralPath $runtimeGif.FullName -Destination $masterPath
    }
}

$masterGifs = @(Get-ChildItem -LiteralPath $MasterDirectory -File -Filter "*.gif" | Sort-Object Name)
if ($masterGifs.Count -ne $runtimeGifs.Count) {
    throw "Master/runtime count mismatch: $($masterGifs.Count) masters for $($runtimeGifs.Count) runtime GIFs."
}
$runtimeNames = @($runtimeGifs | ForEach-Object { $_.Name } | Sort-Object)
$masterNames = @($masterGifs | ForEach-Object { $_.Name } | Sort-Object)
$nameDifferences = @(Compare-Object $runtimeNames $masterNames)
if ($nameDifferences.Count -gt 0) {
    throw "Master/runtime name mismatch: $($nameDifferences.InputObject -join ', ')"
}

$capturedAtUtc = [DateTime]::UtcNow.ToString("o")
if (Test-Path -LiteralPath $MasterManifestPath -PathType Leaf) {
    try {
        $existingManifest = Get-Content -LiteralPath $MasterManifestPath -Raw | ConvertFrom-Json
        if (-not [string]::IsNullOrWhiteSpace([string]$existingManifest.capturedAtUtc)) {
            $capturedAtUtc = [string]$existingManifest.capturedAtUtc
        }
    }
    catch {
        throw "Cannot read existing master manifest: $MasterManifestPath"
    }
}

$masterEntries = @()
foreach ($masterGif in $masterGifs) {
    $metadata = Get-GifMetadata -Path $masterGif.FullName
    $masterEntries += [ordered]@{
        name = $masterGif.Name
        sha256 = $metadata.Sha256
        bytes = $metadata.Bytes
        width = $metadata.Width
        height = $metadata.Height
        frames = $metadata.FrameCount
        rawDurationMs = $metadata.RawDurationMs
        runtimeDurationMs = $metadata.RuntimeDurationMs
        loop = $metadata.Loop
        hasAlpha = $metadata.HasAlpha
    }
}
$masterManifest = [ordered]@{
    schema = "marqueemanager.sprite-masters.v1"
    capturedAtUtc = $capturedAtUtc
    sprites = @($masterEntries)
}
Write-Utf8NoBom -Path $MasterManifestPath -Value (($masterManifest | ConvertTo-Json -Depth 6) + [Environment]::NewLine)
Assert-MasterIntegrity

$stagingBase = Join-Path $PluginRoot ".temp"
New-Item -ItemType Directory -Path $stagingBase -Force | Out-Null
$stagingDirectory = Join-Path $stagingBase ("sprite-gif-optimizer-" + [Guid]::NewGuid().ToString("N"))
$stagingDirectory = [System.IO.Path]::GetFullPath($stagingDirectory)
$resolvedStagingBase = [System.IO.Path]::GetFullPath($stagingBase).TrimEnd("\") + "\"
if (-not $stagingDirectory.StartsWith($resolvedStagingBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe staging path: $stagingDirectory"
}
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

$versionLine = (& $ConvertPath -version | Select-Object -First 1)
$results = @()
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$promotionStarted = $false
$backupDirectory = Join-Path $stagingDirectory "runtime-backup"
$backupManifestPath = Join-Path $backupDirectory "optimization-manifest.json"

try {
    $ordinal = 0
    foreach ($masterGif in $masterGifs) {
        $ordinal++
        $source = Get-GifMetadata -Path $masterGif.FullName
        $maximumHeight = if ($masterGif.Name.StartsWith("full_", [StringComparison]::OrdinalIgnoreCase)) { 320 } else { 96 }
        $targetHeight = [Math]::Min($source.Height, $maximumHeight)
        $targetWidth = if ($source.Height -gt $maximumHeight) {
            [Math]::Max(1, [int][Math]::Floor($source.Width * $maximumHeight / [double]$source.Height))
        }
        else {
            $source.Width
        }
        $geometry = "$($targetWidth)x$($targetHeight)!"
        $plan = @(Get-ResamplePlan -DelaysCs $source.NormalizedDelaysCs -FramesPerSecond $TargetFps)
        $outputPath = Join-Path $stagingDirectory $masterGif.Name

        Write-Host ("[{0}/{1}] {2}: {3}x{4}/{5} frames -> {6}x{7}/{8} planned frames" -f `
            $ordinal, $masterGifs.Count, $masterGif.Name, $source.Width, $source.Height, `
            $source.FrameCount, $targetWidth, $targetHeight, $plan.Count)

        $arguments = @(
            "-limit", "thread", "1",
            $masterGif.FullName,
            "-background", "none",
            "-coalesce",
            "-filter", "Triangle",
            "-resize", $geometry,
            "+repage"
        )
        foreach ($plannedFrame in $plan) {
            $arguments += @(
                "(",
                "-clone", [string]$plannedFrame.SourceIndex,
                "-set", "delay", [string]$plannedFrame.DelayCs,
                ")"
            )
        }
        $arguments += @(
            "-delete", "0-$($source.FrameCount - 1)",
            "-layers", "RemoveDups",
            "-coalesce",
            "+repage"
        )
        $arguments += @(
            "-set", "dispose", $(if ($source.HasAlpha) { "Background" } else { "None" }),
            $outputPath
        )

        $itemWatch = [System.Diagnostics.Stopwatch]::StartNew()
        & $ConvertPath @arguments
        $exitCode = $LASTEXITCODE
        $itemWatch.Stop()
        if ($exitCode -ne 0) {
            throw "ImageMagick failed for $($masterGif.Name) with exit code $exitCode."
        }
        if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
            throw "ImageMagick did not create $outputPath"
        }

        $runtime = Get-GifMetadata -Path $outputPath
        if ($runtime.Width -ne $targetWidth -or $runtime.Height -ne $targetHeight) {
            throw "Unexpected dimensions for $($masterGif.Name): $($runtime.Width)x$($runtime.Height), expected ${targetWidth}x${targetHeight}."
        }
        if ($runtime.FrameCount -lt 1 -or $runtime.FrameCount -gt $plan.Count) {
            throw "Unexpected frame count for $($masterGif.Name): $($runtime.FrameCount), plan=$($plan.Count)."
        }
        if ($runtime.RuntimeDurationMs -ne $source.RuntimeDurationMs) {
            throw "Duration mismatch for $($masterGif.Name): $($runtime.RuntimeDurationMs) ms, expected $($source.RuntimeDurationMs) ms."
        }
        if ([string]$runtime.Loop -ne [string]$source.Loop) {
            throw "Loop mismatch for $($masterGif.Name): $($runtime.Loop), expected $($source.Loop)."
        }
        if ($runtime.HasAlpha -ne $source.HasAlpha) {
            throw "Alpha mismatch for $($masterGif.Name): $($runtime.HasAlpha), expected $($source.HasAlpha)."
        }
        if (@($runtime.RawDelaysCs | Where-Object { $_ -lt 2 }).Count -gt 0) {
            throw "Runtime GIF still contains a frame shorter than 20 ms: $($masterGif.Name)."
        }
        Assert-FullCanvasFrames -Path $outputPath -ExpectedWidth $targetWidth -ExpectedHeight $targetHeight

        $sourceDecodedBytes = [int64]$source.Width * $source.Height * 4 * $source.FrameCount
        $runtimeDecodedBytes = [int64]$runtime.Width * $runtime.Height * 4 * $runtime.FrameCount
        $results += [ordered]@{
            name = $masterGif.Name
            masterSha256 = $source.Sha256
            runtimeSha256 = $runtime.Sha256
            source = [ordered]@{
                bytes = $source.Bytes
                width = $source.Width
                height = $source.Height
                frames = $source.FrameCount
                effectiveDurationMs = $source.RuntimeDurationMs
                decodedBytes = $sourceDecodedBytes
            }
            runtime = [ordered]@{
                bytes = $runtime.Bytes
                width = $runtime.Width
                height = $runtime.Height
                frames = $runtime.FrameCount
                effectiveDurationMs = $runtime.RuntimeDurationMs
                decodedBytes = $runtimeDecodedBytes
            }
            conversionMs = [int64]$itemWatch.ElapsedMilliseconds
        }
    }

    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    foreach ($runtimeGif in $runtimeGifs) {
        Copy-Item -LiteralPath $runtimeGif.FullName -Destination (Join-Path $backupDirectory $runtimeGif.Name)
    }
    if (Test-Path -LiteralPath $RuntimeManifestPath -PathType Leaf) {
        Copy-Item -LiteralPath $RuntimeManifestPath -Destination $backupManifestPath
    }
    $promotionStarted = $true

    foreach ($result in $results) {
        $sourcePath = Join-Path $stagingDirectory ([string]$result.name)
        $destinationPath = Join-Path $RuntimeDirectory ([string]$result.name)
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
    }

    $stopwatch.Stop()
    $runtimeManifest = [ordered]@{
        schema = "marqueemanager.sprite-runtime-optimization.v1"
        generatedAtUtc = [DateTime]::UtcNow.ToString("o")
        imageMagick = [string]$versionLine
        targetFps = $TargetFps
        ordinaryMaxHeight = 96
        backdropMaxHeight = 320
        minimumFrameDelayMs = 20
        fullCanvasFrames = $true
        exactDuplicateFramesRemoved = $true
        totalConversionMs = [int64]$stopwatch.ElapsedMilliseconds
        sprites = @($results)
    }
    Write-Utf8NoBom -Path $RuntimeManifestPath -Value (($runtimeManifest | ConvertTo-Json -Depth 8) + [Environment]::NewLine)
}
catch {
    if ($promotionStarted) {
        foreach ($runtimeGif in $runtimeGifs) {
            $backupPath = Join-Path $backupDirectory $runtimeGif.Name
            $runtimePath = Join-Path $RuntimeDirectory $runtimeGif.Name
            if (Test-Path -LiteralPath $backupPath -PathType Leaf) {
                Copy-Item -LiteralPath $backupPath -Destination $runtimePath -Force
            }
        }
        if (Test-Path -LiteralPath $backupManifestPath -PathType Leaf) {
            Copy-Item -LiteralPath $backupManifestPath -Destination $RuntimeManifestPath -Force
        }
        elseif (Test-Path -LiteralPath $RuntimeManifestPath -PathType Leaf) {
            Remove-Item -LiteralPath $RuntimeManifestPath -Force
        }
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        $resolved = [System.IO.Path]::GetFullPath($stagingDirectory)
        if ($resolved.StartsWith($resolvedStagingBase, [StringComparison]::OrdinalIgnoreCase) -and
            $resolved.Length -gt $resolvedStagingBase.Length + 12) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}

$sourceBytesTotal = 0L
$runtimeBytesTotal = 0L
$sourceDecodedTotal = 0L
$runtimeDecodedTotal = 0L
$sourceFramesTotal = 0
$runtimeFramesTotal = 0
foreach ($result in $results) {
    $sourceBytesTotal += [int64]$result.source.bytes
    $runtimeBytesTotal += [int64]$result.runtime.bytes
    $sourceDecodedTotal += [int64]$result.source.decodedBytes
    $runtimeDecodedTotal += [int64]$result.runtime.decodedBytes
    $sourceFramesTotal += [int]$result.source.frames
    $runtimeFramesTotal += [int]$result.runtime.frames
}

[pscustomobject]@{
    Sprites = $results.Count
    SourceFrames = $sourceFramesTotal
    RuntimeFrames = $runtimeFramesTotal
    SourceMiB = [Math]::Round($sourceBytesTotal / 1MB, 2)
    RuntimeMiB = [Math]::Round($runtimeBytesTotal / 1MB, 2)
    SourceDecodedMiB = [Math]::Round($sourceDecodedTotal / 1MB, 2)
    RuntimeDecodedMiB = [Math]::Round($runtimeDecodedTotal / 1MB, 2)
    ElapsedSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
    MasterDirectory = $MasterDirectory
    RuntimeManifest = $RuntimeManifestPath
} | Format-List
