using Microsoft.Build.Locator;
using RepoDoctor.Spike;

var msbuild = MSBuildLocator.RegisterDefaults();
Console.Error.WriteLine($"[spike] MSBuild {msbuild.Version} from {msbuild.MSBuildPath}");

return await SpikeRunner.RunAsync(args);
