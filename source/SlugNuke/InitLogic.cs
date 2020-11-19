using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using Project = Nuke.Common.ProjectModel.Project;

namespace SlugNuke
{
	public class InitLogic
	{
		public AbsolutePath SourceDirectory { get; set; }
		public AbsolutePath RootDirectory { get; set; }
		public AbsolutePath TestsDirectory { get; set; }
		public AbsolutePath OutputDirectory { get; set; }

		internal AbsolutePath CurrentSolutionPath { get; set; }
		internal AbsolutePath ExpectedSolutionPath { get; set; }
		internal string DotNetPath { get; set; }


		public List<InitProject> Projects = new List<InitProject>();


		/// <summary>
		/// Performs initial logic to ensure that the Solution is ready for the SlugNuke build process.
		///   - Solution is in the proper directory structure
		///     - If not, it will move it to the proper structure
		///   - Ensure a .Nuke file exists.
		/// </summary>
		/// <returns></returns>
		public async Task<bool> Initialize () {
			// Find the Solution - Assume we are in the root folder right now.
			List<string> solutionFiles = SearchAccessibleFiles(RootDirectory.ToString(), ".sln");
			ControlFlow.Assert(solutionFiles.Count != 0, "Unable to find the solution file");
			ControlFlow.Assert(solutionFiles.Count == 1, "Found more than 1 solution file under the root directory -  - We can only work with 1 solution file." + RootDirectory.ToString());
			string solutionFile = solutionFiles[0];
			Logger.Normal("Solution File found:  {0}", solutionFile);

			// A.  Proper Directory Structure
			ControlFlow.Assert(ProperDirectoryStructure(solutionFile),"Attempted to put solution in proper directory structure, but failed.");

			// B.  Nuke File Exists
			ControlFlow.Assert(NukeFileIsProper(solutionFile),"Unable to format Nuke file in proper format");

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

			List<InitProject> movedProjects = new List<InitProject>();
			// Step 3
			foreach (Output outputRec in output)
			{
				if (outputRec.Text.EndsWith(".csproj"))
				{
					InitProject project = GetInitProject(outputRec.Text);

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


			// Step 5.  Readd project to solution
			if ( movedProjects.Count > 0 ) {
				foreach ( InitProject project in movedProjects ) { MoveProjectStepB(project); }
			}
			return true;
		}



		/// <summary>
		/// Moves a project of a solution:  Moves it's folder location to new location and then updates the solution.
		/// </summary>
		/// <param name="project">InitProject object representing the project to move.</param>
		/// <returns></returns>
		private bool MoveProjectStepA (InitProject project) {
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


		private bool MoveProjectStepB (InitProject project) {
			// Now add it back to project with new location
			string addParam = Path.Combine(project.NewPath, project.Namecsproj);
			IProcess sln = ProcessTasks.StartProcess(DotNetPath, "sln " + ExpectedSolutionPath + " add " + addParam, logOutput: true);
			sln.AssertWaitForExit();
			ControlFlow.Assert(sln.ExitCode == 0, "Failed to re-add Project: " + project.Name + " to solution so we could complete the move");

			Logger.Success("Project: {0} successfully relocated into proper new directory layout.", project.Name);
			return true;
		}


		/// <summary>
		/// Takes the current Project path and creates an official InitProject object from it.
		/// </summary>
		/// <param name="path">Path as returned from "dotnet sln" command</param>
		/// <returns></returns>
		public InitProject GetInitProject (string path)
		{
			InitProject initProject = new InitProject();
			string parentPath = 
			

			initProject.Namecsproj = Path.GetFileName(path);
			initProject.Name = Path.GetFileName(Path.GetDirectoryName(path));
			initProject.OriginalPath = (AbsolutePath) Path.GetDirectoryName(Path.Combine(CurrentSolutionPath, path));
			initProject.NewPath = ExpectedSolutionPath / initProject.Name;

			return initProject;
		}



		List<string> SearchAccessibleFiles(string root, string searchTerm)
		{
			var files = new List<string>();

			foreach (var file in Directory.EnumerateFiles(root).Where(m => m.Contains(searchTerm)))
			{
				files.Add(file);
			}
			foreach (var subDir in Directory.EnumerateDirectories(root))
			{
				try
				{
					files.AddRange(SearchAccessibleFiles(subDir, searchTerm));
				}
				catch (UnauthorizedAccessException ex)
				{
					// ...
				}
			}

			return files;
		}
	}
}
