# Sterling Software Centre v0.1.0

First usable **local-mode** Windows x64 release. Download `SterlingSoftwareCentre-v0.1.0-win-x64.zip`, verify its matching `.sha256`, extract all files and run `Sterling.App.exe`. The .NET runtime is included.

## Working features

- WPF desktop GUI with visible version, provider status and operation logs.
- WinGet search, persistent multi-select deployment basket, per-user/machine inventory, selected/bulk eligible updates, confirmed uninstall and explicit captured-version replacement.
- Sequential package jobs, failure continuation, failed-only retry, stop-after-current, state verification, durable last-job results and restart reporting.
- Editable/importable/exportable application lists and versioned JSON capture/restore bundles; exact/newest policies, availability preflight and no silent version substitution.
- Configurable internal-source Chocolatey provider; no automatic installation or public Community Repository backend.
- Separate current-user Chrome bookmark backup/replace-restore recipe with a prior-file backup.
- GitHub app-update notifications and confirmed checksum-verified download/apply helper with copy-failure recovery and previous-file backups.

## Configuration and limitations

Requires compatible WinGet/Desktop App Installer and English WinGet output for package operations. Chocolatey needs a separately installed standard-path client and an authorised HTTPS internal/customer source; repository licensing/permission remains the organisation's responsibility. No Scoop, Microsoft Store source, custom installer execution or offline installer bundle. Inventory is current-user/machine and can contain unmatched duplicate registry entries; provider association does not prove installation origin. Cross-provider matching is limited to normalised names, so review business applications manually.

**Not included:** Windows agent/service installation, controller, LAN/VPN enrolment/discovery/push deployment, remote queues, central update approval, scheduled monitoring/auto-update, generic registry imports, shortcuts and ordered per-application recipe steps. This release does not fulfil the remote-management portion of the original brief. There are no placeholder controls claiming otherwise.

Self-update is portable/non-elevated only; binaries are unsigned. Hash verification is not an independent digital signature. Interrupted copying due to power loss may require manual recovery. Captures/bookmarks are unencrypted and contain private inventory/URLs; protect them. No unexpected restart is requested, but third-party installers remain an external behaviour boundary.

## Validation

- Release build and focused deterministic checks cover exact arguments, source restrictions, capture schema/round-trip, policy gates, retry/continuation, cancellation, unavailable replacements, installed-state verification, hash rejection, ZIP traversal and updater copy-failure recovery.
- Read-only integration exercised actual WinGet detection, search and scope-aware inventory on the development Windows PC (WinGet 1.29.380).
- Native Windows GUI inspected for search results, selection and layout.
- Publish script tests the **extracted release ZIP** by starting and rendering the self-contained WPF executable; GitHub Actions repeats this on a fresh hosted Windows runner.
- Actual third-party install/update/uninstall, Chocolatey against a customer repository, Chrome restore on a disposable profile, full OS reinstall recovery and a newer-to-older GitHub updater lifecycle have **not** been validated on a clean customer-like VM. No claim of those runtime tests is made. Use a disposable test PC before production deployment.
