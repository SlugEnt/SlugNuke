using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using Nuke.Common;
using Nuke.Common.Tooling;
using Console = Colorful.Console;


namespace SlugNuke
{
	public static class Utility
	{
		const string ENV_GITVERSION = "GITVERSION_EXE";

		
		/// <summary>
		/// Ensures that GitVersion has a system wide environment variable set.  If not it will attempt to locate it and set the environment variable.
		/// </summary>
		/// <param name="targetEnvironment">Whether to target user or system/machine setting.  You must run the app as administrator to use Machine.</param>
		/// <returns></returns>
		public static bool ValidateGetVersionEnvVariable(EnvironmentVariableTarget targetEnvironment = EnvironmentVariableTarget.Process)
		{
			string envGitVersion = Environment.GetEnvironmentVariable(ENV_GITVERSION);


			if (envGitVersion == null)
			{
				Logger.Warn("GitVersion environment variable not found.  Will attempt to set.");

				string cmd = "where";
				string cmdArgs = "gitversion.exe";

				IProcess process = ProcessTasks.StartProcess(cmd, cmdArgs,logOutput: true);
				process.AssertWaitForExit();
				ControlFlow.Assert(process.ExitCode == 0, "The " + ENV_GITVERSION + " environment variable is not set and attempt to fix it, failed because it appears GitVersion is not installed on the local machine.  Install it and then re-run and/or set the environment variable manually");

				// Set the environment variable now that we found it
				string value = process.Output.First().Text;
				Environment.SetEnvironmentVariable(ENV_GITVERSION,value,targetEnvironment);
				envGitVersion = Environment.GetEnvironmentVariable(ENV_GITVERSION);
				string val = ToolPathResolver.TryGetEnvironmentExecutable("GITVERSION_EXE");
				Console.WriteLine("Toolpathresolver: " + val);
				Console.WriteLine();
				string msg =
					"GitVersion Environment variable has been set!  You will need to ensure you close the current console window before continuing to pickup the change.";
				Console.WriteWithGradient(msg, Color.Fuchsia, Color.Yellow, 16);
				Console.ReplaceAllColorsWithDefaults();
			}

			return true;
		}
	}
}
