# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

French-speaking home users on Windows 10/11, often non-technical or semi-technical, maintaining their own PC. They usually arrive when disk space is shrinking or the machine feels cluttered, and they want to reclaim space without risking personal files or breaking Windows. French is the source language; an English translation is being introduced progressively, starting with the main window, settings, disk space, PC health, startup, uninstall and updates.

## Product Purpose

OpenCleaner is a free, open-source Windows utility for inspecting temporary files, caches, and disk usage, then cleaning only what the user explicitly confirms. Every file deletion is guarded and preceded by a verified encrypted backup, and cleanups can be restored. Success means the user reclaims meaningful space with confidence, understands what is being removed, and never loses data they did not choose to lose.

## Positioning

OpenCleaner pairs safety-first, reversible cleaning with full transparency: encrypted backups with verified restores and guarded paths, plus open-source code, no telemetry, no account, and no cloud dependency. Either promise alone is easy to copy; together they are the position a closed, growth-oriented cleaner cannot truthfully occupy. Feature breadth follows that position rather than chasing CCleaner parity.

## Operating Context

- Windows 10/11 x64 desktop application built with WPF on .NET 10; it is not a browser app and has no web runtime.
- Core workflow: choose a category → **Analyser** (read-only) → review results and risk level → optionally narrow the selection → confirm **Nettoyer** → restore from **Restauration** if needed.
- Backups live under `%LOCALAPPDATA%\OpenCleaner\Backups`; encryption keys are protected with user-scoped DPAPI and a 256-bit recovery code is shown once at first launch.
- The app runs in the user's context. Scheduled cleaning is a Windows task restricted to low-risk files in allowed categories; startup changes cover HKCU and the Startup folder, with machine entries read-only without elevation.
- Network access is limited to Velopack update checks against GitHub and user-launched `winget` software updates. There is no application telemetry.
- Maintenance and evaluation use the audit reports in `docs/`, the CCleaner comparison, release notes, CI (build, tests, NuGet audit), the WPF preview tool, and Velopack packaging.
- Product copy, UI labels, documentation, and the public site are written in French.

## Capabilities and Constraints

Confirmed capabilities: quick clean; system and browser caches (Chrome, Edge, Brave, Firefox, Opera, Vivaldi); app caches (Steam, Office, Thunderbird, npm, Yarn, NuGet, pip, Gradle, VS Code, Teams, Discord, Spotify, JetBrains, Cursor, Claude, ChatGPT, plus Slack, Notion, WhatsApp, Signal, Zoom, Microsoft Store, OneDrive and Crashpad caches or logs); SHA-256 duplicate finder (bounded parallel full hashing; multi-hard-link files are never proposed because removing one name frees nothing); privacy trace and secret-file findings requiring manual review; disk space analyzer with a proportional treemap, instant drill-down through the scanned tree, a folder list, the largest files, and installed applications mapped to the measured folders with a flagged registry estimate when a folder was not measured; PC health score (100 points across storage, memory, startup, maintenance, cleanliness) with explained findings, read-only cache measurement, direct safe actions, a path-free 30-day cleanup history, a local daily free-space trend, and a path-free JSON diagnostic export; recycle bin, and DNS cache; uninstaller launcher; startup manager for HKCU and Startup-folder items plus machine HKLM Run/RunOnce keys, with registry backup, per-operation elevation and an estimated impact; `winget` software updates; three-pass secure file erasure; scheduled cleaning restricted to Safe files in whitelisted categories, including application caches, with the last run and next occurrence visible in Settings; user-defined folder exclusions enforced at scan and delete time; conservative HKCU registry cleanup with export before modification; locally persisted theme and low-space alert threshold.

