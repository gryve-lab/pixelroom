param([switch]$Live)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe' }
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'build') -Force | Out-Null
foreach ($architecture in @('anycpu','x86')) {
    $exe = Join-Path $PSScriptRoot ('build/CoreTests-' + $architecture + '.exe')
    $statisticsTests = @((Join-Path $PSScriptRoot 'src/Statistics.cs'), (Join-Path $PSScriptRoot 'tests/StatisticsTests.cs'), (Join-Path $PSScriptRoot 'tests/StatisticsFixture.cs'))
    & $compiler /nologo /target:exe ('/platform:' + $architecture) /optimize+ ('/out:' + $exe) /r:System.dll /r:System.Core.dll /r:System.Security.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll @statisticsTests (Join-Path $PSScriptRoot 'src/Core.cs') (Join-Path $PSScriptRoot 'src/BrowseRules.cs') (Join-Path $PSScriptRoot 'src/TransferMetrics.cs') (Join-Path $PSScriptRoot 'src/Filesystem.cs') (Join-Path $PSScriptRoot 'src/FilesystemCopy.cs') (Join-Path $PSScriptRoot 'src/FilesystemBatch.cs') (Join-Path $PSScriptRoot 'src/FileDrop.cs') (Join-Path $PSScriptRoot 'src/FolderUpload.cs') (Join-Path $PSScriptRoot 'src/FilesystemShare.cs') (Join-Path $PSScriptRoot 'src/Preferences.cs') (Join-Path $PSScriptRoot 'src/CredentialStore.cs') (Join-Path $PSScriptRoot 'tests/CoreTests.cs') (Join-Path $PSScriptRoot 'tests/ExplorerFixture.cs') (Join-Path $PSScriptRoot 'tests/ExplorerTests.cs') (Join-Path $PSScriptRoot 'tests/FolderShareTests.cs') (Join-Path $PSScriptRoot 'tests/BrowseTransferTests.cs') (Join-Path $PSScriptRoot 'tests/BatchTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Kompilasi pengujian gagal.' }
    $testDirectory = Join-Path $PSScriptRoot ('test-work/' + $architecture + '-' + [guid]::NewGuid().ToString('N'))
    $testArgs = @($testDirectory)
    if ($Live) { $testArgs += '--live' }
    & $exe @testArgs
    if ($LASTEXITCODE -ne 0) { throw ('Pengujian gagal untuk ' + $architecture) }
    $sessionExe = Join-Path $PSScriptRoot ('build/ConnectionTests-' + $architecture + '.exe')
    $sessionSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
    & $compiler /nologo /target:exe /main:ConnectionTests ('/platform:' + $architecture) /optimize+ ('/out:' + $sessionExe) /r:System.dll /r:System.Core.dll /r:System.Security.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll @sessionSources (Join-Path $PSScriptRoot 'tests/ConnectionTests.cs') (Join-Path $PSScriptRoot 'tests/ExplorerFixture.cs') (Join-Path $PSScriptRoot 'tests/StatisticsFixture.cs') (Join-Path $PSScriptRoot 'tests/StatisticsWindowTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Connection test compilation failed.' }
    & $sessionExe (Join-Path $testDirectory 'connection-lifecycle')
    if ($LASTEXITCODE -ne 0) { throw ('Connection tests failed for ' + $architecture) }
}
