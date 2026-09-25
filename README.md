# Sterling Software Centre

Portable Windows x64 software and recovery tools for Sterling Tech engineers. **v0.1.2** uses .NET 10/WPF and includes its runtime. Download the ZIP and matching SHA-256 from [GitHub Releases](https://github.com/jacksonsystems59/sterlingsoftwarecenter/releases/latest), verify the checksum, extract every file, then run `Sterling.App.exe` as the intended Windows user.

## What's new in v0.1.2

- Collapsible Common applications, keyboard-accessible suggestions, exact-package details and vendor/package links.
- Automatic asynchronous inventory when opening Installed & Updates, completion/error status, green eligible-update rows and direct Update ticked/Update all eligible jobs.
- Separate Browser Backup and grouped automatic/manual capture readiness, with explicit older-version choices.
- Chocolatey setup/configuration, tracked source refresh, conservative security-advisory matching, Windows Health, and functional PrinterBackup.zip capture/review/restore.
- Five-second responsive splash and background update checks with an orange version-update indicator.

## Installation and application updates

The ZIP is self-contained; no separately installed .NET runtime is required. Keep the DLLs, `Tools` scripts, JSON catalogues, manifest and updater together. Use a writable ordinary folder rather than a protected folder or junction. Windows 10/11 x64 with a compatible WinGet installation is needed for WinGet operations. WinGet parsing currently requires English output.

Both published **v0.1.0 and v0.1.1 already include self-update**. In v0.1.1 open Settings & help → Check for Sterling Software Centre updates → Download and update, review, acknowledge and Start. Newer releases show an orange indicator next to the version. Updates never install silently. The helper verifies the published filename-bound SHA-256, stages the ZIP, waits for Sterling to close, backs up replaced files, restarts and verifies the displayed version. Settings/logs in `%LocalAppData%\Sterling\SoftwareCentre` and additional capture/preset files are preserved. No service settings are changed.

A copy failure attempts rollback. Power loss/process termination during copying can require manual recovery from `update-backups`. Old backup/staging folders remain until the engineer removes them. Self-update refuses elevation and symlinked/protected installations. Binaries are unsigned; checksum validation is not an independent publisher signature. This GitHub repository is public; no PAT is embedded and private-release authentication is not implemented. Manual alternative: extract the new ZIP into a fresh folder and reopen existing captures.

## Find & Deploy

Common applications are maintained in `assets/common-applications.json`: Google.Chrome, Mozilla.Firefox, Brave.Brave, Microsoft.Edge and VideoLAN.VLC. Their current official WinGet manifests were inspected for unattended methods and scope. The Microsoft.Office manifest is labelled Microsoft 365 Apps for enterprise, but its remote Office configuration and resulting enterprise deployment were not validated, so the common entry is deliberately unavailable. No alternative Office edition is substituted. Use an approved Office Deployment Tool configuration.

Typing offers a small local suggestion catalogue (including Firefox and Malwarebytes); click a suggestion or use Down/Enter to fill the query. Search/Enter performs a search, never an installation. Selecting a result loads exact `winget show` metadata with a six-hour in-process cache. Missing metadata is reported. Vendor/package release notes are distinguished from verified security fixes.

Tick search results to build a persistent basket, or tick common applications and Install selected. Review all packages, IDs, scopes, versions and warnings inside the main window. Acknowledgement and Start are required for installation, uninstall, replacement and restore. Back/Cancel makes no changes. Jobs run sequentially, verify provider state, continue after failures, support stop-after-current and retry only failed steps. Unverified successful commands require manual attention rather than automatic replay.

## Installed & Updates

Entering the tab starts an asynchronous provider/registry scan. Refresh inventory rescans immediately. Selection is retained by exact package identity. Green rows indicate eligible updates; versions and exclusions remain visible. **Update ticked and Update all eligible start directly in Jobs**, without Review or confirmation. Pressing these buttons requests the eligible package updates and their package licence agreements. UAC can still appear.

Eligibility requires an exact manageable match, a strictly newer comparable numeric version, no pin, no policy exclusion and no detected provider ambiguity. Uncertain/bounded/non-comparable versions are held; selected ineligible entries are reported as skipped. Provider state and pins are rechecked before execution. Sterling never uses `upgrade --all` or requests a PC reboot. Third-party installers remain responsible for their own behaviour. Coordinate with other management tools such as NinjaOne and choose one update-policy owner per application.

Application details show installed/available versions, source metadata and release notes. Security coverage is intentionally limited: `assets/security-findings.json` contains vetted product/edition/platform/version ranges and authoritative dated links. A non-match displays **No verified security information available**, which does not mean secure. The bundled Firefox finding covers a conservative subset of affected Release versions; ESR is not conflated with Release. This is not a vulnerability scanner or complete CVE feed.

## Captures and browser backup

Capture this PC records current-user and machine inventory. **Save capture retains every captured row**, including manual items. Readiness checking splits the display into automatic install and manual attention with reasons. Automatic items need a known scope, exact available package/version and verified silent installer type or current curated method. Unsupported/ambiguous/custom/interactive or missing versions remain manual. Tick ready items for bulk restore. Choosing an older comparable captured version presents Install newest, Continue with captured version, and Cancel, with any matched advisory.

Schema 2 still opens schema 1 captures from v0.1.0. JSON captures contain inventory and optional companion data, not installers/licences. Move the JSON and its `.data-…` folder together. Capture data operations start unchecked; an explicitly saved deployment preset can preserve its options. Captures do not enumerate all other users' software, every portable application or every Store application. Verify business software before erasing Windows.

**Browser Backup**, a separate area within Capture & Restore, detects profiles under the selected Windows user directory. Tick browsers/profiles and data types, then choose a destination that survives reinstalling Windows. The result is a checksummed BrowserBackup.zip and per-profile report. A selected backup is included with the next saved software capture. Data files are unencrypted; protect them.

Supported recipes:

| Browser | Bookmarks | Settings | Extensions |
|---|---|---|---|
| Chrome, Edge, Brave | Validated Bookmarks JSON; reviewed replacement with prior-file backup | Only bookmark-bar and home-button visibility; merged into existing Preferences | IDs/versions as reference; manual reinstall |
| Firefox | Latest existing `.jsonlz4` bookmark snapshot, with its age reported; restore through Firefox's own bookmark restore chooser | `xulstore.json` UI state | IDs/versions as reference; manual reinstall |

No passwords, cookies, sessions, authentication databases, full profiles or extension binaries are copied. Close all instances/background processes first; locked or missing data is reported per profile. Browser Sync may subsequently reconcile changes. Select the target Windows user and an existing matching browser profile. Install/open the browser once as that user if no profile exists, then close it and detect again. The optional install-before-data action verifies the package job before applying data. Access-denied profiles require the intended user session; Sterling does not impersonate users. Unrelated software never displays Chrome controls.

## Chocolatey and source checks

Settings & help visibly reports Chocolatey status. Install/Configure accepts an administrator-approved local Chocolatey `.nupkg` and its independently obtained SHA-256, explains ProgramData/PATH changes, uses UAC, verifies choco.exe and disables the default Community source. Obtain the approved package through your organisation's distribution process. This path runs the chosen package's installer script; treat it as privileged software.

Configure an authorised internal/customer HTTPS source once; subsequent searches reuse it. Direct Community endpoints and embedded credentials are rejected. Advanced source configuration remains available. A proxy alone does not establish permission for MSP/endpoint-product use. Review [Chocolatey terms](https://community.chocolatey.org/terms), [organisational clarification](https://blog.chocolatey.org/2026/06/updated-tos/) and [setup documentation](https://docs.chocolatey.org/en-us/choco/setup/).

Refresh sources now is separate from inventory scanning. A configurable 1–168-hour interval updates WinGet source metadata and checks up to 150 common/installed tracked IDs, plus the configured Chocolatey source. The UI records attempts, last complete check, next due date, errors and stale state. Comparisons concern tracked IDs only; a failed query does not imply package removal, and publisher/source metadata changes do not prove ownership transfer. Chocolatey publisher metadata is unavailable; source connectivity is checked explicitly.

## Windows Health

Settings & help provides DISM CheckHealth, ScanHealth, RestoreHealth, SFC /scannow and a full sequence. Normal Windows UAC is used. One operation runs at a time, with actual output, durable/exportable logs and result interpretation. Successful process launch or unknown/localised output is never labelled healthy. Full sequence uses CheckHealth, ScanHealth, repair if indicated or explicitly selected, then SFC; an unrepairable store or failed repair holds SFC. RestoreHealth can change Windows and need an online repair source. No automatic reboot.

See [Microsoft image repair guidance](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/repair-a-windows-image?view=windows-11).

## Other → Printing

Detect/Open Print Management checks the local console. Install/Enable queries the actual Print.Management.Console capability on the running Windows image, uses UAC and verifies installation. Unsupported editions/source failures are reported. Opening the MMC console remains a manual migration/inspection alternative.

Capture printers lists queues, connections, driver versions/architecture, ports, supported print tickets and the current user's default. Tick queues, choose Backup printers and drivers and select an external/network destination. The reviewed elevated export produces **PrinterBackup.zip** with a versioned manifest, Inventory.txt, SHA-256 integrity inventory, supported preferences and applicable printer-class driver exports using PnPUtil. It never exports every system driver. Missing preferences/driver exports are explicitly reported.

Open the ZIP to verify integrity and Windows architecture/version. Select printers and review each conflict decision before restoring. The working restore path stages applicable missing drivers, creates supported TCP/IP/local ports, recreates queues/shared connections, applies supported print tickets, then selected-user defaults/preferences. Existing queues default to Skip; Replace queue is explicit. Conflicting drivers/ports are retained for manual resolution. Results distinguish restored, skipped, partial and failed items. No print job or reboot is initiated.

WSD/IPP/vendor-specific monitors require rediscovery/vendor software; server connections need the server and permissions. Server sharing/ACLs are not recreated automatically. Driver-version differences block preference restoration. Per-user DEVMODE export/restore is best-effort; it is never assumed equivalent across vendors/drivers. A different UAC identity cannot restore the selected user's defaults/preferences. Use Print Management/vendor tools for remaining items. Bundle checksums detect accidental changes, not a maliciously rewritten bundle; only restore trusted backups.

Implementation references: [PrintManagement cmdlets](https://learn.microsoft.com/en-us/powershell/module/printmanagement/), [PrintUI preference export/restore](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/rundll32-printui), [PnPUtil driver export](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax), [Windows capabilities](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/features-on-demand-non-language-fod?view=windows-11).

## Build, tests and scope

```powershell
dotnet build SterlingSoftwareCentre.slnx -c Release
dotnet run --project tests/Sterling.Tests -c Release
# Optional live, read-only WinGet checks:
dotnet run --project tests/Sterling.Tests -c Release -- --live-winget
./build/Publish.ps1 -Version 0.1.2
# Elevated printer integration test: creates/restores/removes its own test queue only
./build/Test-Printing.ps1 -OutputDirectory C:\Temp\Sterling-Printer-Test
```

Publish tests the extracted ZIP with core checks, WPF workflow checks, executable icon extraction and five-second startup rendering. `--live-ui-tests <directory>` additionally exercises read-only live inventory/search/details/browser/printer detection and saves screenshots. The printer integration test requires the Microsoft Print To PDF driver and normal elevation; it tests a dedicated TCP/IP queue without sending a print job. See release notes for performed tests and limits.

This remains a local-PC application. Remote agent/service installation, controller enrolment, LAN push, scheduled package-update policies, generic registry/shortcut recipes and arbitrary custom installers are not implemented. There is no background Windows service or network listener. Original editable branding and export instructions remain in [assets/branding](assets/branding/README.md).
