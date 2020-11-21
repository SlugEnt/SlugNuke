using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.GitVersion;
using Console = Colorful.Console;


namespace SlugNuke
{
	/// <summary>
	///  Processes all Git Related stuff 
	/// </summary>
	class GitProcessor {
		const string VERSIONS_FILENAME = "versions.txt";
		const int VERSION_HISTORY_TO_KEEP = 4;
		const int VERSION_HISTORY_LIMIT = 7;
		const string COMMIT_MARKER = "|^|";
		const string GIT_COMMAND_MARKER = "|";

		internal string DotNetPath { get; set; }
		internal AbsolutePath RootDirectory;
		internal GitVersion _gitVersion;

		/// <summary>
		/// The Branch that the repository is currently on
		/// </summary>
		public string CurrentBranch { get; set; }

		public string Version { get; set; }
		public string SemVersion { get; set; }


		/// <summary>
		/// Keeps track of all of the Git Command output for debugging purposes.
		/// </summary>
		public List<Output> GitCommandOutputHistory = new List<Output>();


		public GitProcessor (AbsolutePath rootPath, GitVersion gitVersion) {
			RootDirectory = rootPath;
			_gitVersion = gitVersion;
			DotNetPath = ToolPathResolver.GetPathExecutable("dotnet");
		}



		/// <summary>
		/// Gets the current branch that the project is on
		/// </summary>
		/// <returns></returns>
		public string GetCurrentBranch () {
			string cmdArgs = "branch --show-current";
			if (!ExecuteGit(cmdArgs, out List<Output> output)) throw new ApplicationException("GetCurrentBranch::: Git Command failed:  git " + cmdArgs);
			CurrentBranch = output.First().Text;
			return CurrentBranch;
		}



		/// <summary>
		/// Determines if there are any uncommitted changes on the current branch.
		/// </summary>
		/// <returns></returns>
		public bool IsUncommittedChanges () {
			string gitArgs = "update-index -q --refresh";
			if (!ExecuteGit(gitArgs, out List<Output> output)) throw new ApplicationException("IsUncommittedChanges::: Git Command failed:  git " + gitArgs);

			gitArgs = "diff-index --quiet HEAD --";
			if (!ExecuteGit(gitArgs, out output)) throw new ApplicationException("There are uncommited changes on the current branch: " + CurrentBranch +  "  Commit or discard existing changes and then try again.");
			return true;
		}



		/// <summary>
		/// Used to push the app into a Development commit, which means it is tagged with a SemVer tag, such as 2.5.6-alpha1001
		/// </summary>
		public void CommitSemVersionChanges () {
			try {
				string tagName = "Ver" + SemVersion;
				string tagDesc = "Deployed Version:  " + CurrentBranch + "  |  " + SemVersion;

				string gitArgs = "add .";
				if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitVersionChanges:::  .Git Command failed:  git " + gitArgs);

				gitArgs = string.Format("commit -m \"{0} {1}", COMMIT_MARKER, tagDesc);
				if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

				gitArgs = string.Format("tag -a {0} -m \"{1}\"", tagName, tagDesc);
				if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

				gitArgs = "push --set-upstream origin " + CurrentBranch;
				if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

				gitArgs = "push --tags origin";
				if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);
			}
			catch ( Exception e ) {
				PrintGitHistory();
				throw e;
			}
		}




