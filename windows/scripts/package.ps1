<#
.SYNOPSIS
  Builds the Velopack package and Setup.exe for Akoya for Windows. Used by release-windows.yml.

.PARAMETER Version     X.Y.Z, without the windows-v prefix.
.PARAMETER OutDir      Where vpk writes Setup.exe, the .nupkg files and releases.win.json.
.PARAMETER Token       When set, the previous release is downloaded first so vpk can also build a delta package.
.PARAMETER RequireIcon Fail instead of warning when windows/src/Akoya.App/Assets/akoya.ico is missing.
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutDir,
    [string]$RepoUrl = 'https://github.com/rohitnikamm/akoya',
    [string]$Token = '',
    [switch]$RequireIcon
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$temp = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }

# Show a failure as an annotation on the check run, with the end of the tool's output (the job log is not
# readable from every environment).
trap {
    $msg = ($_.Exception.Message -replace '\r?\n', ' | ')
    Write-Host "::error::package.ps1: $msg"
    exit 1
}

function Invoke-Native([string]$Exe, [string[]]$Arguments) {
    $lines = & $Exe @Arguments 2>&1 | ForEach-Object { $_.ToString() }
    $code = $LASTEXITCODE
    $lines | ForEach-Object { Write-Host $_ }
    if ($code -ne 0) {
        $tail = ($lines | Select-Object -Last 12) -join ' | '
        throw "$Exe failed with exit code ${code}: $tail"
    }
}

# vpk must be the same version as the Velopack package the app is built with: read it from the one pin.
$props = Get-Content (Join-Path $root 'windows\Directory.Packages.props') -Raw
$m = [regex]::Match($props, 'Include="Velopack"\s+Version="([^"]+)"')
if (-not $m.Success) { throw 'Velopack is not pinned in windows/Directory.Packages.props' }
$velopack = $m.Groups[1].Value

$tools = Join-Path $temp 'vpk-tool'
Invoke-Native dotnet @('tool', 'install', 'vpk', '--version', $velopack, '--tool-path', $tools)
$vpk = Join-Path $tools 'vpk.exe'
if ($env:GITHUB_ENV) { "VPK=$vpk" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8 }

# Self-contained win-x64: users do not need the .NET runtime.
$publish = Join-Path $temp 'akoya-publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
Invoke-Native dotnet @('publish', (Join-Path $root 'windows\src\Akoya.App\Akoya.App.csproj'),
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', "-p:Version=$Version", '-o', $publish)

# The assembly is Akoya.App (WPF pack URIs name it), but the file users see and the startup entry run is Akoya.exe.
# The apphost finds Akoya.App.dll by an embedded name, so renaming the exe is safe.
Move-Item (Join-Path $publish 'Akoya.App.exe') (Join-Path $publish 'Akoya.exe')

New-Item -ItemType Directory -Force $OutDir | Out-Null
if ($Token) {
    # Best effort: the first release has no predecessor.
    & $vpk download github --repoUrl $RepoUrl --token $Token --channel win --outputDir $OutDir
    if ($LASTEXITCODE -ne 0) { Write-Host '::notice::no previous Windows release to base a delta on' }
}

$packArgs = @('pack', '--packId', 'Akoya', '--packVersion', $Version, '--packDir', $publish,
    '--mainExe', 'Akoya.exe', '--packTitle', 'Akoya', '--packAuthors', 'rohitnikamm',
    '--runtime', 'win-x64', '--channel', 'win', '--shortcuts', 'StartMenuRoot', '--outputDir', $OutDir)
$icon = Join-Path $root 'windows\src\Akoya.App\Assets\akoya.ico'
if (Test-Path $icon) { $packArgs += @('--icon', $icon) }
elseif ($RequireIcon) { throw "missing $icon" }
else { Write-Host "::warning::$icon is missing; packing without an icon" }
Invoke-Native $vpk $packArgs

$setup = Get-ChildItem $OutDir -Filter '*Setup.exe' | Select-Object -First 1
if (-not $setup) { throw "vpk produced no Setup.exe in $OutDir" }
$mb = [math]::Round($setup.Length / 1MB, 1)
Write-Host "::notice::Setup.exe size=$($setup.Length) ($mb MB) name=$($setup.Name) velopack=$velopack"
if ($env:GITHUB_STEP_SUMMARY) {
    "Setup.exe: **$($setup.Length) bytes** ($mb MB), $($setup.Name), Velopack $velopack, version $Version" |
        Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}
