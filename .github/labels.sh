#!/usr/bin/env bash
# Run once with GitHub CLI logged in: gh auth login
R=uWaazy/ygp-playtime-sync
mk(){ gh label create "$1" -R $R -c "$2" -d "$3" --force; }
mk bug d73a4a "Something is broken"
mk enhancement a2eeef "New feature or request"
mk security b60205 "Security related"
mk documentation 0075ca "Docs or README"
mk maintenance cfd3d7 "Refactor, build, chore"
mk needs-triage fbca04 "Not reviewed yet"
mk needs-info d876e3 "Waiting for more details from the reporter"
mk ygp-api 1d76db "Depends on the YGP site/API"
mk ignore-for-release ededed "Hide from release notes"