		/// <summary>
		/// Performs the transition from a Development branch to a Master branch
		/// </summary>
		public void CommitMasterVersionChanges()
		{
			string gitArgs;

			try {
				// First we need to checkout master and merge it.
				if ( CurrentBranch != "master" ) {
					gitArgs = "checkout master";
					if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitMasterVersionChanges:::  .Git Command failed:  git " + gitArgs);

					gitArgs = string.Format("merge {0} --no-ff --no-edit -m \"Merging Branch: {0}\"", CurrentBranch);
					if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitMasterVersionChanges:::  .Git Command failed:  git " + gitArgs);
				}

				// Update the versions file with latest info
				ProcessVersionsFile();

				string tagName = "Ver" + Version;
				string tagDesc = "Deployed Version:  " + CurrentBranch + "  |  " + Version;

				gitArgs = "add .";
				if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitVersionChanges:::  .Git Command failed:  git " + gitArgs);

				gitArgs = string.Format("commit -m \"{0} {1}", COMMIT_MARKER, tagDesc);
				if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

				gitArgs = string.Format("tag -a {0} -m \"{1}\"", tagName, tagDesc);
				if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

				gitArgs = "push origin ";
				if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

				gitArgs = "push --tags origin";
				if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

				// Delete the Feature Branch
				if ( CurrentBranch != "master" ) {
					// See if branch exists on origin.  If not we expect an error below.
					gitArgs = "ls-remote --exit-code --heads origin " + CurrentBranch;
					bool bErrorIsExpected = false;
					if ( !ExecuteGit_NoOutput(gitArgs) ) bErrorIsExpected = true;

					gitArgs = "branch -d " + CurrentBranch;
					if ( !ExecuteGit_NoOutput(gitArgs) ) 
							throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

					gitArgs = "push origin --delete " + CurrentBranch;
					if ( !ExecuteGit_NoOutput(gitArgs) ) 
						if (!bErrorIsExpected)
							throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);
					Logger.Success("The previous 2 commits will issue errors if the local branch was never pushed to origin.  They can be safely ignored.");
				}
			}
			catch (Exception e)
			{
				PrintGitHistory();
				throw e;
			}
		}

		/// <summary>
		/// Executes the Git Command, returning ONLY true or false to indicate success or failure
		/// </summary>
		/// <param name="cmdArguments"></param>
		/// <returns></returns>
		private bool ExecuteGit_NoOutput (string cmdArguments) {
			string command = "git";

			// Log it
			Output output =new Output();
			output.Text = GIT_COMMAND_MARKER +  command + " " + cmdArguments;
			GitCommandOutputHistory.Add(output);

			IProcess process = ProcessTasks.StartProcess(command, cmdArguments, RootDirectory, logOutput: true);
			process.AssertWaitForExit();

			// Copy output to history.
			GitCommandOutputHistory.AddRange(process.Output);

			if (process.ExitCode != 0) return false;
			return true;
		}


		/// <summary>
		/// Executes the requested Git Command AND returns the output.  Returns True on success, false otherwise
		/// </summary>
		/// <param name="cmdArguments"></param>
		/// <param name="output"></param>
		/// <returns></returns>
		private bool ExecuteGit (string cmdArguments, out List<Output> output) {
			string command = "git";


			// Log it
			Output outputCmd = new Output();
			outputCmd.Text = GIT_COMMAND_MARKER + command + " " + cmdArguments;
			GitCommandOutputHistory.Add(outputCmd);

			IProcess process = ProcessTasks.StartProcess(command, cmdArguments, RootDirectory, logOutput: true);
			process.AssertWaitForExit();
			output = process.Output.ToList();

			// Copy output to history.
			GitCommandOutputHistory.AddRange(process.Output);

			if ( process.ExitCode != 0 ) return false;
			return true;
		}


		/// <summary>
		/// Processes the Versions file, Updating it to the latest from the current Git Branch if they are not the same.
		/// </summary>
		/// <returns></returns>
		public string ProcessVersionsFile () {
			string fileName = RootDirectory / VERSIONS_FILENAME;
			string [] versionLines = File.ReadAllLines(fileName);
			List<string> versionList = new List<string>(versionLines);

			// If file is too big, then reduce to minimum size
			if ( versionList.Count > VERSION_HISTORY_LIMIT ) {
				int toRemove = versionList.Count - VERSION_HISTORY_TO_KEEP;
				versionList.RemoveRange(0,toRemove);
			}


			// Get the last record, which is the latest Version
			string latestFileVersion = "0.0.0";
			if (versionList.Count != 0)
				latestFileVersion = versionList.Last();

			
			// Now use GitVersion to get latest version as GitVersion sees it.
			string gvLatestVersion = _gitVersion.MajorMinorPatch;
			string gvLatestSemVer = _gitVersion.SemVer;
			

			// Write latest version to file if not the same as the current last entry.
			string gvFull = gvLatestVersion + "|" + gvLatestSemVer;
			if ( gvFull != latestFileVersion ) {
				versionList.Add(gvFull);
				File.WriteAllLines(fileName, versionList.ToArray());
			}

			Version = gvLatestVersion;
			SemVersion = gvLatestSemVer;

			return versionList.Last();
		}


		/// <summary>
		/// Prints the history of the Git commands to the console.
		/// </summary>
		private void PrintGitHistory () {
			
			Console.WriteLine("");
			Console.WriteLine("Git Command Execution History is below for debugging purposes",Color.DeepSkyBlue);
			foreach ( Output line in GitCommandOutputHistory ) {
				if ( line.Text.StartsWith(GIT_COMMAND_MARKER) ) 
					Console.WriteLine("  " + line.Text.Substring(1), Color.Orange);
				else 
					Console.WriteLine("     "  + line.Text,Color.DarkKhaki);
				
			}

		}
	}
}
