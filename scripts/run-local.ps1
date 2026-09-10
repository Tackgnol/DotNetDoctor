[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Target = '.',

    [ValidateSet('bootstrap', 'code-quality', 'both')]
    [string]$Profile = 'both',

    [switch]$Audit,

    [switch]$InstallOnly,

    [switch]$Reinstall,

    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$packageDir = Join-Path $repoRoot 'artifacts/local-packages'
$toolDir = Join-Path $repoRoot 'artifacts/local-tool'
$tool = Join-Path $toolDir $(if ($IsWindows) { 'repo-doctor.exe' } else { 'repo-doctor' })

if ($Reinstall -or -not (Test-Path -LiteralPath $tool)) {
    New-Item -ItemType Directory -Force -Path $packageDir, $toolDir | Out-Null
    $localVersion = "0.1.0-local.$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())"
    & dotnet pack (Join-Path $repoRoot 'src/RepoDoctor.Cli/RepoDoctor.Cli.csproj') -c Release -o $packageDir -p:Version=$localVersion
    if ($LASTEXITCODE -ne 0) { throw 'Could not pack RepoDoctor.' }

    if (Test-Path -LiteralPath $tool) {
        & dotnet tool uninstall RepoDoctor.Cli --tool-path $toolDir | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not replace the existing repository-local RepoDoctor tool.' }
    }

    $escapedPackageDir = [Security.SecurityElement]::Escape($packageDir)
    $nugetConfig = Join-Path $packageDir 'NuGet.local.config'
    @"
<configuration>
  <packageSources><clear /><add key="local" value="$escapedPackageDir" /></packageSources>
</configuration>
"@ | Set-Content -LiteralPath $nugetConfig

    & dotnet tool install RepoDoctor.Cli --tool-path $toolDir --configfile $nugetConfig --version $localVersion
    if ($LASTEXITCODE -ne 0) { throw 'Could not install RepoDoctor into the repository-local tool directory.' }
}

Write-Output "Using repository-local RepoDoctor: $tool"
if ($InstallOnly) { exit 0 }

$targetPath = [IO.Path]::GetFullPath($Target)
$profiles = switch ($Profile) {
    'bootstrap' { 'bootstrap-server-core.json' }
    'code-quality' { 'dotnet-code-quality.json' }
    'both' { 'bootstrap-server-core.json', 'dotnet-code-quality.json' }
}

$exitCode = 0
foreach ($profileFile in $profiles) {
    $profilePath = Join-Path $repoRoot "profiles/$profileFile"
    Write-Output "Running $profileFile"
    $arguments = @(
        'scan',
        $targetPath,
        '--profile',
        $profilePath,
        '--timeout-seconds',
        $TimeoutSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
    )
    if ($Audit -and $profileFile -eq 'bootstrap-server-core.json') {
        $arguments += '--audit'
    }

    & $tool @arguments
    $scanExit = $LASTEXITCODE
    if ($scanExit -eq 130) { $exitCode = 130 }
    elseif ($scanExit -eq 2 -and $exitCode -ne 130) { $exitCode = 2 }
    elseif ($scanExit -eq 1 -and $exitCode -eq 0) { $exitCode = 1 }
}

exit $exitCode
