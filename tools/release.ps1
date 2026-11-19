# 測試服 release: run the tests, build into build/ once, zip that exact build for GitHub, restart the 測試服 on it,
# then publish a GitHub pre-release with the same zip. tools\promote.ps1 later moves the same build to the 正式服.
# Run from anywhere:
#   powershell -ExecutionPolicy Bypass -File tools\release.ps1 -Notes path\to\notes.md
param([Parameter(Mandatory = $true)][string]$Notes)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$build = Join-Path $project 'build'
$gh = 'C:\Program Files\GitHub CLI\gh.exe'

$defs = Get-Content (Join-Path $project 'unity\Assets\Scripts\Sim\Defs.cs') -Raw
if ($defs -notmatch 'Version = "([^"]+)"') { throw 'Cannot read Defs.Version' }
$version = $Matches[1]   # saved now: later -match calls overwrite $Matches
$tag = "v$version"
if (-not (Test-Path $Notes)) { throw "Release notes not found: $Notes" }
if (git -C $project status --porcelain) { throw 'Commit your changes first: the release is built from the committed tree.' }

# 1. Server tests
Push-Location $project
try { py -3 -m unittest tests.test_service; if ($LASTEXITCODE -ne 0) { throw 'Server tests failed' } } finally { Pop-Location }

# 2. Stop the 測試服 (it runs from build/unity, which the build replaces). The 正式服 under prod\ keeps running.
$prodDir = [regex]::Escape((Join-Path $project 'prod'))
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -notmatch $prodDir } | Where-Object {
  ($_.CommandLine -match 'run_server\.py' -and $_.CommandLine -notmatch 'Wordnest') -or
  ($_.CommandLine -match [regex]::Escape((Join-Path $build 'unity\RiftWard.exe'))) -or
  ($_.CommandLine -match 'StartServer\.cmd')
} | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2

# 3. Build once, package that build
& (Join-Path $PSScriptRoot 'build_unity.ps1')
py -3 (Join-Path $PSScriptRoot 'package_beta.py') --source build --out RiftWard-Test-Windows.zip
if ($LASTEXITCODE -ne 0) { throw 'Packaging failed' }
$zip = Join-Path $project 'RiftWard-Test-Windows.zip'

# 4. Restart the server and wait for it to publish this version
Start-Process cmd.exe -ArgumentList '/c', "`"$(Join-Path $build 'StartServer.cmd')`"" -WorkingDirectory $build
$manifest = Join-Path $build 'releases\manifest.json'
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Seconds 2
  if ((Test-Path $manifest) -and ((Get-Content $manifest -Raw | ConvertFrom-Json).version -eq $version)) { break }
}
if ((Get-Content $manifest -Raw | ConvertFrom-Json).version -ne $version) { throw 'Server did not publish the new version' }

# 5. Push, tag and publish the same zip
git -C $project push origin main
git -C $project tag $tag
git -C $project push origin $tag
& $gh release create $tag $zip --title "測試服 $tag" --notes-file $Notes --prerelease
Write-Output "RELEASED $tag"
