using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;

using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using NukeConf;
using static Nuke.Common.IO.FileSystemTasks;
using System.Xml.XPath;
using Nuke.Common.CI;
using Console = Colorful.Console;

namespace SlugNuke
{
	/// <summary>
	/// Sets up a solution to be compatible with SlugNuke, including moving directories, ensuring config files, exist, etc.
	/// </summary>
	public class SetupSlugNukeSolution
	{
		public AbsolutePath SourceDirectory { get; set; }
		public AbsolutePath RootDirectory { get; set; }
		public AbsolutePath TestsDirectory { get; set; }
		public AbsolutePath OutputDirectory { get; set; }

		internal AbsolutePath CurrentSolutionPath { get; set; }
		internal AbsolutePath ExpectedSolutionPath { get; set; }
		internal string DotNetPath { get; set; }


		private readonly List<VisualStudioProject> Projects = new List<VisualStudioProject>();


		/// <summary>
		/// Performs initial logic to ensure that the Solution is ready for the SlugNuke build process.
		///   - Solution is in the proper directory structure
		///     - If not, it will move it to the proper structure
		///   - Ensure a .Nuke file exists.
		/// </summary>
		/// <returns></returns>
		public async Task<bool> Initialize () {
			// Find the Solution - Assume we are in the root folder right now.
			List<string> solutionFiles = SearchForSolutionFile(RootDirectory.ToString(), ".sln");
			ControlFlow.Assert(solutionFiles.Count != 0, "Unable to find the solution file");
			ControlFlow.Assert(solutionFiles.Count == 1, "Found more than 1 solution file under the root directory -  - We can only work with 1 solution file." + RootDirectory.ToString());
			string solutionFile = solutionFiles[0];
			Logger.Normal("Solution File found:  {0}", solutionFile);

			// A.  Proper Directory Structure
			ControlFlow.Assert(ProperDirectoryStructure(solutionFile),"Attempted to put solution in proper directory structure, but failed.");

			// B.  Nuke File Exists
			ControlFlow.Assert(NukeFileIsProper(solutionFile),"Unable to format Nuke file in proper format");

			// C.  Ensure the NukeSolutionBuild file is setup.
			await ValidateNukeSolutionBuild();

			// D.  Copy the GitVersion.Yml file
			Assembly assembly = Assembly.GetExecutingAssembly();
			string assemblyFile = assembly.Location;
			string assemblyFolder = Path.GetDirectoryName(assemblyFile);
			Logger.Info("Assembly Folder: " + assemblyFolder);
			string src = Path.Combine(assemblyFolder, "GitVersion.yml");
			AbsolutePath dest = RootDirectory / "GitVersion.yml";

			if (!FileExists(dest))
				File.Copy(src,dest,false);


			// E.  Ensure GitVersion.exe environment variable exists

			return true;
		}



		/// <summary>
		/// Ensures there is a valid NukeSolutionBuild.Conf file and updates it if necessary OR creates it.
		/// </summary>
		/// <returns></returns>
		private async Task<bool> ValidateNukeSolutionBuild () {
			bool updates = false;
			AbsolutePath nsbFile = RootDirectory / "nukeSolutionBuild.conf";

			CustomNukeSolutionConfig customNukeSolutionConfig;
			if ( FileExists(nsbFile) ) {
				using ( FileStream fs = File.OpenRead(nsbFile) ) { customNukeSolutionConfig = await JsonSerializer.DeserializeAsync<CustomNukeSolutionConfig>(fs,CustomNukeSolutionConfig.SerializerOptions()); }
			}
			else {
				customNukeSolutionConfig = new CustomNukeSolutionConfig();
				customNukeSolutionConfig.DeployToVersionedFolder = true;
			}


			// Ensure Deploy Roots have values.

			for ( int i = 0; i < 2; i++ ) {
				string name;
				Configuration config;

				if ( i == 0 ) {
					name = "Production";
					config = Configuration.Release;
				}
				else {
					name = "Test";
					config = Configuration.Debug;
				}

				if ( !customNukeSolutionConfig.IsRootFolderSpecified(config) ) {
					Console.WriteLine("Enter the root deployment folder for {0} [{1}]", Color.Yellow, name, config);
					string answer = Console.ReadLine();
					if ( i == 0 )
						customNukeSolutionConfig.DeployProdRoot = answer;
					else
						customNukeSolutionConfig.DeployTestRoot = answer;
					updates = true;
				}
			}


			bool updateProjectAdd = false;

			// Now go thru the projects and update the config
			foreach (VisualStudioProject project in Projects) {
				NukeConf.Project nukeConfProject = customNukeSolutionConfig.GetProjectByName(project.Name);
				if ( nukeConfProject == null ) {
					updateProjectAdd = true;
					nukeConfProject = new NukeConf.Project() {Name = project.Name};
					nukeConfProject.Framework = project.Framework;
					if ( project.IsTestProject )
						nukeConfProject.Deploy = CustomNukeDeployMethod.None;
					else
						nukeConfProject.Deploy = CustomNukeDeployMethod.Copy;

					updates = true;
					customNukeSolutionConfig.Projects.Add(nukeConfProject);
				}
				else {
					// Check for updated values:
					if ( nukeConfProject.Framework != project.Framework ) {
						nukeConfProject.Framework = project.Framework;
						updates = true;
					}
				}
				
			}

			// We now always write the config file at the end of Setup.  This ensure we get any new properties.
			string json = JsonSerializer.Serialize<CustomNukeSolutionConfig>(customNukeSolutionConfig, CustomNukeSolutionConfig.SerializerOptions());
			File.WriteAllText(nsbFile,json);
			if ( updateProjectAdd ) { Logger.Warn("The file: {0} was updated.  One ore more projects were added.  Ensure they have the correct Deploy setting.", nsbFile); }
			
			return true;
		}



