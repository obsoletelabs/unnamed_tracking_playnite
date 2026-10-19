# Quick actions from Playnite

Right-click a game (or select several) and open the **Unnamed Tracking** section of the context menu. These
actions cover the everyday edits so you do not have to open the website for them.

| Action | Works on | What it does |
| --- | --- | --- |
| **Set status ▸ (a Playnite completion status)** | one or more games | Sets the game's Playnite completion status, then syncs it. The notification shows the Unnamed Tracking status it maps to (for example *Playing* → `PLAYING`). A custom status with no obvious match is sent as a neutral status, never as an arbitrary name. |
| **Rate…** | one game | Asks for a score from 0 to 10 (`8`, `7.5` or `7,5`). Leave it empty to clear the score. Stored as Playnite's *User score* and synced as the rating. |
| **Add note…** | one game | Appends a dated line (`2026-10-19: …`) to the game's **Notes** field, keeping everything already written, then syncs it. |
| **Add screenshots, clips or audio…** | one game | Pick one or more files. Images become screenshots, videos become clips and audio becomes soundtrack; the server decides and reports each file separately. |
| **Add document…** | one game | Pick any files for the game's Docs tab. |
| **Open in Unnamed Tracking** | one game | Opens the game's page in your normal browser (no extra sign-in inside Playnite). |

There is also **Open Unnamed Tracking in your browser** in Playnite's main menu.

## Playnite stays the source of truth

Status, score and notes are Playnite's own fields. The actions change them in Playnite and push the change
right away with the normal library sync, so the next sync never undoes them. Changing a status, score or note
on the website only affects the website until you change it again in Playnite; the extension does not read
those fields back.

## Screenshots, clips and documents

Uploading files needs one extra capability, **Add screenshots, clips, soundtracks and documents to your
games**. Sign in from the extension settings and keep it ticked on the approval page. The extension asks for it
only from servers that offer it, so signing in to an older server is not refused. If you signed in before this
existed, or unticked it, the menu actions tell you to sign in again. A legacy API key is not limited by
capability.

Each file is its own upload. The server enforces the allowed file types and the size limits you or your
administrator set under **Administration → Limits**; a reverse proxy in front of the server can have a lower
limit of its own. A file that cannot be added is named with the reason, and the other files are still added. If
the game is not in Unnamed Tracking yet, the extension offers to sync it first.

These actions cannot add world saves or modpacks, delete files, or change anything else.

## Other details that now sync

Besides the existing fields, the library sync now sends Playnite's **date added** (so the server's "added"
date matches Playnite, for games you have had for a while) and the first **region**. Both are omitted when
Playnite has none.
