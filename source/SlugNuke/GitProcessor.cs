using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.GitVersion;

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

		internal string DotNetPath { get; set; }
		internal AbsolutePath RootDirectory;
		internal GitVersion _gitVersion;

		/// <summary>
		/// The Branch that the repository is currently on
		/// </summary>
		public string CurrentBranch { get; set; }

		public string Version { get; set; }
		public string SemVersion { get; set; }


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
			if (!ExecuteGit(cmdArgs, out List<Output> output)) throw new ApplicationException("Git Command failed:  git " + cmdArgs);
			CurrentBranch = output.First().Text;
			return CurrentBranch;
		}



		public bool IsUncommittedChanges () {
			string gitArgs = "update-index -q --refresh";
			if (!ExecuteGit(gitArgs, out List<Output> output)) throw new ApplicationException("Git Command failed:  git " + gitArgs);

			gitArgs = "diff-index --quiet HEAD --";
			if (!ExecuteGit(gitArgs, out output)) throw new ApplicationException("There are uncommited changes on the current branch: " + CurrentBranch + "Git Command failed:  git " + gitArgs + "  Commit or discard existing changes and then try again.");
			return true;
		}



		/// <summary>
		/// Used to push the app into a Development commit, which means it is tagged with a SemVer tag, such as 2.5.6-alpha1001
		/// </summary>
		public void CommitSemVersionChanges () {
			string tagName = "Ver" + SemVersion;
			string tagDesc = "Deployed Version:  " + CurrentBranch + "  |  " + SemVersion;

			string gitArgs = "add .";
			if (!ExecuteGit_NoOutput(gitArgs)) throw new ApplicationException("CommitVersionChanges:::  .Git Command failed:  git " + gitArgs);

			gitArgs = "commit -m " + COMMIT_MARKER + " " + tagDesc;
			if (!ExecuteGit_NoOutput(gitArgs)) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

			gitArgs = "tag -a " + tagName + " -m " + tagDesc;
			if (!ExecuteGit_NoOutput(gitArgs)) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

			gitArgs = "push --set-upstream origin " + CurrentBranch;
			if (!ExecuteGit_NoOutput(gitArgs)) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

			gitArgs = "push --tags origin";
			if (!ExecuteGit_NoOutput(gitArgs)) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);
		}




		/// <summary>
		/// Used to push the app into a Development commit, which means it is tagged with a SemVer tag, such as 2.5.6-alpha1001
		/// </summary>
		public void CommitMasterVersionChanges()
		{
			string tagName = "Ver" + Version;
			string tagDesc = "Deployed Version:  " + CurrentBranch + "  |  " + Version;


			// First we need to checkout master and merge it.
			string gitArgs = "checkout master";
			if (!ExecuteGit_NoOutput(gitArgs)) throw new ApplicationException("CommitMasterVersionChanges:::  .Git Command failed:  git " + gitArgs);

			gitArgs = "merge " + CurrentBranch + " --no-ff --no-edit -m " + "Merging Branch: " + CurrentBranch;
			if (!ExecuteGit_NoOutput(gitArgs)) throw new ApplicationException("CommitMasterVersionChanges:::  .Git Command failed:  git " + gitArgs);

			gitArgs = "add .";
			if (!ExecuteGit_NoOutput(gitArgs)) throw new ApplicationException("CommitVersionChanges:::  .Git Command failed:  git " + gitArgs);

			gitArgs = "commit -m " + COMMIT_MARKER + " " + tagDesc;
			if (!ExecuteGit_NoOutput(gitArgs)) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

			gitArgs = "tag -a " + tagName + " -m " + tagDesc;
			if (!ExecuteGit_NoOutput(gitArgs)) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

			gitArgs = "push origin ";
			if (!ExecuteGit_NoOutput(gitArgs)) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

			gitArgs = "push --tags origin";
			if (!ExecuteGit_NoOutput(gitArgs)) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

			// Delete the Feature Branch
			gitArgs = "branch -d " + CurrentBranch;
			if (!ExecuteGit_NoOutput(gitArgs)) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

			gitArgs = "push origin --delete " + CurrentBranch;
			if (!ExecuteGit_NoOutput(gitArgs)) throw new ApplicationException("CommitVersionChanges:::   .Git Command failed:  git " + gitArgs);

		}

		/// <summary>
		/// Executes the Git Command, returning ONLY true or false to indicate success or failure
		/// </summary>
		/// <param name="cmdArguments"></param>
		/// <returns></returns>
		private bool ExecuteGit_NoOutput (string cmdArguments) {
			string command = "git";
			IProcess process = ProcessTasks.StartProcess(command, cmdArguments, RootDirectory, logOutput: false);
			process.AssertWaitForExit();
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
			IProcess process = ProcessTasks.StartProcess(command, cmdArguments, RootDirectory, logOutput: true);
			process.AssertWaitForExit();
			output = process.Output.ToList();
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
			string latestFileVersion = versionList.Last();

			
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
	}
}
