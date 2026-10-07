# Release process

Version **0.2.0** restores the recovered release. The tag supplies the release
version to MSBuild, which derives assembly/file versions and stamps the output
manifest. The validators compare the expected release version, built assembly,
file version, packaged manifest and PEXT filename. Normal main/PR builds use the
project's default version. Keep README and release notes current for releases.

1. Review/merge the companion PR against `main` after both build workflows and
   the wiki job pass. Keep unrelated work such as the AFK PR separate.
2. Complete the Windows/Playnite [runtime acceptance checklist](../user-guide/screenshots.md)
   and capture the actual requested screenshots. Record exact versions/results.
3. Verify release notes and all version metadata. Build the PEXT from a clean
   checkout and validate its actual entries. Inspect the resulting artifact.
4. When release publication is explicitly intended, create and push a tag such as
   `v0.2.0` on the reviewed commit. The PEXT workflow builds with `0.2.0` and
   validates that exact version before publishing. Tags require major.minor.patch.
5. Review the generated release notes and attachment, then test installation of
   that exact PEXT. Do not rebuild and silently replace an already distributed
   version with different content.

Main pushes and draft PRs build artifacts only. The existing release action runs
only for an intentional version tag; ordinary maintenance commits do not create
a release. No force-push, unrelated branch edits, or automatic PR merge is needed.

## Recovered version history

On 2026-10-07 the available local history of `Rosefall-a/UnnamedTrackingPlaynite`
was restored to [obsoletelabs/unnamed_tracking_playnite](https://github.com/obsoletelabs/unnamed_tracking_playnite).
The recovery preserves 141 original commits and all 18 original tags, ending at
`154f6d45e504c9b04f20e08b865278a2b6ebdc8c` (`v0.2.0-beta.3`). Source, regression
tests, packaging workflows and the wiki are included; original authorship and
the license remain intact.

The historical tags do not match their extension metadata: `v0.2.0-beta.1` and
`v0.2.0-beta.2` declare `0.1.1`, while `v0.2.0-beta.3` declares `0.1.2`. A locally
recovered package named `UnnamedTrackingPlaynite-0.2.0.pext` also contains a
`0.1.1` manifest. The recovery retains these historical tag names and the latest
source's `0.1.2` version in the initial recovery without treating the filenames
as new releases.

The last downloaded package, obtained on 2026-10-03 at 21:15 Australia/Perth,
has a genuine `0.2.0` manifest and `0.2.0.0` assembly version. Its assembly embeds
commit `fd114c4691a6d02d1496d0ff463ce1381eb6b732`, recorded at 15:40:35 that day.
The cached Git tree for that commit has identical C# and XAML blob hashes to the
recovered baseline. Nine changed file entries concern packaging, project build
configuration, CI workflows and documentation; no runtime source file differs.
The time between the baseline and that recorded commit is 17 hours 13 minutes
33 seconds of elapsed history, which does not measure development effort.

The cached `pack.ps1` was recovered byte-for-byte. Supporting release version
validation and build stamping were reconstructed, retaining both recovered CI
workflows. The recovered default version is now `0.2.0`; future release tags
follow the versioned-build process above. The original newer commit objects and
intermediate history are unavailable, so the restored work has a new commit.
