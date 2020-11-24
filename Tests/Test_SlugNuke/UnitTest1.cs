using System.IO;
using Microsoft.Build.Framework;
using Nuke.Common.IO;
using NUnit.Framework;
using SlugNuke;

namespace Test_SlugNuke
{
	public class Tests {
		public string rootPath = @"C:\dev\projects\ProjA";
		
		[SetUp]
		public void Setup()
		{
		}


		[TestCase()]
		[TestCase(@"C:\dev\projects\ProjA",@"A.B\A.B.csproj","A.B",@"C:\dev\projects\ProjA\A.B",@"C:\dev\projects\ProjA\Src\A.B")]
		[Test]
		public void PathTests(string currentSolutionLocation, string currentProjectLoc, string expName, string expOrigPath, string expNewPath) {
			SetupSlugNukeSolution init = new SetupSlugNukeSolution() { 
				RootDirectory = (AbsolutePath) rootPath,
				SourceDirectory = (AbsolutePath) rootPath / "Src",
				CurrentSolutionPath = (AbsolutePath) currentSolutionLocation,
				ExpectedSolutionPath = (AbsolutePath) rootPath / "Src"
			};
		VisualStudioProject project;
			project =  init.GetInitProject(currentProjectLoc);
			Assert.AreEqual(expName,project.Name,"A10: Name different");
			Assert.AreEqual((AbsolutePath) expNewPath,project.NewPath,"A20: New Path different");
			Assert.AreEqual((AbsolutePath) expOrigPath, project.OriginalPath,"A30: Original paths different");
		}
	}
}