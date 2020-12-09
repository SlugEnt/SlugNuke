using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Nuke.Common;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;


public partial class Build 
	{

		/// <summary>
		/// Returns the Nuke or visual Studio Project that corresponds to the NukeConf Project.
		/// </summary>
		/// <param name="confProject">NukeConf Project that you want to retrieve from the Nuke Solution</param>
		/// <returns></returns>
		public Project GetSolutionProject (NukeConf.Project confProject)
		{
			string fullName = SourceDirectory / confProject.Name / confProject.Name + ".csproj";
			return Solution.GetProject(fullName);
		}


	
}

