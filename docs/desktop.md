# Portable Windows desktop

BidParser Desktop is a local, portable WPF host for the same parser and workbook engine used by
the web application. It targets Windows x64 and ships as one self-contained `BidParser.exe`: there
is no installer, separate .NET installation, local server, database, login, or elevation step.

The desktop is intentionally not a second implementation of parsing. `BidParser.Application`
owns host-neutral selection, container validation, automatic detection, wrong-format guidance,
template validation, output naming, and workbook/ZIP generation. Both hosts compose those services
with the concrete registry in `BidParser.Parsing` and the writer in `BidParser.Output`.

## Supported scope

- Every registered non-Dell PDF, XLSX, and XLS format is available. Vendors are alphabetical and
  parser formats preserve registry order.
- Auto detection is offered where `AutoDetectTypes` declares it. Guidance always follows the
  resolved concrete parser.
- CRM-template choices, FX Rate, Uplift, Discount Off MSRP, On Cost, and Solution ID/SAID splitting
  are derived from parser and template capabilities; remote configuration cannot add capabilities.
- Validation mismatches and parser warnings remain saveable through the explicit **Save anyway**
  action.
- Dell is excluded from desktop v1. Dell quotes must be acquired through the authenticated Quote
  API in the web host; accepting manual Dell JSON would create a different contract.

The desktop processes the selected source in place and retains the parsed result only in memory.
It writes nothing until the user chooses a destination. It creates no quote copy, history row,
metric, registry value, service, scheduled task, or configuration cache. The last successful save
folder and update dismissal last only for the running process.

## User workflow

1. Select a vendor, file type, and CRM template. The initial vendor prompt is **Select vendor** and
   the file-type control remains disabled until a vendor is chosen. Auto is selected where
   available; a vendor with one concrete format selects it automatically.
2. Enter only the numeric options shown for that selection. FX must be greater than zero; required
   percentages must be present; On Cost is optional.
3. Choose or drop one source file. The extension, magic bytes, parser MIME set, and 10 MiB limit are
   checked before parsing.
4. Select **Parse quote**. Existing synchronous parsers run away from the UI thread. Cancellation
   is checked around selection, detection, parsing, splitting, and writing; cancellation inside one
   synchronous parser call is best-effort and its eventual result is discarded.
5. Review the persistent result area and choose **Save** or **Save anyway**. The save
   dialog uses the canonical `OutputNaming` filename and offers XLSX or ZIP as appropriate.

Bundled/active vendor defaults apply on initial vendor resolution and every vendor change. Manual
edits survive parser/template changes within that vendor. A remote default arriving after startup
updates only untouched visible fields; switching away discards that vendor's session edits. Reset
clears file/result/error state and dirty flags, reapplies active defaults, and retains the current
vendor/parser/template. FX is normalized to four decimal places and percentages to two on focus
loss; non-numeric and negative values are rejected without imposing an arbitrary 100% ceiling.

Saving uses a GUID-named staging file in the destination directory. The completed, closed file is
moved into place only after the destination is checked again. A conflict offers overwrite or a
numbered copy, and staging files are deleted after success, failure, or cancellation.

Keyboard shortcuts are `Ctrl+O` for the source picker, `Escape` to cancel/close/reset as applicable,
and `Ctrl+Shift+C` to copy sanitized failure details. Failure details contain identifiers, stage,
exception type, application version, and timestamp—not stack traces, quote contents, credentials,
or unnecessary source paths.

## Themes and accessibility

The 960×620-DIP window has an 840×560 minimum, standard Windows chrome, a fixed 320-DIP settings
column, star-width result workspace, and bottom command bar. Light and dark resources follow the
Windows app-mode preference and update during the session; Windows high-contrast mode switches to
the `SystemColors` resource set. No theme setting is persisted.

Inter Regular and SemiBold are packaged WPF resources with Segoe UI fallback. The application
manifest declares Per-Monitor-V2 awareness. Controls carry accessible names, logical tab order,
keyboard actions, and live-region status updates. Before release, manually verify both themes,
high contrast, keyboard/focus/Narrator behavior, and 100%, 125%, 150%, and 200% display scaling on
Windows.

## Remote configuration and trust boundary

Two schema-v1 documents are embedded in the executable and validated synchronously before the
window opens:

- `{ "schemaVersion": 1, "guidanceMessages": [...] }`
- `{ "schemaVersion": 1, "vendorDefaults": [...] }`

After the usable window is shown, three independent five-second operations request:

- `https://raw.githubusercontent.com/regalen/BidParser/main/config/guidanceMessages.json`
- `https://raw.githubusercontent.com/regalen/BidParser/main/config/vendorDefaults.json`
- `https://api.github.com/repos/regalen/BidParser/releases/latest`

