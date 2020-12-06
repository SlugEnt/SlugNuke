using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotCover;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.EntityFramework;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Tools.ReportGenerator;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Collections;
using NukeConf;
using SlugNuke;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml.XPath;
using Nuke.Common.Tools.MSBuild;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotCover.DotCoverTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Tools.ReportGenerator.ReportGeneratorTasks;
using Project = Nuke.Common.ProjectModel.Project;

[assembly: InternalsVisibleTo("Test_SlugNuke")]


[CheckBuildProjectConfigurations]
[ShutdownDotNetAfterServerBuild]
public partial class Build : NukeBuild {
	/// Support plugins are available for:
	///   - JetBrains ReSharper        https://nuke.build/resharper
	///   - JetBrains Rider            https://nuke.build/rider
	///   - Microsoft VisualStudio     https://nuke.build/visualstudio
	///   - Microsoft VSCode           https://nuke.build/vscode

	public static int Main () { return Execute<Build>(x => x.Compile); }



	[Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
	Configuration Configuration;

#pragma warning disable CS0649,IDE0044
	[Parameter("Nuget API Key used to deploy to Nuget compatible Repo.")]
	string NugetApiKey;

	[Parameter("URL of the Nuget compatible Repository that packages should be pushed to.")]
	string NugetRepoUrl;

	[Parameter("When Publishing, it skips the deployment of any project with a deploy method of Nuget")]
	bool SkipNuget;

	[Solution] readonly Solution Solution;
#pragma warning restore CS0649


#pragma warning disable IDE0051

	AbsolutePath SourceDirectory => RootDirectory / "src";
	AbsolutePath TestsDirectory => RootDirectory / "tests";
	AbsolutePath OutputDirectory => RootDirectory / "artifacts";

	CustomNukeSolutionConfig CustomNukeSolutionConfig;

	GitProcessor _gitProcessor;

	bool IsProductionBuild = false;

	List<PublishResult> PublishResults = new List<PublishResult>();


	/// <summary>
	/// Pre-Processing that must occur for majority of the targets to work.
	/// </summary>
	private void PreProcessing () {
		if ( Configuration == null ) {
			if ( InvokedTargets.Contains(PublishProd) )
				Configuration = Configuration.Release;
			else
				Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;
		}


		Utility.ValidateGetVersionEnvVariable();

		// Loads the Solution specific configuration information for building.
		string json = File.ReadAllText(RootDirectory / "NukeSolutionBuild.Conf");
		CustomNukeSolutionConfig = JsonSerializer.Deserialize<CustomNukeSolutionConfig>(json, CustomNukeSolutionConfig.SerializerOptions());
		ControlFlow.Assert(CustomNukeSolutionConfig.CheckRootFolders(),
		                   "The DeployProdRoot or DeployTestRoot in the NukeSolutionBuild.Conf do not contain valid entries.  Run SlugNuke --Target Setup to fix.");

		// Setup the GitProcessor
		_gitProcessor = new GitProcessor(RootDirectory);


		if ( _gitProcessor.GitVersion == null ) Logger.Error("GitVersion not Loaded");

		// Get current branch and ensure there are no uncommitted updates.  These methods will throw if anything is out of sorts.
		_gitProcessor.GetCurrentBranch();
		_gitProcessor.IsUncommittedChanges();
		_gitProcessor.IsBranchUpToDate();

		if ( _gitProcessor.IsCurrentBranchMainBranch() && InvokedTargets.Contains(Publish) ) {
			string msg =
				@"The current branch is the main branch, yet you are running a Test Publish command.  This is unsupported as it will cause version issues in Git.  " +
				"Either create a branch off master to put the changes into (this is probably what you want) OR change Target command to PublishProd.";
			ControlFlow.Assert(1 == 0, msg);
		}
	}



	/// <summary>
	/// Logic to initialize a project so it is prepared to be built by Nuke.  This only needs to be done once.
	/// Re-arranges projects, creates necessary files, etc.
	/// </summary>
	Target Setup => _ => _
	                     .Description(
		                     "Called when first placing a solution under SlugNuke build control.  Also, call anytime you change a project's framework or add projects.")
	                     .Executes(async () => {
		                     SetupSlugNukeSolution initializationLogic = new SetupSlugNukeSolution()
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
	Target Info => _ => _.Description("Provides information about the current solution")
	                     .Executes(() => {
		                     Logger.Normal("Root =         " + RootDirectory.ToString());
		                     Logger.Normal("Source =       " + SourceDirectory.ToString());
		                     Logger.Normal("Tests:         " + TestsDirectory.ToString());
		                     Logger.Normal("Output:        " + OutputDirectory);
		                     Logger.Normal("Solution:      " + Solution.Path);

		                     Logger.Normal("Build Assemnbly Dir:       " + BuildAssemblyDirectory);
		                     Logger.Normal("Build Project Dir:         " + BuildProjectDirectory);
		                     Logger.Normal("NugetPackageConfigFile:    " + ToolPathResolver.NuGetPackagesConfigFile);
		                     //Logger.Normal("Executing Assembly Dir:    " + ToolPathResolver. .ExecutingAssemblyDirectory);
		                     Logger.Normal("Nuget Assets Config File:  " + ToolPathResolver.NuGetAssetsConfigFile);
		                     Logger.Normal();
	                     });



	Target Clean => _ => _.Before(Restore)
	                      .Executes(() => {
		                      SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
		                      TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
		                      EnsureCleanDirectory(OutputDirectory);
	                      });


	Target Restore => _ => _.DependsOn(Clean).Executes(() => { DotNetRestore(s => s.SetProjectFile(Solution)); });


	Target Compile => _ => _.DependsOn(Restore)
	                        .Executes(() => {
		                        string assemblyVer = _gitProcessor.GitVersion.AssemblySemVer;
		                        string fileVer = _gitProcessor.GitVersion.AssemblySemFileVer;

		                        // If this is a master build (PublishMaster) we commit all code (now that we know compile and tests are good) and then proceed with the packaging
		                        // This ensure we do not build the package with -alpha suffix.
		                        // For non master build (Publish) we will carry out this step AFTER the Packing.
		                        _gitProcessor.GetNextVersionAndTags(IsProductionBuild);

		                        string infoVer = _gitProcessor.SemVersion;

		                        Logger.Normal("Source = " + SourceDirectory.ToString());
		                        Logger.Normal("Tests:  " + TestsDirectory.ToString());
		                        Logger.Normal("Configuration: " + Configuration.ToString());
		                        Logger.Normal("Solution = " + Solution.Name);
		                        Logger.Normal("GitVer.Inform = " + _gitProcessor.GitVersion.InformationalVersion);
								

		                        DotNetBuild(s => s.SetProjectFile(Solution)
		                                          .SetConfiguration(Configuration)
		                                          .SetAssemblyVersion(assemblyVer)
		                                          .SetFileVersion(fileVer)
		                                          .SetInformationalVersion(infoVer)
		                                          .SetVerbosity(DotNetVerbosity.Minimal)
		                                          .EnableNoRestore());
	                        });


	/// <summary>
	/// Commits changes to Git after Compile and Test
	/// </summary>
	Target GitCommit => _ => _.After(Test)
	                          .After(Compile)
	                          .Executes(() => {
		                          if ( IsProductionBuild )
			                          _gitProcessor.CommitMainVersionChanges();
		                          else {
			                          // Update the Versions file with the latest
			                          _gitProcessor.CommitSemVersionChanges();
		                          }
	                          });


	/// <summary>
	/// Prep for sending to a Nuget style repo
	/// </summary>
	Target Pack => _ => _.DependsOn(Compile)
	                     .DependsOn(Test)
	                     .DependsOn(GitCommit)
	                     .Executes(() => {
		                     OutputDirectory.GlobFiles("*.nupkg", "*symbols.nupkg").ForEach(DeleteFile);


		                     // Build the necessary packages 
		                     foreach ( NukeConf.Project project in CustomNukeSolutionConfig.Projects ) {
			                     if ( project.Deploy == CustomNukeDeployMethod.Nuget ) {
				                     string fullName = SourceDirectory / project.Name / project.Name + ".csproj";
				                     IReadOnlyCollection<Output> output = DotNetPack(_ => _.SetProject(Solution.GetProject(fullName))
				                                                                           .SetOutputDirectory(OutputDirectory)
				                                                                           .SetIncludeSymbols(true)
				                                                                           .SetAssemblyVersion(_gitProcessor.GitVersion.AssemblySemVer)
				                                                                           .SetFileVersion(_gitProcessor.GitVersion.AssemblySemFileVer)
				                                                                           .SetInformationalVersion(_gitProcessor.InformationalVersion)
				                                                                           .SetVersion(_gitProcessor.SemVersionNugetCompatible));
				                     foreach ( Output item in output ) {
					                     if ( item.Text.Contains("Successfully created package") ) {
						                     string file = item.Text.Substring(item.Text.IndexOf("'") + 1);
						                     PublishResults.Add(new PublishResult(project.Name, project.Deploy.ToString(), file));
					                     }
				                     }
			                     }

		                     }
	                     });


	/// <summary>
	/// Deploy to its final staging location.   If the version already existed we skip it.
	/// </summary>
	Target Publish => _ => _
	                       .Description(
		                       "Used for publishing a non-master branch.  The version of the app and in Git will be sometype of 'alpha' version, ie, 1.3.6-beta.5")
	                       .DependsOn(Pack)
	                       .Requires(() => NugetApiKey)
	                       .Requires(() => NugetRepoUrl)
	                       .Executes(() => { ExecutePublish(); });




	/// <summary>
	/// Deploy to its final staging location.   If the version already existed we skip it.
	/// </summary>
	Target PublishProd => _ => _.Description("Used when you want to move a non-master branch to master.  It will change version to a Major.Minor.Fix version")
	                            .DependsOn(Pack)
	                            .Requires(() => NugetApiKey)
	                            .Requires(() => NugetRepoUrl)
	                            .Executes(() => {
		                            if ( SkippedTargets.Count > 0 ) {
			                            ControlFlow.Assert(
				                            1 == 0,
				                            "You cannot use the --skip flag with PublishProd.  PublishProd Process requires the previous steps to have completed.");
		                            }

		                            ExecutePublish();
	                            });




	Target Test => _ => _.Description("Runs all unit tests.")
	                     .DependsOn(Compile)
	                     .Triggers(CodeCoverage)
	                     .Executes(() => {
		                     foreach ( NukeConf.Project nukeConfProject in CustomNukeSolutionConfig.Projects ) {
			                     if ( nukeConfProject.IsTestProject ) {
				                     string fullName = TestsDirectory / nukeConfProject.Name / nukeConfProject.Name + ".csproj";
				                     Project project = Solution.GetProject(fullName);
				                     ControlFlow.Assert(project != null,
				                                        "Unable to find the project named: " +
				                                        nukeConfProject.Name +
				                                        " in the Nuke Solution.  May need to re-run SlugNuke Setup");
				                     AbsolutePath CoveragePath = OutputDirectory / ("Coverage_" + project.Name);
				                     DotNetTest(t => t.SetProjectFile(project.Directory)
				                                      .SetConfiguration(Configuration)
				                                      .EnableNoBuild()
				                                      .EnableNoRestore()
				                                      .SetProcessLogOutput(true)
				                                      .SetProcessArgumentConfigurator(arguments => arguments.Add("/p:CollectCoverage={0}", true)
					                                                                      .Add("/p:CoverletOutput={0}/", CoveragePath)
					                                                                      .Add("/p:CoverletOutputFormat={0}", "cobertura")
					                                                                      .Add("/p:Threshold={0}", CustomNukeSolutionConfig.CodeCoverageThreshold)
					                                                                      .Add("/p:SkipAutoProps={0}", true)
					                                                                      .Add("/p:ExcludeByAttribute={0}",
					                                                                           "\"Obsolete%2cGeneratedCodeAttribute%2cCompilerGeneratedAttribute\"")
					                                                                      .Add("/p:UseSourceLink={0}", true))
				                                      .SetResultsDirectory(OutputDirectory / "Tests"));

			                     }
							 }

	                     });


	Target CodeCoverage => _ => _
	        .Description("Reports on Code Coverage Results")
			// Prevent errors from stopping the publish process.
	        .After(Publish)
	        .After(PublishProd)

	         .DependsOn(Test)
	         .Executes(() => {
		        if ( !CustomNukeSolutionConfig.UseCodeCoverage ) return;

	             foreach ( NukeConf.Project nukeConfProject in CustomNukeSolutionConfig.Projects ) {
	                 if ( nukeConfProject.IsTestProject ) {
	                     string fullName = TestsDirectory / nukeConfProject.Name / nukeConfProject.Name + ".csproj";
	                     Project project = Solution.GetProject(fullName);
	                     ControlFlow.Assert(project != null,
	                                        "Unable to find the project named: " +
	                                        nukeConfProject.Name +
	                                        " in the Nuke Solution.  May need to re-run SlugNuke Setup");
	                     AbsolutePath CoveragePath = OutputDirectory / ("Coverage_" + project.Name);

	                     ReportGenerator(r => r.SetTargetDirectory(CoveragePath)
	                                           .SetProcessWorkingDirectory(CoveragePath)
	                                           .SetReportTypes(ReportTypes.HtmlInline, ReportTypes.Badges)
	                                           .SetReports("coverage.cobertura.xml")
	                                           .SetProcessToolPath("reportgenerator"));

	                     AbsolutePath coverageFile = CoveragePath / "index.html";
	                     Process.Start(@"cmd.exe ", @"/c " + coverageFile);
	                 }
				 }
	         });


/// <summary>
	/// We need to see if this is a publishmaster target.  If so, set the flag.
	/// </summary>
	protected override void OnBuildInitialized()
    {
	    base.OnBuildInitialized();

	    if (InvokedTargets.Contains(PublishProd)) IsProductionBuild = true;

        // We need to do pre-processing if this is not the Setup target.
        if (!InvokedTargets.Contains(Setup))
			PreProcessing();
    }


	/// <summary>
	/// Runs after Build Completion
	/// </summary>
    protected override void OnBuildFinished () {
	    if ( InvokedTargets.Contains(Setup) ) return;
        Logger.Info("Version Information");
        Logger.Info("   Overall Version #:  {0}", _gitProcessor.Version);
        Logger.Info("   SemVer Version #:  {0}", _gitProcessor.SemVersion);
        Console.WriteLine();
        Console.WriteLine("Project Summary");
        Console.WriteLine();

        foreach (PublishResult result in PublishResults) {
	        Logger.Info("  {0,-25}  |  {1}{3}       {2}", result.NameOfProject, result.DeployMethod, result.DeployName, Environment.NewLine);
        }

	    base.OnBuildFinished();
    }


    /// <summary>
    /// Publishes projects that are deployed to a provided folder location.
    /// </summary>
    /// <returns></returns>
    public bool PublishCopiedFolders () {
	    foreach ( NukeConf.Project project in CustomNukeSolutionConfig.Projects ) {
            // Calculate the name of the Version folder
		    string versionFolder = "";
		    if ( project.Deploy != CustomNukeDeployMethod.Copy ) continue;

		    if ( CustomNukeSolutionConfig.DeployToVersionedFolder ) {
			    if ( CustomNukeSolutionConfig.DeployFolderUsesSemVer )
				    versionFolder = "Ver" + _gitProcessor.SemVersionNugetCompatible;
			    else
				    versionFolder = "Ver" + _gitProcessor.Version;
		    }

            // Calculate App Name Path
            string appPath = project.Name;
            if ( CustomNukeSolutionConfig.DeployToAssemblyFolders ) {
	            Project nukeProject = GetSolutionProject(project);
	            ControlFlow.Assert(nukeProject != null, "Unable to find the Nuke Project for: " + project.Name);

	            // Load the project file to get the Assembly name.  If not found, then its the projectName
	            XDocument doc = XDocument.Load(nukeProject.Path);
	            XElement element = doc.XPathSelectElement("//PropertyGroup/AssemblyName");
	            if ( element != null ) 
                    if ( !String.IsNullOrEmpty(element.Value) ) 
                        if ( !element.Value.Contains('.') )  
	                        appPath = element.Value; 
						else {
				            string [] appParts = element.Value.Split('.',StringSplitOptions.RemoveEmptyEntries);
				            appPath = string.Join(Path.DirectorySeparatorChar,appParts);
				            appPath = appPath + Path.DirectorySeparatorChar + element.Value;
                        }
            }

            // Destination
		    AbsolutePath root;
		    if ( Configuration == "Release" )
			    root = (AbsolutePath) CustomNukeSolutionConfig.DeployProdRoot;
		    else
			    root = (AbsolutePath) CustomNukeSolutionConfig.DeployTestRoot;

            // Build Full Path
		    AbsolutePath deploy = (AbsolutePath) root / appPath / versionFolder;

		    // Source
		    AbsolutePath src = (AbsolutePath) SourceDirectory / project.Name / "bin" / Configuration / project.Framework;

		    Utility.CopyEntireDirectory(src, deploy);
            
            PublishResults.Add(new PublishResult(project.Name,project.Deploy.ToString(),deploy));
            Logger.Success("Project:  {0}  Deployed to Copy Folder:  {1}", project.Name, deploy);

	    }
	    return true;
    }



	/// <summary>
	/// Runs the Publish command for either Test or Prod.
	/// </summary>
	/// <returns></returns>
    public bool ExecutePublish()
    {
	    if (SkippedTargets.Count > 0)
	    {
		    ControlFlow.Assert(
			    1 == 0, "You cannot use the --skip flag with PublishProd.  PublishProd Process requires the previous steps to have completed.");
	    }

	    if (!SkipNuget)
	    {
		    GlobFiles(OutputDirectory, "*.nupkg")
			    .NotEmpty()
			    .Where(x => !x.EndsWith("symbols.nupkg"))
			    .ForEach(x => {
				    IReadOnlyCollection<Output> result =
					    DotNetNuGetPush(s => s.SetTargetPath(x).SetSource(NugetRepoUrl).SetApiKey(NugetApiKey).SetSkipDuplicate(true));
				    if (result.Count > 0)
				    {
					    // Look for skipped message.
					    foreach (Output outputLine in result)
					    {
						    if (outputLine.Text.Contains("already exists at feed"))
						    {
							    string msg = @"A nuget package  <" +
							                 Path.GetFileName(x) +
							                 ">  with this name and version already exists. " +
							                 "Assuming this is due to you re-running the publish after a prior error that occurred after the push to Nuget was successful.  " +
							                 "Will carry on as though this push was successfull.  " +
							                 "Otherwise, if this should have been a new update, then you will need to make another commit and re-publish";
							    Logger.Warn(msg);
						    }
						    else PublishResults.Add(new PublishResult("", "Nuget", x));
						}
				    }
			    });

		    // Now process Copy Outputs.
		    PublishCopiedFolders();

		    Logger.Success("Version: " + _gitProcessor.Version + " fully committed and deployed to target location.");
		}

	    return true;
    }

}
