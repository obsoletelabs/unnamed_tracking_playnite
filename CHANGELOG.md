# Release notes

## Unreleased (0.3.0)

- Report the built extension version during the existing host connection probe so remote-start setup can distinguish outdated and unreported PCs. Headers are ignored by older hosts; existing synchronization remains available.

- Add optional browser-to-PC game launching through remote launch v1. Enable receiving explicitly, approve games.launch and confirm on the PC by default. Requests use local Playnite GUIDs and the supported SDK, expire after 60 seconds and never carry paths or commands. Durable receipts prevent replay after a lost acknowledgement or restart. Requires host #319; older hosts keep existing synchronization. Breaking changes: None.

- **Quick actions in the game menu.** Set a status, rate (0-10), add a dated note, add screenshots/clips/audio
  and add documents to a game without opening the website; open the game's page in your browser. Status, score and
  notes are Playnite's own fields and are pushed with the normal sync. Uploading files uses the new optional
  `content.write` capability, requested only from servers that offer it (host change required).
- Send Playnite's date added and first region with each game.
- Fix completion-status mapping: Playnite's default **Not Played** was sent as *Played*, and **Abandoned** as backlog;
  they now map to backlog and *Dropped*. Quick edits wait for a running synchronization instead of failing.
- Remember the linked remote game for a few minutes instead of scanning the server's whole game list for every
  save upload, download and file upload.
- Show a notification when automatic save upload or download is switched on or off for a game.
- **Sign in with your browser instead of pasting an API key.** The extension uses
  the server's user-approved integration access (the same consent flow as the
  Discord Activity): the user approves exactly what Playnite may do, and the
  extension receives a revocable credential that only reaches library sync, saves
  and the display name. It is encrypted with Windows DPAPI and bound to its server.
  **Sign out** revokes it on the server; Connected apps lists this computer.
  `utk_` API keys keep working against servers without Playnite sign-in.
- Send each game's first Playnite platform, so the server can name and group copies
  of one game from different stores and platforms.
- Reorganise the settings into Account, Automatic synchronization and Library sync.
- Handle `playnite://unnamedtracking/settings`, `/sign-in` (only for the server
  already configured) and `/sync` links, and add *Sign in to Unnamed Tracking...* /
  *Unnamed Tracking settings...* to the Extensions menu.
- Give the sidebar item an icon (it was an empty coloured square).
- Verified in a real portable Playnite 10.62: extension load, browser sign-in and the
  signed-in settings, with the credential stored encrypted.
- The portable test harness skips the symbolic-link save checks, with a message,
  on Windows accounts that cannot create symbolic links.
- Requires host `obsoletelabs/unnamed_tracking_app_2#276` for sign-in; older hosts
  keep the API-key path. Breaking changes: None.
- Report Playnite's state as source observations through the versioned host contract
  (`/api/playnite/*`): platform, last played, installed state, wishlist ownership and the complete
  library inventory, keyed by the stable Playnite game ID. The extension never creates or merges
  identities.
- Probe the host first; an older host (or another contract version) keeps using the original
  library routes only, so the extension stays compatible in both directions.
- Send an inventory only after a whole-library sync whose evidence succeeded; selected-game,
  game-stopped, cancelled and failed syncs never mark anything removed.
- Requires host #100 for the new behaviour; against an older host the sync is unchanged. Breaking changes: None.

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