Each request uses the application-lifetime `HttpClient`, system proxy and TLS behavior,
`BidParser/<version>` User-Agent, `ResponseHeadersRead`, no redirects, no credentials, and
no retries or disk cache. Configuration responses are capped at 256 KiB and release metadata at
128 KiB. One failure does not affect either of the other operations; the embedded configuration or
no-update state remains silently active.

Only integer `schemaVersion: 1` is supported. Unknown JSON properties and references to parsers or
vendors unknown to the installed build are ignored. Duplicate known assignments, malformed known
entries, unsafe guidance, negative/over-precision defaults, or another schema version reject the
entire affected remote document. Guidance accepts plain text plus `<p>`, `<strong>`, `<b>`, `<ul>`,
and `<li>` with no attributes or namespaces. DTDs, external entities, links, images, styles, scripts,
event handlers, XAML, and other markup are rejected.

Public JSON may change safe wording and numeric prefills only. Parser anchors, algorithms,
capabilities, validation, output mapping, filenames, CRM rules, endpoints, and executable behavior
remain compiled code.

## Updates

The desktop reads its exact `AssemblyInformationalVersion`, parses it with `NuGet.Versioning`, and
compares it with the public repository's latest stable GitHub release. One leading `v` is removed
from the release tag and build metadata does not affect precedence. An invalid version, older/equal
release, 404, timeout, rate limit, malformed response, or blocked GitHub produces no notification.

A newer release shows a dismissible bar for the current session. **View update** opens a URL built
from the fixed public repository base and the validated tag. The application never trusts an API-
supplied download URL and does not download, install, replace, elevate, or auto-update itself.

## Build and publish

Linux can compile the Windows target because the project sets `EnableWindowsTargeting`, and the
configuration/update tests are deliberately cross-platform:

```bash
dotnet build src/BidParser.Desktop/BidParser.Desktop.csproj --configuration Release
dotnet test tests/BidParser.Desktop.Configuration.Tests/BidParser.Desktop.Configuration.Tests.csproj
```

The release executable must be published on Windows so the SDK writes the application icon and
Win32 version resources correctly:

```powershell
dotnet publish src/BidParser.Desktop/BidParser.Desktop.csproj `
  -p:PublishProfile=win-x64 `
  -p:BidParserVersion=1.0.0
```

The profile is self-contained `win-x64`, single-file, includes native libraries for self-extraction,
does not trim or require ReadyToRun, and embeds symbols. The publish directory must contain exactly
`BidParser.exe`. Native runtime components may self-extract beneath `%TEMP%/.net`; quote data is
not extracted there.

All production code, configuration fallbacks, fonts, license text, and brand assets have tracked
copies under `src/BidParser.Desktop`; no build or runtime path may point into the handoff. It can be
deleted without changing the project.

`build/verify-desktop-publish.ps1` checks the one-file contract, exact ProductVersion and numeric
FileVersion, launches the executable long enough to catch startup failures, and emits the SHA-256
used in both release descriptions.

## Release prerequisites and runbook

The public remote-configuration documents are tracked in this repository:

```text
config/
  guidanceMessages.json
  vendorDefaults.json
```

A valid `vMAJOR.MINOR.PATCH` tag on `main` gates both artifacts through one version, publishes the
Docker image, builds/tests/verifies `BidParser.exe` on `windows-latest`, then creates the GitHub
Release in `regalen/BidParser`. The release carries `BidParser.exe`, `BidParser.exe.sha256`, portable
usage instructions, unsigned-build warning, and the common changelist. The workflow uses the
repository `GITHUB_TOKEN` with `contents: write` and `packages: write`; no external release token
is required.

Current readiness must be checked immediately before release:

```bash
curl -fS https://raw.githubusercontent.com/regalen/BidParser/main/config/guidanceMessages.json
curl -fS https://raw.githubusercontent.com/regalen/BidParser/main/config/vendorDefaults.json
```

Do not create the first tag while either URL is unavailable or the Windows smoke-test/allowlisting
decision is unresolved.

## Manual Windows acceptance

Run the published executable from a normal user-writable directory on representative corporate
Windows x64 without a separately installed .NET runtime or elevation. Verify:

- Explorer, taskbar, Alt+Tab, fonts, light/dark/high-contrast themes, supported DPI scales,
  keyboard/focus/Narrator behavior, default size, and minimum size.
- Representative PDF, XLSX, and XLS inputs; Auto and wrong-format selection; mismatch and Zebra
  cancellation warnings; HP/Lenovo/SAID split ZIPs; conflict overwrite/copy; and workbook integrity.
- Valid public configuration, offline fallback, unsupported-schema fallback, independent endpoint
  failures, update notification/view/dismissal, blocked GitHub, and rate limiting.
- A fresh download from the public release, plus absence of quote copies, history, database,
  registry writes, services, scheduled tasks, and leftover staging files.

See [troubleshooting.md](troubleshooting.md#portable-windows-desktop) for known desktop failure modes.
