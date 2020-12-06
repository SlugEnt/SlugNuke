# SlugNuke
A customized (Opinionated) Nuke Build app that provides the following capabilities:

- Setup Target, for converting existing solutions into a format required by this build process.
- GitVersion integration
- Automatic 
- Can deploy to Nuget targets or file locations
- Can process multiple nuget target projects automatically
- Can automatically "Move a branch" to master, make all the commits and push at same time


## Important Information
- Any project that starts with the word TEST or ends with the word TEST (case insensitive) will be considered a Unit Test project and moved to the /tests root and classified as a test project.
- Any time you add a new project to the solution you need to re-run setup.
- If you remove a project from a solution you need to manually remove it from the nukeSolutionBuild.conf file.
- Use the [ExcludeFromCodeCoverage] attribute to exclude classes or methods from Code Coverage Results.
- It is recommended to use Source Link for Libraries so that users can debug into them from their programs.  See SourceLink Below
- You must add the package coverlet.collector to all Test projects in order to use code coverage

## Setup
The setup target will take an existing solution target and convert it to the proper format for this build process.  It will perform the following:

- Create a /src folder if it does not exist
- Create a /tests folder if it does not exist
- Create a /artifacts folder if it does not exist
- Create a .nuke file with the proper solution entry so it can be processed by Nuke
- Create a nukeSolutionBuild.conf file which contains information about the solution and how to build the projects.

The app should be run from the root of the Git Project folder (where the .git folder is located) or specified with the --root argument.  It will then scan for the .sln file including all subdirectories.  Once found it will process the solution file and identify all the projects.  It will further attempt to identify the Testing projects (see below).  It will move all non testing projects underneath the /src folder and all testing projects to the /tests folder.  It will update the solution file and save it.

C:\dev\projectA>  dotnet <path_to_slugnuke>\slugnuke.dll --target Setup --root

Test projects are identified by look for projects that start or end in Test.

After the app has run you will need to open the solution and reconnect the test projects to the code projects, since they were possibly moved.

## Arguments
There are a number of arguments that you will likely use:
 - Compile :  Compiles all projects in the solution
 - Publish :  Runs the full cycle for all projects of the solution.  This is considered a Test publish, meaning versions are tagged with SemVer versions.This is typically the Compile, Test, Pack and Publish steps.  
 - PublishProd :  Same as publish, but this is considered the Production publish, so just MajorMinorPatch versions.
 - Configuration:  Debug | Release.  If not specified, then if this is being built locally it will default to Debug.  Also PublishProd will default this to Release, unless it was specified on command line.
 - SkipNuget : Will skip the actual deployment to a Nuget Repository.  
 - Skip : Will skip all targets not explicitly delared on command line.
 
## Assumptions
This app makes assumptions about the development cycle.  Basically, all Production is committed to master.  All development occurs on Develop, Feature, Fix branches.  You test on those branches, optionally pushing these to Nuget as alpha packages.  Eventually the build is good and you are ready to move to production.  Final commits and builds are made, the development branch is deleted.  

### Example Development Cycle
Git Branch         Git Version      Explanation

master            0.1.0             Starting point.  

Feature/testA     0.2.0-testA0001   Begin development work.  Compile, build, test, No Nuke Build yet, no Commits

Repeat 10 times   0.2.0-testA0001   We have not run the Nuke Process yet, so no change to versions

Feature/testA     0.2.0-testA0002   Commit Code version bumps to 0.2.0-testA0002

Feature/testA     0.2.0-testA0003   Develop, finally commit, version bumps.

#### Ready for Nuke build, to push to test systems and nuget repo as an alpha style release
slugNuke Publish
Feature/testA     0.2.0-TestA0004   Pushed to nuget.

Feature/testA     0.2.0-TestA0005   more changes committed.

#### Final Test push
slugnuke Publish

Feature/testA     0.2.0-TestA0006   

#### Push to Production
slugnuke PublishMaster

Feature/testA     0.2.0 
Feature/testA                   More Development



## SourceLink
Official Documentation is here:  [SourceLink](https://github.com/dotnet/sourcelink)

You need to add the following section to those projects that you are pushing to nuget repositories.
```
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup>
    <TargetFramework>netcoreapp2.1</TargetFramework>
 
    <!-- Optional: Publish the repository URL in the built .nupkg (in the NuSpec <Repository> element) -->
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
 
    <!-- Optional: Embed source files that are not tracked by the source control manager in the PDB -->
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
  
    <!-- Optional: Build symbol package (.snupkg) to distribute the PDB containing Source Link -->
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  </PropertyGroup>
  <ItemGroup>
    <!-- Add PackageReference specific for your source control provider (see below) --> 
  </ItemGroup>
</Project>
```

You then need to add the following to the ItemGroup Section Depending on your repository:
##### GitHub
```
<ItemGroup>
  <PackageReference Include="Microsoft.SourceLink.GitHub" Version="1.0.0" PrivateAssets="All"/>
</ItemGroup>
```

##### BitBucket Local Repo
```
<ItemGroup>
  <PackageReference Include="Microsoft.SourceLink.Bitbucket.Git" Version="1.0.0" PrivateAssets="All"/>
</ItemGroup>
```
