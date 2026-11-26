param([string]$ResultsFolder = (Join-Path $PSScriptRoot 'tests'))
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'SopRecorder.exe'))) { & (Join-Path $PSScriptRoot 'Build.ps1') }
$ResultsFolder = [System.IO.Path]::GetFullPath($ResultsFolder)
New-Item -ItemType Directory -Force -Path $ResultsFolder | Out-Null
$testProcess = Start-Process -FilePath (Join-Path $PSScriptRoot 'SopRecorder.exe') -ArgumentList @('--self-test', ('"' + $ResultsFolder + '"')) -WindowStyle Hidden -PassThru
Write-Output ('Integration test process: ' + $testProcess.Id)
if (-not $testProcess.WaitForExit(120000)) { $testProcess.Kill(); throw 'Integration test timed out; see the last completed check in test-results.txt.' }
$report = Join-Path $ResultsFolder 'test-results.txt'
if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report }
if ($testProcess.ExitCode -ne 0) { throw ('Integration test failed with exit code ' + $testProcess.ExitCode) }
