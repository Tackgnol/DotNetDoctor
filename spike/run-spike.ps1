#Requires -Version 7
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = Join-Path $root 'RepoDoctor.Spike\RepoDoctor.Spike.csproj'
$fixture = Join-Path $root 'fixtures\BadProject\BadProject.csproj'
$pkgOut = Join-Path $root '_pkg'
$toolPath = Join-Path $root '_tool'

Remove-Item -Recurse -Force $pkgOut, $toolPath -ErrorAction SilentlyContinue

Write-Host '== pack spike as a .NET tool =='
dotnet pack $proj -c Release -o $pkgOut --nologo
if ($LASTEXITCODE) { throw "pack failed ($LASTEXITCODE)" }

Write-Host '== install tool into an isolated --tool-path (outside the source checkout) =='
dotnet tool install RepoDoctor.Spike --tool-path $toolPath --add-source $pkgOut --version 0.0.1
if ($LASTEXITCODE) { throw "tool install failed ($LASTEXITCODE)" }

Write-Host '== verify analyzer assets shipped in the installed tool =='
$shipped = Get-ChildItem -Recurse -Path $toolPath -Filter '*NetAnalyzers*.dll' -ErrorAction SilentlyContinue
if (-not $shipped) { throw 'FAIL: no NetAnalyzers assemblies in the installed tool directory' }
$shipped | ForEach-Object { Write-Host "  shipped: $($_.Name)" }

Write-Host '== restore the fixture (MVP requires the target restored) =='
dotnet restore $fixture --nologo
if ($LASTEXITCODE) { throw "fixture restore failed ($LASTEXITCODE)" }

Write-Host '== run the externally installed tool against the fixture =='
$exe = Join-Path $toolPath 'repo-doctor-spike.exe'
$out = & $exe $fixture CA2200
$code = $LASTEXITCODE
Write-Host '--- tool stdout ---'
$out | ForEach-Object { Write-Host $_ }
Write-Host "--- exit code: $code ---"

$reported = @($out | Select-String -SimpleMatch 'REPORTED CA2200').Count
$suppressed = @($out | Select-String -SimpleMatch 'SUPPRESSED CA2200').Count

if ($reported -lt 1) { throw "FAIL: expected at least one REPORTED CA2200, got $reported" }
if ($suppressed -lt 1) { throw "FAIL: expected the #pragma-suppressed CA2200 to be recognised, got $suppressed" }
if ($code -ne 1) { throw "FAIL: expected exit code 1 (findings present), got $code" }

Write-Host "PASS: reported=$reported suppressed=$suppressed exit=$code"
