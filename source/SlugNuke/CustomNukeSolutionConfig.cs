using System.Collections.Generic;


namespace NukeConf {
	public class CustomNukeSolutionConfig {
		public string DeployRoot { get; set; }

		public List<Project> Projects { get; set; }
	}


	public class Project {
		public string Name { get; set; }

		public string Deploy { get; set; }
	}
}