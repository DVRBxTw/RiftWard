param([string]$Out = (Join-Path (Split-Path $PSScriptRoot -Parent) 'build\RiftWard.exe'), [switch]$Prod)
# Default: the 測試服 launcher. -Prod compiles the 正式服 one (its server is built in, see Launcher.cs).
# Compiles the C# launcher with the .NET Framework 4 compiler included in Windows; no SDK needed.
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$icon = Join-Path $project 'unity\Assets\Resources\icon.ico'
$args = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001', "/out:$Out",
  "/r:$fw\System.Web.Extensions.dll", "/r:$fw\System.IO.Compression.dll", "/r:$fw\System.IO.Compression.FileSystem.dll",
  '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll')
if (Test-Path $icon) { $args += "/win32icon:$icon" }
if ($Prod) { $args += '/define:PROD' }
$args += (Join-Path $project 'launcher\Launcher.cs')
& "$fw\csc.exe" @args
if ($LASTEXITCODE -ne 0) { throw 'Launcher compile failed' }
Write-Output "LAUNCHER_READY $Out"
