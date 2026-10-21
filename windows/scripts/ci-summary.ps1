<#
.SYNOPSIS
  Summarizes a CI output folder (build.log and tests/*.trx) into summary.md and emits GitHub error annotations.
.PARAMETER OutDir
  Folder holding build.log and tests/*.trx. summary.md is written here.
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# CI summary')
$lines.Add('')

# --- Model ---------------------------------------------------------------
$modelFailure = Join-Path $OutDir 'model-failure.txt'
if (Test-Path -LiteralPath $modelFailure) {
    $lines.Add('## Model')
    $lines.Add('Speech model setup FAILED:')
    $lines.Add('```')
    Get-Content -LiteralPath $modelFailure | ForEach-Object { $lines.Add($_) }
    $lines.Add('```')
    $lines.Add('')
}

# --- Build ---------------------------------------------------------------
$buildLog = Join-Path $OutDir 'build.log'
$lines.Add('## Build')
if (-not (Test-Path -LiteralPath $buildLog)) {
    $lines.Add('No build.log found.')
}
else {
    $log = Get-Content -LiteralPath $buildLog
    $errors = @($log | Where-Object { $_ -match '\berror\s+[A-Za-z]+\d+\s*:' } | ForEach-Object { $_.Trim() } | Select-Object -Unique)
    $succeeded = [bool]($log | Where-Object { $_ -match 'Build succeeded' })
    if ($succeeded -and $errors.Count -eq 0) {
        $lines.Add('Build succeeded.')
    }
    else {
        $lines.Add('Build FAILED.')
    }
    $warningLine = $log | Where-Object { $_ -match '^\s*\d+ Warning\(s\)' } | Select-Object -Last 1
    if ($warningLine) { $lines.Add($warningLine.Trim()) }
    if ($errors.Count -gt 0) {
        $lines.Add('')
        $lines.Add("First $([Math]::Min(40, $errors.Count)) of $($errors.Count) error lines:")
        $lines.Add('```')
        $errors | Select-Object -First 40 | ForEach-Object { $lines.Add($_) }
        $lines.Add('```')
    }
}
$lines.Add('')

# --- Tests ---------------------------------------------------------------
$lines.Add('## Tests')
$testsDir = Join-Path $OutDir 'tests'
$trxFiles = @()
if (Test-Path $testsDir) {
    $trxFiles = @(Get-ChildItem $testsDir -Filter *.trx -Recurse | Sort-Object Name)
}
if ($trxFiles.Count -eq 0) {
    $lines.Add('No .trx files found (tests did not run).')
}

function ConvertTo-AnnotationText([string]$text) {
    return ($text -replace '%', '%25' -replace "`r", '%0D' -replace "`n", '%0A')
}

foreach ($trx in $trxFiles) {
    [xml]$doc = Get-Content -LiteralPath $trx.FullName -Raw
    $ns = [System.Xml.XmlNamespaceManager]::new($doc.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')

    $counters = $doc.SelectSingleNode('//t:ResultSummary/t:Counters', $ns)
    if ($counters) {
        $unit = $doc.SelectSingleNode('//t:TestDefinitions/t:UnitTest', $ns)
        $project = if ($unit -and $unit.storage) { [System.IO.Path]::GetFileNameWithoutExtension($unit.storage) } else { $trx.Name }
        $lines.Add("- ${project}: total $($counters.total), passed $($counters.passed), failed $($counters.failed), not executed $($counters.notExecuted)")
    }
    else {
        $lines.Add("- $($trx.Name): no counters in file")
    }

    $failed = $doc.SelectNodes("//t:UnitTestResult[@outcome='Failed']", $ns)
    foreach ($result in $failed) {
        $name = $result.testName
        $message = ''
        $stack = ''
        $messageNode = $result.SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $ns)
        $stackNode = $result.SelectSingleNode('t:Output/t:ErrorInfo/t:StackTrace', $ns)
        if ($messageNode) { $message = $messageNode.InnerText.Trim() }
        if ($stackNode) {
            $stack = (($stackNode.InnerText -split "`r?`n") | Select-Object -First 15) -join "`n"
        }

        $lines.Add('')
        $lines.Add("### FAILED: $name")
        $lines.Add('```')
        $lines.Add($message)
        if ($stack) {
            $lines.Add('')
            $lines.Add($stack)
        }
        $lines.Add('```')

        Write-Host "::error title=$(ConvertTo-AnnotationText $name)::$(ConvertTo-AnnotationText $message)"
    }
}

# When tests did not run (e.g. the test host crashed) or the run reported a failure, the console log is the evidence.
$testLog = Join-Path $OutDir 'test.log'
if (Test-Path -LiteralPath $testLog) {
    $logLines = @(Get-Content -LiteralPath $testLog)
    $suspicious = [bool]($logLines | Where-Object { $_ -match 'crash|aborted|Test Run Failed|Failed to run' })
    if ($trxFiles.Count -eq 0 -or $suspicious) {
        $lines.Add('')
        $lines.Add('### Last 40 lines of test.log')
        $lines.Add('```')
        $logLines | Select-Object -Last 40 | ForEach-Object { $lines.Add($_) }
        $lines.Add('```')
    }
}
elseif ($trxFiles.Count -eq 0) {
    $lines.Add('No test.log found either.')
}

# The smoke run (the built app started for a few seconds and exited); smoke.txt is written by that step.
$smoke = Join-Path $OutDir 'smoke.txt'
if (Test-Path -LiteralPath $smoke) {
    $lines.Add('')
    $lines.Add('## Smoke')
    $lines.Add('```')
    Get-Content -LiteralPath $smoke | ForEach-Object { $lines.Add($_) }
    $lines.Add('```')
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
Set-Content -Path (Join-Path $OutDir 'summary.md') -Value $lines -Encoding utf8
Write-Host ($lines -join "`n")
