$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$unityProject = Join-Path $project 'unity'
$editor = 'C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Unity.exe'
$godot = Join-Path $PSScriptRoot 'Godot_v4.5.1-stable_win64_console.exe'
# RIFTWARD_STAGE lets a test build go somewhere other than the live build/ folder.
$build = if ($env:RIFTWARD_STAGE) { $env:RIFTWARD_STAGE } else { Join-Path $project 'build' }
$unityOut = Join-Path $build 'unity'
$gameDir = Join-Path $build 'game'
$launcher = Join-Path $project 'launcher'

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
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($unityOut, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
$size = (Get-Item $zip).Length
@{ version = $version; sha256 = $hash; size = $size; package = 'zip' } | ConvertTo-Json -Compress | Set-Content (Join-Path $gameDir 'manifest.json') -Encoding ascii
Remove-Item (Join-Path $gameDir 'RiftWardGame.pck') -ErrorAction SilentlyContinue

# 3. Server files
foreach ($file in @('service.py','run_server.py','schema.sql')) { Copy-Item (Join-Path $project "server\$file") (Join-Path $build "server\$file") -Force }

# 4. Launcher (Godot) export
Copy-Item -LiteralPath (Join-Path $project 'ui-font.ttf') -Destination (Join-Path $launcher 'ui-font.ttf') -Force
& $godot --headless --path $launcher --editor --import --quit | Out-Null
& $godot --headless --path $launcher --export-pack 'Windows Desktop' (Join-Path $build 'RiftWard.pck') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Launcher export failed' }
if (-not (Test-Path (Join-Path $build 'RiftWard.exe'))) { Copy-Item (Join-Path $PSScriptRoot 'Godot_v4.5.1-stable_win64.exe') (Join-Path $build 'RiftWard.exe') }

Write-Output "UNITY_BUILD_READY $version $hash $([math]::Round($size / 1MB, 1)) MB"
