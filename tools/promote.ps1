# 正式服 release: moves the build the 測試服 is running (build/) to the 正式服 (prod/), unchanged. Backs up the 正式服
# database first, restarts only the 正式服, waits for it to publish the version, then publishes a GitHub release
# (marked latest) with the 正式服 download. Run after tools\release.ps1 and testing on the 測試服:
#   powershell -ExecutionPolicy Bypass -File tools\promote.ps1 -Notes path\to\notes.md
param([Parameter(Mandatory = $true)][string]$Notes, [switch]$NoGitHub)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$build = Join-Path $project 'build'
$prod = Join-Path $project 'prod'
$gh = 'C:\Program Files\GitHub CLI\gh.exe'
if (-not (Test-Path $Notes)) { throw "Release notes not found: $Notes" }

# The version is whatever the 測試服 build is: promote never builds anything new.
$manifest = Join-Path $build 'game\manifest.json'
if (-not (Test-Path $manifest)) { throw 'No 測試服 build in build\ - run tools\release.ps1 first.' }
$version = (Get-Content $manifest -Raw | ConvertFrom-Json).version
$tag = "prod-v$version"
Write-Output "Promoting $version to the 正式服"

# 1. Stop the 正式服 only (its processes all live under prod\).
$prodDir = [regex]::Escape($prod)
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -match $prodDir } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2

# 2. Back up the 正式服 database before anything changes.
$data = Join-Path $prod 'server-data'
New-Item -ItemType Directory -Force -Path $data | Out-Null
$db = Join-Path $data 'players.sqlite3'
if (Test-Path $db) { Copy-Item $db (Join-Path $data "backup-before-$version-$(Get-Date -Format yyyyMMdd-HHmmss).sqlite3") }
# First run: same admins and chat filter as the 測試服 (player data starts fresh).
foreach ($f in @('admins.txt', 'chat-filter.txt')) {
  $src = Join-Path $build "server-data\$f"
  if ((Test-Path $src) -and -not (Test-Path (Join-Path $data $f))) { Copy-Item $src (Join-Path $data $f) }
}

# 3. Copy the tested build: game server, player package, launcher, server code, 正式服 config.
foreach ($dir in @('unity', 'game', 'server')) {
  $target = Join-Path $prod $dir
  if (Test-Path $target) { Remove-Item $target -Recurse -Force }
  Copy-Item (Join-Path $build $dir) $target -Recurse
}
foreach ($f in @('RiftWard.exe', 'README.md', 'FONT-LICENSE.txt')) { Copy-Item (Join-Path $build $f) (Join-Path $prod $f) -Force }
Copy-Item (Join-Path $project 'deploy\prod\server.cfg') (Join-Path $prod 'server.cfg') -Force
Copy-Item (Join-Path $project 'deploy\prod\StartServer.cmd') (Join-Path $prod 'StartServer.cmd') -Force
New-Item -ItemType Directory -Force -Path (Join-Path $prod 'releases') | Out-Null

# 4. Package the 正式服 download (its server.cfg points at the 正式服 ports).
py -3 (Join-Path $PSScriptRoot 'package_beta.py') --source prod --out RiftWard-Windows.zip
if ($LASTEXITCODE -ne 0) { throw 'Packaging failed' }
$zip = Join-Path $project 'RiftWard-Windows.zip'

# 5. Start the 正式服 and wait for it to publish this version.
Start-Process cmd.exe -ArgumentList '/c', "`"$(Join-Path $prod 'StartServer.cmd')`"" -WorkingDirectory $prod
$published = Join-Path $prod 'releases\manifest.json'
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Seconds 2
  if ((Test-Path $published) -and ((Get-Content $published -Raw | ConvertFrom-Json).version -eq $version)) { break }
}
if (-not (Test-Path $published) -or (Get-Content $published -Raw | ConvertFrom-Json).version -ne $version) { throw '正式服 did not publish the new version' }
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Seconds 2
  if ((Test-Path (Join-Path $data 'game-server.log')) -and (Select-String -Path (Join-Path $data 'game-server.log') -Pattern 'SERVER_READY' -Quiet)) { break }
}
Write-Output "正式服 running $version (ports 24780 / 24781)"

# 6. Publish the 正式服 release (latest) with the same build.
if (-not $NoGitHub) {
  git -C $project tag $tag
  git -C $project push origin $tag
  # GitHub gets only the launcher: RiftWard.exe (正式服 built in) downloads the game on first start.
  $exe = Join-Path $project 'release-exe\RiftWard.exe'
  New-Item -ItemType Directory -Force -Path (Split-Path $exe) | Out-Null
  & (Join-Path $PSScriptRoot 'build_launcher.ps1') -Out $exe -Prod | Out-Null
  & $gh release create $tag $exe --title "正式服 v$version" --notes-file $Notes --latest
}
Write-Output "PROMOTED $tag"