		/// <summary>
		/// Ensures the Nuke file first line has the solution in the right format.
		/// </summary>
		/// <param name="solutionFile"></param>
		/// <returns></returns>
		private bool NukeFileIsProper (string solutionFile) {
			string expectedNukeLine = Path.GetFileName(ExpectedSolutionPath) + "/" + Path.GetFileName(solutionFile);
			
			// Read Nuke File if it exists.
			//string slnFileName = Path.GetFileName(solutionFile);
			//AbsolutePath fullPath = ExpectedSolutionPath / slnFileName;
			AbsolutePath nukeFile = RootDirectory / ".nuke";
			if ( FileExists(nukeFile) ) {
				string [] lines = File.ReadAllLines(nukeFile.ToString(), Encoding.ASCII);
				if ( lines.Length != 0 )
					if ( lines [0] == expectedNukeLine )
						return true;
			}

			// If here the file does not exist or in wrong format.
			File.WriteAllText(nukeFile,expectedNukeLine);
			return true;
		}


		/// <summary>
		/// Ensure the Directory Structure is correct. Projects are in proper place.  
		/// </summary>
		/// <param name="solutionFile"></param>
		/// <returns></returns>
		private bool ProperDirectoryStructure (string solutionFile) {
			// Create src folder if it does not exist.
			if (!DirectoryExists(SourceDirectory)) { Directory.CreateDirectory(SourceDirectory.ToString()); }

			// Create Tests folder if it does not exist.
			if (!DirectoryExists(TestsDirectory)) { Directory.CreateDirectory(TestsDirectory.ToString()); }

			// Create Artifacts / Output folder if it does not exist.
			if (!DirectoryExists(OutputDirectory)) { Directory.CreateDirectory(OutputDirectory.ToString()); }

			// Query the solution for the projects that are in it.
			// We allow all tests to run, instead of failing at first failure.
			CurrentSolutionPath = (AbsolutePath)Path.GetDirectoryName(solutionFile);

			DotNetPath = ToolPathResolver.GetPathExecutable("dotnet");
			IProcess slnfind = ProcessTasks.StartProcess(DotNetPath, "sln " + CurrentSolutionPath + " list", logOutput: true);
			slnfind.AssertWaitForExit();
			IReadOnlyCollection<Output> output = slnfind.Output;


			// There are 2 things we need to check.
			//  1.  Is solution in right folder?
			//  2.  Are projects in right folder.
			//  The Move process has to do the following:
			//   1. Move the project folder to proper place
			//   2. Remove the project from the solution
			//   3. Do steps 1, 2 for every project
			//   4. Move solution file to proper location
			//   5. Re-add all projects to solution
			bool solutionNeedsToMove = false;
			if ( CurrentSolutionPath.ToString() != ExpectedSolutionPath.ToString() ) solutionNeedsToMove = true;

			List<VisualStudioProject> movedProjects = new List<VisualStudioProject>();
			// Step 3
			foreach (Output outputRec in output)
			{
				if (outputRec.Text.EndsWith(".csproj"))
				{
					VisualStudioProject project = GetInitProject(outputRec.Text);
					Projects.Add(project);

					// Do we need to move the project?
					if ( (project.OriginalPath.ToString() != project.NewPath.ToString()) || solutionNeedsToMove ) {
						movedProjects.Add(project);
						MoveProjectStepA(project);
					}
				}
			}

			// Step 4:  Is Solution in proper directory.  If not move it.
			if ( solutionNeedsToMove ) {
				string slnFileCurrent = CurrentSolutionPath / Path.GetFileName(solutionFile);
				string slnFileFuture = ExpectedSolutionPath / Path.GetFileName(solutionFile);
				File.Move(slnFileCurrent,slnFileFuture);
			}


			// Step 5.  Read project to solution
			if ( movedProjects.Count > 0 ) {
				foreach ( VisualStudioProject project in movedProjects ) { MoveProjectStepB(project); }
			}
			return true;
		}



