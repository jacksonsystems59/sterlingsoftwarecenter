# Sterling Software Centre

Portable software management for Sterling Tech engineers on the current Windows PC. This is the first **local-mode release, v0.1.0**, built with .NET 10 and WPF. The optional agent/controller system described in the project brief is **not implemented in this release**. No network listener or Windows service is installed.

## Download and run

1. Open [GitHub Releases](https://github.com/jacksonsystems59/sterlingsoftwarecenter/releases/latest).
2. Download `SterlingSoftwareCentre-v0.1.0-win-x64.zip` and its `.sha256` file. Compare the hash with `Get-FileHash .\SterlingSoftwareCentre-v0.1.0-win-x64.zip -Algorithm SHA256`.
3. Extract **all files** to a normal local folder. Run `Sterling.App.exe`. Keep the included DLLs and `Sterling.Updater.exe` together. A separately installed .NET runtime is not needed.
4. Open **Settings & help**, check provider status, and review prerequisites. Source agreement acceptance is an explicit saved choice. Package licence acceptance is confirmed before every job.

Windows 10 1809+ / Windows 11 x64 with a compatible Desktop App Installer/WinGet installation is required for WinGet operations; use a currently supported Windows edition in production. The GUI can still open without WinGet. This first release requires **English WinGet table output** and fails closed when headers cannot be understood. Tested locally with WinGet 1.29.380. Microsoft Store packages and offline installers are not supported. Internet or appropriate provider connectivity is required.

Run as the Windows user whose applications and bookmarks you intend to manage. Sterling does not run WinGet as SYSTEM or silently install package managers. Individual installers may show UAC. Do not launch under another administrator's credentials when managing the customer's per-user software. Chocolatey machine operations may require an elevated engineer session; in-place Sterling self-updates refuse elevation.

## Local workflow

**Find & deploy:** select a provider, search, and tick applications. The deployment basket survives subsequent searches and can combine providers. Scope and version policy are editable in the basket. `unknown` means let the provider select its default scope; explicitly select `user` or `machine` where needed. Install selection creates a sequential tracked job. Successful and skipped items are not repeated by Retry failed items. Stop after current package leaves the active installer alone and cancels remaining work. Closing during a job is blocked.

**Installed & updates:** refresh inventory to query WinGet separately for user/machine installations, local Chocolatey records and uninstall registry entries. Provider match means currently manageable, not proof of installation origin. Registry-only entries remain manual review; no fuzzy guess is promoted into an exact ID. Duplicate instances are held for review. Same-normalised-name overlaps between providers are marked for review; this is conservative reporting, not a complete cross-provider identity resolver. Other duplicates can appear when registry/provider names differ. Review them before acting.

Update ticked and Update all eligible use individual exact package IDs, never `upgrade --all`. Unknown/bounded versions, provider pins, exclusions, known duplicate instances and detected cross-provider overlap are ineligible. Pin-query failure blocks provider upgrades. Each update is rechecked immediately before execution. Exclude business applications that must not be managed here. Exclusions are per provider/package ID across scopes and persist in settings. Uninstall requires a review dialog and does not execute arbitrary registry uninstall strings.

Jobs report each package, exit errors, verification and restart requirements. Sterling never passes restart permission to WinGet or initiates an OS restart; third-party package scripts/installers can have their own behaviour. A successful command whose final state cannot be verified is **Needs review**, not automatically retried. Interrupted operations are restored as Needs review on the next launch. Log files and the last job are retained locally. A per-user process lock serialises Sterling jobs; other deployment tools and other Windows user accounts are outside that lock. Coordinate maintenance windows.

## Approved lists

Start with the editable Standard Workstation list (Chrome, 7-Zip, Acrobat Reader), or create a basket from searches. Save deployment list writes a versioned JSON document named for the workstation/customer. Open it in Lists & restore, choose policy/scope per row, tick entries, and add them to the normal basket. To edit an existing list, load it into the basket, add/remove/change items and save it again.

`Newest` installs the newest available version if absent, upgrades an eligible installed version when the provider reports an update, and skips an already satisfied installation. `Captured` requires the exact recorded version. A different installed version is never silently replaced. There are no arbitrary installer arguments or scripts in imported lists. Imported data never triggers execution until an engineer selects and confirms a job.

## Capture and restore

1. Choose Capture this PC. Review the inventory, untick exclusions and save the ticked capture to an external drive or protected network share. Saving on the Windows volume shows an erase warning; Sterling cannot verify that another copy exists.
2. The versioned `.sterling.json` bundle records names, versions, scopes, exact IDs where known, provider, exclusions and manual-attention status. It contains **inventory, not installer binaries, licences or application data**. Custom software is marked Manual attention / custom installer; custom-installer execution is not implemented.
3. After reinstalling Windows, extract Sterling, configure providers and open the bundle. Choose captured versions or newest versions for the entire list and override individual rows. Unknown captured versions require an explicit choice of newest or manual handling.
4. Check availability, tick items and add them to deployment. Availability is checked again before each install/replacement. Source metadata availability does not guarantee that a vendor still hosts its installer; download failures are reported. There is no fallback to newest when a captured version is missing.
5. Uninstall ticked uses current provider state. Replace with captured version checks package availability **before** uninstalling, then verifies removal and installs the requested version. It can affect settings, application data and activation; it is not an automatic rollback. If installation fails after removal, the app remains uninstalled and the result is logged.

Capture includes current-user and machine software only, not every other user's registry hive. Microsoft Store/AppX inventory outside what WinGet reports, portable apps without registration and some unusual installers may be absent. Check business-critical applications manually before erasing a PC.

## Reviewed application-data recipe

Settings & help offers backup/restore of the **current user's Chrome Bookmarks file only**, from a chosen direct profile folder under their Chrome User Data directory. Close all Chrome processes first. Restore validates JSON structure, asks for confirmation and preserves the prior Bookmarks file alongside it before replacement. It does not merge; Chrome Sync may subsequently reconcile changes. Bookmarks contain private titles/URLs: protect the exported file. Passwords, cookies, tokens and other browser secrets are not collected.

This recipe is a separate explicit action after installation. Data backup for other applications, arbitrary ProgramData capture, registry import, shortcut creation, ordered per-package step presets and step-level retries are not implemented. Copying Program Files is not a software backup. Bundles and bookmark backups are ordinary unencrypted files; use BitLocker/protected storage and your customer data-retention policy.

## Chocolatey configuration

Install and configure Chocolatey through your organisation's approved procedure. Sterling detects the standard `%ProgramData%\chocolatey\bin\choco.exe` location. In Settings, supply an authorised **HTTPS internal/customer repository** or appropriately compliant proxy. Direct `chocolatey.org` repositories, credentials embedded in URLs and query-string credentials are rejected. No source is silently added or changed. Search/install/update explicitly specify this repository. Existing Chocolatey source configuration remains unchanged. Alternate Chocolatey installation paths and interactive repository authentication are not supported in v0.1.0.

Chocolatey's [updated organisational policy](https://blog.chocolatey.org/2026/06/updated-tos/) and [terms](https://community.chocolatey.org/terms) apply from 1 January 2027. Configuring a proxy alone does not establish permission for vendor/endpoint-product use; obtain the necessary licensing/permission and use correctly internalised packages. A package script can still download vendor resources. See the [organisation guide](https://docs.chocolatey.org/en-us/guides/organizations/).

Scoop is not implemented. `IPackageProvider` separates detection, search, inventory, availability and execution so a future provider can be added without changing the job model. It must explicitly model user/global scope.

## Sterling application updates

Startup checks (configurable, notify only) and Check for app update query this repository's latest stable GitHub Release over HTTPS. Drafts and prereleases are ignored. A newer three-part version must have the exact conventional ZIP and `.sha256` filenames. Download & apply requires engineer confirmation, downloads the release, checks its filename-bound SHA-256, rejects unsafe ZIP paths, and stages the package in LocalAppData.

The updater waits for Sterling to exit, saves replaced files to `%LocalAppData%\Sterling\SoftwareCentre\update-backups`, copies the new files and restarts the GUI. A copy failure attempts to restore all changed files; failures are reported in `updater.log`, opened in Notepad. Restore backup files manually if necessary. A power loss/process kill during copying is not a fully transactional update; the original downloaded ZIP and backup remain available for recovery. Old backups and staged downloads are retained; engineers can remove them after verification. Extract into a new folder manually if self-update is unsuitable.

Self-update is limited to non-elevated portable copies in writable ordinary folders; junctions/symlinks and protected installations are rejected or fail without elevating. No privileged executable is installed or launched from a user-writable service location. Binaries are not Authenticode-signed in this release. SHA-256 detects corruption; it is **not an independent publisher signature**. Trust depends on HTTPS and the repository/release account. Protect GitHub publishing access. Private-repository authentication is not built into the updater.

## Service installation, controller and monitoring status

**Unavailable in v0.1.0:** Install/remove service, joining/rollout codes, authenticated device enrolment, revocation, central inventory, remote queues, site/customer groups, LAN/VPN discovery, credential-based remote push, central update approvals, scheduled agent monitoring, auto-update policies and restart orchestration. There are no mock buttons for these features and no installation/setup command to run. The portable local application works independently.

A dependable implementation still requires a separately installed/signed agent with protected Program Files binaries, ACL-protected ProgramData configuration/jobs, authenticated encrypted transport, per-device identity and revocation, limited expiring enrolment codes, and a supported intended-user execution path for WinGet/per-user tasks. SYSTEM must not run the WinGet CLI or restore into the wrong profile. A rollout controller on an engineer laptop would be offline when the laptop shuts down; a persistent Windows host and managed TLS/identity are required for ongoing operation. None of these security boundaries is claimed as shipped.

For NinjaOne coexistence, choose one update-policy owner per application. If NinjaOne controls it, exclude its exact ID in Sterling and do not deploy a competing list. Sterling v0.1.0 has no unattended application-update policy; every package job needs an engineer's confirmation.

## Data and security

Portable binaries stay in the extracted folder. Settings, logs, last-job state, downloads and updater backups live under `%LocalAppData%\Sterling\SoftwareCentre`. Captures/lists go to the chosen location. Registry inventory is read-only. No telemetry, stored administrator credentials or enrolment secrets exist. Process arguments use `ArgumentList` and validated exact IDs, not shell command interpolation. Package providers/installer scripts remain a trust boundary: review your repositories and packages. Raw provider logs may include machine paths or installer diagnostic data; protect them and review before sharing.

## Build and release

On Windows with the .NET 10 SDK and PowerShell 7:

```powershell
dotnet build SterlingSoftwareCentre.slnx -c Release
dotnet run --project tests/Sterling.Tests -c Release
# Optional read-only integration checks, only on a prepared English WinGet PC:
dotnet run --project tests/Sterling.Tests -c Release -- --live-winget
./build/Publish.ps1 -Version 0.1.0
```

The script runs focused tests, publishes both programs self-contained for Windows x64 into a fresh folder, creates the ZIP/checksum, extracts the actual ZIP and starts its WPF render smoke test. `Sterling.App.exe --smoke-test C:\path\smoke.png` renders the real window and exits without provider calls. That check validates startup/rendering; it does not validate third-party installations or a reinstall recovery.

The Windows GitHub Actions workflow repeats build/package/smoke validation on branches/PRs. To release a new version, update Directory.Build.props and release notes, commit, then push the corresponding `vX.Y.Z` tag. The tag build verifies the version and creates the GitHub Release with ZIP and checksum. Never reuse a published version/tag. See [release notes](docs/RELEASE-NOTES.md) for validation evidence and current gaps.
