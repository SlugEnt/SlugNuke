using System.IO;
using Nuke.Common.IO;
using NUnit.Framework;
using SlugNuke;

namespace Test_SlugNuke
{
	public class Tests {
		public string rootPath = @"C:\dev\projects\ProjA";
		private string projectName = "Project.subname.name.csproj";

		[SetUp]
		public void Setup()
		{
		}


		[TestCase()]
		[TestCase(@"C:\dev\projects\ProjA",@"A.B\A.B.csproj","A.B",@"C:\dev\projects\ProjA\A.B",@"C:\dev\projects\ProjA\Src\A.B")]
		[Test]
		public void PathTests(string currentSolutionLocation, string currentProjectLoc, string expName, string expOrigPath, string expNewPath)
		{
			InitLogic init = new InitLogic();
			init.RootDirectory = (AbsolutePath) rootPath;
			init.SourceDirectory = init.RootDirectory / "Src";
			init.CurrentSolutionPath = (AbsolutePath) currentSolutionLocation;
			init.ExpectedSolutionPath = init.SourceDirectory;

			InitProject project;
			project =  init.GetInitProject(currentProjectLoc);
			Assert.AreEqual(expName,project.Name,"A10: Name different");
			Assert.AreEqual((AbsolutePath) expNewPath,project.NewPath,"A20: New Path different");
			Assert.AreEqual((AbsolutePath) expOrigPath, project.OriginalPath,"A30: Original paths different");
		}
	}
}