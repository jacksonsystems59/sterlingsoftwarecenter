# Sterling Software Centre v0.1.2

Download **SterlingSoftwareCentre-v0.1.2-win-x64.zip** and its matching `.sha256`. Verify, extract all files, and launch `Sterling.App.exe`. The Windows x64 package includes the .NET runtime.

## Update from v0.1.1

The released v0.1.1 supports this update. Open Settings & help → Check for Sterling Software Centre updates → Download and update → review → acknowledge → Start. The helper checks SHA-256, backs up replaced binaries, preserves captures/presets/settings/logs, restarts and verifies the displayed version. No service settings are changed. Updates remain explicit and non-elevated. Manual alternative: extract into a fresh folder and reopen existing captures. The post-publication validation addendum records the real v0.1.1 → v0.1.2 result.

## Software workflows

- Collapsible Common applications grouped into Browsers, Media and Office, with exact maintained WinGet IDs and installed status. Microsoft 365 Apps for enterprise stays unavailable because the remote installation configuration was not validated; no other Office product is substituted.
- Local typing suggestions with keyboard selection, plus asynchronously loaded exact-package publisher, description, version, source and vendor/package links.
- Opening Installed & Updates starts a scan with completion/error status. Green rows highlight eligible updates. Update ticked and Update all eligible now start directly in Jobs, without Review. Pins, exclusions, unknown/non-comparable versions, non-newer versions and uncertain matches remain held.
- Installation, uninstall, replacement and restore retain embedded review. Captured software is grouped into verified automatic installation versus manual attention. Captures retain all rows. Older comparable captured versions offer newest, continue captured, or cancel, with matched advisory information.

## Recovery and Windows tools

- Separate Browser Backup detects profiles for the selected Windows user and creates integrity-checked backups at the chosen destination. Chrome/Edge/Brave bookmarks and two safe UI settings have reviewed restore recipes. Firefox snapshots use Firefox's manual bookmark restore chooser; xulstore UI settings can be restored. Extension information is an inventory only. Passwords, cookies and signed-in sessions are excluded. Unsupported data and locked profiles report failures explicitly. Unrelated applications no longer show Chrome controls.
- Chocolatey setup accepts an approved local installer nupkg with an expected SHA-256, uses UAC, verifies installation and disables the default public source. Configure the authorised internal/customer source once for subsequent searches.
- Configurable source-check intervals and cached tracked-ID comparisons, with last/next checks and honest stale/error reporting. This is not a full-catalogue comparison or proof of publisher ownership changes.
- Dated authoritative security findings use exact product/edition/platform/version matching. Coverage is deliberately limited; no match displays “No verified security information available”, not an assurance of security.
- Windows Health provides separate DISM checks/repair, SFC and a full sequence, with actual output, conservative outcomes and exportable logs. No automatic reboot.
- **Working PrinterBackup.zip capture/open/review/restore**: versioned manifest, human-readable inventory, integrity checks, printer-class driver export, supported ports/queues/settings and optional user defaults/preferences. Existing queues default to Skip; explicit replacement is reviewed. Driver/port conflicts and WSD/IPP/vendor limitations are reported per printer. Print Management detection/open/optional-capability setup is on the same screen.
- Five-second splash with background release checking and an orange “Update to vX.Y.Z” indicator.

## Performed validation

- 82 focused core checks cover package IDs, update eligibility, capture categorisation/compatibility, browser selection and secret exclusion, advisory ranges, health outcomes, printer restore planning, integrity failure, updater rollback and preservation.
- 17 WPF workflow checks include direct updates without Review, installation cancellation, browser-control visibility, suggestions, common applications, update indicator and ordered data restore.
- Six read-only live Windows UI checks exercised real WinGet inventory/search/details, automatic scan entry, selected-user browser detection and printer inventory.
- A real elevated Windows integration test created a dedicated TCP/IP queue using Microsoft Print To PDF at documentation address 192.0.2.123, exported its printer driver/configuration into PrinterBackup.zip, extracted and restored it, verified queue/port/driver state and existing-queue Skip, then removed the test queue/port. It also captured the existing user default and detected Print Management. No print job was submitted.
- Actual DISM CheckHealth was run; its “component store is repairable” output was retained and is classified as corruption found despite exit 0. No Windows repair was run.
- The release script validates the extracted self-contained ZIP, WPF startup/workflows, executable icon and approximately five-second splash. GitHub's Windows runner repeats these checks.

## Limits and unverified scenarios

No clean customer-VM/reinstall trial, physical printer output, new third-party driver installation, shared-server reconnect, actual default-printer change, or vendor preference equivalence was tested. Per-user preferences may be unavailable or require manual verification; those results are partial, not silently successful. Unsupported WSD/IPP/vendor monitors need rediscovery or vendor software. Print Management capability installation from an absent state and approved Chocolatey installer deployment were not performed on this PC. Full DISM/SFC repair commands were implemented but not run here.

WinGet requires English output. Security coverage is a small vetted dataset, not a comprehensive vulnerability scan. Firefox bookmark restore is manual. Backups are unencrypted and binaries are unsigned. A power interruption during self-update may require recovery from retained backups. Remote agent/controller/service deployment remains outside this local-mode release.
