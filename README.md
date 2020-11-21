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





