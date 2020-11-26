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
using Octokit;
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
		
		/// <summary>
		/// The Branch that the repository is currently on
		/// </summary>
		public string CurrentBranch { get; set; }
		public string Version { get; set; }
		public string SemVersion { get; set; }
		public string GitTagName { get; private set; }
		public string GitTagDesc { get; private set; }

		List<string> _versionList = new List<string>();


		/// <summary>
		/// Returns True if the current branch is the Main Branch.
		/// </summary>
		/// <returns></returns>
		public bool IsCurrentBranchMainBranch () { return IsMainBranch(CurrentBranch);}


		/// <summary>
		/// Returns True if the given branch name is considered the Main branch.
		/// </summary>
		/// <param name="branchName"></param>
		/// <returns></returns>
		public static bool IsMainBranch(string branchName) {
			string lcBranch = branchName.ToLower();
			if ( lcBranch == "master" || lcBranch == "main" )
				return true;
			else
				return false;
		}


		/// <summary>
		/// Will tell you if the Version had been previously committed to Git.  This means that we are possibly only doing these steps to get to a later
		/// step (such as pack or publish) that might have previously failed for some reason (Bad password, userId, path, etc)
		/// </summary>
		public bool WasVersionPreviouslyCommitted { get; private set; }


		/// <summary>
		/// Keeps track of all of the Git Command output for debugging purposes.
		/// </summary>
		public List<Output> GitCommandOutputHistory = new List<Output>();


		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="rootPath"></param>
		/// <param name="gitVersion"></param>
		public GitProcessor (AbsolutePath rootPath) {
			RootDirectory = rootPath;
			DotNetPath = ToolPathResolver.GetPathExecutable("dotnet");

			IdentifyMainBranch();
			Fetch_GitVersion();
			PrintGitCommandVersion();
		}


		/// <summary>
		/// Prints the current version of the Git Command being used.  Version is only shown when PrintGitHistory is called.
		/// </summary>
		private void PrintGitCommandVersion () {
			List<Output> gitOutput;

			try {
				string gitArgs = "--version";
				ControlFlow.Assert(ExecuteGit(gitArgs, out gitOutput) == true, "PrintGitCommandVersion:::  .Git Command Failed:  git " + gitArgs);
			}
			catch (Exception e) { 
				PrintGitHistory();
				throw e;
			}
		}



		/// <summary>
		/// Gets the current branch that the project is on
		/// </summary>
		/// <returns></returns>
		public string GetCurrentBranch () {
			try { 
				string cmdArgs = "branch --show-current";
				if (!ExecuteGit(cmdArgs, out List<Output> output)) throw new ApplicationException("GetCurrentBranch::: Git Command failed:  git " + cmdArgs);
				CurrentBranch = output.First().Text;
				return CurrentBranch;
			}
			catch (Exception e)
			{
				PrintGitHistory();
				throw e;
			}

		}



		/// <summary>
		/// Determines if there are any uncommitted changes on the current branch.
		/// </summary>
		/// <returns></returns>
		public bool IsUncommittedChanges () {
			try { 
				string gitArgs = "update-index -q --refresh";
				if (!ExecuteGit_NoOutput(gitArgs)) throw new ApplicationException("IsUncommittedChanges::: Git Command failed:  git " + gitArgs);

				gitArgs = "diff-index --quiet HEAD --";
				if (!ExecuteGit_NoOutput(gitArgs)) throw new ApplicationException("There are uncommited changes on the current branch: " + CurrentBranch +  "  Commit or discard existing changes and then try again.");
				return true;
			}
			catch (Exception e)
			{
				PrintGitHistory();
				throw e;
			}

		}



		/// <summary>
		/// Used to push the app into a Development commit, which means it is tagged with a SemVer tag, such as 2.5.6-alpha1001
		/// </summary>
		public void CommitSemVersionChanges () {
			try {
				GitTagName = "Ver" + SemVersion;
				GitTagDesc = "Deployed Version:  " + PrettyPrintBranchName(CurrentBranch) + "  |  " + SemVersion;

				string gitArgs = "add .";
				if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitVersionChanges:::  .Git Command failed:  git " + gitArgs);

				gitArgs = string.Format("commit -m \"{0} {1}", COMMIT_MARKER, GitTagDesc);
				if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

				gitArgs = string.Format("tag -a {0} -m \"{1}\"", GitTagName, GitTagDesc);
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
		/// This is the initial Main Version Commit Transition logic.
		/// </summary>
		public void MainVersionCheckout () {
			string gitArgs;
			string commitErrStart = "MainVersionCheckout:::  Git Command Failed:  git ";

			List<Output> gitOutput;

			try {
				// First we need to checkout master and merge it.
				if ( !IsCurrentBranchMainBranch() ) {
					gitArgs = "checkout " + MainBranchName;
					if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitMainVersionChanges:::  .Git Command failed:  git " + gitArgs);

					gitArgs = string.Format("merge {0} --no-ff --no-edit -m \"Merging Branch: {0}   |  {1}\"",  CurrentBranch, GitVersion.MajorMinorPatch);
					if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitMainVersionChanges:::  .Git Command failed:  git " + gitArgs);
				}

				// Read Version info from file.
				ReadVersionsFile(false);

				GitTagName = "Ver" + Version;
				GitTagDesc = "Deployed Version:  " + PrettyPrintBranchName(CurrentBranch) + "  |  " + Version;

				// See if the Tag exists already, if so we will get errors later, better to stop now.
				gitArgs = "describe --tags --abbrev=0";
				ControlFlow.Assert(ExecuteGit(gitArgs, out gitOutput) == true, commitErrStart + gitArgs);
				string latestVer = "";
				
				if ( gitOutput.Count > 0 && gitOutput [0].Text == GitTagName ) {
					WasVersionPreviouslyCommitted = true;
					Logger.Warn("The Git Tag: {0} was previously committed.  We are assuming this is one of 2 things:  1) Just a rebuild of the current branch with no changes.  2) A run to correct a prior error in a later stage.  Certain code sections will be skipped.", GitTagName);
				}
				
			}
			catch (Exception e)
			{
				PrintGitHistory();
				throw e;
			}

		}


		/// <summary>
		/// This is the Main Branch Commit stage
		/// </summary>
		public void CommitMainVersionChanges()
		{
			string gitArgs;
			List<Output> gitOutput;

			// This is not an update, it is a redo of previous run that may have errored or its a clean run, but no changes have been committed.  So we skip this.
			if ( WasVersionPreviouslyCommitted ) return;

			try {
				WriteVersionsFile();

				gitArgs = "add .";
				if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitVersionChanges:::  .Git Command failed:  git " + gitArgs);

				gitArgs = string.Format("commit -m \"{0} {1}", COMMIT_MARKER, GitTagDesc);
				if ( !ExecuteGit(gitArgs, out gitOutput) ) {
					if (!gitOutput.Last().Text.Contains("nothing to commit"))
						throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);
				}

				gitArgs = string.Format("tag -a {0} -m \"{1}\"", GitTagName, GitTagDesc);
				if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

				gitArgs = "push origin ";
				if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

				gitArgs = "push --tags origin";
				if ( !ExecuteGit_NoOutput(gitArgs) ) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

				// Delete the Feature Branch
				if ( CurrentBranch != MainBranchName ) {
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

			IProcess process = ProcessTasks.StartProcess(command, cmdArguments, RootDirectory, logOutput: false);
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

			IProcess process = ProcessTasks.StartProcess(command, cmdArguments, RootDirectory, logOutput: false);
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
		/// <param name="updateVersionFile">If true (Should only be set during Test Builds, not Production) it will also update the versions.txt file</param>
		/// <returns></returns>
		public string ReadVersionsFile (bool updateVersionFile) {
			string fileName = RootDirectory / VERSIONS_FILENAME;
			

			if ( File.Exists(fileName) ) {
				string[] versionLines = File.ReadAllLines(fileName);
				_versionList = new List<string>(versionLines);

				// If file is too big, then reduce to minimum size
				if ( _versionList.Count > VERSION_HISTORY_LIMIT ) {
					int toRemove = _versionList.Count - VERSION_HISTORY_TO_KEEP;
					_versionList.RemoveRange(0, toRemove);
				}
			}

			// Get the last record, which is the latest Version
			string latestFileVersion = "0.0.0";
			if (_versionList.Count != 0)
				latestFileVersion = _versionList.Last();

			
			// Now use GitVersion to get latest version as GitVersion sees it.
			string gvLatestVersion = GitVersion.MajorMinorPatch;
			string gvLatestSemVer = GitVersion.SemVer;
			

			// Write latest version to file if not the same as the current last entry.
			string gvFull = gvLatestVersion + "|" + gvLatestSemVer;
			if ( gvFull != latestFileVersion ) {
				_versionList.Add(gvFull);
			}

			Version = gvLatestVersion;
			SemVersion = gvLatestSemVer;

			if (updateVersionFile) WriteVersionsFile();

			return _versionList.Last();
		}



		private void WriteVersionsFile () {
			string fileName = RootDirectory / VERSIONS_FILENAME;
			File.WriteAllLines(fileName, _versionList.ToArray());
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



		/// <summary>
		/// Sets up and queries the GitVersion.
		/// </summary>
		public void Fetch_GitVersion () {
			GitVersion = GitVersionTasks.GitVersion(s => s
			                                              .SetProcessWorkingDirectory(RootDirectory)
			                                              .SetFramework("netcoreapp3.1")
			                                              .SetNoFetch(false)
			                                              .DisableProcessLogOutput()
			                                              .SetUpdateAssemblyInfo(true))
			                             .Result;
		}


		/// <summary>
		/// Returns the GitVersion object
		/// </summary>
		public GitVersion GitVersion { get; private set; }


		/// <summary>
		/// Determines whether Main or Master is the "main" branch.
		/// </summary>
		private void IdentifyMainBranch () {
			try { 
			string gitArgs = "branch";
			if (!ExecuteGit(gitArgs, out List<Output> output)) throw new ApplicationException("IdentifyMainBranch:::   .Git Command failed:  git " + gitArgs);

			char [] skipChars = new [] {' ', '*'};

			bool found = false;
			foreach ( Output branch in output ) {
				string branchName = branch.Text.TrimStart(skipChars).TrimEnd();
				if ( IsMainBranch(branchName))  {
					if ( found )
						throw new ApplicationException(
							"Appears to be a main and master branch in the repository.  This is not allowed.  Please cleanup the repo so only master or only main exists.");
					found = true;
					MainBranchName = branchName;
				}
			}
			}
			catch (Exception e)
			{
				PrintGitHistory();
				throw e;
			}

		}



		/// <summary>
		/// adds spaces between every slash it finds in the branch name.
		/// </summary>
		/// <param name="branch"></param>
		/// <returns></returns>
		public string PrettyPrintBranchName (string branch) {
			string[] parts = branch.Split('/');
			string newName = "";
			foreach ( string item in parts ) {
				if ( newName != string.Empty )
					newName = newName + " / " + item;
				else
					newName = item;
			}

			return newName;
		}



		/// <summary>
		/// The name of the main branch in the repository.
		/// </summary>
		public string MainBranchName { get; private set; }
	}
}
