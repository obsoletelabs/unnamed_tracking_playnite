# Unnamed Tracking for Playnite

A Playnite 10 companion extension for one-way library and save synchronization
with an Unnamed Tracking account. **0.2.0 restores the recovered release.** The
extension keeps the existing `net462` / PlayniteSDK 6.17.0 generic-plugin model.

The maintained repository is [obsoletelabs/unnamed_tracking_playnite](https://github.com/obsoletelabs/unnamed_tracking_playnite).
Recovered on 2026-10-07 from commit
[`154f6d4`](https://github.com/obsoletelabs/unnamed_tracking_playnite/commit/154f6d45e504c9b04f20e08b865278a2b6ebdc8c),
it retains all 141 available historical commits and 18 original tags. The latest
recovered tag is `v0.2.0-beta.3`, whose source declares version `0.1.2`; see the
[recovered version history](wiki/docs/developer-guide/releases.md#recovered-version-history).
The last downloaded `0.2.0` package was also recovered. Cached source from its
`fd114c4` build confirms the C# and XAML files match this recovered baseline;
the later changes concern release packaging, build configuration and documentation.

- Authenticated full/selected-game sync, GUID matching, metadata and artwork.
- Preview, per-game progress/cancellation, ignore tags, startup and game-stop sync.
- Multiple local save folders/files, versioned remote archives, automatic transfers,
  durable restore backups and rollback on ordinary failure/cancellation.
- Existing save sidebar with access to the embedded website through normal sign-in.

See the [user and developer wiki](wiki/docs/index.md) for the complete guide,
[verified plugin-manager API contract](wiki/docs/developer-guide/api-contract.md),
[local versus remote save data](wiki/docs/user-guide/saves.md), and
[known runtime/screenshot limitations](wiki/docs/user-guide/screenshots.md).

## Install and configure

Install a reviewed `.pext` from [releases](https://github.com/obsoletelabs/unnamed_tracking_playnite/releases)
or a GitHub Actions build artifact into Windows Playnite. In **Add-ons → Extension
settings → Unnamed Tracking**, enter the server base URL and a user API key starting
with `utk_`. Test the connection, preview, and run the initial upload. API credentials
stay in headers; Playnite's stored extension settings remain sensitive local data.

Sync matches unique Playnite GUIDs first, preserves remote folders on rename, and
never sends arbitrary custom status names to the host enum. Library sync overwrites
mapped remote fields with Playnite values. Save transfers are separately configured
per game. Automatic download pauses the first launch; start the game again after
the success notification. See the wiki before enabling automatic saves.

## Build, test and package

On Windows, use the .NET 8 SDK and .NET Framework 4.6.2 targeting support:

```powershell
dotnet restore UnnamedTrackingPlaynite.sln
dotnet build UnnamedTrackingPlaynite.sln -c Release --no-restore
dotnet build tests/Companion.Tests -c Release -f net462
& ./tests/Companion.Tests/bin/Release/net462/Companion.Tests.exe
if ($LASTEXITCODE -ne 0) { throw "Regression tests failed" }
./pack.ps1 -NoBuild
./tests/test-packaging.ps1
```

The PEXT is `artifacts/UnnamedTrackingPlaynite-0.2.0.pext` and contains exactly
`UnnamedTrackingPlaynite.dll` and `extension.yaml` at its root. Toolbox is optional.
For portable helper tests use `dotnet run --project tests/Companion.Tests -c Release -f net8.0`.
No test framework or new extension runtime dependency is required.

Release builds can use `./pack.ps1 -Version 0.2.0`. A `v<major.minor.patch>` tag
sets the CI build version; MSBuild stamps the assembly and output manifest,
and packaging validates that exact version before publishing the matching PEXT.

```sh
python -m pip install -r wiki/requirements.txt
python -m mkdocs build --strict -f wiki/mkdocs.yml
```

Both existing build workflows remain active, including package validation.
Main/PR builds produce artifacts; release publication requires an intentional tag
matching the manifest/project version. See [release notes](CHANGELOG.md) and
[release process](wiki/docs/developer-guide/releases.md).
Headless tests and builds do not claim full Playnite runtime testing; actual
Playnite screenshots could not be obtained in the Linux environment.
