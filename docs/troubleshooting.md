# Troubleshooting

Playnite logs: **Menu → Settings → General → Open log folder** (`extensions.log`). Look for lines starting with `[YGP]`.

## Plugin fails to load ("Failed to load extension")

- The same plugin (same `Id`) cannot be installed as a `.pext` file **and** be active under *Settings → Developer → External extensions* at the same time. Keep only one.
- Close Playnite before installing or updating the `.pext` file.
- If the error mentions `Newtonsoft.Json`, build the `.pext` file using `build.bat` (which includes the necessary DLL) instead of using the Toolbox.

## Login fails or is blocked by Google

Use the **Link by pasting cookie (alternative)** method. Log in to YGP in your web browser, open the developer tools (F12) → Application → Cookies → `https://yourgamerprofile.com`, and copy the value of `__Secure-better-auth.session_token`.

## Session expired

Data submission is suspended until you re-link your account. Pending entries remain in the queue and will be submitted once the account is re-linked.

## Games "not found"

The plugin uses strict title matching (ignoring editions like "Remastered" or "Tom Clancy's"). Sequels, mobile versions, and demos are intentionally excluded. You can create a manual mapping.

## Error 429 (too many requests)

The website has rate-limited your requests. Please wait a few minutes. Avoid using the **Diagnose site** button unless absolutely necessary.

## Playtime hours not updating

The plugin only sends updates if the playtime has **increased**; it never decreases the hours on the website. To force an update, use the game's context menu (*YGP: Settings*) and enable the re-submission option. ## "Route not found" appeared after a site update

Site routes are undocumented and subject to change. Run **Diagnose site**, attach the `extensions.log` file to an issue, and review the `ygp-diagnostico.txt` file before submitting it.