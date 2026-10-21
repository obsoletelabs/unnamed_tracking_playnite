# Library synchronization

## Full and selected-game synchronization

Use **Upload Playnite Library** in extension settings, or **Sync library to
Unnamed Tracking** in Playnite's main menu. For a subset, select games and use
**Unnamed Tracking → Sync selected games to Unnamed Tracking** in the game
context menu. Selected-game sync uses the same client, matching, ignore filter,
and cancellation rules as full sync.

The extension sends Playnite metadata and state to your authenticated account.
It creates missing games and updates linked games. Mapped metadata flows from
Playnite to the app, and a subsequent upload can overwrite remote edits to those
fields. The dedicated Playnite note synchronizes in both directions as described
below. Other application-only fields are left alone.

GUID matching keeps the game linked when its title changes. The remote storage
folder stays stable after linking. Ambiguous duplicate GUIDs or folders belonging
to another GUID are reported as failures; they are not guessed by title.

Playtime is cumulative seconds, favourites retain their boolean state, and
completion statuses are normalized to the host's finite enum. Unknown custom
Playnite statuses become `BACKLOG`, even when playtime is nonzero. Only an absent
or blank status uses playtime to choose `PLAYED` or `BACKLOG`. The
[API mapping table](../developer-guide/api-contract.md) lists every mapped field.

Available covers and backgrounds are uploaded as key art and banner. Artwork
failures appear as warnings after a successful metadata update; they can be
retried by running another library sync.

## Two-way notes

Playnite has one Notes field. On a host offering both `notes.read` and `notes.write`,
it synchronizes with the ordinary Markdown note named **Playnite** on the matched
game. Other app notes remain independent. Approve both capabilities when connecting,
or use **Review app permissions** for an older connection. Full, selected-game,
quick-edit and game-stop sync all use the same note reconciliation.

When only one side changed since the last successful sync, its text updates the
other side. When both changed, the local text is saved as a **Playnite conflict …**
note before the current app note is pulled into Playnite. The same rule applies on
first sync when both sides contain different nonempty text. Conflict copies are
ordinary notes you can review and edit. Retries reuse an identical conflict copy;
an edited copy is never overwritten. Conditional writes prevent an intervening
remote edit from being lost. Editing Playnite's note while a pull runs keeps the
new local edit and reports a warning so the next sync can reconcile it.

Deleting the app's Playnite note clears the local counterpart; a simultaneous local
edit is first retained as a conflict copy. Clearing Playnite's Notes updates the
app note to empty. Other notes and conflict copies are never deleted by sync.
The extension keeps only revision hashes in `note-sync.json` in its plugin data
directory, written atomically. A damaged baseline stops note sync with a warning
and is retained for repair; game metadata sync remains available.

Missing permissions, unsupported hosts and note failures appear as warnings and
keep local text. Older hosts still receive the legacy `notes` game field, which
does not itself populate the app's Notes tab. Upgrade the host and approve note
access for visible, two-way Markdown notes.

## Preview

Click **Preview sync** to see totals and game names for creates, updates, and
ignored entries. Preview reads the remote library but performs no creates,
updates, relationships, artwork uploads, or save transfers. It is a point-in-time
comparison; the next actual sync reads the remote library again.

## Progress and cancellation

Settings display the current game and completed/total count. Menu synchronization
and startup synchronization use Playnite notifications for progress or results.
A second settings/menu sync is rejected while a sync or preview is active.

Use **Cancel** in settings, **Cancel sync** in the Unnamed Tracking sidebar, or
**Cancel Unnamed Tracking synchronization** in the main menu. Cancellation aborts
in-flight API requests and prevents subsequent operations. Requests have a
60-second deadline. Changes already accepted by the server are retained; library
sync is not a multi-game transaction. Run sync again to reconcile partial progress.
Artwork warnings, game failures, and cancellation are distinct results.

## Startup sync

Enable **Sync the library when Playnite starts** to run the same full sync after
startup. It can create games as well as update them. SDK data is captured on the
Playnite context; network requests run asynchronously. Startup results and errors
appear in notifications. Cancel from the main menu or sidebar if needed.

## Game-stop sync

Enable **Sync a game after it stops** to update a linked game's mapped metadata,
playtime, favourite, status, and relationship after a session. It does not create
an unlinked game and does not re-upload artwork. Use full or selected-game sync
first. This setting is independent of per-game save upload-on-stop.

## Ignored games

Games with the configured tag are skipped by full sync, selected sync, preview,
game-stop sync, manual save sync, and automatic save transfers. Tag comparison
trims names and ignores case. Clear the ignore-tag setting to disable filtering;
an empty value remains empty after restarting Playnite. Ignoring does not delete
previously uploaded games or archives. Local save configuration can still be
edited for an ignored game.
