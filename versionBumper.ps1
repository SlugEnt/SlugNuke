Function Add-GitVersionInfo {
	<#
	.Synopsis
		Updates version information from the current branch into Git Commit messages.  During a non Master commit the following will happen:
		 - 
		If the -Master flag is set then a much larger set of functionality is invoked:
		 - A Git Tag commit occurs, which stamps the current version into a tag with a custom tag descriptor
		 - The branch and the tags are all pushed to the origin server.
	#> 
	[cmdletbinding()] 
	param (
		[switch[]]$Master
	)


	# Global Variables
	$curDir = Get-Location
	$fileName = "versions.txt"
	$fullFile = "$curDir\$fileName"
	$countThreshold = 15
	$skipThreshold = 10
	$shouldCommit = $true
	$specialCommitMarker = "|^|"
	$curBranch = ""


	######################
	#  Gets the current Git branch
	######################
	Function GetCurrentBranch {
		# Get Current Branch Name
		$branch = (git branch --show-current)
		Write-Host "Current Branch:  $branch" -foregroundcolor "Green"
		$branch
	}


	######################
	#  Process the Versions File.  Purging old entries if needed and adding new Version info if it does not match latest entry.
	#  Returns a Variable with LatestVersion and LatestSemVer properties.
	######################
	Function ProcessVersionsFile ([switch]$update) {
		Get-Content $fullFile | Measure-Object -Line -outvariable output | Out-Null
		$lines = $output.Lines
		if ($lines -gt $countThreshold) {
		  Write-Host "Purging beginning of file due to file size" -foregroundcolor "cyan"
		  $newFile = Get-Content $fullFile | Select-Object -Skip $skipThreshold 
		  $newFile | Out-File $fullFile
		}


		# Read latest version from file
		$previousVersion = Get-Content $fullFile -Tail 1 
		Write-Host "Previous Version Committed:  $previousVersion" -foregroundcolor "Cyan"


		# Get the Latest Version info from GitVersion
		$latestVersion = GitVersion /showvariable MajorMinorPatch
		$latestSemVer = GitVersion /showvariable SemVer


		$rc = [PSCustomObject]@{
			LatestVersion = $latestVersion
			LatestSemVer = $latestSemVer
		}

		# Write NewVersion to file if not the same as prio
		$newVersion = "$latestVersion|$latestSemVer"
		if ($update) {
			if ($newVersion -ne $previousVersion) {
				$newVersion >> versions.txt
			}
		}
		return $rc
	}

	 



	########################
	###### Script Start
	########################
	Write-Host ""
	Write-Host ""
	Write-Host "Master Switch:  $Master"


	# A.  Get Current Branch
	$curBranch = GetCurrentBranch
	Write-Host ""


	# B.  Determine if there are any changes that need to be committed.  If there are the user needs to fix before we can continue
	git update-index -q --refresh
	git diff-index --quiet HEAD --
	if (!$?) {
		Write-Host ""
		Write-Host "Error:  There are uncommitted changes in the the current branch: $curBranch.  These must be committed or discarded before this script can continue" -foregroundcolor "Red"
		Return 100
	}





	# E.  If doing a mid stream update (Non Master), then commit version and exit
	if (! $Master) {
		# E.1  Start the NUKE build process
		nuke Publish
		if (!$?) {
			Write-Host ""
			Write-Host "Errors during the Nuke Building process for Non-Master branch occurrred.  Cannot continue.  Check for errors in Nuke process in above messages and correct."
			return 400;
		}

		
		# C.  Update the Versions folder with latest.
		$versions = ProcessVersionsFile -update:$true;
		Write-Host "Latest Versions:                $($versions.LatestVersion)  -->  $($versions.LatestSemVer)" -foregroundcolor "Cyan"
		Write-Host
		Write-Host


		# At this point the only change in the Commit tree should be the versions.txt file.  We will commit it 
		# with a custom tag name and then commit a Version Tag
		# We will commit everything, create the tag and then push everything upstream.
		if ($shouldCommit -eq $true) {
			Write-Host "Committing a Version Commit" -Foregroundcolor "Magenta"
			$tagName = "Ver$($versions.LatestSemVer)"
			$tagDesc = "Deployed Version:  $curBranch  |  $($versions.LatestSemVer)"
			git add .
			git commit -m "$specialCommitMarker $tagDesc"
  
			git tag -a $tagName -m $tagDesc
			git push --set-upstream origin $curBranch 
			git push --tags origin
		}

		if (!$?) { 
			Write-Host ""
			Write-Host "ERROR:  Problems adding Version Commit and Tag."
			Return 500
		}
	}


	# F.  Is a Master push, so we needs to: Checkout master, Merge the current branch into master, Push and then delete the feature branch
	else {
		# F.1.  Swap to master branch so we can build production version
			$commitMsg = "Merging branch: $curBranch"
			git checkout master
			git merge $curBranch --no-ff  --no-edit -m $commitMsg

			$versions = ProcessVersionsFile -update:$true;

		# F.2.  Everything looks good.  commit
			$tagName = "Ver$($versions.LatestVersion)"
			$tagDesc = "Deployed Version:  $curBranch  |  $($versions.LatestVersion)"
			git add .
			git commit -m "$specialCommitMarker $tagDesc"
  
			git tag -a $tagName -m $tagDesc
			git push origin
			git push --tags origin

			# Finally, cleanup the feature branch 
			git branch -D $curBranch
			git push origin --delete $curBranch



		# F.3.  Nuke Build the apps
			nuke Publish
			if (!$?) {
				Write-Host ""
				Write-Host "ERROR:  Nuke Master Build process failed.  Check Nuke Process messages for details."
				Return 600
			}

	}
	
	
		# Closing

		Write-Host "Latest Version:  $($versions.LatestVersion) "
		Write-Host "Latest SemVer:   $($versions.latestSemVer)"
		Write-Host "Git Tagged:      $tagDesc"
	
}

Write-Host "Args:  $args"
if ($args[0] -eq "-Master") {
	Add-GitVersionInfo -Master:$true }
else {
	Add-GitVersionInfo -Master:$false }