using RepoDoctor.Core;

namespace RepoDoctor.Cli;

public enum OutputFormat
{
    Console,
    Json,
    Sarif,
}

public sealed record ScanArguments
{
    public required string Target { get; init; }

    public string? ConfigPath { get; init; }

    public string? ProfilePath { get; init; }

    public string? BaselinePath { get; init; }

    public OutputFormat Format { get; init; } = OutputFormat.Console;

    public string? OutputPath { get; init; }

    public bool Audit { get; init; }

    public int TimeoutSeconds { get; init; } = 300;

    public static ScanArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? target = null;
        string? configPath = null;
        string? profilePath = null;
        string? baselinePath = null;
        string? outputPath = null;
        var format = OutputFormat.Console;
        var audit = false;
        var timeoutSeconds = 300;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--config":
                    configPath = RequireValue(args, ref i, "--config");
                    break;
                case "--profile":
                    profilePath = RequireValue(args, ref i, "--profile");
                    break;
                case "--baseline":
                    baselinePath = RequireValue(args, ref i, "--baseline");
                    break;
                case "--output":
                    outputPath = RequireValue(args, ref i, "--output");
                    break;
                case "--audit":
                    audit = true;
                    break;
                case "--format":
                    format = ParseFormat(RequireValue(args, ref i, "--format"));
                    break;
                case "--timeout-seconds":
                    timeoutSeconds = ParseTimeout(RequireValue(args, ref i, "--timeout-seconds"));
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        throw new RepoDoctorValidationException($"unknown option '{arg}'.");
                    }

                    if (target is not null)
                    {
                        throw new RepoDoctorValidationException($"unexpected extra argument '{arg}'.");
                    }

                    target = arg;
                    break;
            }
        }

        if (target is null)
        {
            throw new RepoDoctorValidationException("scan requires a target (a .sln, .slnx, .csproj, or directory).");
        }

        if (configPath is not null && profilePath is not null)
        {
            throw new RepoDoctorValidationException("--config and --profile cannot be used together.");
        }

        if (profilePath is not null && !File.Exists(Path.GetFullPath(profilePath)))
        {
            throw new RepoDoctorValidationException($"profile '{profilePath}' does not exist.");
        }

        if (baselinePath is not null && !File.Exists(Path.GetFullPath(baselinePath)))
        {
            throw new RepoDoctorValidationException($"baseline '{baselinePath}' does not exist.");
        }

        return new ScanArguments
        {
            Target = target,
            ConfigPath = configPath is null ? null : Path.GetFullPath(configPath),
            ProfilePath = profilePath is null ? null : Path.GetFullPath(profilePath),
            BaselinePath = baselinePath is null ? null : Path.GetFullPath(baselinePath),
            Format = format,
            OutputPath = outputPath is null ? null : Path.GetFullPath(outputPath),
            Audit = audit,
            TimeoutSeconds = timeoutSeconds,
        };
    }

    private static string RequireValue(IReadOnlyList<string> args, ref int index, string option)
    {
        if (index + 1 >= args.Count)
        {
            throw new RepoDoctorValidationException($"option '{option}' requires a value.");
        }

        index++;
        return args[index];
    }

    private static OutputFormat ParseFormat(string value) => value switch
    {
        "console" => OutputFormat.Console,
        "json" => OutputFormat.Json,
        "sarif" => OutputFormat.Sarif,
        _ => throw new RepoDoctorValidationException($"unknown format '{value}'. Use console or json."),
    };

    private static int ParseTimeout(string value)
    {
        if (!int.TryParse(value, out var seconds) || seconds <= 0)
        {
            throw new RepoDoctorValidationException($"--timeout-seconds must be a positive integer, got '{value}'.");
        }

        return seconds;
    }
}
