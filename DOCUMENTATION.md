# Briech Ground Control Release And Update Documentation

## Overview

This application is configured to fetch app updates from the GitHub repository below:

- `https://github.com/MadakiElisha/BriechGroundControll`

The in-app updater checks GitHub Releases for:

- `version.txt`
- `checksums.txt`
- `ChangeLog.txt`
- `BriechGroundControl.zip`

The release workflow publishes those assets automatically from GitHub Actions.

## Current Update Channels

### Stable channel

Stable builds are pulled from the latest GitHub Release:

- `https://github.com/MadakiElisha/BriechGroundControll/releases/latest/download/version.txt`
- `https://github.com/MadakiElisha/BriechGroundControll/releases/latest/download/checksums.txt`
- `https://github.com/MadakiElisha/BriechGroundControll/releases/latest/download/BriechGroundControl.zip`

### Beta channel

Beta builds are pulled from the fixed prerelease tag:

- `development-build`

Assets expected there:

- `version.txt`
- `checksums.txt`
- `ChangeLog.txt`
- `BriechGroundControl.zip`

## What Was Configured

### App-side updater

The runtime updater is configured in:

- [app.config](C:/Users/Godsmiracle/source/repos/BriechGroundControll/app.config)
- [Utilities/Update.cs](C:/Users/Godsmiracle/source/repos/BriechGroundControll/Utilities/Update.cs)

Important behavior:

- stable now prefers `UpdateLocationZip`
- beta prefers `BetaUpdateLocationZip`
- the updater falls back to the older base URL only if a zip URL is missing

This matters because GitHub Releases is a better fit for a packaged zip asset than for raw per-file hosting.

### Release automation

The GitHub release workflow is in:

- [.github/workflows/main.yml](C:/Users/Godsmiracle/source/repos/BriechGroundControll/.github/workflows/main.yml)

It now publishes branded release files:

- `BriechGroundControl.zip`
- `BriechGroundControl.msi`
- `checksums.txt`
- `version.txt`
- `ChangeLog.txt`

It also uses branded release titles and tags:

- Stable tag: `BriechGroundControl-<version>`
- Stable title: `Briech Ground Control <version>`
- Beta tag: `development-build`
- Beta title: `Briech Ground Control Development Build`

## How A New Release Should Be Made

### Stable release

1. Update the version metadata in [MissionPlanner.csproj](C:/Users/Godsmiracle/source/repos/BriechGroundControll/MissionPlanner.csproj)
2. Commit and push to `master`
3. GitHub Actions builds the release
4. GitHub publishes a new Release with the required assets
5. The app checks `releases/latest/download/version.txt`
6. Users are offered the update in-app

### Beta release

1. Commit and push to `development`
2. GitHub Actions rebuilds the `development-build` prerelease
3. Users on beta update channel receive the newer build

## Required Release Assets

For the updater to work, each release must contain:

1. `version.txt`
2. `checksums.txt`
3. `ChangeLog.txt`
4. `BriechGroundControl.zip`

Optional:

1. `BriechGroundControl.msi`

## Version File Requirements

`version.txt` must contain a plain version string that .NET can parse, for example:

```text
2026.1.0.0
```

The updater compares the installed local `version.txt` against the remote one.

## Checksums File Requirements

`checksums.txt` is used by the updater to determine which files changed and whether downloads are valid.

If this file is missing or malformed, the update process will not be reliable.

## Important Operational Note

If the GitHub repository or its releases are private, direct unauthenticated downloads from:

- `releases/latest/download/...`
- `releases/download/development-build/...`

will not work for normal end users.

So for this updater design to work as-is, one of these must be true:

1. the repository is public
2. the releases are publicly accessible
3. you move the update artifacts to another public file host

## Files To Edit In Future

If you need to change update hosting again, these are the main files:

- [app.config](C:/Users/Godsmiracle/source/repos/BriechGroundControll/app.config)
- [Utilities/Update.cs](C:/Users/Godsmiracle/source/repos/BriechGroundControll/Utilities/Update.cs)
- [.github/workflows/main.yml](C:/Users/Godsmiracle/source/repos/BriechGroundControll/.github/workflows/main.yml)
- [MissionPlanner.csproj](C:/Users/Godsmiracle/source/repos/BriechGroundControll/MissionPlanner.csproj)

## Recommended Operating Routine

Use this as the normal pattern:

1. develop on `development`
2. test beta updates from `development-build`
3. merge to `master`
4. let GitHub Actions publish the stable release
5. users receive updates from your GitHub repo automatically
