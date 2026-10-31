# Privacy

The plugin does not include telemetry, analytics, or crash reporting. The only external communication is with `https://yourgamerprofile.com`, using HTTPS and your account.

## What is sent to YGP

For each synchronized game: the name, platform, total time played (in minutes), and—if enabled—the start and completion dates. Data required for editing game entries on the website (such as status and rating) is also sent; these values ​​are preserved exactly as they appear on the site.

## What remains on your computer

In the plugin's data folder (`ExtensionsData\<plugin ID>`):

| File | Content |
| --- | --- |
| `sessions/`, `completed/` | Pending and completed game sessions (game ID, name, platform, start/end times, duration) |
| `synced-playtime.json` | The most recently synced playtime for each game |
| `game_mappings.json` | Mapping between Playnite games and YGP games |
| `ygp-game-options.json`, `ygp-play-dates.json` | Per-game options and play dates |
| `ygp-diagnostico.txt` | Created only if you use the diagnostic feature; contains raw responses from the website |

The plugin settings also store your YGP username, ID, and avatar, as well as your **session cookies** (`AuthCookiesJson`) in plain text (unencrypted).

## Session cookies

Session cookies provide access to your YGP account. Anyone who obtains this file could act as you on the website until the session expires. Therefore:

- Do not publish or share the plugin's `ExtensionsData` folder or your Playnite configuration file.
- Review `ygp-diagnostico.txt` before attaching it to any bug report or issue.
- Use the **Disconnect** option in the settings to delete any saved cookies. ## Optional: personal site

Off by default. If you enable it, the game name, platform, start time and total hours of the game you are playing are sent over HTTPS to the URL **you** configure (see [hub-activity.md](hub-activity.md)). The URL and secret live only in your Playnite settings.

## Reading from other plugins

If the GameActivity plugin is installed, the extension reads its history (read-only) to determine the first and last days played.

## Deleting data

Use **Disconnect** and **Clear local sync data** in the settings, or delete the plugin's data folder while Playnite is closed.

## Contact

Open an issue in the repository.