Confirmed constraints: This is not an antivirus; privacy findings are leads, not verdicts. `C:\Windows` is protected except `Windows\Temp`; `ProgramData`, other user profiles, roots, network/device paths, 8.3 aliases, alternate data streams, and links/junctions are refused. Every deletion is revalidated against the last scan and backed up before removal, except explicitly chosen secure erasure. User exclusions are absolute paths without wildcards, stored locally in plain JSON, cover their subfolders, and are enforced both during scans and by the guardian before any backup or deletion; system protections still take precedence. The app never force-closes browsers and never restarts automatically to apply updates. Windows Update cache cleaning, driver updates, forced process suspension, cloud APIs, deleted-file recovery, whole-disk free-space wiping, external DLL extensions, `bin`/`obj` artifact deletion, `node_modules`, Office recovery/sync files, and Docker contexts are out of scope. The disk analyzer is read-only, ignores links, groups the smallest tiles beyond 80, and its sizes can overstate hard-linked, compressed, or system files. Its application view only maps registry install locations under the scanned root, may repeat a shared folder, and flags sizes that fall back to the registry estimate. Duplicate results exclude files with multiple hard links, since removing one name frees nothing. The application-cache category targets only cache and log folders with relative paths; running Electron apps are skipped for those targets, and portable or version-specific layouts may not all be found. Startup impact is an estimate from executable size and location, not a measured boot time. Machine startup changes require an administrator prompt and are limited to HKLM Run/RunOnce values with a verified registry backup; the elevated helper accepts only validated requests and never runs arbitrary commands, and the Common Startup folder stays read-only. PC health scores use documented, deliberately simple thresholds and cache measurement is capped at five seconds. Cleanup history counts only file cleanups journaled with a transaction, reports deleted volume rather than net free space because backups remain on disk, and is pruned after 30 days; registry changes and secure erasure are not included. The free-space trend records at most one point per volume and local calendar day, only when the health page is opened, in a local unencrypted JSON file that contains no paths. Localization is in progress: French is the source language and English covers the main window, language selector, risk labels, sizes and relative dates; the remaining views are still being migrated. The diagnostic export contains drive letters, measurements and findings but no file paths or usernames, and is written only where the user chooses. A non-blocking startup banner points to the health page when a known volume has less than 10% free space; it only reads measurements and never cleans. The duplicate finder hashes at most four files concurrently. Preferences store only the theme and the low-space alert threshold (5–25%, default 10%) in local plain JSON; unreadable or out-of-range values fall back to normalized defaults. The scheduled-run journal keeps the last 200 runs with date, file count, concerned volume and error count only; a hard process kill can prevent the final entry. The disk treemap supports arrow-key selection, a visible focus ring and Enter activation. Localization is in progress: French and English catalogs exist, English currently covers the main window, settings, disk space, PC health, startup, uninstall and updates while the other views migrate; French remains the source language.

## Brand Commitments

- Name: **OpenCleaner**, under the MIT license.
- Existing tagline: “OpenCleaner — De l’air pour votre PC.”
- Voice: plain, calm French; explicit about risk and irreversibility; no growth-hack urgency, no dark patterns.
- Free and open source, with no advertising, no account, and no application telemetry.
- Safety is visible in the product: explicit confirmations, risk labels, backups, and restore paths are part of the identity, not optional polish.

## Evidence on Hand

- Product and security documentation: `README.md`.
- Audits dated 2026-10-14, 2026-10-26, and 2026-10-29 in `docs/`, including the 290-test validation (247 Core, 28 plugins, 15 UI), the localization catalog tests, and the live read-only health, history, trend, and disk scans used for 1.4.0. Localization design and migration status: `docs/LOCALIZATION.md`.
- Feature comparison with CCleaner: `docs/CCLEANER-COMPARAISON.md`.
- Release notes for 1.1.0 through 1.3.0 in `docs/releases/`.
- Real UI captures in `docs/preview/` and `artifacts/ui-preview/`.
- Limits documented in the audits: some third-party cache paths remain unvalidated on real installations; tests use temporary fixtures; the SQLite transaction journal is neither encrypted nor atomic with NTFS; unsigned builds may trigger SmartScreen.

Absent and not to be fabricated: testimonials, customer logos, user counts, download figures, pricing tiers, performance benchmarks, press coverage, third-party security certifications, or claims of complete CCleaner parity.

## Product Principles

1. **Never destroy without a verified way back.** A backup must be readable and authenticated before deletion; secure erasure is the one explicit, warned exception.
2. **Show before acting.** Scanning is read-only, risk is labeled in the user's language, and destructive actions require an explicit confirmation.
3. **The user stays in control.** No forced browser closes, no automatic restarts, no silent updates or background deletions.
4. **Local and private by default.** No account, no telemetry, no cloud dependency; network calls are user-initiated.
5. **Refuse ambiguity instead of maximizing bytes.** Protecting system-critical or uncertain paths matters more than reporting a larger cleanup number.

## Accessibility & Inclusion

No formal accessibility standard has been confirmed. Existing audits require visible keyboard focus, readable contrast, explicit empty states, French risk labels, and usable layouts at minimum window sizes; future work should preserve those. Localization is planned, so user-facing strings should remain separable from logic even while French is the only shipped language.
