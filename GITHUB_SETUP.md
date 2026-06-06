# GitHub Setup

This workspace is prepared for GitHub, but it has not been initialized as a Git repository yet.

## Initialize Locally

```powershell
git init
git add .gitignore .github README.md CHANGELOG.md SECURITY.md RELEASE_NOTES-v1.0.md BetterTaskManager.slnx docs scripts src
git commit -m "Release Better Task Manager v1.0.0"
git tag v1.0.0
```

## Connect To GitHub

Create an empty GitHub repository, then run:

```powershell
git remote add origin https://github.com/YOUR-USER/YOUR-REPO.git
git branch -M main
git push -u origin main
git push origin v1.0.0
```

## Release Asset

Upload this zip to the GitHub release:

```text
outputs\BetterTaskManager-v1.0-win-x64.zip
```

