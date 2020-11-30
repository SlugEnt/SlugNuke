using System;
using System.Collections.Generic;
using System.Text;

namespace SlugNuke
{
	/// <summary>
	/// Used for storing the result of a single Publish Operation.
	/// </summary>
	public class PublishResult
	{
		public string NameOfProject { get; set; }
		public string DeployMethod { get; set; }
		public string DeployName { get; set; }


		public PublishResult (string nameOfProject, string deployMethod, string deployTarget) {
			NameOfProject = nameOfProject;
			DeployMethod = deployMethod;
			DeployName = deployTarget;
		}
	}
}
