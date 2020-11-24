using System;
using System.Collections.Generic;
using System.Text;
using Nuke.Common.IO;

namespace SlugNuke
{
	/// <summary>
	/// Represents a Visual Studio Project for the Setup Target Functionality
	/// </summary>
	public class VisualStudioProject {
		public string Name { get; set; }
		public string Namecsproj { get; set; }
		public AbsolutePath OriginalPath { get; set; }
		public AbsolutePath NewPath { get; set; }
		public bool IsTestProject { get; set; }
		public string Framework { get; set; }
		public string DeployType { get; set; }


		
	}
}
