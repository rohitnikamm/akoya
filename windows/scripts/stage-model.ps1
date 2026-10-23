<#
.SYNOPSIS
  Puts the pinned speech model into <Destination>\models\<FolderName>\ so the app can ship it. Used by package.ps1
  and windows.yml. The URL, size, SHA-256 and file sizes are read from ModelManifest.cs (the single source).

.PARAMETER Destination The folder that will contain models\<FolderName>\ (the publish folder).
.PARAMETER CacheDir    Where the downloaded tarball is kept; a tarball whose SHA-256 matches is reused.
#>
param(
    [Parameter(Mandatory)][string]$Destination,
    [string]$CacheDir = ''
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$temp = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
if (-not $CacheDir) { $CacheDir = Join-Path $temp 'akoya-model-cache' }

$manifest = Get-Content (Join-Path $root 'windows\src\Akoya.Core\Model\ModelManifest.cs') -Raw
function Pin([string]$pattern) {
    $m = [regex]::Match($manifest, $pattern)
    if (-not $m.Success) { throw "ModelManifest.cs: no match for $pattern" }
    $m.Groups[1].Value
}
$url = Pin 'Url\s*=\s*"([^"]+)"'
$size = [int64](Pin 'TarballBytes\s*=\s*(\d+)')
$sha = Pin 'TarballSha256\s*=\s*"([0-9a-f]{64})"'
$folder = Pin 'FolderName\s*=\s*"([^"]+)"'
$fileBytes = @{}
foreach ($m in [regex]::Matches($manifest, '\["([^"]+)"\]\s*=\s*(\d+)')) { $fileBytes[$m.Groups[1].Value] = [int64]$m.Groups[2].Value }
if ($fileBytes.Count -ne 4) { throw "expected 4 pinned file sizes in ModelManifest.cs, found $($fileBytes.Count)" }

function Test-Tarball([string]$path) {
    (Test-Path $path) -and ((Get-Item $path).Length -eq $size) -and
        ((Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -eq $sha)
}

New-Item -ItemType Directory -Force $CacheDir | Out-Null
$tarball = Join-Path $CacheDir "$folder.tar.bz2"
if (Test-Tarball $tarball) {
    Write-Host "::notice::model tarball cache hit ($tarball)"
} else {
    Write-Host "::notice::model tarball cache miss, downloading $url"
    $part = "$tarball.part"
    Remove-Item $part, $tarball -Force -ErrorAction SilentlyContinue
    curl.exe -L --fail --retry 3 -o $part $url
    if ($LASTEXITCODE -ne 0) { throw 'model download failed' }
    if (-not (Test-Tarball $part)) { throw "the downloaded model does not match the pinned size $size / SHA-256 $sha" }
    Move-Item $part $tarball
}

$extract = Join-Path $temp ("akoya-model-extract-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $extract | Out-Null
try {
    tar -xjf $tarball -C $extract
    if ($LASTEXITCODE -ne 0) { throw 'model extraction failed' }
    $target = Join-Path (Join-Path $Destination 'models') $folder
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    New-Item -ItemType Directory -Force $target | Out-Null
    foreach ($name in $fileBytes.Keys) {
        $src = Join-Path (Join-Path $extract $folder) $name
        if (-not (Test-Path $src)) { throw "the model tarball has no $name" }
        Copy-Item $src (Join-Path $target $name)
        $actual = (Get-Item (Join-Path $target $name)).Length
        if ($actual -ne $fileBytes[$name]) { throw "$name is $actual bytes, expected $($fileBytes[$name])" }
    }
    Write-Host "::notice::staged the speech model in $target"
} finally {
    Remove-Item $extract -Recurse -Force -ErrorAction SilentlyContinue
}
