# Screenshots and runtime verification

Captured on 2026-10-18 from a portable **Playnite 10.62** on Windows 11, with the
extension built from this repository and a local Unnamed Tracking server
(host PR obsoletelabs/unnamed_tracking_app_2#276). Only the Playnite window was
captured.

| View | Evidence |
| --- | --- |
| Extension settings, signed out | ![Settings before signing in](../assets/settings-signed-out.png) |
| Extension settings, signed in | ![Settings after signing in: Connected as devadmin, Sign out](../assets/settings-signed-in.png) |
| Consent page opened by the extension | ![Connect Playnite consent page](../assets/sign-in-consent.jpg) |
| Connected apps on the server | ![Connected apps listing the Playnite desktop app](../assets/connected-apps.jpg) |
| Sync preview, progress, context menus, save sync UI, embedded view | Not captured yet: the test install had no game library. Use the procedure below. |

The sign-in run is reproducible without touching the desktop: install the build into
a portable Playnite's `Extensions` folder, set `ApiUrl` in
`ExtensionsData/4a8d9c7b-2e54-4a6b-9c6d-0a2b8f1e7d43/config.json`, start Playnite and
open `playnite://unnamedtracking/sign-in` (`Playnite.DesktopApp.exe --uridata
playnite://unnamedtracking/sign-in`). The consent page address is written to
`extensions.log`; approve it in a browser signed in to the server, and the extension
logs *Connected as ...*. `playnite://unnamedtracking/settings` then opens the settings
dialog for a capture.

To add the missing views, record extension version, Playnite version, Windows
version, capture date and the action shown, save sanitized images under
`wiki/docs/assets/`, and replace the row above.

## Manual runtime acceptance checklist

Use disposable test saves and a test account. Verify settings persistence,
connection errors, full/selected sync, rename without duplication, custom status
normalization, ignore tags, preview with no writes, progress/cancel, artwork
warnings, startup and game-stop events. Test multiple folders and a single file,
automatic upload versioning, launch pause and the second launch, cancel/failure
with original saves intact, shutdown during transfer, and normal browser sign-in
and unavailable-server behavior. Confirm PEXT installation and extension reload.

The 2026-10-18 run covered installation and extension load, settings persistence,
browser sign-in through a `playnite://` link, the signed-in settings view, and an
encrypted stored credential. Library sync, saves and the embedded view were exercised
against the same server through the extension's own code outside Playnite, not yet
inside a Playnite library. The checklist remains an explicit release gate.

## Note synchronization and access review, 2026-10-20

Verified with the updated extension on the PC's installed **Playnite 10.60** runtime,
copied into a separate test directory with a disposable profile and a separate IPC
endpoint. The normal Playnite profile and library were preserved. A synthetic ordinary
user on a local host received an explicitly approved one-hour test connection.

| View | Evidence |
| --- | --- |
| Existing connections can review newly offered permissions | ![Review app permissions](../assets/playnite-notes/settings-review-access.png) |
| Native settings connection test succeeds | ![Connection successful](../assets/playnite-notes/connection-success.png) |
| Original note entered in the native test game | ![Native note before synchronization](../assets/playnite-notes/native-note-before.png) |
| Selected-game synchronization succeeds | ![One game synchronized](../assets/playnite-notes/native-sync-success.png) |
| A subsequent web edit is pulled into Playnite's actual Notes field | ![Web edit visible in Playnite](../assets/playnite-notes/native-note-after-pull.png) |
| Concurrent edits preserve both values and leave other app notes independent | ![Shared note, conflict copy and unrelated note](../assets/playnite-notes/app-note-conflicts.png) |

The native run created one synthetic game, pushed its note into the host's named
Markdown note and then pulled a conditional web edit into the real Playnite database
through the UI dispatcher. Both selected-game runs reported one success without
warnings. The permission-review button was captured; renewed browser consent was
verified through the public protocol rather than clicked in the native UI.

The production transport also passed 15 assertions against the actual host as the
ordinary user: creation, pull, conditional push, conflict preservation, unrelated-note
preservation, rejection of stale writes, foreign-note denial, delete denial, and actual
PNG/text-document uploads. The 293-assertion regression harness passed on both
.NET Framework and the portable target. Audio/video uploads and the complete save
checklist above were not rerun through the native UI in this session.
The temporary grant was revoked after testing; the next request returned HTTP 401,
and both local credential copies were removed.
