# SlugNuke
A customized (Opinionated) Nuke Build app that provides the following capabilities:

- Setup Target, for converting existing solutions into a format required by this build process.
- GitVersion integration
- Automatic 
- Can deploy to Nuget targets or file locations
- Can process multiple nuget target projects automatically
- Can automatically "Move a branch" to master, make all the commits and push at same time


## Setup
The setup target will take an existing solution target and convert it to the proper format for this build process.  It will perform the following:

- Create a /src folder if it does not exist
- Create a /tests folder if it does not exist
- Create a /artifacts folder if it does not exist
- Create a .nuke file with the proper solution entry so it can be processed by Nuke
- Create a nukeSolutionBuild.conf file which contains information about the solution and how to build the projects.

The app should be run from the root of the Git Project folder (where the .git folder is located) or specified with the --root argument.  It will then scan for the .sln file including all subdirectories.  Once found it will process the solution file and identify all the projects.  It will further attempt to identify the Testing projects (see below).  It will move all non testing projects underneath the /src folder and all testing projects to the /tests folder.  It will update the solution file and save it.

Test projects are identified in one of 2 ways.  First it will look at the .csproj file and determine if it contains a Microsoft.NET.Test.Sdk entry.  If so, it will be considered a Test Project.  Second, it will look to see if the project starts with the word Test or ends with the word Test.






