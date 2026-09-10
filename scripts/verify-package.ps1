[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = [IO.Path]::GetFullPath((Join-Path $tempBase ("repo-doctor-verify-" + [Guid]::NewGuid().ToString('N'))))
$packageDir = Join-Path $tempRoot 'packages'
$toolDir = Join-Path $tempRoot 'tool'
$sampleDir = Join-Path $tempRoot 'path with spaces/sample'

if (-not $tempRoot.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Temporary directory escaped the system temporary directory: $tempRoot"
}

try {
    New-Item -ItemType Directory -Path $packageDir, $toolDir | Out-Null

    & dotnet pack (Join-Path $repoRoot 'src/RepoDoctor.Cli/RepoDoctor.Cli.csproj') -c Release --no-restore -o $packageDir
    if ($LASTEXITCODE -ne 0) { throw 'dotnet pack failed.' }

    $packages = @(Get-ChildItem -LiteralPath $packageDir -Filter 'RepoDoctor.Cli.*.nupkg')
    if ($packages.Count -ne 1) { throw "Expected one package, found $($packages.Count)." }
    $package = $packages[0].FullName
    $packageVersion = $packages[0].BaseName.Substring('RepoDoctor.Cli.'.Length)

    $archive = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $entries = @($archive.Entries.FullName)
        foreach ($required in @(
            'README.md',
            'LICENSE',
            'THIRD_PARTY_NOTICES.md',
            'tools/net10.0/any/profiles/bootstrap-server-core.json',
            'tools/net10.0/any/profiles/dotnet-code-quality.json',
            'tools/net10.0/any/schemas/profile.v1.schema.json',
            'tools/net10.0/any/docs/CONFIGURATION.md'
        )) {
            if ($required -notin $entries) { throw "Package is missing $required." }
        }
        foreach ($analyzer in @('Microsoft.CodeAnalysis.NetAnalyzers.dll', 'Microsoft.CodeAnalysis.CSharp.NetAnalyzers.dll', 'Meziantou.Analyzer.dll')) {
            if (-not ($entries | Where-Object { $_.EndsWith("bundled-analyzers/$analyzer", [StringComparison]::Ordinal) })) {
                throw "Package is missing $analyzer."
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    $nugetConfig = Join-Path $tempRoot 'NuGet.config'
    $escapedPackageDir = [Security.SecurityElement]::Escape($packageDir)
    @"
<configuration>
  <packageSources><clear /><add key="local" value="$escapedPackageDir" /></packageSources>
</configuration>
"@ | Set-Content -LiteralPath $nugetConfig
    & dotnet tool install RepoDoctor.Cli --tool-path $toolDir --configfile $nugetConfig --version $packageVersion
    if ($LASTEXITCODE -ne 0) { throw 'Tool installation failed.' }

    foreach ($installedFile in @('bootstrap-server-core.json', 'dotnet-code-quality.json', 'profile.v1.schema.json', 'CONFIGURATION.md')) {
        if (-not (Get-ChildItem -LiteralPath $toolDir -Recurse -File -Filter $installedFile)) {
            throw "Installed tool is missing $installedFile."
        }
    }

    $tool = Join-Path $toolDir $(if ($IsWindows) { 'repo-doctor.exe' } else { 'repo-doctor' })
    & $tool --version | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Installed tool did not start.' }

    & dotnet new classlib --framework net10.0 --no-restore --output $sampleDir
    if ($LASTEXITCODE -ne 0) { throw 'Sample project creation failed.' }
    & dotnet restore (Join-Path $sampleDir 'sample.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'Sample project restore failed.' }
    & dotnet new sln --name Sample --format slnx --output $sampleDir
    if ($LASTEXITCODE -ne 0) { throw 'Sample solution creation failed.' }
    $sampleSolution = Join-Path $sampleDir 'Sample.slnx'
    & dotnet sln $sampleSolution add (Join-Path $sampleDir 'sample.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'Adding the sample project to its solution failed.' }

    & $tool init $sampleDir | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'repo-doctor init failed.' }
    & $tool profile validate (Join-Path $sampleDir '.repo-doctor/profile.json') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Profile validation failed.' }
    & $tool config explain $sampleDir | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Config explanation failed.' }
    & $tool rule explain CA2200 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Rule explanation failed.' }

    $exportedProfile = Join-Path $tempRoot 'exported-profile.json'
    & $tool profile export $sampleDir --output $exportedProfile | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $exportedProfile)) { throw 'Profile export failed.' }

    $jsonPath = Join-Path $tempRoot 'scan.json'
    & $tool scan (Join-Path $sampleDir 'sample.csproj') --audit --format json --output $jsonPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Audited scan failed with exit code $LASTEXITCODE." }
    $report = Get-Content -Raw -LiteralPath $jsonPath | ConvertFrom-Json
    if ($report.analysis.completeness -ne 'complete') { throw 'Packaged scan was incomplete.' }
    if (@($report.audit.sources).Count -eq 0) { throw 'Packaged audit did not record a vulnerability source.' }

    $sarifPath = Join-Path $tempRoot 'scan.sarif'
    & $tool scan $sampleSolution --format sarif --output $sarifPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "SARIF scan failed with exit code $LASTEXITCODE." }
    $sarif = Get-Content -Raw -LiteralPath $sarifPath | ConvertFrom-Json
    if ($sarif.version -ne '2.1.0' -or @($sarif.runs).Count -ne 1) { throw 'Packaged SARIF output is invalid.' }

    $installedQualityProfile = Get-ChildItem -LiteralPath $toolDir -Recurse -File -Filter 'dotnet-code-quality.json' | Select-Object -First 1
    $sampleQualityProfile = Join-Path $sampleDir '.repo-doctor/dotnet-code-quality.json'
    Copy-Item -LiteralPath $installedQualityProfile.FullName -Destination $sampleQualityProfile
    @{
        schemaVersion = 1
        profile = '.repo-doctor/dotnet-code-quality.json'
        rules = @{}
        exclude = @()
    } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $sampleDir '.repo-doctor.json')
    & $tool profile validate $sampleQualityProfile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Code-quality profile validation failed.' }
    & $tool rule explain RD1001 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Project-configuration rule explanation failed.' }
    $qualityExport = Join-Path $tempRoot 'exported-code-quality.json'
    & $tool profile export $sampleDir --output $qualityExport | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $qualityExport)) { throw 'Code-quality profile export failed.' }
    $qualityPath = Join-Path $tempRoot 'code-quality.json'
    & $tool scan (Join-Path $sampleDir 'sample.csproj') --profile $installedQualityProfile.FullName --format json --output $qualityPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Code-quality scan failed with exit code $LASTEXITCODE." }
    $qualityReport = Get-Content -Raw -LiteralPath $qualityPath | ConvertFrom-Json
    if ($qualityReport.policy.profileId -ne 'experimental/dotnet-code-quality') { throw 'Code-quality profile was not selected.' }
    $configurationStage = @($qualityReport.analysis.stages | Where-Object name -eq 'project-configuration')
    if ($configurationStage.Count -ne 1 -or $configurationStage[0].status -ne 'complete') { throw 'Project-configuration stage did not complete.' }

    Write-Output "Packaged tool verification passed on $([Runtime.InteropServices.RuntimeInformation]::OSDescription)."
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
