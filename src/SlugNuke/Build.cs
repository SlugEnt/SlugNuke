using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.EntityFramework;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Collections;
using NukeConf;
using SlugNuke;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using Project = Nuke.Common.ProjectModel.Project;

[assembly:  InternalsVisibleTo("Test_SlugNuke")]


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

#pragma warning disable CS0649,IDE0044
    [Parameter] string NugetApiKey;
    [Parameter] string NugetRepoUrl;
    [Solution] readonly Solution Solution;
#pragma warning restore CS0649


#pragma warning disable IDE0051

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath OutputDirectory => RootDirectory / "artifacts";

    CustomNukeSolutionConfig CustomNukeSolutionConfig;

    GitVersion _gitVersion;
    GitProcessor _gitProcessor;

    bool IsMasterBuild = false;


    /// <summary>     
    /// Called before most of the other Targets, to ensure various required items are setup
    /// </summary>
    Target PreProcessing => _ => _
        .Unlisted()
	    .Executes(async() => { 
	                                            // Setup the Git Object.  This specifically sets the working directory.
	    _gitVersion = GitVersionTasks.GitVersion(s => s
	                                                             .SetProcessWorkingDirectory(RootDirectory)
	                                                             .SetFramework("netcoreapp3.1")
	                                                             .SetNoFetch(false)
	                                                             .DisableProcessLogOutput()
	                                                             .SetUpdateAssemblyInfo(true))
	                                            .Result;

	    // Loads the Solution specific configuration information for building.
        using (FileStream fs = File.OpenRead(RootDirectory / "NukeSolutionBuild.Conf")) { CustomNukeSolutionConfig = await JsonSerializer.DeserializeAsync<CustomNukeSolutionConfig>(fs,CustomNukeSolutionConfig.SerializerOptions()); }

        // Setup the GitProcessor
        _gitProcessor = new GitProcessor(RootDirectory, _gitVersion);


        // Get current branch and ensure there are no uncommitted updates.  These methods will throw if anything is out of sorts.
        _gitProcessor.GetCurrentBranch();
        _gitProcessor.IsUncommittedChanges();
    });



    /// <summary>
    /// Logic to initialize a project so it is prepared to be built by Nuke.  This only needs to be done once.
    /// Re-arranges projects, creates necessary files, etc.
    /// </summary>
    Target Setup => _ => _
        .Description("Called when first placing a solution under SlugNuke build control.  Also, call anytime you change a project's framework or add projects.")
	    .Executes(async () => {
        InitLogic initializationLogic = new InitLogic()
	    {

		    RootDirectory = RootDirectory,
		    SourceDirectory = SourceDirectory,
		    TestsDirectory = TestsDirectory,
		    OutputDirectory = OutputDirectory,
            ExpectedSolutionPath = SourceDirectory
	    };
            
	    await initializationLogic.Initialize();
        });



    /// <summary>
    /// Provides basic information about the project.
    /// </summary>
    Target Info => _ => _
        .Description("Provides information about the current solution")
        .DependsOn(PreProcessing)
	    .Executes(() =>
	    {
		    Logger.Normal("Source = " + SourceDirectory.ToString());
		    Logger.Normal("Tests:  " + TestsDirectory.ToString());
		    Logger.Normal();
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
        .DependsOn(PreProcessing)
        .Executes(() =>
        {
	        Logger.Normal("Source = " + SourceDirectory.ToString());
	        Logger.Normal("Tests:  " + TestsDirectory.ToString());
	        Logger.Normal("Configuration: " + Configuration.ToString());
            Logger.Normal("Solution = " + Solution.Name);
            Logger.Normal("GitVer.Inform = " + _gitVersion.InformationalVersion);
            DotNetBuild(s => s
                             .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(_gitVersion.AssemblySemVer)
                .SetFileVersion(_gitVersion.AssemblySemFileVer)
                .SetInformationalVersion(_gitVersion.InformationalVersion)
                .SetVerbosity(DotNetVerbosity.Minimal)
                .EnableNoRestore());
        });



    /// <summary>
    /// Prep for sending to a Nuget style repo
    /// </summary>
    Target Pack => _ => _
        .DependsOn(PreProcessing)
        .DependsOn(Compile)
		.DependsOn(Test)

	    .Executes(() =>
	    {
            // If this is a master build (PublishMaster) we commit all code (now that we know compile and tests are good) and then proceed with the packaging
            // This ensure we do not build the package with -alpha suffix.
            // For non master build (Publish) we will carry out this step AFTER the Packing.
		    if ( IsMasterBuild ) {
                _gitProcessor.CommitMasterVersionChanges();
		    }


		    OutputDirectory.GlobFiles("*.nupkg", "*symbols.nupkg").ForEach(DeleteFile);


            // Build the necessary packages 
            foreach ( NukeConf.Project project in CustomNukeSolutionConfig.Projects ) {
			    if ( project.Deploy == CustomNukeConfigEnum.Nuget ) {
				    string fullName = SourceDirectory / project.Name / project.Name + ".csproj";
				    DotNetPack(_ => _
				                    .SetProject(Solution.GetProject(fullName))
				                    .SetOutputDirectory(OutputDirectory)
				                    .SetAssemblyVersion(_gitVersion.AssemblySemVer)
				                    .SetFileVersion(_gitVersion.AssemblySemFileVer)
				                    .SetInformationalVersion(_gitVersion.InformationalVersion)
				                    .SetVersion(_gitVersion.NuGetVersionV2));

                }

            }
	    });


    /// <summary>
    /// Deploy to its final staging location.   If the version already existed we skip it.
    /// </summary>
    Target Publish => _ => _
        .Description("Used for publishing a non-master branch.  The version of the app and in Git will be sometype of 'alpha' version, ie, 1.3.6-beta.5")
       .DependsOn(Pack)
       .Requires(() => NugetApiKey)
       .Requires(() => NugetRepoUrl)
       .Executes(() =>
       {
		       GlobFiles(OutputDirectory, "*.nupkg")
			       .NotEmpty()
			       .Where(x => !x.EndsWith("symbols.nupkg"))
			       .ForEach(x => {IReadOnlyCollection<Output> result =  DotNetNuGetPush(s => s
			                                            .SetTargetPath(x)
			                                            .SetSource(NugetRepoUrl)
			                                            .SetApiKey(NugetApiKey)
			                                            .SetSkipDuplicate(true)
			                                            );
				       if ( result.Count > 0 ) {
                           // Look for skipped message.
                           foreach ( Output outputLine in result ) {
	                           if ( outputLine.Text.Contains("already exists at feed") ) {
		                           string msg = @"A nuget package with this name and version already exists. " +
		                                        "Assuming this is due to you re-running the publish after a prior error that occurred after the push to Nuget was successful.  " +
		                                        "Will carry on as though this push was successfull.  " +
												"Otherwise, if this should have been a new update, then you will need to make another commit and re-publish";
                                   Logger.Warn(msg);
	                           }
                           }
                       }
			       });

	       
		       // Update the Versions file with the latest
		       string lastVersion = _gitProcessor.ProcessVersionsFile();
		       _gitProcessor.CommitSemVersionChanges();
	       

		       Logger.Success("Version: " + lastVersion + " fully committed and deployed to target location.");
       });


    

    /// <summary>
    /// Deploy to its final staging location.   If the version already existed we skip it.
    /// </summary>
    private Target PublishMaster => _ => _
        .Description("Used when you want to move a non-master branch to master.  It will change version to a Major.Minor.Fix version")
        .DependsOn(Pack)
       .Requires(() => NugetApiKey)
       .Requires(() => NugetRepoUrl)
       .Executes(() =>
       {

           GlobFiles(OutputDirectory, "*.nupkg")
               .NotEmpty()
               .Where(x => !x.EndsWith("symbols.nupkg"))
               .ForEach(x => {
                   IReadOnlyCollection<Output> result = DotNetNuGetPush(s => s
                                            .SetTargetPath(x)
                                            .SetSource(NugetRepoUrl)
                                            .SetApiKey(NugetApiKey)
                                            .SetSkipDuplicate(true)
                                             );
                   if (result.Count > 0)
                   {
                           // Look for skipped message.
                           foreach (Output outputLine in result)
                       {
                           if (outputLine.Text.Contains("already exists at feed"))
                           {
                               string msg = @"A nuget package with this name and version already exists. " +
                                            "Assuming this is due to you re-running the publish after a prior error that occurred after the push to Nuget was successful.  " +
                                            "Will carry on as though this push was successfull.  " +
                                            "Otherwise, if this should have been a new update, then you will need to make another commit and re-publish";
                               Logger.Warn(msg);
                           }
                       }
                   }
               });

           Logger.Success("Version: " + _gitProcessor.Version + " fully committed and deployed to target location.");
       });



    /// <summary>
    /// Run the unit tests
    /// </summary>
	Target Test => _ => _
	        .Description("Runs all unit tests.")
            .DependsOn(Compile)
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


    Target Temp => _ => _ 
                        .DependsOn(PreProcessing)
	    .Executes(() => {
	    PublishCopiedFolders();
    });


    /// <summary>
    /// We need to see if this is a publishmaster target.  If so, set the flag.
    /// </summary>
    protected override void OnBuildInitialized()
    {
	    base.OnBuildInitialized();
	    if (InvokedTargets.Contains(PublishMaster)) IsMasterBuild = true;
    }



    /// <summary>
    /// Copies an entire directory, files and subdirs to a destination location.
    /// </summary>
    /// <param name="sourcePath"></param>
    /// <param name="DestPath"></param>
    public static void CopyEntireDirectory(string sourcePath, string DestPath)
    {
	    Parallel.ForEach(Directory.GetFileSystemEntries(sourcePath, "*", SearchOption.AllDirectories)
	                     , (fileName) =>
	                     {
		                     string output = Regex.Replace(fileName, "^" + Regex.Escape(sourcePath), DestPath);
		                     if (File.Exists(fileName))
		                     {
			                     Directory.CreateDirectory(Path.GetDirectoryName(output));
			                     File.Copy(fileName, output, true);
		                     }
		                     else
			                     Directory.CreateDirectory(output);
	                     });
    }

    
    public bool PublishCopiedFolders () {
	    foreach ( NukeConf.Project project in CustomNukeSolutionConfig.Projects ) {
		    if ( project.Deploy == CustomNukeConfigEnum.Copy ) {
			    // Destination
			    AbsolutePath deploy = (AbsolutePath) CustomNukeSolutionConfig.DeployRoot / project.Name / ("Ver" + _gitVersion.SemVer);

			    // Source
			    AbsolutePath src = (AbsolutePath) SourceDirectory / project.Name / "bin" / Configuration / project.Framework;

			    CopyEntireDirectory(src, deploy);
		    }

	    }
	    return true;
    }

}
