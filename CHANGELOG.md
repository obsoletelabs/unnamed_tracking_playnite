# Release notes

## 0.2.0 — 2026-10-07 recovery release

- Restore release-version packaging from the cached 2026-10-03 source and last
  downloaded `0.2.0` package; the recovered C# and XAML source is unchanged.
- Pass the release tag version through MSBuild, the output manifest, assembly/file
  versions, package validators and PEXT filename.
- Preserve both recovered CI workflows and all validation checks, including
  regression coverage for explicit version validation and empty manifest fields.
- Update the maintained source/release links to `obsoletelabs/unnamed_tracking_playnite`.

The original `0.2.0` package embeds source commit `fd114c4` from 2026-10-03.
That commit's build/release configuration is restored where available and
reconstructed where only metadata remains. Playnite runtime acceptance and real
screenshots remain unverified. Breaking changes: None.

## 0.1.2 — Unreleased

- Preserve GUID identity and remote storage folders when Playnite titles change;
  reject ambiguous identities and malformed remote lists before creating games.
- Send valid complete create payloads, preserving the existing finite custom-status
  normalization and all current metadata/artwork mappings.
- Capture SDK data on the supported Playnite context before async network work.
- Add reachable selected-game sync, preview/in-flight cancellation, clear artwork
  warnings and automatic-error notifications.
- Harden save configuration, paginated GUID lookup, multiple locations and file
  targets; stage restores, retain durable backups and roll back ordinary failures.
- Pause game launch for asynchronous automatic save downloads; launch again after
  the completion notification. Cancel and shutdown stop outstanding transfers.
- Expose the existing embedded view with explicit normal browser sign-in limits;
  reject credential-bearing server URLs and unexpected download URLs/redirects.
- Consolidate PEXT packaging/validation, add production helper/package regression
  tests, remove confirmed obsolete backup/marker source and align LF attributes.
- Add the MkDocs user/developer wiki and honest runtime/screenshot availability.
- Keep normal CI builds/artifacts; publish releases only on matching intentional tags.

Windows Playnite runtime validation and real screenshots remain pending. This
version is prepared for review and has not been published.

## 0.1.1

Existing companion baseline: library metadata/artwork synchronization, preview,
ignore tags, automatic events, save archive integration and packaging. The main
branch's PR #16 status-validation fix keeps custom Playnite completion names out
of the finite host API enum. That behavior is preserved in 0.1.2.
