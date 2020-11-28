using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nuke.Common.ProjectModel;

namespace NukeConf {
	/// <summary>
	/// The method used to deploy a given package.
	/// </summary>
	public enum CustomNukeDeployMethod {
		/// <summary>
		/// Not deployed
		/// </summary>
		None = 0,

		/// <summary>
		/// Is a nuget package and will be deployed to a nuget repository.
		/// </summary>
		Nuget = 1,

		/// <summary>
		/// Is copied to a deployment folder location
		/// </summary>
		Copy = 2
	}


	/// <summary>
	/// Contains information about the solution and projects that SlugNuke needs in order to build and publish the projects.
	/// </summary>
	public class CustomNukeSolutionConfig {
		/// <summary>
		/// The root folder to deploy Production (Release) to
		/// </summary>
		public string DeployProdRoot { get; set; }

		/// <summary>
		/// The root folder to deploy Test (Debug) to
		/// </summary>
		public string DeployTestRoot { get; set; }

		/// <summary>
		/// If true, it will deploy to a subfolder with the name of the Version tag - Ver#.#.#. If False, no subfolder is created.
		/// </summary>
		public bool DeployToVersionedFolder { get; set; } = true;


		/// <summary>
		/// If true the DeployToVersionedFolder uses a full SemVer, Ie. Ver#.#.#-xyz
		/// </summary>
		public bool DeployFolderUsesSemVer { get; set; } = true;


		/// <summary>
		/// If true, the name of the project (Full Namespace name) will be used with every . in the name being a new subfolder.
		/// So:  MySpace.MyApp.SubApp would be deployed to a folder DeployRoot\Prod\MySpace\MyApp\SubApp\MySpace.MyApp.SubApp\Ver#.#.#
		/// </summary>
		public bool DeployToAssemblyFolders { get; set; } = false;


		/// <summary>
		/// Projects in the solution
		/// </summary>
		public List<Project> Projects { get; set; }


		/// <summary>
		/// Constructor
		/// </summary>
		public CustomNukeSolutionConfig () {
			Projects = new List<Project>();
		}

		/// <summary>
		/// Returns the project with the given name
		/// </summary>
		/// <param name="name"></param>
		/// <returns></returns>
		public Project GetProjectByName (string name) { return Projects.FirstOrDefault(project => project.Name == name); }


		public static JsonSerializerOptions SerializerOptions () {
			JsonSerializerOptions options = new JsonSerializerOptions();
			options.Converters.Add(new JsonStringEnumConverter());
			options.WriteIndented = true;
			return options;
		}


		/// <summary>
		/// Validates that the DeployRoot folder based upon the current config is set to a value.
		/// </summary>
		/// <param name="config"></param>
		/// <returns></returns>
		public bool IsRootFolderSpecified (Configuration config) {
			if ( config == "Release" ) {
				if ( String.IsNullOrEmpty(DeployProdRoot) ) return false;
			}
			else if (String.IsNullOrEmpty(DeployTestRoot)) return false;
			return true;
		} 

	}


	public class Project {
		public string Name { get; set; }

		public CustomNukeDeployMethod Deploy { get; set; }

		public string Framework { get; set; }
	}
}