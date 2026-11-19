$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$unityProject = Join-Path $project 'unity'
$editor = 'C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Unity.exe'
# RIFTWARD_STAGE lets a test build go somewhere other than the live build/ folder.
$build = if ($env:RIFTWARD_STAGE) { $env:RIFTWARD_STAGE } else { Join-Path $project 'build' }
$unityOut = Join-Path $build 'unity'
$gameDir = Join-Path $build 'game'

$defs = Get-Content (Join-Path $unityProject 'Assets\Scripts\Sim\Defs.cs') -Raw
if ($defs -notmatch 'Version = "([^"]+)"') { throw 'Cannot read Defs.Version' }
$version = $Matches[1]

# 1. Unity player build
if (Test-Path $unityOut) { Remove-Item $unityOut -Recurse -Force }
$env:RIFTWARD_BUILD_DIR = $unityOut
$log = Join-Path $build 'unity-build.log'
$p = Start-Process -FilePath $editor -ArgumentList @('-batchmode','-quit','-projectPath',"`"$unityProject`"",'-executeMethod','RiftWard.EditorTools.BuildTools.BuildWindows','-logFile',"`"$log`"") -PassThru -Wait
if ($p.ExitCode -ne 0 -or -not (Test-Path (Join-Path $unityOut 'RiftWard.exe'))) { throw "Unity build failed, see $log" }
Get-ChildItem $unityOut -Directory | Where-Object { $_.Name -like '*DoNotShip' } | Remove-Item -Recurse -Force

# 2. Client update package
New-Item -ItemType Directory -Force -Path $gameDir, (Join-Path $build 'server'), (Join-Path $build 'releases') | Out-Null
$zip = Join-Path $gameDir 'RiftWard.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
# Write entries by hand: .NET Framework's CreateFromDirectory stores backslash paths, which the launcher rejects.
$archive = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
try {
  $rootLen = (Resolve-Path $unityOut).Path.TrimEnd('\').Length + 1
  foreach ($f in Get-ChildItem $unityOut -Recurse -File) {
    $name = $f.FullName.Substring($rootLen).Replace('\', '/')
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName, $name, 'Optimal')
  }
} finally { $archive.Dispose() }
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
$size = (Get-Item $zip).Length
@{ version = $version; sha256 = $hash; size = $size; package = 'zip' } | ConvertTo-Json -Compress | Set-Content (Join-Path $gameDir 'manifest.json') -Encoding ascii
Remove-Item (Join-Path $gameDir 'RiftWardGame.pck') -ErrorAction SilentlyContinue

# 3. Server files
foreach ($file in @('service.py','run_server.py','schema.sql')) { Copy-Item (Join-Path $project "server\$file") (Join-Path $build "server\$file") -Force }
Copy-Item (Join-Path $project 'deploy\test\server.cfg') (Join-Path $build 'server.cfg') -Force   # build/ is the 測試服

# 4. Launcher (C#)
& (Join-Path $PSScriptRoot 'build_launcher.ps1') -Out (Join-Path $build 'RiftWard.exe') | Out-Null
foreach ($old in @('RiftWard.pck', 'GODOT-LICENSE.txt', 'GODOT-COPYRIGHT.txt')) { Remove-Item (Join-Path $build $old) -ErrorAction SilentlyContinue }
$test = Start-Process (Join-Path $build 'RiftWard.exe') -ArgumentList '--self-test', "`"$build`"" -Wait -PassThru -NoNewWindow
if ($test.ExitCode -ne 0) { throw 'Launcher self-test failed' }

Write-Output "UNITY_BUILD_READY $version $hash $([math]::Round($size / 1MB, 1)) MB"
