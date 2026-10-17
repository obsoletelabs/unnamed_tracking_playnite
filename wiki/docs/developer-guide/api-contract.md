# Synchronization model and API contract

## Verified host source

Audited on 2026-10-02 against `Rosefall-a/unnamed_tracking_app` branch
[`plugin-manager` at `45424fe6846587e2c25cbdafa882c621d2dbcd04`](https://github.com/Rosefall-a/unnamed_tracking_app/tree/45424fe6846587e2c25cbdafa882c621d2dbcd04).
The relevant game schema, status enum and archive routes also match application
`main` at `b3bcb73dfbc77ad6ab457c23c85dbc384ffe53bc`. This is source-contract
verification, not a live host/Playnite end-to-end test.

Authoritative sources are
[game schemas](https://github.com/Rosefall-a/unnamed_tracking_app/blob/45424fe6846587e2c25cbdafa882c621d2dbcd04/src/backend/src/api/schemas/game.py),
[game routes](https://github.com/Rosefall-a/unnamed_tracking_app/blob/45424fe6846587e2c25cbdafa882c621d2dbcd04/src/backend/src/api/routes/games.py),
[archive routes](https://github.com/Rosefall-a/unnamed_tracking_app/blob/45424fe6846587e2c25cbdafa882c621d2dbcd04/src/backend/src/api/routes/game_archives.py),
[status enum](https://github.com/Rosefall-a/unnamed_tracking_app/blob/45424fe6846587e2c25cbdafa882c621d2dbcd04/src/backend/src/database/models/game.py), and
[authentication](https://github.com/Rosefall-a/unnamed_tracking_app/blob/45424fe6846587e2c25cbdafa882c621d2dbcd04/src/backend/src/core/auth.py).

Requests use `Authorization: Bearer utk_<secret>`. The plugin-manager branch
explicitly rejects `utpm_` management tokens on application APIs. The extension
uses the user API directly and is not a Plugin API v1 gateway consumer.

## Endpoints

| Operation | Request | Response expected |
| --- | --- | --- |
| List / lookup / preview | `GET /api/game/list?skip=N&limit=200` | JSON array of games with valid UUID `id`, `folder_location`, optional UUID `playnite_guid` |
| Connection test | Same list endpoint with `skip=0&limit=1` | Valid JSON game array; empty is allowed |
| Create | `POST /api/game/create` | Game object with non-empty UUID `id` |
| Update / parent relationship | `PATCH /api/game/update/{id}` | Successful HTTP response |
| Artwork | `POST /api/game/{id}/assets/key_art` or `/banner` | Multipart `file`, successful HTTP response |
| List saves | `GET /api/game/{id}/archives/save` | JSON array of named archives with UUIDs and version arrays |
| Create save archive | `POST /api/game/{id}/archives/save` | Multipart `name` and `file`; archive object with UUID `id` |
| Rename legacy archive | `PATCH /api/game/{id}/archives/{archive_id}` | JSON `name`; successful HTTP response |
| Upload save version | `POST /api/game/{id}/archives/{archive_id}/versions` | Multipart `file`, successful HTTP response |
| Download version | `GET /api/game/{id}/archives/{archive_id}/versions/{version_id}/download` | ZIP bytes |

Only HTTP 2xx is success. Redirects are rejected. No write retry is automatic:
an interrupted request may already have committed remotely. Re-list on the next
sync. Null, wrong-shaped, missing-ID or repeating paginated game lists fail closed
before create. Archive lists validate IDs, names and versions; fractional
`updated_at` / `uploaded_at` values are deserialized as doubles. Returned download
URLs must exactly match the selected game/archive/version's expected relative
path. No server-supplied external destination receives the API key.

## Identity and duplicate prevention

All SDK inputs are captured before network work and deduplicated by Playnite GUID.
Library mutations within one extension instance share a semaphore. Lookup scans
all pages, retaining games by remote ID so duplicate folder entries cannot hide
GUIDs. Match unique `playnite_guid` first, then the generated
`playnite-<sanitized-name>-<GUID without dashes>` or historical `playnite-<GUID>`
folder only if it is unbound or agrees with the GUID. Ambiguous GUIDs/folders and
folders bound to another GUID produce errors.

Preserve the remote folder after matching, including a title rename. Create sends
the complete validated-enum payload in one POST; the older empty-status bootstrap
request was invalid under the current host schema. Successful entries update the
current lookup index. Host folder uniqueness handles competing instances with
409, which is reported for review. Do not claim cross-device exactly-once writes.

## Field mappings

| Playnite value | Remote field / normalization |
| --- | --- |
| `Id` | `playnite_guid` UUID; never library/storefront ID |
| `Name`, `SortingName` | `title`, `sort_title`; missing sorting name falls back to title |
| `Description`, `Notes` | `description`, `notes` |
| `ReleaseDate` | `release_date` as `yyyy-MM-dd` or null |
| Developers, publishers, series | Joined distinct names in `developer`, `publisher`, `series` |
| Tags | `tags` with original names |
| Genres, platforms, regions | Additional `Genre:`, `Platform:`, `Region:` namespaced tags; host has no dedicated platform field |
| Features | Distinct `features` names |
| Categories | Distinct `collections` names |
| Links | `links` with label and URL; unnamed links use `Playnite link` |
| Source, age ratings | `source`, joined `age_rating` |
| Favourite | `favorite` boolean |
| Cumulative `Playtime` | `playtime_seconds`, clamped to signed 64-bit maximum |
| `UserScore` | `rating_overall`, divided by 10 to map 0–100 to 0–10 |
| Completion status | Finite normalized `status`; table below |
| Cover, background | `key_art`, `banner` multipart assets |
| ATLauncher source | Existing Minecraft game as `parent_game_id`, `relationship_type=modpack` when available |

Mapped update fields are authoritative one-way Playnite values. Host-only fields
are omitted. Host limits still apply: titles/sort titles 500 characters,
developer/publisher/series 200, source 50, age rating 20, link labels 50 and URLs
2,048; scores 0–10 and playtime nonnegative. A server validation failure is
reported per game rather than silently truncating user metadata.

## Status normalization (preserved from PR #16)

Trim and lowercase the Playnite name. Apply these rules in order:

| Name contains / equals | Host enum |
| --- | --- |
| `wishlist` or `wish list` | `WISHLIST` |
| `dropped` | `DROPPED` |
| `hold` | `ON_HOLD` |
| `playing` or `progress` | `PLAYING` |
| `mastered` | `MASTERED` |
| `beaten`, `completed`, or exact `complete` | `BEATEN` |
| `played` | `PLAYED` |
| Unknown nonblank custom name | `BACKLOG` regardless of playtime |
| No/blank name | `PLAYED` when playtime > 0, otherwise `BACKLOG` |

Never send an arbitrary custom Playnite name or dynamically expand the host enum.
Startup and manual/selected sync use the same payload mapping; game-stop sync
updates only linked, nonignored games and does not upload artwork.

## Observation contract v1 (host `/api/playnite/*`)

Host: `obsoletelabs/unnamed_tracking_app_2` issue #100 (documented there in
`development/playnite-observations.md`). The extension is evidence only: it reports what the
Playnite library holds, keyed by the stable Playnite game ID, and never creates, merges or splits
canonical identities. The original library routes above stay in use for create, update, artwork
and saves, so an older host keeps working.

| Step | Request | Notes |
| --- | --- | --- |
| Negotiate | `GET /api/playnite/contract` | After the library lookup of every sync. `supported_versions` must contain `1`. A 404, an unreadable answer or another version leaves the sync on the original routes only (a warning is logged; no data is sent). |
| Evidence | `POST /api/playnite/observations` | After create/update succeeded, 500 games per request: `contract_version`, then per game `playnite_id`, `name`, `platforms`, `release_date`, `playtime_seconds`, `last_played_at`, `installed`, `favorite`, `status`, `modified_at`, and `owned: false` for a Wishlist entry. Omitted values are unknown to the host. A failing page is a per-game warning (`Operation = Observation`); the next sync reports the same state again and the host treats a replay as unchanged. |
| Inventory | `POST /api/playnite/inventory` | Only after a whole-library sync (manual or startup) whose evidence all succeeded and that was not cancelled. Lists every Playnite ID, including games carrying the ignore tag. Selected-game and game-stopped syncs never send it, so a partial, failed, cancelled or filtered sync never makes a game look removed. |

Mapping notes: the platform is the first listed platform plus the list; last played is
`Game.LastActivity` in unix seconds; installed is `Game.IsInstalled`; `modified_at` is `Game.Modified`
and lets the host ignore a late retry of an older report; playtime is capped at the host maximum.
Playnite has no ownership or achievement model: ownership is never guessed and achievement counts
are never sent. The completion status is the same normalized value as the update route.

The host keeps a name or status the user locked in the application; the extension's update still
succeeds for its other fields (previously a locked, changed title answered 403 for the whole update).
A 409 (`duplicate_folder_location`, `playnite_game_in_trash`) is still reported per game with the
server's message.