		/// <summary>
		/// Moves a project of a solution:  Moves it's folder location to new location and then updates the solution.
		/// </summary>
		/// <param name="project">VisualStudioProject object representing the project to move.</param>
		/// <returns></returns>
		private bool MoveProjectStepA (VisualStudioProject project) {
			// Move project to new location
			if (project.OriginalPath.ToString() != project.NewPath.ToString())
				Directory.Move(project.OriginalPath, project.NewPath);

			// Remove from Solution
			string removeParam = Path.Combine(project.OriginalPath, project.Namecsproj);
			IProcess sln = ProcessTasks.StartProcess(DotNetPath, "sln " + CurrentSolutionPath + " remove " + removeParam, logOutput: true);
			sln.AssertWaitForExit();
			ControlFlow.Assert(sln.ExitCode == 0,"Failed to remove Project: " + project.Name + " from solution so we could move it.");
			
			return true;
		}


		private bool MoveProjectStepB (VisualStudioProject project) {
			// Now add it back to project with new location
			string addParam = Path.Combine(project.NewPath, project.Namecsproj);
			IProcess sln = ProcessTasks.StartProcess(DotNetPath, "sln " + ExpectedSolutionPath + " add " + addParam, logOutput: true);
			sln.AssertWaitForExit();
			ControlFlow.Assert(sln.ExitCode == 0, "Failed to re-add Project: " + project.Name + " to solution so we could complete the move");

			Logger.Success("Project: {0} successfully relocated into proper new directory layout.", project.Name);
			return true;
		}


		/// <summary>
		/// Takes the current Project path and creates an official VisualStudioProject object from it.
		/// </summary>
		/// <param name="path">Path as returned from "dotnet sln" command</param>
		/// <returns></returns>
		public VisualStudioProject GetInitProject (string path) {
			VisualStudioProject visualStudioProject = new VisualStudioProject()
			{
				Namecsproj = Path.GetFileName(path),
				Name = Path.GetFileName(Path.GetDirectoryName(path))
			};
			
			
			string lcprojName = visualStudioProject.Name.ToLower();

			AbsolutePath newRootPath = ExpectedSolutionPath;
			if ( lcprojName.StartsWith("test") || lcprojName.EndsWith("test") ) {
				visualStudioProject.IsTestProject = true;
				newRootPath = TestsDirectory;
			}
			
			visualStudioProject.OriginalPath = (AbsolutePath) Path.GetDirectoryName(Path.Combine(CurrentSolutionPath, path));
			visualStudioProject.NewPath = newRootPath / visualStudioProject.Name;


			// Determine Framework type.
			DetermineFramework(visualStudioProject);
			return visualStudioProject;
		}



		/// <summary>
		/// Determines the Project's targeted framework.
		/// </summary>
		/// <param name="project"></param>
	private void DetermineFramework (VisualStudioProject project) {
			// Determine csproj path
			AbsolutePath csprojPath = project.OriginalPath / project.Namecsproj;


			XDocument doc = XDocument.Load(csprojPath);
			string value = doc.XPathSelectElement("//PropertyGroup/TargetFramework").Value;
			ControlFlow.Assert(value != string.Empty,"Unable to locate a FrameWork value from the csproj file.  This is a required property. Project: " + project.Namecsproj);
			project.Framework = value;
		}



		List<string> SearchForSolutionFile(string root, string searchTerm)
		{
			var files = new List<string>();

			foreach (var file in Directory.EnumerateFiles(root).Where(m => m.EndsWith(searchTerm)))
			{
				files.Add(file);
			}
			foreach (var subDir in Directory.EnumerateDirectories(root))
			{
				try
				{
					files.AddRange(SearchForSolutionFile(subDir, searchTerm));
				}
				catch (UnauthorizedAccessException)
				{
					// ...
				}
			}

			return files;
		}


	}
}
