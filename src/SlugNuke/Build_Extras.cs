﻿using System;
using System.Collections.Generic;
using System.Text;
using Nuke.Common.ProjectModel;


	public partial class Build 
	{

		/// <summary>
		/// Returns the Nuke or visual Studio Project that corresponds to the NukeConf Project.
		/// </summary>
		/// <param name="confProject">NukeConf Project that you want to retrieve from the Nuke Solution</param>
		/// <returns></returns>
		public Project GetSolutionProject (NukeConf.Project confProject)
		{
			string fullName = this.SourceDirectory / confProject.Name / confProject.Name + ".csproj";
			return Solution.GetProject(fullName);
		}
	}
