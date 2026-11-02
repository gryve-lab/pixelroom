param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'build'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework compiler not found.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
$arguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/utf8output',('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')),('/out:' + (Join-Path $OutputDirectory 'Pixelroom.exe')),'/r:System.dll','/r:System.Core.dll','/r:System.Security.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll')
$iconPath = Join-Path $PSScriptRoot 'pixelroom.ico'
if (Test-Path -LiteralPath $iconPath) { $arguments += '/win32icon:' + $iconPath }
& $compiler @arguments @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Pixelroom.exe.config') -Destination (Join-Path $OutputDirectory 'Pixelroom.exe.config') -Force
Write-Output ('Build completed: ' + (Join-Path $OutputDirectory 'Pixelroom.exe'))
