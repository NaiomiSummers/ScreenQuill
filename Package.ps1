$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'SopRecorder'
& (Join-Path $source 'Build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'ScreenQuill build failed.' }
$destination = Join-Path $PSScriptRoot 'SOP-Recorder-Portable.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$temporary = Join-Path $PSScriptRoot ('portable-' + [Guid]::NewGuid().ToString('N') + '.zip')
try {
    $zip = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
    try {
        # Explicit release contents: never package settings, recordings, exports or QA results.
        $files = @('Build.ps1','Launch.cmd','README.md','SopRecorder.exe','START-HERE.txt','Test.ps1','LICENSE','THIRD-PARTY-NOTICES.md','terms.md','VALIDATION.md')
        $files += @(Get-ChildItem -LiteralPath (Join-Path $source 'src') -File | ForEach-Object { 'src/' + $_.Name })
        foreach ($relative in $files) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $source $relative), ('SopRecorder/' + $relative), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose() }
    Move-Item -LiteralPath $temporary -Destination $destination -Force
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
}
Write-Output $destination
