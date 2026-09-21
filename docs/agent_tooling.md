# Agent tooling (MCP) — detailed reference

Companion to the **Agent tooling (MCP)** section of `AGENTS.md`, which carries the rules. This file
carries the detail: what each server exposes, and how to choose between them.

Two MCP servers are configured for every agent in `.mcp.json` at the repo root: **`roslyn`**
(semantic C#/.NET analysis over `BidParser.sln`) and **`playwright`** (a live browser). Use
whichever answers the question fastest — they replace guesswork, not the build. Neither is
mandatory.

## Roslyn MCP

The solution is `BidParser.sln`; point the tools at it (or at the `.cs` file under analysis).
Prefer Roslyn over text search whenever the question is about *meaning* rather than *characters*.

- **Find/understand symbols** — `search_symbols`, `get_symbol_info`, `go_to_definition`,
  `get_document_outline`, `get_type_hierarchy`.
- **Relationships** — `find_references`, `find_implementations` (e.g. every `IParser`
  implementation), `find_callers`. Use these instead of grepping a method name: text search misses
  overrides and interface dispatch, and drowns in unrelated matches.
- **Diagnostics** — `get_diagnostics` for compiler and analyzer errors on a file or the solution,
  before reaching for a full `dotnet build`. `diagnose` is a different tool: it reports the MCP
  server's own health and workspace state — use it when Roslyn itself misbehaves, not to check
  your code.
- **Refactoring** — `rename_symbol`, `change_signature`,
  `extract_method`/`extract_interface`/`extract_base_class`, `move_type_to_file`,
  `inline_variable`, `remove_unused_usings`, `add_missing_usings`, `format_document`, and the other
  solution-aware rewrites. These update every call site; hand-edited renames do not.
- **Flow/metrics** — `analyze_data_flow`, `analyze_control_flow`, `get_code_metrics`.

**Working order for C# tasks:** ask Roslyn for the symbols and relationships first → let that name
the files worth opening → read only the source the task needs → make the change → validate with the
builds and tests in `AGENTS.md` §Commands. Opening with a repo-wide text sweep is the slow path.

**Establish blast radius before a refactor.** Anything touching `IParser`, `LineItem`,
`TemplateLayout`, `CrmWriter`, or a shared `Constants` member fans out across every parser and
writer. `find_references`, `find_callers`, `find_implementations`, and `get_type_hierarchy` give the
real call set — a grep gives a guess. Once the extent is known, prefer the solution-aware rewrites
(`rename_symbol`, `change_signature`) over hand edits. No tool maps project→project references; read
the `.csproj` files for that.

**Cold start is slow.** The first call loads the solution through MSBuild and can take a few
minutes. A slow first response is not a failure — allow for it rather than abandoning the tool.

## Plain text/file search

grep/ripgrep and glob stay the right tools for everything Roslyn does not model:

- Frontend code — `frontend/**` TypeScript, React components, CSS, Tailwind config.
- Configuration and data — `.json`, `.yml`, `Dockerfile`, `docker-compose.yml`, `.env.example`,
  `.csproj`/`Directory.*.props`, migrations SQL, `.github/workflows/`.
- Documentation and specs — `docs/**`, `*.md`, sample/golden filenames under `samples/`.
- String-level questions in C# too: locating a literal, a slug, a log message, or an anchor string;
  sweeping the repo for a term whose type you don't yet know. Grep to find the entry point, then
  switch to Roslyn to reason about it.

## Playwright MCP

Browser validation at implementation time. When a change affects frontend behaviour or a
user-visible workflow, drive the running app (`npm run dev`, or the Docker stack) and look, rather
than assuming. Worth exercising:

- rendering and navigation;
- buttons, forms, dialogs, and dropdowns;
- uploads via `browser_file_upload`;
- the parse workflow end to end (login → vendor/file-type selection → upload → result popup →
  download);
- frontend behaviour that shifted because the API changed;
- reproducing a browser-visible defect, and confirming the fix.

`browser_snapshot` reads the DOM, and `browser_console_messages` / `browser_network_requests`
surface failures a screenshot hides.

It is a manual-testing instrument — one-off, and it leaves nothing behind in the repo. Delete any
`.playwright-mcp/` snapshot directory it writes into the working directory.

### Browser

`.mcp.json` pins `--browser chromium`, which is Playwright's own bundled build. Without it
`@playwright/mcp` defaults to the branded `chrome` channel and fails on any machine without Google
Chrome installed (`Chromium distribution 'chrome' is not found at /opt/google/chrome/chrome`) —
a machine-specific dependency this repository does not take. Install the bundled browser once with:

```bash
npx playwright install chromium
```

Do not swap the pin back to `chrome`, `msedge`, or another channel to work around a local
install — that trades a portable dependency for a machine-specific one.

## MCP browser sessions are not tests

`@playwright/test` is a separate thing: the frontend devDependency for repeatable E2E specs
**committed to the repository**. `frontend/playwright.config.ts` and
`frontend/tests/administrative-enhancements.spec.ts` currently provide five mocked-API checks for
XLSM file selection, parser-capability-driven On Cost, and runtime-configuration interactions. Run them from `frontend/`
with `npx playwright test`; there is no CI job for this harness.

Add a committed spec when it buys durable regression protection:

- critical workflows (login, upload → parse → download);
- a bug fix that could plausibly recur;
- complex frontend interactions;
- behaviour that has broken before.

Do **not** add a permanent spec for every frontend change, and do not write brittle low-value ones
merely because Playwright is available. Never expect an MCP session to persist as a test, or use
`@playwright/test` for a one-off "does this look right" check.

## Changes spanning backend and frontend

Roslyn for the backend impact → make the backend and frontend changes → run the builds and tests in
`AGENTS.md` §Commands → run the app → Playwright MCP over the affected workflow → fix what surfaced
→ re-run both the automated checks and the browser pass before calling it done.

Tooling never replaces building and testing. Roslyn diagnostics are a fast pre-check and a browser
pass is manual evidence — neither substitutes for a green suite or CI.
