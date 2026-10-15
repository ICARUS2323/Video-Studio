$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$projectRoot = Split-Path -Parent $PSScriptRoot
$releaseDirectory = Join-Path $projectRoot 'release\VideoStudio'
$exePath = Join-Path $releaseDirectory 'VideoStudio.exe'
if (-not (Test-Path -LiteralPath $exePath)) { throw 'Önce tools/build.ps1 çalıştırın.' }
$iconDirectory = Join-Path $releaseDirectory 'icons'
New-Item -ItemType Directory -Path $iconDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\Assets\video-studio.ico') -Destination (Join-Path $iconDirectory 'video-studio-v2.ico') -Force
$archivePath = Join-Path $projectRoot 'release\VideoStudio-win-x64.zip'
$temporaryArchive = $archivePath + '.tmp'
$archiveStream = [IO.File]::Create($temporaryArchive)
$archive = [IO.Compression.ZipArchive]::new($archiveStream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $releaseDirectory -File -Recurse) {
        $relative = $file.FullName.Substring($releaseDirectory.TrimEnd('\').Length + 1)
        if ($relative.StartsWith('data\', [StringComparison]::OrdinalIgnoreCase)) { continue }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, ('VideoStudio/' + $relative.Replace('\','/')), [IO.Compression.CompressionLevel]::Fastest) | Out-Null
    }
} finally { $archive.Dispose(); $archiveStream.Dispose() }
Move-Item -LiteralPath $temporaryArchive -Destination $archivePath -Force
$shortcutShell = New-Object -ComObject WScript.Shell
$shortcut = $shortcutShell.CreateShortcut((Join-Path $projectRoot 'Video Studio.lnk'))
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $releaseDirectory
$shortcut.IconLocation = (Join-Path $releaseDirectory 'icons\video-studio-v2.ico') + ',0'
$shortcut.Description = 'Video Studio - video ve ses indirici'
$shortcut.Save()
Write-Host ('Paket: ' + $archivePath)
Write-Host 'Video Studio kısayolu proje klasörüne eklendi.'
