using System.Text;

using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;

using static Nuke.Common.Tools.DotNet.DotNetTasks;

internal class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Release'")]
    private readonly string Configuration = "Release";

    [Solution] private readonly Solution Solution = null!;

    private AbsolutePath SourceDirectory => RootDirectory / "src";
    private AbsolutePath TestDirectory => SourceDirectory / "SteamShare.Test";
    private AbsolutePath PublishDirectory => RootDirectory / "publish" / "out";

    [Parameter("Target Runtime Identifiers (RIDs) to publish for - Default is 'win-x64,linux-x64'")]
    private readonly string[] Rids = ["win-x64", "linux-x64"];

    private Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s.SetProjectFile(Solution));
        });

    private Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoRestore());
        });

    private Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetTest(s => s
                .SetProjectFile(TestDirectory)
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .SetProcessEnvironmentVariable("STEAMSHARE_TEST_MODE", "dummy"));
        });

    private Target Pack => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            foreach (var rid in Rids)
            {
                // CLI self-contained single-file
                DotNetPublish(s => s
                    .SetProject(SourceDirectory / "SteamShare.CLI")
                    .SetConfiguration(Configuration)
                    .SetRuntime(rid)
                    .SetPublishTrimmed(true)
                    .SetSelfContained(true)
                    .SetProperty("PublishSingleFile", "true")
                    .SetOutput(PublishDirectory / $"cli-sc-{rid}"));

                // CLI framework-dependent single-file
                DotNetPublish(s => s
                    .SetProject(SourceDirectory / "SteamShare.CLI")
                    .SetConfiguration(Configuration)
                    .SetRuntime(rid)
                    .SetSelfContained(false)
                    .SetProperty("PublishSingleFile", "true")
                    .SetOutput(PublishDirectory / $"cli-fd-{rid}"));

                // GUI self-contained single-file
                DotNetPublish(s => s
                    .SetProject(SourceDirectory / "SteamShare.UI")
                    .SetConfiguration(Configuration)
                    .SetRuntime(rid)
                    .SetSelfContained(true)
                    .SetPublishTrimmed(true)
                    .SetProperty("PublishSingleFile", "true")
                    .SetOutput(PublishDirectory / $"gui-sc-{rid}"));

                // GUI framework-dependent single-file
                DotNetPublish(s => s
                    .SetProject(SourceDirectory / "SteamShare.UI")
                    .SetConfiguration(Configuration)
                    .SetRuntime(rid)
                    .SetSelfContained(false)
                    .SetProperty("PublishSingleFile", "true")
                    .SetOutput(PublishDirectory / $"gui-fd-{rid}"));

                // Combined self-contained (NOT single-file)
                DotNetPublish(s => s
                    .SetProject(SourceDirectory / "SteamShare.UI")
                    .SetConfiguration(Configuration)
                    .SetRuntime(rid)
                    .SetPublishTrimmed(true)
                    .SetSelfContained(true)
                    .SetOutput(PublishDirectory / $"combined-sc-{rid}"));
                DotNetPublish(s => s
                    .SetProject(SourceDirectory / "SteamShare.CLI")
                    .SetConfiguration(Configuration)
                    .SetRuntime(rid)
                    .SetPublishTrimmed(true)
                    .SetSelfContained(true)
                    .SetOutput(PublishDirectory / $"combined-sc-{rid}"));

                // Combined framework-dependent (NOT single-file)
                DotNetPublish(s => s
                    .SetProject(SourceDirectory / "SteamShare.UI")
                    .SetConfiguration(Configuration)
                    .SetRuntime(rid)
                    .SetSelfContained(false)
                    .SetOutput(PublishDirectory / $"combined-fd-{rid}"));
                DotNetPublish(s => s
                    .SetProject(SourceDirectory / "SteamShare.CLI")
                    .SetConfiguration(Configuration)
                    .SetRuntime(rid)
                    .SetSelfContained(false)
                    .SetOutput(PublishDirectory / $"combined-fd-{rid}"));

                // Copy docs into every publish output
                foreach (var variant in new[] { "cli-sc", "cli-fd", "gui-sc", "gui-fd", "combined-sc", "combined-fd" })
                {
                    var dir = PublishDirectory / $"{variant}-{rid}";
                    (RootDirectory / "README.en.md").Copy(dir / "README.en.md", ExistsPolicy.MergeAndOverwrite);
                    (RootDirectory / "README.md").Copy(dir / "README.md", ExistsPolicy.MergeAndOverwrite);
                    (RootDirectory / "LICENSE").Copy(dir / "LICENSE", ExistsPolicy.MergeAndOverwrite);
                }
            }
        });

    private Target Format => _ => _
        .Executes(() =>
        {
            DotNet($"format {Solution} --verify-no-changes");
        });
}
