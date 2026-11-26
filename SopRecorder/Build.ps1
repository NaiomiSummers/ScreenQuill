$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'This app needs the .NET Framework 4.x compiler included with Windows 11.' }
$references = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll')
$arguments = @(('/resource:' + (Join-Path $PSScriptRoot 'terms.md') + ',ScreenQuill.terms'), '/nologo', '/target:winexe', '/platform:x64', '/optimize+', ('/win32manifest:' + (Join-Path $PSScriptRoot 'src\app.manifest')), ('/out:' + (Join-Path $PSScriptRoot 'SopRecorder.exe')))
foreach ($reference in $references) { $arguments += '/reference:' + $reference }
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output 'Built SopRecorder.exe. Double-click it to launch.'
