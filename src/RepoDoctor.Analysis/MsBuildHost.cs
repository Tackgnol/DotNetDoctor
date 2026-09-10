using Microsoft.Build.Locator;

namespace RepoDoctor.Analysis;

public static class MsBuildHost
{
    private static readonly Lock Gate = new();
    private static VisualStudioInstance? _instance;

    public static VisualStudioInstance EnsureRegistered()
    {
        lock (Gate)
        {
            if (_instance is not null)
            {
                return _instance;
            }

            _instance = MSBuildLocator.IsRegistered
                ? MSBuildLocator.QueryVisualStudioInstances().First()
                : MSBuildLocator.RegisterDefaults();

            return _instance;
        }
    }
}
