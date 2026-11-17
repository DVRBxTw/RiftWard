param([string]$Version = '0.3.0-beta.2')
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$engine = Join-Path $PSScriptRoot 'Godot_v4.5.1-stable_win64_console.exe'
$build = Join-Path $project 'build'
$gameDir = Join-Path $build 'game'
$releases = Join-Path $build 'releases'
$launcher = Join-Path $project 'launcher'
foreach ($script in @('main.gd','beta_game.gd')) {
    $validation = & $engine --headless --path $project --script ('res://' + $script) --check-only 2>&1
    if ($LASTEXITCODE -ne 0 -or ($validation -match 'SCRIPT ERROR:|ERROR:')) {
        $validation | Write-Output
        throw ('Script validation failed: ' + $script)
    }
}
New-Item -ItemType Directory -Force -Path $gameDir,$releases,(Join-Path $build 'server') | Out-Null
Copy-Item -LiteralPath (Join-Path $project 'ui-font.ttf') -Destination (Join-Path $launcher 'ui-font.ttf') -Force
& $engine --headless --path $project --editor --import --quit
if ($LASTEXITCODE -ne 0) { throw 'Game import failed' }
& $engine --headless --path $project --export-pack 'Windows Desktop' (Join-Path $gameDir 'RiftWardGame.pck')
if ($LASTEXITCODE -ne 0) { throw 'Game export failed' }
& $engine --headless --path $launcher --editor --import --quit
if ($LASTEXITCODE -ne 0) { throw 'Launcher import failed' }
& $engine --headless --path $launcher --export-pack 'Windows Desktop' (Join-Path $build 'RiftWard.pck')
if ($LASTEXITCODE -ne 0) { throw 'Launcher export failed' }
if (-not (Test-Path -LiteralPath (Join-Path $build 'RiftWard.exe'))) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Godot_v4.5.1-stable_win64.exe') -Destination (Join-Path $build 'RiftWard.exe')
}
if (-not (Test-Path -LiteralPath (Join-Path $build 'RiftWard_console.exe'))) {
    Copy-Item -LiteralPath $engine -Destination (Join-Path $build 'RiftWard_console.exe')
}
foreach ($name in @('service.py','run_server.py','schema.sql')) {
    Copy-Item -LiteralPath (Join-Path $project ('server/' + $name)) -Destination (Join-Path $build ('server/' + $name)) -Force
}
Copy-Item -LiteralPath (Join-Path $project 'server/StartServer.cmd.in') -Destination (Join-Path $build 'StartServer.cmd') -Force
Copy-Item -LiteralPath (Join-Path $project 'server/server.cfg') -Destination (Join-Path $build 'server.cfg') -Force
Copy-Item -LiteralPath (Join-Path $project 'README.md') -Destination (Join-Path $build 'README.md') -Force
foreach ($name in @('FONT-LICENSE.txt','GODOT-LICENSE.txt','GODOT-COPYRIGHT.txt')) {
    Copy-Item -LiteralPath (Join-Path $project ('licenses/' + $name)) -Destination (Join-Path $build $name) -Force
}
$pack = Join-Path $gameDir 'RiftWardGame.pck'
$digest = (Get-FileHash -LiteralPath $pack -Algorithm SHA256).Hash.ToLowerInvariant()
Copy-Item -LiteralPath $pack -Destination (Join-Path $releases ($digest + '.pck')) -Force
$manifest = @{version=$Version;sha256=$digest;size=(Get-Item -LiteralPath $pack).Length}
$json = $manifest | ConvertTo-Json -Compress
[System.IO.File]::WriteAllText((Join-Path $releases 'manifest.json'),$json,(New-Object System.Text.UTF8Encoding($false)))
[System.IO.File]::WriteAllText((Join-Path $gameDir 'manifest.json'),$json,(New-Object System.Text.UTF8Encoding($false)))
Write-Output ('BUILD_READY ' + $Version + ' ' + $digest)
