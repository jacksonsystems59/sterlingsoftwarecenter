# Sterling Software Centre v0.1.1

Download **SterlingSoftwareCentre-v0.1.1-win-x64.zip** and its matching `.sha256`, extract all files and run `Sterling.App.exe`. Windows x64; the .NET runtime is included.

## Upgrade from v0.1.0

The published v0.1.0 binary already contains a GitHub updater. In **Settings & help**, choose **Check for app update**, then **Download & apply**, and confirm. The v0.1.0 confirmation dialog is expected. The new helper verifies the restarted v0.1.1 displayed version. No mandatory one-time migration is needed. Manual alternative: extract v0.1.1 into a fresh folder and launch it; existing LocalAppData settings remain available. Open existing captures normally.

## Changes

- Install, Update ticked, bulk update, uninstall, replacement, restore and failed-job retry now use an embedded Review tab with every affected application, provider, exact ID, installed/proposed versions, scope and warnings. Blocked selections remain visible. Start requires acknowledgement; Back/Cancel makes no changes. Progress and results stay in Jobs & logs.
- Capture & Restore now combines bulk selection with per-application version, provider, policy and data details. Chrome bookmarks are the tested optional recipe, with source/target profile, checksum validation and previous-file backup. Selected data restoration follows successful software installation. Other applications explicitly show no tested data recipe.
- Schema 2 supports portable companion bookmark files. Move the JSON and its `.data-…` folder together. Schema 1/v0.1.0 captures remain supported. Optional data starts unchecked for captures; explicitly saved deployment presets can retain the selection.
- Sterling Tech navy, teal and mint styling, clearer navigation, tables, status badges, focus indicators, empty states and progress display.
- Original SVG/PNG icon pack, 16–256px Windows ICO, editable splash artwork and regeneration guide in `assets/branding`.
- Responsive branded splash displayed for at least four seconds while startup work runs. The main window stays hidden until ready.
- In-app GitHub release notes and confirmed Download and update. The helper keeps previous binaries, preserves user files, recovers from copy failures, restarts and verifies the displayed version through a receipt. Updates remain manual and portable/non-elevated.

## Validation

- 60 deterministic core checks: capture compatibility, optional-data defaults, portable attachments, bookmark restore and prior-file preservation, dependency ordering, policy gates, job verification/retry, checksum failure, ZIP traversal rejection, updater copy recovery and user-file preservation.
- 16 Windows WPF workflow checks: embedded install and Update ticked review, cancellation without changes, consent gating, selected exclusions, old capture import, unsupported recipe messaging, bulk software/data review and ordered restore using controlled providers and a temporary Chrome profile.
- The release script checks the actual extracted self-contained ZIP, embedded workflow tests, executable icon extraction, WPF rendering and four-second startup timing. Windows CI repeats these checks.
- Live v0.1.0 → v0.1.1 verification is performed after publication; the release validation addendum records its result.

## Remaining limitations

Local-PC management only. Agent/service installation, controller, enrolment, remote deployment and scheduled policy management are not shipped. WinGet currently requires English table output; Chocolatey needs an approved internal HTTPS repository. No Microsoft Store/Scoop backend, generic registry/shortcut recipes, arbitrary custom installers or offline installer bundles.

Tests use controlled package providers and temporary data for mutations. Actual third-party installation/removal, customer Chocolatey repositories, a clean customer VM and full Windows reinstall recovery were not exercised for this release. Read-only live WinGet checks are reported separately. Binaries are unsigned; SHA-256 is not an independent publisher signature. Power loss during copying can require manual recovery from the retained backup. Captures/bookmark files are unencrypted. No private-repository credentials are embedded; this repository is public and the updater does not implement private-release authentication.
