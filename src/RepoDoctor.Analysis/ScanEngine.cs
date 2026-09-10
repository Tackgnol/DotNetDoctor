using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using RepoDoctor.Core;
using RepoDoctor.Core.Catalog;
using RepoDoctor.Core.Policy;
using RepoDoctor.Core.Reporting;

namespace RepoDoctor.Analysis;

public sealed record ScanRequest
{
    public required string Target { get; init; }

    public string? RepositoryConfigPath { get; init; }

    public required string ToolVersion { get; init; }

    public required EffectivePolicy Policy { get; init; }

    public required DiagnosticCatalog Catalog { get; init; }

    public string? BaselinePath { get; init; }

    public bool Audit { get; init; }

    public int TimeoutSeconds { get; init; } = 300;
}

public sealed record ScanOutcome(ScanReport Report, int ExitCode, bool Cancelled);

public static class ScanEngine
{
    public static async Task<ScanOutcome> RunAsync(ScanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (request.TimeoutSeconds > 0)
        {
            linked.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
        }

        var token = linked.Token;
        var problems = new List<AnalysisProblem>();

        AnalyzerBundle bundle;
        try
        {
            bundle = AnalyzerBundle.Load();
        }
        catch (DirectoryNotFoundException ex)
        {
            return Failure(request, startedAt, stopwatch, "static-analysis",
                new AnalysisProblem { Severity = AnalysisProblemSeverity.Error, Stage = "static-analysis", Message = ex.Message });
        }

        foreach (var failure in bundle.LoadFailures)
        {
            problems.Add(new AnalysisProblem { Severity = AnalysisProblemSeverity.Warning, Stage = "analyzer-load", Message = failure });
        }

        if (bundle.Analyzers.Length == 0)
        {
            return Failure(request, startedAt, stopwatch, "static-analysis",
                new AnalysisProblem { Severity = AnalysisProblemSeverity.Error, Stage = "static-analysis", Message = "no analyzers were loaded from the bundle." });
        }

        DiscoveryResult discovery;
        try
        {
            discovery = TargetDiscovery.Resolve(request.Target, request.RepositoryConfigPath);
        }
        catch (RepoDoctorValidationException ex)
        {
            return Failure(request, startedAt, stopwatch, "discovery",
                new AnalysisProblem { Severity = AnalysisProblemSeverity.Error, Stage = "discovery", Message = ex.Message });
        }

        try
        {
            var load = await WorkspaceLoad.LoadAsync(discovery, token);
            problems.AddRange(load.Problems);

            var projectStatuses = new List<ProjectAnalysisStatus>();
            var findings = new List<Finding>();
            var rulesRan = new HashSet<string>(StringComparer.Ordinal);
            var rulesAttempted = new HashSet<string>(StringComparer.Ordinal);
            var suppressedByRepository = new HashSet<string>(StringComparer.Ordinal);
            var subjectDocumentCount = 0;
            var sourceRequested = request.Policy.Rules.Any(r => request.Catalog.Get(r.RuleId).Kind == CatalogEntryKind.SourceDiagnostic);
            var configurationRules = request.Policy.Rules
                .Where(r => request.Catalog.Get(r.RuleId).Kind == CatalogEntryKind.ProjectConfiguration)
                .ToList();
            var configurationRequested = configurationRules.Count > 0;
            var configurationSucceeded = true;
            var configurationFailedRules = new HashSet<string>(StringComparer.Ordinal);
            var anyStaticAnalyzed = false;

            foreach (var project in load.Projects.Where(p => p.IsSubject))
            {
                token.ThrowIfCancellationRequested();

                if (project.IsMultiTarget)
                {
                    problems.Add(Problem(AnalysisProblemSeverity.Warning, "static-analysis",
                        $"multi-target project is unsupported in MVP ({string.Join(", ", project.TargetFrameworks)}); it was not analyzed.", project.Identity));
                    projectStatuses.Add(Status(project, ProjectAnalysisState.Unsupported, "multi-target", null));
                    configurationSucceeded &= !configurationRules.Any(r => r.Scope == RuleScope.All || !project.IsTestProject);
                    continue;
                }

                if (project.RestoreMissing || project.RoslynProject is null)
                {
                    projectStatuses.Add(Status(project, ProjectAnalysisState.Failed, "missing restore or compilation", null));
                    configurationSucceeded &= !configurationRules.Any(r => r.Scope == RuleScope.All || !project.IsTestProject);
                    continue;
                }

                var result = await ProjectAnalyzer.AnalyzeAsync(
                    project.RoslynProject, project, request.Policy, bundle, request.Catalog,
                    discovery.RepositoryRoot, load.Configuration, token);

                findings.AddRange(result.Findings);
                problems.AddRange(result.Problems);
                foreach (var rule in result.RulesRan)
                {
                    rulesRan.Add(rule);
                    rulesAttempted.Add(rule);
                }

                foreach (var rule in result.RulesSuppressedByRepository)
                {
                    suppressedByRepository.Add(rule);
                }

                subjectDocumentCount += project.RoslynProject.DocumentIds.Count;

                var configurationResult = await ProjectConfigurationChecks.AnalyzeAsync(
                    project,
                    configurationRules,
                    request.Catalog,
                    discovery.RepositoryRoot,
                    load.Configuration,
                    load.SdkVersion,
                    token);
                findings.AddRange(configurationResult.Findings);
                problems.AddRange(configurationResult.Problems);
                foreach (var rule in configurationResult.RulesAttempted)
                {
                    rulesAttempted.Add(rule);
                }

                foreach (var rule in configurationResult.RulesCompleted)
                {
                    rulesRan.Add(rule);
                }

                configurationSucceeded &= configurationResult.Succeeded;
                if (!configurationResult.Succeeded)
                {
                    foreach (var rule in configurationResult.RulesAttempted)
                    {
                        configurationFailedRules.Add(rule);
                    }
                }

                var hadError = result.Problems.Any(p => p.Severity == AnalysisProblemSeverity.Error);
                anyStaticAnalyzed |= result.CompilerStateUsable && !hadError;
                var state = result.CompilerStateUsable && !hadError && configurationResult.Succeeded
                    ? ProjectAnalysisState.Analyzed
                    : ProjectAnalysisState.Partial;
                projectStatuses.Add(Status(project, state,
                    state == ProjectAnalysisState.Partial ? "incomplete compilation or analyzer state" : null,
                    result.EffectiveConfigDigest));
            }

            foreach (var dependency in load.Projects.Where(p => !p.IsSubject))
            {
                projectStatuses.Add(Status(dependency, ProjectAnalysisState.Skipped, "semantic dependency only", null));
            }

            NuGetAuditResult? auditResult = null;
            AuditInfo? auditInfo = null;
            if (request.Audit)
            {
                var auditProjects = load.Projects
                    .Where(p => p.IsSubject && !p.RestoreMissing && !p.IsMultiTarget)
                    .ToList();
                auditResult = await NuGetAudit.RunAsync(auditProjects, token);
                problems.AddRange(auditResult.Problems);
                auditInfo = new AuditInfo
                {
                    CompletedAtUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    Sources = auditResult.Sources,
                    Freshness = "The SDK output does not expose advisory-source freshness.",
                };

                var auditRule = request.Policy.Rules.SingleOrDefault(r => r.RuleId == "NUGET-VULNERABILITY");
                if (auditResult.Succeeded && auditRule is not null)
                {
                    var auditFindings = NuGetAudit.ToFindings(
                        auditResult,
                        auditRule,
                        request.Catalog.Get("NUGET-VULNERABILITY"),
                        load.Configuration);
                    if (auditRule.Scope == RuleScope.Production)
                    {
                        var testProjects = load.Projects.Where(p => p.IsTestProject).Select(p => p.Identity).ToHashSet(StringComparer.Ordinal);
                        auditFindings = auditFindings.Where(f => !testProjects.Contains(f.ProjectPath)).ToList();
                    }

                    findings.AddRange(auditFindings);
                    rulesRan.Add(auditRule.RuleId);
                    rulesAttempted.Add(auditRule.RuleId);
                }
            }

            var excludeMatcher = new ExcludeMatcher(request.Policy.Excludes);
            var beforeExclude = findings.Count;
            var kept = excludeMatcher.IsEmpty
                ? findings
                : findings.Where(f => f.Location is null || !excludeMatcher.IsExcluded(f.Location.FilePath)).ToList();
            var excludedCount = beforeExclude - kept.Count;
            if (excludedCount > 0)
            {
                problems.Add(Problem(AnalysisProblemSeverity.Info, "exclude",
                    $"{excludedCount} finding location(s) hidden by exclude patterns.", projectPath: null));
            }

            var ordered = FindingOrder.Sort(kept);
            ordered = AssignIds(ordered);

            var subjects = projectStatuses.Where(p => p.IsSubject).ToList();
            var anyAnalyzed = subjects.Any(p => p.State is ProjectAnalysisState.Analyzed or ProjectAnalysisState.Partial);
            var allAnalyzed = subjects.Count > 0 && subjects.All(p => p.State == ProjectAnalysisState.Analyzed);
            var hasErrorProblem = problems.Any(p => p.Severity == AnalysisProblemSeverity.Error);
            var hasWorkspaceError = load.Problems.Any(p => p.Severity == AnalysisProblemSeverity.Error);

            var completeness = subjects.Count == 0 || !anyAnalyzed
                ? Completeness.Failed
                : allAnalyzed && !hasErrorProblem && bundle.LoadFailures.Count == 0
                    ? Completeness.Complete
                    : Completeness.Partial;

            var selectedRuleIds = request.Policy.Rules.Select(r => r.RuleId).ToHashSet(StringComparer.Ordinal);
            rulesRan.ExceptWith(configurationFailedRules);
            var skipped = request.Policy.DisabledRules.Select(r => r.RuleId)
                .Concat(selectedRuleIds.Where(id => request.Catalog.Get(id).Kind switch
                {
                    CatalogEntryKind.SourceDiagnostic => !bundle.Supports(id),
                    CatalogEntryKind.DependencyAudit => !request.Audit || auditResult?.Succeeded != true,
                    CatalogEntryKind.ProjectConfiguration => !rulesAttempted.Contains(id),
                    _ => true,
                }))
                .Concat(suppressedByRepository)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            var noFindings = rulesRan
                .Where(id => !ordered.Any(f => f.RuleId == id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            var analysis = new AnalysisSection
            {
                Completeness = completeness,
                Stages = BuildStages(
                    cancelled: false,
                    hasWorkspaceError,
                    sourceRequested,
                    anyStaticAnalyzed,
                    configurationRequested,
                    configurationSucceeded,
                    request.Audit,
                    auditResult?.Succeeded == true),
                Projects = projectStatuses
                    .OrderBy(p => p.ProjectPath, StringComparer.Ordinal)
                    .ToList(),
                RulesAttempted = rulesAttempted.Count,
                RulesCompleted = rulesRan.Count,
                RulesWithNoFindings = noFindings,
                RulesDisabledOrSkipped = skipped,
                Problems = problems,
            };

            if (request.BaselinePath is null)
            {
                var gate = GateEvaluation.Evaluate(
                    completeness, cancelled: false, comparisonRequested: false, comparisonValid: true, ordered, request.Policy.Gate);
                var report = BuildReport(request, discovery, load, startedAt, stopwatch, bundle, analysis, ordered, gate, subjectDocumentCount, auditInfo);
                return new ScanOutcome(report, GateEvaluation.ExitCode(false, completeness, gate), Cancelled: false);
            }

            return CompareToBaseline(request, discovery, load, startedAt, stopwatch, bundle, analysis, ordered, completeness, subjectDocumentCount, auditInfo);
        }
        catch (OperationCanceledException)
        {
            var report = Failure(request, startedAt, stopwatch, "static-analysis",
                new AnalysisProblem { Severity = AnalysisProblemSeverity.Error, Stage = "static-analysis", Message = "scan was cancelled or timed out." },
                cancelled: true).Report;
            return new ScanOutcome(report, 130, Cancelled: true);
        }
    }

    private static ScanOutcome CompareToBaseline(
        ScanRequest request,
        DiscoveryResult discovery,
        WorkspaceLoadResult load,
        DateTimeOffset startedAt,
        System.Diagnostics.Stopwatch stopwatch,
        AnalyzerBundle bundle,
        AnalysisSection analysis,
        IReadOnlyList<Finding> ordered,
        Completeness completeness,
        int subjectDocumentCount,
        AuditInfo? auditInfo)
    {
        ScanReport baseline;
        try
        {
            baseline = ReportJson.Deserialize(File.ReadAllText(request.BaselinePath!));
        }
        catch (Exception ex) when (ex is IOException or RepoDoctorValidationException or System.Text.Json.JsonException)
        {
            var unusable = analysis with
            {
                Stages = WithBaselineStage(analysis.Stages, StageStatus.Failed, "baseline could not be read"),
            };
            var report = BuildReport(request, discovery, load, startedAt, stopwatch, bundle, unusable, ordered,
                NotEvaluated("the requested baseline could not be read"), subjectDocumentCount, auditInfo) with
            {
                Comparison = new ComparisonInfo { Status = "baseline-unusable", IncompatibilityReasons = [ex.Message] },
            };
            return new ScanOutcome(report, 2, Cancelled: false);
        }

        var headForCompare = BuildReport(request, discovery, load, startedAt, stopwatch, bundle, analysis, ordered,
            NotEvaluated("comparison pending"), subjectDocumentCount, auditInfo);

        var comparison = BaselineComparison.Compare(baseline, headForCompare);

        if (!comparison.Valid)
        {
            var invalid = analysis with
            {
                Stages = WithBaselineStage(analysis.Stages, StageStatus.Failed,
                    comparison.Reasons.Count > 0 ? comparison.Reasons[0] : null),
            };
            var report = BuildReport(request, discovery, load, startedAt, stopwatch, bundle, invalid, ordered,
                NotEvaluated("the baseline comparison is not valid"), subjectDocumentCount, auditInfo) with
            {
                Comparison = comparison.ToComparisonInfo(),
            };
            return new ScanOutcome(report, 2, Cancelled: false);
        }

        var section = analysis with
        {
            Stages = WithBaselineStage(analysis.Stages, StageStatus.Complete, null),
        };
        var gate = GateEvaluation.Evaluate(
            completeness, cancelled: false, comparisonRequested: true, comparisonValid: true, comparison.HeadFindings, request.Policy.Gate);
        var finalReport = BuildReport(request, discovery, load, startedAt, stopwatch, bundle, section, comparison.HeadFindings, gate, subjectDocumentCount) with
        {
            Comparison = comparison.ToComparisonInfo(),
            Audit = auditInfo,
        };
        return new ScanOutcome(finalReport, GateEvaluation.ExitCode(false, completeness, gate), Cancelled: false);
    }

    private static List<AnalysisStage> WithBaselineStage(IReadOnlyList<AnalysisStage> stages, StageStatus status, string? detail) =>
        stages.Select(s => s.Name == "baseline-comparison" ? s with { Status = status, Detail = detail } : s).ToList();

    private static GateResult NotEvaluated(string reason) => new()
    {
        Status = GateStatus.NotEvaluated,
        BlockingFindingIds = [],
        NotEvaluatedReason = reason,
    };

    private static List<Finding> AssignIds(IReadOnlyList<Finding> ordered)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<Finding>(ordered.Count);
        foreach (var finding in ordered)
        {
            var index = seen.TryGetValue(finding.Fingerprint, out var n) ? n : 0;
            seen[finding.Fingerprint] = index + 1;
            var shortHash = finding.Fingerprint.Length >= 12 ? finding.Fingerprint[^12..] : finding.Fingerprint;
            result.Add(finding with { Id = $"{shortHash}-{index}" });
        }

        return result;
    }

    private static IReadOnlyList<AnalysisStage> BuildStages(
        bool cancelled,
        bool hasWorkspaceError,
        bool sourceRequested,
        bool staticSucceeded,
        bool configurationRequested,
        bool configurationSucceeded,
        bool auditRequested,
        bool auditSucceeded)
    {
        var staticStatus = !sourceRequested
            ? StageStatus.Disabled
            : cancelled
            ? StageStatus.Cancelled
            : staticSucceeded
                ? StageStatus.Complete
                : StageStatus.Failed;

        return
        [
            new AnalysisStage { Name = "discovery", Status = StageStatus.Complete },
            new AnalysisStage { Name = "workspace-load", Status = hasWorkspaceError ? StageStatus.Failed : StageStatus.Complete },
            new AnalysisStage { Name = "static-analysis", Status = staticStatus },
            new AnalysisStage
            {
                Name = "project-configuration",
                Status = !configurationRequested
                    ? StageStatus.Disabled
                    : cancelled
                        ? StageStatus.Cancelled
                        : configurationSucceeded ? StageStatus.Complete : StageStatus.Failed,
                Detail = configurationRequested ? null : "no project-configuration rules selected",
            },
            new AnalysisStage
            {
                Name = "dependency-audit",
                Status = !auditRequested ? StageStatus.Disabled : auditSucceeded ? StageStatus.Complete : StageStatus.Failed,
                Detail = auditRequested ? null : "not requested (--audit)",
            },
            new AnalysisStage { Name = "baseline-comparison", Status = StageStatus.Disabled, Detail = "not requested (--baseline)" },
        ];
    }

    private static ScanReport BuildReport(
        ScanRequest request,
        DiscoveryResult discovery,
        WorkspaceLoadResult load,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        AnalyzerBundle bundle,
        AnalysisSection analysis,
        IReadOnlyList<Finding> findings,
        GateResult gate,
        int subjectDocumentCount,
        AuditInfo? auditInfo = null)
    {
        var subjects = load.Projects.Where(p => p.IsSubject).Select(p => p.Identity).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var dependencies = load.Projects.Where(p => !p.IsSubject).Select(p => p.Identity).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var frameworks = load.Projects.Where(p => p.IsSubject).SelectMany(p => p.TargetFrameworks).Distinct(StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToList();

        return new ScanReport
        {
            Tool = new ToolInfo
            {
                Version = request.ToolVersion,
                AnalyzerPackages = request.Catalog.Bundle.Select(b => $"{b.Package} {b.Version}").ToList(),
                RoslynVersion = bundle.RoslynVersion,
                BundleHash = request.Catalog.Hash,
                FingerprintSchema = FindingFactory.FingerprintVersion,
            },
            Policy = ToPolicyInfo(request.Policy),
            Environment = new EnvironmentInfo
            {
                SelectedSdkVersion = load.SdkVersion,
                CliRuntime = System.Environment.Version.ToString(),
                OperatingSystem = RuntimeInformation.OSDescription,
                OsFamily = OsFamily(),
                MsBuildVersion = load.MsBuildVersion,
            },
            Scope = new ScopeInfo
            {
                TargetPath = Path.GetRelativePath(discovery.RepositoryRoot, discovery.TargetPath).Replace('\\', '/'),
                SubjectProjects = subjects,
                DependencyProjects = dependencies,
                TargetFrameworks = frameworks,
                Configuration = load.Configuration,
                SubjectDocumentCount = subjectDocumentCount,
            },
            Provenance = GitProvenance.TryRead(discovery.RepositoryRoot),
            Analysis = analysis,
            Findings = findings,
            Comparison = null,
            Audit = auditInfo,
            Gate = gate,
            Timing = new TimingInfo
            {
                StartedAtUtc = startedAt.ToString("o", CultureInfo.InvariantCulture),
                ElapsedSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 3),
            },
        };
    }

    private static PolicyInfo ToPolicyInfo(EffectivePolicy policy) => new()
    {
        ProfileId = policy.ProfileId,
        ProfileVersion = policy.ProfileVersion,
        ProfileHash = policy.ProfileHash,
        EffectivePolicyHash = policy.PolicyHash,
        HashAlgorithm = EffectivePolicy.HashAlgorithm,
        SelectedRules = policy.Rules
            .Select(r => new SelectedRuleInfo { RuleId = r.RuleId, Severity = r.Severity, Scope = r.Scope, Source = r.Source })
            .ToList(),
        DisabledRules = policy.DisabledRules.Select(r => r.RuleId).ToList(),
        Gate = new GatePolicyInfo { MinimumSeverity = policy.Gate.MinimumSeverity, MinimumConfidence = policy.Gate.MinimumConfidence },
        Exclusions = policy.Excludes,
    };

    private static ScanOutcome Failure(
        ScanRequest request,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string failedStage,
        AnalysisProblem problem,
        bool cancelled = false)
    {
        var completeness = Completeness.Failed;
        var analysis = new AnalysisSection
        {
            Completeness = completeness,
            Stages =
            [
                new AnalysisStage { Name = "discovery", Status = failedStage == "discovery" ? StageStatus.Failed : StageStatus.Complete },
                new AnalysisStage { Name = "workspace-load", Status = failedStage is "discovery" ? StageStatus.Failed : StageStatus.Complete },
                new AnalysisStage
                {
                    Name = "static-analysis",
                    Status = HasSelectedKind(request, CatalogEntryKind.SourceDiagnostic)
                        ? cancelled ? StageStatus.Cancelled : StageStatus.Failed
                        : StageStatus.Disabled,
                },
                new AnalysisStage
                {
                    Name = "project-configuration",
                    Status = HasSelectedKind(request, CatalogEntryKind.ProjectConfiguration)
                        ? cancelled ? StageStatus.Cancelled : StageStatus.Failed
                        : StageStatus.Disabled,
                },
            new AnalysisStage
            {
                Name = "dependency-audit",
                Status = request.Audit ? StageStatus.Failed : StageStatus.Disabled,
                Detail = request.Audit ? "NuGet vulnerability audit is unavailable in this release." : null,
            },
                new AnalysisStage { Name = "baseline-comparison", Status = StageStatus.Disabled },
            ],
            Projects = [],
            RulesAttempted = 0,
            RulesCompleted = 0,
            Problems = [problem],
        };

        var gate = new GateResult { Status = GateStatus.NotEvaluated, BlockingFindingIds = [], NotEvaluatedReason = "analysis did not complete" };

        var report = new ScanReport
        {
            Tool = new ToolInfo
            {
                Version = request.ToolVersion,
                AnalyzerPackages = request.Catalog.Bundle.Select(b => $"{b.Package} {b.Version}").ToList(),
                RoslynVersion = "unknown",
                BundleHash = request.Catalog.Hash,
                FingerprintSchema = FindingFactory.FingerprintVersion,
            },
            Policy = ToPolicyInfo(request.Policy),
            Environment = new EnvironmentInfo
            {
                SelectedSdkVersion = "unknown",
                CliRuntime = System.Environment.Version.ToString(),
                OperatingSystem = RuntimeInformation.OSDescription,
                OsFamily = OsFamily(),
                MsBuildVersion = "unknown",
            },
            Scope = new ScopeInfo
            {
                TargetPath = request.Target,
                SubjectProjects = [],
                DependencyProjects = [],
                TargetFrameworks = [],
                Configuration = "Debug",
                SubjectDocumentCount = 0,
            },
            Provenance = null,
            Analysis = analysis,
            Findings = [],
            Comparison = null,
            Gate = gate,
            Timing = new TimingInfo
            {
                StartedAtUtc = startedAt.ToString("o", CultureInfo.InvariantCulture),
                ElapsedSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 3),
            },
        };

        return new ScanOutcome(report, cancelled ? 130 : 2, cancelled);
    }

    private static ProjectAnalysisStatus Status(
        LoadedProject project,
        ProjectAnalysisState state,
        string? detail,
        string? effectiveConfigDigest) => new()
        {
            ProjectPath = project.Identity,
            IsSubject = project.IsSubject,
            State = state,
            TargetFrameworks = project.TargetFrameworks,
            IsTestProject = project.IsTestProject,
            EffectiveConfigDigest = effectiveConfigDigest,
            Detail = detail,
        };

    private static string OsFamily()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "windows";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "linux";
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" : "other";
    }

    private static AnalysisProblem Problem(AnalysisProblemSeverity severity, string stage, string message, string? projectPath) => new()
    {
        Severity = severity,
        Stage = stage,
        Message = message,
        ProjectPath = projectPath,
    };

    private static bool HasSelectedKind(ScanRequest request, CatalogEntryKind kind) =>
        request.Policy.Rules.Any(rule => request.Catalog.Get(rule.RuleId).Kind == kind);
}
