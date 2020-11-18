using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Collections;
using NukeConf;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using Project = Nuke.Common.ProjectModel.Project;


[CheckBuildProjectConfigurations]
[ShutdownDotNetAfterServerBuild]
class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main () => Execute<Build>(x => x.Compile);


    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")] 
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;
    
    [Parameter] string NugetApiKey;
    [Parameter] string NugetRepoUrl;


    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;
    [GitVersion(Framework = "netcoreapp3.1")] readonly GitVersion GitVersion;

    AbsolutePath SourceDirectory => RootDirectory / "source";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath OutputDirectory => RootDirectory / "output";

    CustomNukeSolutionConfig CustomNukeSolutionConfig;

    Target Info => _ => _
	    .Executes(() =>
	    {
		    Logger.Normal("Source = " + SourceDirectory.ToString());
		    Logger.Normal("Tests:  " + TestsDirectory.ToString());
		    Logger.Normal();
	    });
    

    // Loads the Solution specific configuration information for building.
    Target LoadSolutionConfig => _ => _
	    .Executes(async() =>
	    {
		    using ( FileStream fs = File.OpenRead(RootDirectory / "NukeSolutionBuild.Conf") ) { CustomNukeSolutionConfig = await JsonSerializer.DeserializeAsync<CustomNukeSolutionConfig>(fs); }
	    });


    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(OutputDirectory);
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
	        Logger.Normal("Source = " + SourceDirectory.ToString());
	        Logger.Normal("Tests:  " + TestsDirectory.ToString());
	        Logger.Normal("Configuration: " + Configuration.ToString());
            Logger.Normal("Solution = " + Solution.Name);
            Logger.Normal("GitVer.Inform = " + GitVersion.InformationalVersion);
            DotNetBuild(s => s
                             .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(GitVersion.AssemblySemVer)
                .SetFileVersion(GitVersion.AssemblySemFileVer)
                .SetInformationalVersion(GitVersion.InformationalVersion)
                .SetVerbosity(DotNetVerbosity.Minimal)
                .EnableNoRestore());
        });



    Target Pack => _ => _
		.DependsOn(Compile)
		.DependsOn(LoadSolutionConfig)
		
	    .Executes(() =>
	    {
		    OutputDirectory.GlobFiles("*.nupkg", "*symbols.nupkg").ForEach(DeleteFile);

		    foreach ( NukeConf.Project project in CustomNukeSolutionConfig.Projects ) {
			    if ( project.Deploy.ToLower() == "nuget" ) {
				    DotNetPack(_ => _
				                    .SetProject(Solution.GetProject(project.Name))
				                    .SetOutputDirectory(OutputDirectory)
				                    .SetAssemblyVersion(GitVersion.AssemblySemVer)
				                    .SetFileVersion(GitVersion.AssemblySemFileVer)
				                    .SetInformationalVersion(GitVersion.InformationalVersion)
				                    .SetVersion(GitVersion.NuGetVersionV2));

                }
            }
	    });

    Target Publish => _ => _
       .DependsOn(Pack)
       .Requires(() => NugetApiKey)
       .Requires(() => NugetRepoUrl)
       .Executes(() =>
       {
	       GlobFiles(OutputDirectory, "*.nupkg")
		       .NotEmpty()
		       .Where(x => !x.EndsWith("symbols.nupkg"))
		       .ForEach(x =>
		       {
			       DotNetNuGetPush(s => s
			                            .SetTargetPath(x)
			                            .SetSource(NugetRepoUrl)
			                            .SetApiKey(NugetApiKey)
			       );
		       });
       });


    Target Test => _ => _
                        //.DependsOn(Compile)
                        .Executes(() =>
                        {
	                        foreach ( Project project in Solution.AllProjects ) {
		                        if ( project.Path.ToString().StartsWith(TestsDirectory) ) {
			                        string projectTestDirectory = Path.GetDirectoryName(project.Path.ToString());
			                        string dotnetPath = ToolPathResolver.GetPathExecutable("dotnet");

			                        // We allow all tests to run, instead of failing at first failure.
			                        IProcess dotnetTest = ProcessTasks.StartProcess(dotnetPath, "test " + projectTestDirectory, logOutput: true);
			                        dotnetTest.AssertWaitForExit();
			                        IReadOnlyCollection<Output> output = dotnetTest.Output;

                                    // Write the last line of output in green or red depending on outcome
                                    string testResults = "";
                                    if ( output.Count > 0 ) { testResults = output.Last().Text; }

                                    if ( dotnetTest.ExitCode != 0 ) {
	                                    Logger.Warn(testResults);
	                                    ControlFlow.Assert(dotnetTest.ExitCode == 0, "Unit Tests Failed");
                                    }
                                    else 
                                        Logger.Success(testResults);

		                        }
                            }
                        });
}
