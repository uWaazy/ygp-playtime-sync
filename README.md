<div align="center">

# YGP Playtime Sync

**Send your Playnite playtime to your [Your Gamer Profile](https://yourgamerprofile.com/) account, automatically.**

![Status](https://img.shields.io/badge/status-beta-orange)
![Version](https://img.shields.io/badge/version-0.5.0--beta-blue)
![Playnite](https://img.shields.io/badge/Playnite-10.x-5b8def)
![Languages](https://img.shields.io/badge/UI-English%20%7C%20Portugu%C3%AAs%20%7C%20Espa%C3%B1ol-green)

English · [Português (Brasil)](README.pt-BR.md) · [Español](README.es.md)

</div>

> **Beta.** This is the first public release. It works day to day, but expect rough edges and please report bugs (see [Reporting problems](#reporting-problems)).
>
> **Unofficial project.** It is not affiliated with or endorsed by Your Gamer Profile (YGP). YGP has no public API for writing playtime, so the plugin talks to the site the same way your browser does, while you are signed in. Those routes are undocumented and **may change without notice**.

## What it does

- **Automatic sync when you close a game** (sessions of 1 minute or more), plus a periodic check in the background.
- **Always sends the game's total time** and never lowers the hours that the site already has.
- **Adds new games and updates existing ones** on your profile. The platform, status and rating you set on the site are preserved.
- **Recent games stay on top.** Syncs go from the game you played longest ago to the one you played last, so your latest games end up at the top of your profile's recent list, the same effect as editing hours by hand on the site.
- **Automatic completion (optional).** Choose from **7 to 30 days**. If you don't open a game for that long, the plugin sets the game's completion date on YGP to the **last day you actually played it**. See [Automatic completion](#automatic-completion).
- **Start and completion dates** from your play history (uses the *GameActivity* plugin if you have it, plus its own log). Can be switched on/off per game.
- **Detects manual hour edits** in Playnite (for example with FriendlySetTime) and updates the site.
- **Sync entire library** in one click, with a progress bar. Older games are included.
- **Filters:** ignores apps/software, games under 1 minute, duplicate copies of the same game, and a list of names you choose.
- **Offline-safe queue:** sessions are stored locally, survive restarts and are retried automatically.
- **Creator Profile card** at the top right of the settings page: the plugin author's YGP avatar, username, a link to follow them and the last game played on their profile.
- **Interface in English, Português (Brasil) and Español.** It follows Playnite's language, and you can change it in the settings.

## Requirements

- Windows with **Playnite 10.x**
- A **Your Gamer Profile** account

## Installation

1. Download the latest `.pext` from the [**Releases**](https://github.com/SEU-USUARIO/ygp-playtime-sync/releases) page.
2. Close Playnite, then double-click the `.pext` file (or use *Add-ons → Install from file*). Start Playnite again.
3. Open **Add-ons → Extensions settings → Generic → Your Gamer Profile Sync**.
4. Click **Link account (sign in)** and sign in to YGP in the window that opens. If Google blocks sign-in in the embedded browser, use **Link by pasting a cookie (alternative)** and follow the on-screen steps.
5. Click **Sync entire library (older games)** once. It takes a few minutes (about 1 to 2 seconds per game).

From then on, everything happens in the background.

## Settings

| Setting | What it does |
| --- | --- |
| Sync automatically in the background | Sends your hours without you doing anything |
| Sync when a game closes | Immediate sync after each session |
| Detect manual playtime edits | Picks up hours you change by hand in Playnite |
| Send the start date by default | Sends the first day you played (can be changed per game) |
| Show a Playnite notification | Shows the game, hours added, game total and overall total |
| Always sync the total accumulated time | Keeps the site total equal to Playnite |
| Periodic sync interval | How often the pending queue is checked (1 to 60 min) |
| Automatic completion + days | See below |
| Ignore apps/software · Ignored names | Keeps non-games out of your profile |
| Language | Automatic, English, Português (Brasil) or Español |

**Per game:** right-click a game → **YGP: Settings** (a single submenu) to sync that game's playtime right away or to turn its start date and completion date on or off.

## Automatic completion

Turn on **Set the completion date when I haven't played a game for a while** and pick how many days (7 to 30).

When a game has not been opened for that many days, the plugin sends your **last play date** to YGP as the completion date. It is checked at startup, hourly while Playnite is open, and when you save the settings.

> Example, with 7 days: you last played a game on the 22nd. On the 29th the plugin sets its completion date to the **22nd** (not the 29th).

- Each "last played" date is sent only once per game. If you play again and stop again, the new date is sent.
- Only the completion **date** is set. For games already on your profile, your status and rating on the site are not touched.
- If you leave this option off, you can still set dates by hand on the site, or per game with right-click → **YGP: Settings**.

## How the connection works

- You sign in on the official YGP site inside Playnite's embedded browser. The plugin **never sees your password**.
- To act on your behalf, it keeps the site's **session cookie** in the plugin's settings on your computer. See [Privacy and security](#privacy-and-security).
- Data goes only to `https://yourgamerprofile.com` over HTTPS. There is **no telemetry, analytics or third-party service**.
- The **Diagnose site** and **Learn site routes** buttons (Advanced) are for troubleshooting. Diagnose sends many requests to the site, so only use it when something stops working.

## Privacy and security

What is sent to YGP: game name, platform, total playtime in minutes and, if enabled, start and completion dates.

What stays on your PC (in Playnite's `ExtensionsData` folder for this plugin): the session queue, the record of what was already sent, game mappings, per-game options and play dates.

**Your session cookie is stored as plain text** in the plugin settings, like most Playnite plugin settings. Anyone who gets that file can act as you on YGP until the session expires. So:

- Don't share Playnite's `ExtensionsData` folder or its settings files.
- Review `ygp-diagnostico.txt` before attaching it to a bug report; it contains raw site responses.
- **Disconnect** (in the settings) deletes the stored cookie.

## Known limitations (beta)

- The site's routes are not documented, so a YGP update can break syncing until the plugin is updated.
- Game matching is strict on purpose (sequels and Mobile/Demo versions are not merged). Some games may need to be added manually on the site.
- The "recent games on top" order depends on how YGP records edits; it matches what happens when you edit hours on the site, but YGP could change that.
- The site may rate-limit requests (HTTP 429). Just wait a few minutes and sync again.

## Reporting problems

Open an [issue](https://github.com/SEU-USUARIO/ygp-playtime-sync/issues) and include:

1. Your Playnite and plugin versions.
2. What you did and what you expected.
3. The `extensions.log` from Playnite (*Menu → Settings → General → Open log folder*). Look for lines starting with `[YGP]`.
4. If it is a sync problem, the `ygp-diagnostico.txt` from the **Open diagnostics folder** button. Review it first for personal data.

## Roadmap

- Wider testing during the beta
- Fixes for whatever YGP changes on the site
- Your feedback: suggestions are welcome in the issues

## License and source

The plugin is free to use. The source code is not published at this time; this repository holds the documentation and releases.

## Credits

Built for [Playnite](https://playnite.link/). Thanks to everyone testing the beta.

