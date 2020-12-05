using System;
using System.Collections.Generic;
using System.Drawing;
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


		/// <summary>
		/// Checks to ensure that if any of the projects have a Deploy method of Copy that the DeployRoot folders are specified.
		/// </summary>
		/// <returns></returns>
		public bool CheckRootFolders () {
			bool hasCopyMethod = false;

			foreach ( Project project in Projects ) {
				if ( project.Deploy == CustomNukeDeployMethod.Copy ) hasCopyMethod = true;
			}

			// If no projects require a root folder then it is ok.
			if ( !hasCopyMethod ) return true;

			// Ensure Deploy Roots have values if at least one of the projects has a deploy method of Copy
			for (int i = 0; i< 2; i++ ) {
				Configuration config;

				if (i == 0 ) {
					config = Configuration.Release;
				}
				else {
					config = Configuration.Debug;
				}

				if (!IsRootFolderSpecified(config))
				{
					Console.WriteLine("There are 1 or more projects with a Deploy method of Copy, but no Deploy Root folders have been specified.");
					return false;
				}
			}

			return true;
		}
	}


	public class Project {
		public string Name { get; set; }

		public CustomNukeDeployMethod Deploy { get; set; }

		public string Framework { get; set; }

		public bool IsTestProject { get; set; }
	}
}