using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NukeConf {

	public enum CustomNukeConfigEnum {
		None = 0,
		Nuget = 1,
		Copy = 2
	}


	public class CustomNukeSolutionConfig {
		public string DeployRoot { get; set; }

		public List<Project> Projects { get; set; }


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
			options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
			options.WriteIndented = true;
			return options;
		}


		public string Serialize () {
			JsonSerializerOptions options = new JsonSerializerOptions();
			options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
			options.WriteIndented = true;
			return JsonSerializer.Serialize(this, options);
		}


		public static CustomNukeSolutionConfig Deserialize (string json) {
			JsonSerializerOptions options = new JsonSerializerOptions();
			options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
			return JsonSerializer.Deserialize<CustomNukeSolutionConfig>(json, options);
		}
	}


	public class Project {
		public string Name { get; set; }

		public CustomNukeConfigEnum Deploy { get; set; }
	}
}