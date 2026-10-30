# Launch a game from another device

Remote launch requires the updated host remote launch v1 API and this updated companion. It is off by default. It starts an existing local Playnite game on an awake Windows computer with Playnite open. It does not stream the game, wake the PC or install anything.

## Enable receiving

1. Enter the server URL and sign in with your browser in **Add-ons → Extension settings → Unnamed Tracking**.
2. Click **Enable remote launch** and approve the optional games.launch capability in the browser. Existing connections do not gain it automatically. API keys cannot receive launches.
3. Keep **Ask on this PC before each remote launch** selected for a local Yes/No prompt. Clear it and save settings only if you want unattended launching.
4. Sync the game, then leave Playnite open. The settings status shows whether receiving is connected.

Open the same game in a signed-in browser, choose **More ways to launch**, then **Remote start on <computer>** under **Remote start in Playnite**. Offline computers are disabled. Confirm on the PC when prompted. The browser tracks that one request and explains any rejection or uncertainty.

**Disable remote launch** stops the local receiver immediately and disables it on the host when reachable. Sign out also revokes its credential. You can remove the permission under **Account → Connected apps** on the server. Renewing consent creates a new receiver identity.

## What can launch

Only a GUID already present in this PC's Playnite database can start. The game must be installed, not running and not tagged with the configured ignore tag. The host only accepts owned, linked games outside Wishlist and Trash. It never sends executable paths or arguments. The companion calls the supported Playnite StartGame API on the UI dispatcher.

Existing Playnite launch scripts and automatic save-download guards still run. A save download can pause the first launch; wait for success and deliberately launch again. **Accepted** means the SDK received the action, not proof that the game is running. Store clients may still require sign-in.

## Lost connections and duplicate protection

Requests expire after 60 seconds. The PC polls every five seconds and is shown online for 20 seconds after a successful poll. Network failures back off; expired or revoked access stops receiving until reviewed. Older hosts leave normal synchronization available while remote launch reports that an update is needed.

The host dispatches each request at most once. The companion records its ID on disk before calling Playnite. A lost acknowledgement retries the recorded outcome, including after a restart, without starting the game again. The journal is stored under this extension's user-data directory in remote-launch/<server hash>; consumed IDs remain for seven days. Multiple receivers cannot open the same journal. Corrupt or inaccessible history stops receiving and is preserved for diagnosis. Do not delete it to retry a launch.

If the browser says the outcome is unknown, check the PC before deliberately requesting another launch. **Check this request** checks the original action after a transport error; it does not create a second one. A PC that went to sleep will not execute an old request when it wakes.

Steam Remote Play remains a separate planned integration; remote launching does not open a stream.

## Compatibility and the host's update prompt

Use extension 0.3.0 or newer for remote start. Older extensions can still synchronize normally with the updated host. This extension also keeps its ordinary synchronization and legacy fallback on older hosts; only remote start requires the new host API.

During the existing sync connection probe, 0.3.0 reports its built extension version. The host can show an update/setup popup for an outdated or unreported PC when you open remote start. Older builds did not report a version, so the host cannot infer their exact release. Sync after upgrading to refresh the report. Merely updating does not grant remote-launch permission or turn receiving on.
