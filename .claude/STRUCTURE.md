# STRUCTURE.md

Detailed file-by-file map of the AI Usage Tracker. This complements `CLAUDE.md` (which
covers the big-picture architecture and conventions) with the concrete inventory: every
source file's responsibility, the full bridge-action catalog, the DB schema, and the
settings keys.

> **Keep this in sync.** When you add, remove, rename, or repurpose a file, a bridge
> action, a DB table/column, or a settings key, **update this file AND `CLAUDE.md` in the
> same change.** `PROGRESS.md` remains the feature-history log; these two are the reference.

---

## Directory tree (source only — `bin/`, `obj/` are build output)

```
AIUsage/
├── AIUsage.csproj            # net10.0 WinExe; embeds wwwroot + appicon; NuGet refs; <Version> + git-commit/build-date stamping
├── Program.cs                # entry point, window setup, handler registration, CLI verbs
├── WebAssets.cs              # embedded-resource extraction (web assets + icon)
├── CLAUDE.md                 # architecture & conventions (read first)
├── PROGRESS.md               # feature history / decisions / first-run checklist
├── PLAN-AI-USAGE-TRACKER.md  # original implementation plan
├── REVIEW-AI-USAGE.md        # plan review notes
├── LICENSE                   # MIT
├── THIRD-PARTY-NOTICES.md    # vendored JS + NuGet dep licenses
├── .claude/
│   ├── STRUCTURE.md          # this file
│   └── settings.local.json   # Claude Code local settings
├── Bridge/
│   ├── MessageRouter.cs      # JSON request/response bus (WebView ↔ .NET)
│   └── Handlers/             # one static Register(router) class per domain
│       ├── SessionHandlers.cs
│       ├── ManualHandlers.cs
│       ├── TicketHandlers.cs     # tickets.list/fetch/sync/fetchMore, jira.test, clickup.test
│       ├── SettingsHandlers.cs
│       ├── StatsHandlers.cs
│       ├── ExportHandlers.cs
│       ├── LiveCodeHandlers.cs   # Live Code page: tickets, folder, agents, terminal, metrics
│       └── AppHandlers.cs        # app.info (version/commit/build date for the UI footer)
├── Scanner/
│   ├── TranscriptScanner.cs  # incremental JSONL walk, offset/WAL bookkeeping
│   ├── SessionAggregator.cs  # SOLE owner of the transcript schema; aggregates + ReadLive/ContextWindow/ReadDetail
│   ├── ActiveSessions.cs     # top-N recently-active Claude Code sessions (for the metrics panel)
│   ├── FolderSessions.cs     # existing sessions in one folder (Resume Sessions picker)
│   └── TicketKeyInferrer.cs  # branch/cwd/prompt → ticket keys, allowlist filter
├── Terminal/                 # Live Code terminal backend
│   ├── ConPtySession.cs      # pseudo-console session (Porta.Pty wrapper): Start/Write/Resize + Output/Exited
│   ├── ClaudeCommand.cs      # builds+sanitizes the `claude …` lines typed into the shell (quote/control-char safe)
│   ├── ShellResolver.cs      # PowerShell / Git Bash resolution (fallback to PowerShell)
│   ├── AgentCatalog.cs       # lists .claude/agents (project + user + custom dir) with name/description
│   ├── ClaudeCli.cs          # resolves the claude CLI on PATH (install check)
│   ├── GitWorktree.cs        # git-worktree isolation: IsGitRepo / Create / TryRemoveIfClean
│   └── PromptWatcher.cs      # best-effort auto-approve: detects prompts, injects Enter
├── Platform/
│   ├── FolderDialog.cs       # UI-thread-marshalled Photino folder picker (+ manual fallback)
│   ├── MessageDialog.cs      # native Yes/No confirm — for decisions the WebView must not make
│   ├── ClaudeAccount.cs      # subscription plan + usage-reset date from ~/.claude.json
│   ├── ClaudeUsage.cs        # rolling usage-limit bars (Anthropic oauth/usage endpoint)
│   └── AppVersion.cs         # reads this assembly's stamped semver/commit/build-date
├── Data/
│   ├── Db.cs                 # connection open (WAL+FK), portable-first DB path
│   ├── Migrations.cs         # idempotent schema, seeds, SchemaVersion (v8)
│   ├── Rows.cs               # generic Query→dictionaries / Scalar helpers
│   ├── TicketKey.cs          # THE ticket-key regexes (JIRA + ClickUp native): IsValid / Normalize / Require (shared by every writer)
│   └── Repositories/
│       ├── SessionRepo.cs
│       ├── TicketRepo.cs
│       ├── ToolUsageRepo.cs   # agent/skill/mcp/hook counts per session (dashboard charts)
│       ├── SessionDailyRepo.cs # per-session, per-local-day token buckets (rolling-window sums)
│       └── ManualEntryRepo.cs
├── Tickets/                   # provider-neutral ticket abstraction (routes JIRA vs ClickUp)
│   ├── ITicketProvider.cs    # FetchAsync/AssignedAsync + Id/DisplayName/ItemNoun
│   ├── TicketInfo.cs         # TicketProviderIds consts + the provider-neutral ticket record
│   ├── TicketProviders.cs    # ProviderIdFor(key) routing (config-only, no network); For/Enabled construct clients
│   ├── JiraTicketProvider.cs    # wraps JiraClient behind ITicketProvider
│   ├── ClickUpTicketProvider.cs # wraps ClickUpClient behind ITicketProvider
│   └── TicketSync.cs         # provider-neutral lazy background fetch (was Jira/JiraSync.cs)
├── Jira/
│   ├── JiraClient.cs         # read-only JIRA Cloud REST client (+ ADF description parse)
│   └── JiraSiteUrl.cs        # site-URL validation: https-only, host, no userinfo; host-change check
├── ClickUp/
│   └── ClickUpClient.cs      # read-only ClickUp REST v2 client (personal API token, constant host)
├── Settings/
│   └── SettingsStore.cs      # typed settings + DPAPI-protected secrets
├── Export/
│   └── XlsxWriter.cs         # hand-rolled minimal OOXML .xlsx writer
├── Resources/
│   └── appicon.ico           # multi-size icon (BMP frames; exe + window icon)
├── AIUsage.Tests/            # xUnit test project (dotnet test AIUsage.Tests) — NOT in a .sln
│   ├── AIUsage.Tests.csproj  # net10.0; ProjectReference → AIUsage.csproj; xUnit + Sqlite
│   ├── Helpers/TestDb.cs     # fresh in-memory migrated SQLite DB per test
│   ├── TicketKeyInferrerTests.cs # incl. CU- native-branch extraction, gated by clickUpNative
│   ├── TicketKeyTests.cs         # Data/TicketKey validator: JIRA + ClickUp native shapes, Require throwing
│   ├── TicketProvidersTests.cs   # ProviderIdFor routing: native/custom-ID-prefix/default-JIRA
│   ├── TicketHandlersTests.cs    # tickets.sync outcome tally (pure counting, no network)
│   ├── LiveCodeHandlersTests.cs  # OrderByUpdatedDesc/ParseUpdated merge-sort across trackers
│   ├── ClickUpClientTests.cs     # task/list parsing, IsNotFound ECODE mapping, path building
│   ├── ClaudeAccountTests.cs     # plan label incl. zero/none tier, past-reset-date hiding
│   ├── ClaudeUsageTests.cs       # Bars parsing: Enterprise spend-only, Pro rolling windows
│   ├── SettingsAllowlistTests.cs # EffectiveKeyAllowlist / CombineAllowlist unioning
│   ├── JiraSiteUrlTests.cs       # https-only site URL, normalization, host-change → clear token
│   ├── ClaudeCommandTests.cs     # typed-command safety: quote breakout, control chars, caps, shape, TrackerLabel
│   ├── SessionAggregatorTests.cs  # Aggregate(...) + ContextWindow
│   ├── XlsxWriterTests.cs
│   ├── MigrationsTests.cs        # incl. SchemaVersion 8 + Tickets.provider backfill
│   ├── SessionRepoTests.cs
│   ├── SessionDailyRepoTests.cs
│   ├── TokensWeeklyTests.cs      # StatsHandlers.TokensWeeklySql
│   ├── NonTicketProjectsTests.cs # StatsHandlers.NonTicketProjectsSql
│   ├── DataRepoTests.cs      # Ticket (incl. provider column) + ManualEntry repo round-trips
│   └── AppVersionTests.cs    # AppVersion.Parse + stamped-assembly sanity check
└── wwwroot/                  # frontend (embedded into the exe at build time)
    ├── index.html            # shell: sidebar nav + <main id="content"> + script tags
    ├── css/app.css
    ├── lib/chart.umd.js      # vendored Chart.js 4.4.9 (no CDN)
    ├── lib/xterm.js          # vendored xterm.js 5.3.0 (Live Code terminal; no CDN)
    ├── lib/xterm.css
    ├── lib/xterm-addon-fit.js
    └── js/
        ├── bridge.js         # Bridge.call() + Bridge.on() event channel
        ├── app.js            # hash router + window.App helpers (toast/confirm/…) + scan button
        └── views/            # one self-registering module per page (window.Views.*)
            ├── dashboard.js
            ├── sessions.js
            ├── session.js        # session detail page (#session/<id>)
            ├── manual.js
            ├── tickets.js
            ├── livecode.js
            └── settings.js
```

---

## Backend files (C#)

| File | Responsibility |
|---|---|
| `Program.cs` | `[STAThread] Main`: `Db.Initialize()`, parse args (`--route` opens the window on a page; anything else → `RunCli`), build the `PhotinoWindow` (1280×860 restore size, maximized, DevTools on), set icon via `WebAssets.ExtractIcon`, register all handler groups (incl. `LiveCodeHandlers.Register(router, window)`), load `index.html` from the extracted web dir over `file://`. `RunCli` implements `--scan`, `--sql` (read-only), `--set` (incl. `clickup_token` → `SetProtected`), `--pty-test` (ConPTY streaming smoke test), `--envtest` (API-key strip check), `--shelltest` (print resolved shells), `--accounttest` (print plan + usage reset), `--usagetest` (print the Live Code usage bars via `ClaudeUsage.ReadAsync`), `--clickuptest <key>` (fetch one ClickUp task and print it), `--detailtest <sessionId>` (print the session-detail deep re-parse — per-tool/per-model/timing — as fed to the detail page), `--version` (print the stamped semver + commit + build date). `--route` accepts a `page/<param>` form (e.g. `session/<id>`). |
| `Bridge/Handlers/AppHandlers.cs` | Action `app.info` → `{version, commit, buildDate, short, detail}` from `Platform.AppVersion`, read by the sidebar footer on startup. |
| `Platform/AppVersion.cs` | Static class read once from this assembly's attributes: `Semver`/`Commit`/`BuildDate` (parsed from `AssemblyInformationalVersionAttribute` + the `BuildDate` `AssemblyMetadataAttribute`, both stamped by `AIUsage.csproj`), `Short` ("v1.0.0 · 7c7e4f5", degrades to "v1.0.0" without a commit), `Detail` (multi-line tooltip). `Parse(string?)` is public so the split logic is unit-testable without a real stamped assembly. All best-effort — a build outside a git checkout just omits the commit. |
| `WebAssets.cs` | `EnsureExtracted()` copies embedded `web/**` resources to `%LOCALAPPDATA%\AIUsage\web` (overwrites each launch) and returns the path; dev fallback to on-disk `wwwroot`. `ExtractIcon()` writes the embedded `appicon.ico` to `%LOCALAPPDATA%\AIUsage`. |
| `Bridge/MessageRouter.cs` | Owns the handler dictionary and JSON (camelCase) (de)serialization. `OnMessage` parses `{id,action,payload}` on a `Task.Run` pool thread, dispatches, replies `{id,ok,data|error}`. `PushEvent(event, data)` sends unsolicited `{type:"event",…}` messages (streaming channel). Registers built-in `ping`. |
| `Bridge/Handlers/SessionHandlers.cs` | Actions `scan.run`, `sessions.list/detail/assignTicket/confirmLink/removeLink/dismiss/reopen`. `sessions.detail {sessionId}` loads the stored row (`SessionRepo.Get`) + does an on-demand deep re-parse of that one transcript (`SessionAggregator.ReadDetail`) for the exact per-tool/per-model/timing breakdown, folds in `SubagentTokens`, derives the activity category (link category, else edit-vs-read guess — matching the dashboard), builds the `agents`/`skills`/`hooks` lists and an `mcps` list (grouped `{server,tool,count}` parsed from the `mcp__server__tool` tool names), and returns everything the detail page renders (falls back to stored counters if the transcript file is gone → `transcriptAvailable:false`). Validates ticket keys (`^[A-Z][A-Z0-9]{1,9}-\d{1,6}$`, uppercased). Documents the `Task.Run` null-unwrap → canceled-task trap (see CLAUDE.md). |
| `Bridge/Handlers/ManualHandlers.cs` | Actions `categories.list`, `manual.list/create/delete`. |
| `Bridge/Handlers/TicketHandlers.cs` (renamed from `JiraHandlers.cs`) | Actions `tickets.list` (backfills a missing `provider` column value from `TicketProviders.ProviderIdFor` for rows synced before v8), `tickets.fetch`/`tickets.sync` (route each key through `TicketProviders.For`/`.ProviderIdFor`; `sync`'s `Tally` is pure counting — ok/dead/failed/skipped — testable without a network call), `tickets.fetchMore {provider, cursor}` (JIRA pages via `nextPageToken`, ClickUp via a plain page number — both surfaced uniformly as `cursor`), `jira.test`, `clickup.test` (verifies the token, lists workspaces, auto-fills `clickup_team_id` when there's exactly one; deliberately ignores `clickup_enabled` since it's the setup-time check). |
| `Bridge/Handlers/SettingsHandlers.cs` | Actions `settings.get` (never returns token values — write-only; also returns `jiraSiteUrlInsecure`, `jiraEnabled`, and the ClickUp fields: `clickupEnabled`/`clickupTokenSet`/`clickupTeamId`/`clickupCustomIdPrefixes`), `settings.set` (routes `jiraToken`/`clickupToken` to `SetProtected`; **validates `jiraSiteUrl` through `JiraSiteUrl.Normalize` before writing anything** and clears `jira_token` when the host changes, returning `{tokenCleared}`; validates `clickupTeamId` as digits-only and `clickupCustomIdPrefixes` as a comma-separated list of project-key-shaped tokens (`ParsePrefixes`); changing the project key allowlist or the ClickUp Custom Task ID prefixes calls `PurgeDisallowedAutoLinks` — toggling `jiraEnabled`/`clickupEnabled` alone does NOT purge, since it doesn't change which keys are allowed and the scanner can't re-infer a dropped auto-link). `PurgeDisallowedAutoLinks()` removes auto links whose key is outside the **effective** allowlist (user allowlist ∪ ClickUp prefixes; native `CU-` keys are always kept via `TicketKey.ClickUpNativeGlob`), keeping manual/confirmed links, and cleans orphan tickets. |
| `Bridge/Handlers/StatsHandlers.cs` | Action `stats.dashboard` (tiles + all chart datasets: weekly tickets, activity doughnut, top-tickets, non-ticket projects, token/model weekly, type×activity, plus `agentUsage`/`skillUsage`/`mcpUsage`/`hookUsage` — top-12 name→total from `ToolUsage` per category, for the Automation & extensions charts). `tokensWeekly` comes from the extracted `TokensWeeklySql` const (so it's unit-testable): tokens are attributed to the week they were **spent** via `SessionDailyTokens`, sessions with no buckets fall back to their `started_at` week (`NOT EXISTS`, so no double-count and the grand total is preserved), a recursive day spine zero-fills quiet weeks, and `WHERE w.week IS NOT NULL` drops the `(NULL,NULL)` base row an empty DB produces. `nonTicketProjects` comes from the extracted `NonTicketProjectsSql` const (also unit-tested): sessions with **no** `SessionTicketLinks` row (any source), grouped by `project_dir` **case-insensitively** (`GROUP BY lower(...)`, `MIN()` picks the displayed spelling — the same Windows folder appears as both `C:\…` and `c:\…` in transcripts), NULL/empty dir → `(unknown folder)`, top 10 by tokens. |
| `Bridge/Handlers/ExportHandlers.cs` | Actions `export.sessions/manual/tickets`. `BuildWorkbook(what)` (public/testable) builds the same dataset as the page; saves directly to Downloads (fallback Documents) and reveals via `explorer /select` — **not** Photino `ShowSaveFile` (returns null off the UI thread). |
| `Bridge/Handlers/LiveCodeHandlers.cs` | `Register(router, window)`. **Multiple concurrent sessions, one per tab**: a `Dictionary<string, LiveSession>` keyed by a frontend-minted `tabId` replaces the old singleton statics (all reads/writes under `Gate`). `LiveSession` holds `{ Session, ActiveFolder, ActiveSessionId, ActiveModel, LastSessionId, LastFolder, TicketKey, Worktree }`. Actions `livecode.config/saveConfig/tickets/listAgents/pickFolder/pickAgentFile/folderInfo/start/stop/closeTab/resume/reset/attach/metrics/list/running/activeSessions`, `pty.input/resize` — every per-session action requires `tabId` (`RequireTabId`). `start` spawns the shell, strips `ANTHROPIC_API_KEY`, fetches the ticket via `TicketProviders.For(ticketKey)` and types the `claude --session-id <guid> …` kickoff (built by `Terminal/ClaudeCommand.BuildTicket`, which sanitizes + quotes every untrusted value and takes a `TrackerLabel` enum — `Jira`/`ClickUp` — so the kickoff sentence names the right tracker without ever putting fetched text in that label), resolves the permission mode through **`GrantPermissionMode`** (the payload's `autoApprove`/`bypass` is only a *request*: a native `MessageDialog.Confirm` grants it, once per tab per mode, cached in `PermissionGrants` and dropped on `closeTab`; denied/headless/dialog-failure → no elevated mode, and the result carries `permissionMode`/`permissionRequested`/`permissionDenied`), wires a `PromptWatcher` in auto-approve mode, and — when a ticket is selected — auto-links it via `SessionRepo.LinkLiveCodeSession`. `LaunchInPty` captures the `tabId` in the `pty.output`/`pty.exit` events; the exit closure identity-checks (`ReferenceEquals`) so a superseded/stopped session doesn't clobber a newer one (an intentional Stop/Reset disposes with `_disposed=true`, which suppresses the exit event). Finished-status items (`IsDone`, computed per-tracker — JIRA: `JiraTicketProvider.DoneStatuses`; ClickUp: the task's status `type`) are dropped by `MergeForPicker` before ranking/truncating to `ticketCount`, so a done ticket can never occupy a picker slot. A picked **Custom Agent** file (`pickAgentFile`) is installed into `.claude/agents` (`InstallAgentFile`) and the kickoff becomes "Use the &lt;agent&gt; agent to work on &lt;ticket&gt;" (no `--agent` flag — prompt-based invocation); returns `agentUsed`. `config` returns global page state (plan/usage/`claudeInstalled`/`apiKeyPresent`/`lastCustomAgent`+`lastCustomAgentName`, and `ticketsConfigured`/`ticketProviders` — whether any tracker is enabled+configured via `TicketProviders.Enabled()`, and which); `tickets` merges every enabled tracker's assigned items (`FetchAssignedAsync` per provider, oversized, then `MergeForPicker` drops done tickets, `OrderByUpdatedDesc`/`ParseUpdated`-sorts by parsed instant — not raw string, since JIRA's/ClickUp's date formats don't compare correctly as strings — and truncates to `ticketCount`; one tracker's exception is captured per-provider into `errors` rather than blanking the other); `listAgents` lists project + user agents; `metrics {tabId}` returns week tokens (global) + that tab's session tokens & live context % (via `FindActiveTranscript`→`<guid>.jsonl`) + `activeSessions` (top 5). `resume {tabId}` re-launches `claude --resume <lastId> … 'continue'`; `reset {tabId}` sends `/exit`, tree-kills, then restarts a fresh Claude session on the same ticket (reuses `StartTicketSession`); `stop {tabId}` disposes the session but keeps the entry (Resume); `closeTab {tabId}` disposes, removes the entry, and (if the tab used a worktree) runs `GitWorktree.TryRemoveIfClean` → `{worktreeKept, worktreeReason, worktreePath}`; `attach {tabId}` returns that tab's buffered output to replay after navigation; `list` returns all live tabs; `running` returns `{running, count}` for the sidebar dot; `activeSessions` is a scan-free top-5 list. **Resume Sessions**: `sessionsInFolder {folder}` → `{sessions:[{sessionId,label,updated}]}` (via `FolderSessions.List`); `resumeSession {tabId,folder,sessionId,shell,autoApprove,bypass,cols,rows}` types `claude --resume <id>` (interactive, `ClaudeCommand.BuildResumeSession`, no prompt) into the tab's terminal. A supplied `ticketKey` is validated with `Data/TicketKey.Require` before anything is launched or linked (this path used to be the only unvalidated writer of `ticket_key`). **Same-folder isolation**: `folderInfo {folder}` → `{isGitRepo}`; `start`/`reset` accept an `isolation` param — `"worktree"` calls `GitWorktree.Create` and launches in the worktree cwd (transcript + auto-link use it), stores `WorktreeInfo` on the entry, and returns `{isolated, worktreePath, folder}`. `StopSession` preserves `Worktree` (reset reuse + close cleanup). `start`/`reset` share `StartTicketSession`; all launches share `LaunchInPty`; per-tab `LastSessionId`/`LastFolder` survive Stop for Resume. |
| `Scanner/TranscriptScanner.cs` | `Run()` (lock-guarded) walks each scan root's project dirs for `*.jsonl`, skips files older than `backfill_from`, cheap-prechecks `ScanState` (size+mtime), then inside `BEGIN IMMEDIATE` re-reads state, reads complete lines from the saved offset (`ReadCompleteLines` stops before a partial trailing line), aggregates, upserts sessions + auto links, handles shrink/rewrite via full reparse (`ResetCountersForFile` + `DeleteSessionsNotIn`), accumulates that file's per-day token buckets (`SessionDailyRepo.Accumulate`, after the session upsert so the FK parent exists; `SessionDailyRepo.DeleteForFile` runs beside `ResetCountersForFile` on a full reparse), refreshes that file's `ToolUsage` rows (`ReadToolUsage` + `ToolUsageRepo.ReplaceForFile`, set semantics — separate from the token counters), saves the new offset, commits. When the `toolusage_backfill_pending` setting is set (by the v6 migration), a one-time `BackfillToolUsage` pass re-parses every transcript for ToolUsage and clears the flag; likewise `dailytokens_backfill_pending` (v7) triggers `BackfillDailyTokens`, a full re-parse whose `SessionDailyRepo.ReplaceForFile` **replaces** (never adds to) each file's buckets, so it's safe over files this same scan already accumulated and idempotent if re-run. Returns `ScanResult(Sessions, NewFiles, UpdatedFiles, SkippedFiles)`. |
| `Scanner/SessionAggregator.cs` | `SessionAggregate` (all counters additive) + `Aggregate(lines, filePath)`. **The only code that knows the undocumented Claude Code transcript JSONL schema** — put format-drift fixes here; malformed lines are skipped. Ticket-key source priority: branch(0) → cwd(1) → prompt_text(2). `SessionAggregate.DailyTokens` (`day → (In, Out)`, via `AddDaily`) splits the same in/out counters by the local day of each assistant message's `timestamp` — `LocalDay(ts)` parses the ISO-8601-UTC stamp and formats the **local** `yyyy-MM-dd` (null if unparseable; offset-less stamps assumed UTC), matching the `date('now','localtime',…)` the rolling-window queries use. Messages with no timestamp or no in/out tokens get no bucket, but still count in the flat totals. Also `ReadLive(file)` (cwd/model/context tokens), `LastContextTokens(file)`, `ContextWindow(model)` (1M, or 200k for Haiku), `SubagentTokens(mainTranscriptPath)` → `SubagentUsage{InOut, CacheCreation, CacheRead}` (summed recursively across `<sessionId>/subagents/agent-*.jsonl`, the sidechain files the scanner skips; powers the Live Code session Tokens (in+out) + Cache split incl. agents), and `FirstUserPrompt(file, maxLen)` (first string-content user prompt, one line, trimmed — labels the Resume Sessions picker) for the Live Code panels. `ReadToolUsage(file)` → `Dictionary<sessionId, ToolUsageCounts>` (a full-file parse, set semantics; sidechains skipped) feeds the dashboard's automation charts: per-session sub-agent (Agent/Task `subagent_type`), skill (Skill `skill`), MCP-server (the server in `mcp__server__tool`) and hook (`hook_success`/`hook_error` attachment lines) counts. `ReadDetail(file, sessionId)` → `SessionDetail` powers the session **detail** page: a deep single-file re-parse giving exact per-tool counts (`ToolCounts`), per-model token usage (`Models`→`ModelUsage`), reply/prompt/tool-call counts, an Agent/Active/Idle time split (`AgentMs`/`ActiveMs`/`IdleMs` — each inter-event gap classified: before a human prompt = active, before an assistant reply or tool-result = agent, any gap >5 min = idle; the three partition ended−started), plus the sub-agents / skills / hooks used: `Agents` (from Agent/Task tool_use `subagent_type`), `Skills` (from Skill tool_use `skill`), and `Hooks` (from `type:"attachment"` lines whose `attachment.type` is `hook_success`/`hook_error`, keyed by `hookName`). MCP tools aren't separate — they're the `mcp__…` entries in `ToolCounts`. |
| `Scanner/FolderSessions.cs` | `List(folder, max=25)` → `FolderSession(SessionId, Label, UpdatedIso)` for the transcripts in a folder's encoded project dir (`~/.claude/projects/<encoded-cwd>`), newest-first. Label = `SessionAggregator.FirstUserPrompt`. Empty for a folder with no transcripts. Powers `livecode.sessionsInFolder`. |
| `Scanner/TicketKeyInferrer.cs` | Extracts/validates ticket keys against the project-key allowlist; `IsRealBranch` filters out detached-`HEAD`/empty branches. |
| `Data/Db.cs` | `Initialize(path?)`, `Open()` (WAL + foreign_keys), `DbPath`. `ResolveDefaultPath` is portable-first (next to exe when writable, else `%APPDATA%\AIUsage\`, one-time copy of an existing %APPDATA% DB incl. `-wal`/`-shm`). |
| `Data/Migrations.cs` | Idempotent `CREATE TABLE IF NOT EXISTS` for all tables + indexes (incl. `ToolUsage`, `SessionDailyTokens`), `AddColumnIfMissing` for post-ship columns (incl. `Tickets.description`, `Tickets.provider`), `Seed` (ActivityCategories), `SetVersion` (currently **8**). On upgrade from a pre-6 DB that already has sessions, sets the `toolusage_backfill_pending` setting so the next scan backfills `ToolUsage` for existing sessions; likewise a pre-7 DB gets `dailytokens_backfill_pending` for `SessionDailyTokens`. The v8 migration backfills `Tickets.provider = 'jira'` for rows that already have a `last_synced` timestamp (pre-ClickUp rows are, by definition, JIRA); rows synced later carry their real provider. |
| `Data/Rows.cs` | `Query(conn, sql, params (name,value)[])` → `List<Dictionary<string,object?>>` (JSON-friendly) and `Scalar(conn, sql)`. |
| `Data/TicketKey.cs` | The single definition of a valid ticket key: JIRA-style `^[A-Z][A-Z0-9]{1,9}-\d{1,6}\z` PLUS ClickUp **native** `^CU-(?=[0-9a-z]*[a-z])[0-9a-z]{6,12}\z` (must contain a letter, so a JIRA project literally named `CU` stays a JIRA key; both anchor `\z` not `$` so a trailing newline can't sneak an Enter keystroke into the typed Live Code line). `IsValid`, `IsClickUpNative`, `Normalize` (trim; ClickUp native ids keep a lowercase body, everything else uppercases), `ProjectOf` (null for a ClickUp native key — it has no project part), `Require` (normalize-or-throw with the message the UI toasts), `ClickUpNativeGlob` (SQL GLOB matching the same native shape, for the allowlist-purge SQL). Used by `SessionHandlers.RequireSessionAndKey`, `ManualHandlers.manual.create`, `LiveCodeHandlers.StartTicketSession`, and inside `SessionRepo.AddAutoLink`/`AssignTicket`/`LinkLiveCodeSession` so no caller can write an unvalidated key. |
| `Data/Repositories/SessionRepo.cs` | `ResetCountersForFile`, `DeleteSessionsNotIn`, `Upsert`, `AddAutoLink`, `AssignTicket`, `ConfirmLink`, `RemoveLink`, `SetReviewState` (the three key writers — `AddAutoLink`, `AssignTicket`, `LinkLiveCodeSession` — run the key through `TicketKey.Require`), `List(conn, filter)` (ordered `COALESCE(ended_at, started_at) DESC` — **last activity**, not start: resuming a session leaves `started_at` at the original date, so start-ordering buried today's work and made the Sessions page look like it hadn't picked the resume up), `Get(conn, id)` (full stored row + ticket links + explicit category name, for the detail page; null if unknown), `LinkLiveCodeSession` (placeholder row + `livecode`-source link, before the transcript is scanned). |
| `Data/Repositories/ToolUsageRepo.cs` | `ReplaceForFile(conn, perSession)` — set-semantics replace of a session's `ToolUsage` rows (delete-then-insert, guarded so an unknown session id can't violate the FK). Populated from `SessionAggregator.ReadToolUsage`; read by `stats.dashboard`. |
| `Data/Repositories/SessionDailyRepo.cs` | `SessionDailyTokens` — per-session, per-local-day token spend. `Accumulate(conn, agg)` adds an aggregate's `DailyTokens` (additive upsert, mirroring `SessionRepo.Upsert`; `INSERT..SELECT..WHERE EXISTS` guards the FK and disambiguates the trailing `ON CONFLICT`), `DeleteForFile(conn, path)` zeroes a file's buckets before a full reparse, `ReplaceForFile(conn, path, aggs)` overwrites them for the v7 backfill, `RollingTokens(conn, days)` sums `input + output` over a window ending today (`days = 7` → today plus the six before it, via `date('now','localtime','-6 days')`). Read by `livecode.metrics` for the "last 7 days" tile. |
| `Data/Repositories/TicketRepo.cs` | `Upsert` (provider-neutral, from a `Tickets.TicketInfo`; all fields incl. `description`/`provider`, COALESCEd so bulk search doesn't wipe them), `MarkFailed`, `UnsyncedKeys`, `AllKeys`, `List` (ordered `updated DESC, key DESC`). |
| `Data/Repositories/ManualEntryRepo.cs` | `Create` (auto-creates the Ticket row), `Delete`, `List`, `Categories`. |
| `Jira/JiraClient.cs` | `FromSettings()` → null when site/email/token is unset **or** the stored site URL isn't a valid https address (never send the credential in cleartext). `FetchIssueAsync` (summary/status/type/project/priority/sprint/updated + **description**, flattening the ADF tree to text; discovers the Sprint custom-field id once, cached in `jira_sprint_field`), `SearchIssuesAsync` (token-paginated `POST /rest/api/3/search/jql`; no description), `TestConnectionAsync` (`/myself`). 404→dead-key, 401→credential error. |
| `Jira/JiraSiteUrl.cs` | Validation for `jira_site_url`, the address a **reversible** Basic credential is sent to. `Normalize` (absolute `https://` + host + no `user:pass@`, trailing slashes trimmed, or `ArgumentException` whose message the UI toasts), `IsSecure`, `Authority` (`host:port`), `PointsAtADifferentHost(old,new)` (the signal to drop the stored token; cosmetic edits don't count). Used by `settings.set`, `--set jira_site_url`, `jira.test`'s error text and `JiraClient.FromSettings`. Covered by `JiraSiteUrlTests`. |
| `Tickets/ITicketProvider.cs` | `ITicketProvider` — `Id`, `DisplayName` ("JIRA"/"ClickUp", also the literal text used in the Live Code kickoff prompt), `ItemNoun` ("ticket"/"task"), `FetchAsync(key)`, `AssignedAsync(max)`. |
| `Tickets/TicketInfo.cs` | `TicketProviderIds` (`Jira`="jira", `ClickUp`="clickup" consts) + the `TicketInfo` record both providers map onto (`Key`, `Provider`, `Summary`, `Status`, `IssueType`, `Project`, `Sprint`, `Priority`, `Updated`, `Description`, `IsDone`). |
| `Tickets/TicketProviders.cs` | `ProviderIdFor(key)` — config-only routing, no network: a ClickUp native key always routes to ClickUp; otherwise the key's project prefix is checked against `clickup_custom_id_prefixes`; everything else routes to JIRA. `For(key)`/`Enabled()` additionally gate on `jira_enabled`/`clickup_enabled` and client configuration, constructing `JiraTicketProvider`/`ClickUpTicketProvider` (or null/empty — enrichment is optional). |
| `Tickets/JiraTicketProvider.cs` | Wraps `JiraClient` behind `ITicketProvider`. `DoneStatuses` (Closed/Done/Ready for Release) and `AssignedJql` (`assignee = currentUser() ORDER BY updated DESC`) — moved here from `LiveCodeHandlers` so the picker and `TicketInfo.IsDone` agree on "done". Exposes the underlying `Client` for the JQL-specific `tickets.fetchMore` path. |
| `Tickets/ClickUpTicketProvider.cs` | Wraps `ClickUpClient` behind `ITicketProvider`. `AssignedAsync` walks up to 3 pages (`MaxPages`, a hard cap regardless of `max`) then sorts client-side (ClickUp's `order_by=updated` direction isn't guaranteed). |
| `Tickets/TicketSync.cs` | Provider-neutral replacement for the old JIRA-only `Jira/JiraSync.cs`. `TryFetchInBackground(key)` resolves the owning provider via `TicketProviders.For`, checks `TicketRepo.UnsyncedKeys` first, and fire-and-forgets `FetchOneAsync` — silently no-ops when no tracker owns/configures the key or the fetch fails. `FetchOneAsync(provider, key)` fetches + upserts, or `MarkFailed` on a dead key. |
| `ClickUp/ClickUpClient.cs` | Read-only ClickUp REST v2 client. Host is a **compile-time constant** (`https://api.clickup.com/api/v2`, no URL setting — unlike JIRA there's nothing to point at another host). `FromSettings()` → null without a token. `FetchTaskAsync(key)` builds the path via `BuildTaskPath` (native id → `/task/<id>`; Custom Task ID → `/task/<key>?custom_task_ids=true&team_id=…`, throwing if no workspace is set yet) and parses via `ParseTask` (folder/list → `Project`, a folder named "sprint…" → `Sprint`, `custom_item_id` present → `IssueType:"Custom"`, status `type` `done`/`closed` → `IsDone`; a syntactically valid `custom_id` is only kept as the key when its project prefix is in the caller-supplied `clickup_custom_id_prefixes` set, else it falls back to `CU-<id>` — so an unconfigured prefix can't get misrouted to JIRA by `TicketProviders.ProviderIdFor`). `AssignedAsync(page)` resolves the current user id once (`GetUserIdAsync`, cached) then pages `/team/{team}/task?assignees[]=…`; `ParseTaskList` drops any task whose id/custom-id doesn't fold into a valid `TicketKey` — never persists a bad key. `TestConnectionAsync()` (used by `clickup.test`) returns the username + the user's workspaces (`/user` + `/team`). `IsNotFound(code, body)` treats a clean 404 **or** a 401/400 whose body's `ECODE` starts with `ITEM_` as "not found" (ClickUp's own docs say a dead/foreign task can surface as either) — covered by `ClickUpClientTests` with a real captured `ECODE:"ITEM_013"` shape, but not yet verified against a live workspace. `EnsureOk` never includes response bodies beyond a short generic message (no raw token/detail leakage). |
| `Settings/SettingsStore.cs` | Typed accessors over the `Settings` table; `SetProtected`/`GetProtected` (DPAPI `dpapi:` on Windows, `plain:` fallback elsewhere). Helpers: `ScanRoots`, `ProjectKeyAllowlist`, `BackfillFrom`, `JiraEnabled()` (`jira_enabled != "0"` — absent/unset defaults ON), `ClickUpEnabled()` (`clickup_enabled == "1"` — absent defaults OFF), `ClickUpCustomIdPrefixes()`, `EffectiveKeyAllowlist()`/`CombineAllowlist` (user allowlist ∪ ClickUp prefixes when ClickUp is enabled; empty user allowlist stays empty). |
| `Export/XlsxWriter.cs` | Minimal OOXML `.xlsx` via `System.IO.Compression` (inline strings + numeric cells, XML-escaping, sheet-name sanitising) — no external dependency. |
| `Terminal/ConPtySession.cs` | Pseudo-console session via **Porta.Pty** (raw ConPTY didn't stream on this build — see PROGRESS.md). `Start(app,args,cwd,envOverrides,cols,rows)`, `Write`, `Resize`, `Snapshot` (rolling 512KB output buffer for terminal re-attach), `Dispose`; `Output`/`Exited` events. `BuildEnvironment` applies overrides and ALSO unsets null-valued keys in this process (Porta.Pty inherits parent env). `Dispose` kills the whole process **tree** via `taskkill /T /F` (so Stop halts `claude`/`node`, not just the shell). |
| `Terminal/ClaudeCommand.cs` | Builds every `claude …` command line the Live Code page types into a shell: `BuildTicket`, `BuildResume`, `BuildResumeSession`, plus the `Quote`/`Sanitize` primitives. `BuildTicket` takes a `TrackerLabel` enum (`Jira`/`ClickUp` — a closed set, never a fetched string) to phrase the kickoff sentence as "JIRA ticket …" or "ClickUp task …". `Sanitize` drops **all** control characters (they are consumed by the shell's *line editor* — PSReadLine/readline — below where quoting applies: 0x15 kills the line, 0x03 cancels it, 0x1B reverts it) and folds the Unicode quote classes to ASCII (PowerShell terminates a verbatim string on **any** of `U+0027 U+2018 U+2019 U+201A U+201B`, so a smart apostrophe in a JIRA summary used to close the string and run whatever followed). `Quote` sanitizes then single-quotes for the target shell. Summary/description are capped (`SummaryMaxChars` 200 / `DescriptionMaxChars` 800), the fetched description is fenced in `<ticket-description>` and labelled UNTRUSTED, `model`/`permissionMode` are allowlisted, and session ids must match `^[A-Za-z0-9._-]{1,64}$`. Covered by `ClaudeCommandTests`. |
| `Terminal/ShellResolver.cs` | `Resolve("powershell"\|"bash")` → exe + kind + `FellBack`. Git Bash probed on Git-for-Windows install paths + `git.exe` derivation; a bare PATH `bash.exe` is used only as a last resort and **never** the System32/SysWOW64 WSL shim (`IsSystemShim`). Falls back to PowerShell if not found. |
| `Terminal/AgentCatalog.cs` | `List(projectDir)` → agents from `<projectDir>/.claude/agents` + `~/.claude/agents` (name/description frontmatter; first-seen wins). `ReadAgentName(mdPath)` returns an agent file's name; `InstallAgentFile(mdPath, workingFolder)` copies a chosen agent `.md` into the working folder's `.claude/agents` (so Claude finds it) and returns its name. |
| `Terminal/PromptWatcher.cs` | Best-effort auto-approve: ANSI-strips the output stream, detects Claude confirmation prompts (`❯ 1.`, `(y/n)`, …) and returns Enter to inject (1.5s cooldown). Fragile by nature. |
| `Terminal/GitWorktree.cs` | Git-worktree isolation for same-folder sessions. `IsGitRepo(folder)` (never throws); `Create(folder, suffix)` → `WorktreeInfo(WorktreePath, Cwd, Branch, BaseSha, Toplevel)` via `git worktree add -b livecode/<suffix>-<hex>` in a sibling `<toplevel>-worktrees/<…>` off HEAD (throws on git error so the caller doesn't launch); `TryRemoveIfClean(info)` removes the worktree + branch only if `git status --porcelain` is empty AND `rev-list <base>..<branch>` is 0, else `(false, reason)`. All git runs via `Process`. |
| `Platform/MessageDialog.cs` | `Confirm(window, title, message)` — native Photino Yes/No box, UI-thread-marshalled like `FolderDialog`, blocking the calling bridge thread. **Fails closed**: false when declined *or* when the dialog can't be shown. Used for decisions the WebView must not be able to make on its own (the Live Code permission modes — 2026-08 audit, AIU-07). Callers must be invoked with an unbounded client timeout. |
| `Platform/FolderDialog.cs` | `Pick(window, title, initial)` — Photino `ShowOpenFolder`; `PickFile(window, title, filterName, exts, initialDir)` — `ShowOpenFile`. Both marshalled onto the UI thread (block the caller); return null on failure (page has manual path fields as fallback). |
| `Platform/ClaudeAccount.cs` | `Read()` → `ClaudeAccountInfo { Plan, UsageResetsAt }` from `~/.claude.json`. `Parse(json, nowUtc)` (public, testable) combines `organizationType` (`MapOrgType`: `claude_team`→"Team", `claude_enterprise`→"Enterprise", `claude_individual`/null→no org label) and `userRateLimitTier` (`MapTier`: strips `default_claude_`/`default_`/`claude_` prefixes, "zero"/"none"→no tier label, else e.g. `max_5x`→"Max 5x") into `"{org} · {tier}"`, `org`, or `tier` alone — so a flat-spend-cap Enterprise seat (tier "zero") reads plainly as "Enterprise", not "Enterprise · Zero". `planLimitsEndDate` is parsed to `UsageResetsAt` only when it's still in the future (`> nowUtc`); a stale/past date is hidden rather than shown. Reads ONLY non-secret enums/date — never org name, email, or tokens. |
| `Platform/ClaudeUsage.cs` | `ReadAsync()` → `ClaudeUsageInfo { Bars }` (`HasAny`) for the Live Code usage panel — a flat, ordered `UsageBar{Id,Label,Pct,ResetsAt,Detail}` list rather than fixed session/week fields, because different subscriptions surface a different subset of windows. GETs Anthropic's `oauth/usage` endpoint (the same one Claude Code's `/usage` reads), authed with the access token from `~/.claude/.credentials.json` (`claudeAiOauth.accessToken`; token used only to sign the request, never stored/logged/returned; skipped if expired). `Parse(json)` (public, testable) reads `five_hour`→SESSION, `seven_day`→WEEK, `seven_day_opus`/`seven_day_sonnet`→WEEK · OPUS/SONNET (per-model caps, Max/Team), each as `{utilization, resets_at}`; an `extra_usage` block (`is_enabled` + `monthly_limit>0`) becomes a bar with a `$used / $limit` `Detail` string (from `used_credits`/`monthly_limit`/`decimal_places`/`currency`; `spend_limit_reached`→100%), labelled **MONTHLY LIMIT** if it's the only bar (an Enterprise seat with a flat spend cap and no rolling windows — confirmed against a live Enterprise seat: `five_hour`/`seven_day` null, `extra_usage` populated) or **EXTRA USAGE** otherwise. Unrecognised/codenamed windows (e.g. `nimbus_quill`) are ignored — no label is guessed. Cached 5 min (SemaphoreSlim-guarded); best-effort — offline/signed-out returns last-good or null. |

---

## Frontend files (`wwwroot/`)

- **No ES modules** (they don't load over `file://` in WebView2). Classic `<script>` tags in `index.html`, loaded in order: `chart.umd.js` → `bridge.js` → view modules → `app.js`.
- Each view is an IIFE assigning `window.Views.<name> = { render(container) }`.
- Shared state lives on the globals `window.Bridge`, `window.Views`, `window.App`.

| File | Responsibility |
|---|---|
| `index.html` | Shell: `#sidebar` nav links (`data-route`), `<main id="content">`, `#toast`, "Scan now" button + status + `#app-version` (bottom-left), script tags. |
| `js/bridge.js` | `Bridge.call(action, payload, timeoutMs=120000)` → Promise over `window.external.sendMessage/receiveMessage`; correlates by `crypto.randomUUID()` id; `timeoutMs:0` disables the timeout. `Bridge.on(event, handler)` subscribes to server-pushed `{type:"event"}` messages (returns an unsubscribe fn). |
| `js/app.js` | Hash router (`navigate()` on `hashchange`, wipes `#content` and calls the view's `render(container, param)`; the hash may carry a `/param` — `#session/<id>` → route `session`, param `<id>` — and the `session` route keeps the "Sessions" nav item highlighted), `window.App` helpers (`toast`, `esc`, `fmtNum`, `fmtDate`, `refresh`, `exportExcel`, `confirm` [promise modal], `choose` [multi-button promise modal → chosen key]), the scan button + startup ping-then-scan, `loadVersion()` (fetches `app.info` once at startup, sets `#app-version`'s text to `short` and `title` to the multi-line `detail` tooltip — silently leaves it blank on failure, no toast), and a global 3s poll of `livecode.running` that colours the sidebar "Live Code" dot (green = `count>0`, red = none). `setupNavPopover()` shows a hover panel over the Live Code nav item listing live tabs (`livecode.list`); clicking a row calls `Views.livecode.focusTab(tabId)`. Background (auto) scan only re-renders the dashboard so form state elsewhere survives. |
| `js/views/dashboard.js` | Stat tiles + Chart.js charts; fixed `CATEGORY_COLORS` palette (color follows the category, not chart rank); consumes `stats.dashboard`. **Non-ticket sessions** (between Top tickets and Ticket type × AI activity) is a horizontal bar chart of unlinked spend per project folder, in `AMBER` so it doesn't read as more blue Top-tickets bars, with its own tokens/sessions toggle (`setNonTicketMetric`); axis labels are the last two path segments (`projectLabel`), full path in the tooltip. Non-ticket rows also count towards `hasData`, so a DB with sessions but no links still renders the charts. Includes an **Automation & extensions** section — four horizontal bar charts (`renderExtCharts`): sub-agents / skills / MCP servers / hooks by total uses, one hue each (`EXT_COLORS`), long labels truncated on the axis with the full name in the tooltip; hidden when there's no such data. |
| `js/views/sessions.js` | Sessions table with All / Needs review / Not-ticket-related tabs, tool-mix bar, ticket-badge confirm/remove, inline assign, dismiss/reopen; Export button. Each row's title is a link to `#session/<id>` (detail page). **No inline `onclick`**: every action is a `data-sess-act` (+ `data-sess-id`/`data-sess-key`/`data-sess-filter`) attribute dispatched by one delegated click/keydown listener attached via `bindActions(el)` — idempotent per element, because the router re-renders into the same `#content` node. `App.esc` is an HTML-*text* escaper and gives no protection inside an event-handler attribute (the attribute is entity-decoded during tokenization, *then* compiled as JS), so nothing interpolated may land in one. `bindActions` is exported and reused by `session.js`. |
| `js/views/session.js` | Session **detail** page (`#session/<id>`, reached from the Sessions list). Renders cards from `sessions.detail`: **Overview** (started/ended, Agent·Active·Idle time split, primary model, category, review state, total tokens with in/out/cache split + a "+ sub-agents" note, prompt/reply/tool-call counts), **Tools** (one coloured segment per tool + a name×count list), **Models** (per-model output bar), **Agents & extensions** (four labelled chip groups: Agents / MCP tools / Skills / Hooks; "—" per empty group), **Token cost** (cost derived here from a per-model-version `$/Mtok` rate table (`rateFor`: family + lowest priced version, `<synthetic>` free, unknown ids = current Opus) → est. cost, cache-hit %, output share, and cache-read/write·output·input breakdown bars), and **Tickets** last (reuses `Views.sessions` confirm/unlink/assign via the shared `Views.sessions.bindActions` delegated listener + `data-sess-act` attributes — no inline handlers). `back()` = `history.back()` (fallback `#sessions`). |
| `js/views/manual.js` | Manual-entry form (category/tool/date/description) + recent-entries list with delete; Export button. |
| `js/views/tickets.js` | Tickets table (Key/Summary/Project/Type/Priority dot/Status/Sprint/Sessions/Manual/Last synced), tracker-neutral: a badge shows JIRA/ClickUp per row (from `provider`) instead of hardcoding JIRA language, status row tint, All / AI-touched tabs, "✨ AI" badge, "Sync all" + "Fetch more"; Export button. |
| `js/views/livecode.js` | Live Code page — **multiple sessions as tabs**. The ticket picker is a **merged JIRA+ClickUp picker**: `livecode.tickets` returns one combined, already-sorted list (each row tagged `provider`), and `livecode.config`'s `ticketsConfigured`/`ticketProviders` drive whether the picker (and its controls) render at all. Module-closure `tabs[]` + `activeTabId` (survive navigation). A tab bar (`＋ New tab`, soft cap 6, `×` closes with confirm-if-running) sits above a per-active-tab control panel (ticket picker, folder, shell/model/agent, Custom Agent, Start/Stop/Resume/Reset + Auto-approve + Bypass, plus a per-tab "this session" tokens/context readout). One xterm per tab lives in a persistent `#lc-terminals` container (only the active tab shown); a single `pty.output`/`pty.exit` subscription routes by `tabId`, so background tabs keep streaming. `newTab()` inherits last-used defaults; `reconcile()` merges `tabs[]` with `livecode.list` on load; `reattachAll()` replays each running tab's buffer; `focusTab(tabId)` (called by the sidebar popover) activates a tab. Shared bottom panel keeps Plan + week-tokens + active-sessions. Pollers: `pollMetrics` (4s, active tab) + `pollActive` (2s). **Same-folder safeguard**: each tab tracks `activeFolder`/`isolated`; `conflictingTab()` (normalized paths) detects another running tab in the chosen folder, and `resolveIsolation()` shows the git-repo-aware `App.choose` warning (worktree / same folder / cancel) → passes `isolation` to start; isolated tabs show a `⑂` marker and closing them toasts kept-vs-removed. **Agent lock + Resume Sessions**: `refreshControlLocks(t)` is the single authority — Custom Agent disabled when an Agent is picked OR a picked-resume runs; Shell + Model disabled while a picked-resume runs. `loadFolderSessions` enables/disables the Resume Sessions button by folder session count; `openResumeSessions` (modal) → `resumePickedSession` calls `livecode.resumeSession` (confirm-replace if running) and sets `resumedPick`; stop/exit clear it. |
| `js/views/settings.js` | Settings form: JIRA panel (site URL `type="url"`, https required — a warning banner appears when `jiraSiteUrlInsecure`, and `save()` toasts when a host change cleared the stored token; email/token write-only; a `jiraEnabled` toggle), plus a **ClickUp settings panel** (enable toggle, API token — write-only — team/workspace id, Custom Task ID prefixes, "Test connection" → `clickup.test`, which also lists workspaces and can auto-fill the team id), scan paths, allowlist, backfill date, fetch JQL. Note: an invalid stored site URL makes `settings.set` reject the whole payload, so it must be fixed before other settings can be saved from any panel. |

---

## Database schema (SQLite, `SchemaVersion` = 8)

| Table | Purpose / key columns |
|---|---|
| `Sessions` | One row per Claude Code session (`id` PK). Metadata (file_path, project_dir, git_branch, title[/_is_custom], model, started/ended_at, cc_version), additive token & tool counters, `review_state` (`pending`/`not_ticket_related`/...). |
| `ScanState` | `file_path` PK → `last_offset`, `last_mtime`, `last_size` for incremental scanning. |
| `Tickets` | `key` PK; tracker-neutral fields summary/status/issue_type/project/sprint/priority/updated/**description**/**provider** (`jira`/`clickup`; v8, backfilled `'jira'` for pre-existing synced rows), `last_synced`, `fetch_failed`. |
| `ActivityCategories` | Seeded list: Generated code, Wrote tests, Refactored, Debugged, Reviewed, Wrote docs, Investigated. |
| `SessionTicketLinks` | (`session_id`→Sessions ON DELETE CASCADE, `ticket_key`) PK; `source` (auto/manual/confirmed/livecode), `inferred_from` (branch/cwd/prompt_text), `category_id`. |
| `ToolUsage` | (`session_id`→Sessions ON DELETE CASCADE, `category`, `name`) PK; `count`. `category` ∈ agent/skill/mcp/hook. Set-semantics, derived from a full transcript parse (not the incremental token scan); powers the dashboard automation charts. |
| `SessionDailyTokens` | (`session_id`→Sessions ON DELETE CASCADE, `day`) PK; `file_path`, `input_tokens`, `output_tokens`. `day` is the **local** `yyyy-MM-dd` the tokens were spent on. Same additive semantics as the `Sessions` counters (and always sums back to them), just day-resolved, so a session spanning days/weeks contributes to a rolling window only the part that falls inside it. Indexed on `day`. |
| `ManualEntries` | `id` PK; ticket_key, entry_date, category_id, description, tool_used, created_at. |
| `Settings` | `key` PK → `value` (see settings keys below). |
| `SchemaVersion` | single `version` row. |

Indexes: `idx_links_ticket`, `idx_manual_ticket`, `idx_sessions_started`, `idx_toolusage_cat`.

---

## Bridge action catalog

`scan.run` · `sessions.list` · `sessions.detail` · `sessions.assignTicket` · `sessions.confirmLink` ·
`sessions.removeLink` · `sessions.dismiss` · `sessions.reopen` · `categories.list` ·
`manual.list` · `manual.create` · `manual.delete` · `tickets.list` · `tickets.fetch` ·
`tickets.sync` · `tickets.fetchMore {provider,cursor}` · `jira.test` · `clickup.test` · `settings.get` · `settings.set` ·
`stats.dashboard` · `export.sessions` · `export.manual` ·
`export.tickets` · `app.info` · `livecode.config` · `livecode.saveConfig` · `livecode.tickets` ·
`livecode.listAgents` · `livecode.pickFolder` · `livecode.pickAgentFile` · `livecode.folderInfo` · `livecode.sessionsInFolder` · `livecode.start` · `livecode.stop` ·
`livecode.closeTab` · `livecode.metrics` · `livecode.resume` · `livecode.resumeSession` · `livecode.reset` · `livecode.attach` · `livecode.list` ·
`livecode.running` · `livecode.activeSessions` · `livecode.usage` · `pty.input` · `pty.resize` · `ping` (built-in).

Live Code supports **multiple concurrent sessions, one per UI tab**. Every per-session action
(`start`/`resume`/`reset`/`stop`/`closeTab`/`attach`/`metrics`, `pty.input`, `pty.resize`) takes a
frontend-minted **`tabId`** (a GUID, stable across a tab's Stop→Resume/Reset). `livecode.list`
returns all live tabs `[{tabId, folder, ticketKey, running, canResume, model}]` (rebuilds tabs
after navigation; feeds the sidebar hover panel). `livecode.running` returns `{running, count}`.
`livecode.closeTab` disposes a tab's session and drops its entry; `livecode.stop` keeps the entry
(so Resume still works). The stream **events** `pty.output`/`pty.exit` carry `{tabId, …}` so the
right tab's terminal renders them.

`livecode.usage` returns `{available, bars}` where `bars` is a variable-length list `[{id, label, pct, resetsAt, detail}]` (server-computed % from `ClaudeUsage.Bars`; `available:false` when signed out/offline → page hides the bars). Bars seen in practice: `five_hour`/SESSION, `seven_day`/WEEK, `seven_day_opus`/`seven_day_sonnet` (per-model caps, Max/Team), and `extra_usage` (MONTHLY LIMIT when it's the seat's only window — an Enterprise flat spend cap — else EXTRA USAGE, with a `$used / $limit` `detail` string). The Live Code bottom panel polls it every 60s (backend caches 5 min) and renders each bar beside Plan/Tokens, colour-graded ≥80% warn / ≥95% crit.

`livecode.metrics {tabId}` returns `{weekTokens, sessionTokens, mainTokens, agentTokens, cacheTokens, cacheCreation, cacheRead, contextTokens, contextSize, contextPct, active, activeSessions}`. `sessionTokens` is `mainTokens + agentTokens` = `input + output` **including sub-agents** — the SAME formula as the dashboard and `weekTokens` (all count in+out only), so the numbers stay consistent; sub-agents (Task-tool sidechains under `<sessionId>/subagents/*.jsonl`, which the scanner skips) are re-added here for this readout only. `cacheTokens` (`cacheCreation + cacheRead`, incl. sub-agents) is shown as a **separate** "Cache" field with a created/read tooltip, so cache never inflates the headline Tokens. The UI adds an "incl. N agents" suffix + Main/agents tooltip on Tokens when `agentTokens > 0`. The DB/dashboard/`weekTokens` exclude cache and sub-agents (v1 design). `weekTokens` is `SessionDailyRepo.RollingTokens(conn, 7)` — a **rolling last-7-days** sum over the per-day buckets, labelled "Tokens — last 7 days" and aligned with the WEEK usage bar next to it. It used to be `SUM(input_tokens + output_tokens)` over `Sessions` grouped by `strftime('%Y-%W', started_at)`, which put a multi-day session's entire spend in the week it *started* — so a session running across the Monday boundary counted zero and the tile read near-zero for the first days of each week.

**Server-pushed events** (via `MessageRouter.PushEvent` → `Bridge.on`): `pty.output` (base64 terminal bytes), `pty.exit` (exit code).

---

## Settings keys (`Settings` table)

| Key | Meaning |
|---|---|
| `scan_paths` | Transcript scan roots (defaults to `%USERPROFILE%\.claude\projects`). |
| `projects` | (project list setting). |
| `project_key_allowlist` | Allowed JIRA project prefixes for ticket inference (e.g. `SFTY`); changing it purges disallowed auto-links. |
| `backfill_from` | Ignore transcript files older than this date. |
| `jira_site_url` | JIRA Cloud base URL. |
| `jira_email` | JIRA account email (basic-auth user). |
| `jira_token` | API token — **DPAPI-protected** (`dpapi:` prefix), write-only in the UI. Never paste a real token into Claude. |
| `jira_fetch_jql` | JQL for "Fetch more" (default `assignee = currentUser() ORDER BY updated DESC`). |
| `jira_sprint_field` | Discovered Sprint custom-field id (cache; `-` = instance has no Sprint field). |
| `jira_enabled` | Whether JIRA is used for routing/enrichment (`SettingsStore.JiraEnabled()`: any value other than `"0"` is ON — **absent defaults to ON** so existing installs keep working unchanged). |
| `clickup_enabled` | Whether ClickUp is used for routing/enrichment (`ClickUpEnabled()`: `"1"` is ON — **absent defaults to OFF**). |
| `clickup_token` | ClickUp personal API token (`pk_…`) — **DPAPI-protected** (`dpapi:` prefix), write-only in the UI. Never paste a real token into Claude. |
| `clickup_team_id` | ClickUp workspace ("team") id; digits only. Auto-filled by `clickup.test` when the token has exactly one workspace. |
| `clickup_custom_id_prefixes` | Comma-separated ClickUp Custom Task ID prefixes (e.g. `DEV,OPS`) — keys with one of these project prefixes route to ClickUp instead of JIRA, and (when ClickUp is enabled) are unioned into the effective allowlist. |
| `livecode_last_folder` | Live Code: last-used working folder (default). |
| `livecode_last_shell` | Live Code: last-used shell (`powershell`/`bash`). |
| `livecode_last_model` | Live Code: last-used model (`''`/`opus`/`sonnet`/`haiku`). |
| `livecode_auto_approve` | Live Code: auto-approve toggle state (`1`/`0`). (Bypass is never persisted.) |
| `livecode_custom_agent` | Live Code: path to a chosen agent `.md` file to use for the session. |
| `livecode_ticket_count` | Live Code: how many assigned tickets the picker lists (default `3`, clamped 1–20). Set in Settings → Live Code; `livecode.tickets` and `livecode.config` (`ticketCount`) read it. |
| `toolusage_backfill_pending` | Internal one-shot flag (set by the v6 migration, cleared after the next scan) — triggers a full-transcript `ToolUsage` backfill for pre-v6 sessions. |
| `dailytokens_backfill_pending` | Internal one-shot flag (set by the v7 migration, cleared after the next scan) — triggers a full-transcript `SessionDailyTokens` backfill for pre-v7 sessions. |

---

## Dependencies (NuGet)

Photino.NET · Microsoft.Data.Sqlite · System.Security.Cryptography.ProtectedData ·
SQLitePCLRaw.bundle_e_sqlite3 · **Porta.Pty** (ConPTY wrapper for the Live Code terminal; managed-only).

---

## Versioning & license

- **Version**: `AIUsage.csproj`'s `<Version>` (semver, bumped by hand). The `SetGitCommitHash`
  target (runs `git rev-parse --short=7 HEAD`, best-effort) sets `SourceRevisionId`, which the
  SDK appends as `+<hash>` onto `AssemblyInformationalVersion`; an `AssemblyMetadata` item stamps
  the UTC `BuildDate`. `Platform/AppVersion.cs` reads both back at runtime; `app.info` /
  `--version` / the sidebar footer all read from it — nothing else derives a version independently.
- **License**: MIT (`LICENSE`, copyright Aaron Brata Aditama). Vendored JS (Chart.js, xterm.js,
  xterm-addon-fit.js) and NuGet deps (Photino.NET/SQLitePCLRaw under Apache-2.0; the rest MIT) are
  listed with their notices in `THIRD-PARTY-NOTICES.md`.

---

## Tests (`AIUsage.Tests/`, xUnit)

Run with **`dotnet test AIUsage.Tests`**. The project references `AIUsage.csproj` and is
**deliberately not** part of a `.sln` (a solution would make the bare root `dotnet run`/`dotnet build`
ambiguous). Because the test folder is nested inside the app project directory, `AIUsage.csproj`
excludes `AIUsage.Tests\**` from its default compile glob.

| Test file | Covers |
| --- | --- |
| `TicketKeyInferrerTests.cs` | Allowlist filtering, key-shape regex, `IsRealBranch`, ClickUp-native branch extraction gated by the `clickUpNative` constructor flag. |
| `TicketKeyTests.cs` | `Data/TicketKey` — accepted/rejected key shapes for **both** grammars (JIRA + ClickUp native, incl. the "must contain a letter" rule and `\z` vs `$`), `Normalize`, `Require` throwing with the UI message. |
| `TicketProvidersTests.cs` | `TicketProviders.ProviderIdFor` — native key → ClickUp, custom-ID-prefix match → ClickUp, everything else → JIRA. |
| `TicketHandlersTests.cs` | `TicketHandlers.Tally` — pure counting of `tickets.sync` outcomes (ok/dead/failed/skipped), no network. |
| `LiveCodeHandlersTests.cs` | `LiveCodeHandlers.OrderByUpdatedDesc`/`ParseUpdated` — merges JIRA + ClickUp ticket lists by parsed instant, not raw string, incl. null/unparseable sorting last. |
| `ClickUpClientTests.cs` | `ClickUpClient` — `BuildTaskPath` (native vs Custom Task ID, missing-workspace error), `ParseTask`/`ParseTaskList` (folder/sprint/priority/status/description mapping, dropping tasks with no valid key), `IsNotFound` (404, and 401/400 with a real captured `ECODE:"ITEM_013"` body vs a non-ITEM_ code). |
| `ClaudeAccountTests.cs` | `ClaudeAccount.Parse` — plan label for every org/tier combination incl. a zero/none tier (no tier text) and Enterprise-alone, past-vs-future reset-date filtering, malformed JSON. |
| `ClaudeUsageTests.cs` | `ClaudeUsage.Parse` — a captured real Enterprise seat response (spend-only, MONTHLY LIMIT), a Pro-shaped response (rolling SESSION/WEEK bars, no extra_usage bar). |
| `SettingsAllowlistTests.cs` | `SettingsStore.CombineAllowlist`/`EffectiveKeyAllowlist` — empty allowlist stays empty, ClickUp prefixes union in when the user allowlist is non-empty. |
| `ClaudeCommandTests.cs` | The typed `claude …` line: PowerShell/bash quote-breakout payloads for every member of the single-quote class, control-character stripping, length caps, model/permission allowlists, session-id rejection, untrusted-description fencing, the `TrackerLabel` enum ("JIRA ticket …" vs "ClickUp task …"), and the ordinary-input command shape. |
| `JiraSiteUrlTests.cs` | `Jira/JiraSiteUrl` — https-only acceptance, rejection of http/ftp/relative/userinfo/no-host, normalization, `Authority`, and `PointsAtADifferentHost` (cosmetic edits vs a real host change). |
| `SessionAggregatorTests.cs` | `Aggregate` — grouping, sidechain skip, tool buckets, token accumulation, timestamps, title precedence, source priority (branch>cwd>prompt), malformed-line skip; `ContextWindow` sizing. |
| `XlsxWriterTests.cs` | Zip parts, numeric-vs-string cells, null cells, XML escaping, column-letter mapping, sheet-name sanitising. |
| `MigrationsTests.cs` | SchemaVersion 8, table creation (incl. `Tickets.provider`), category seed, idempotency, no spurious backfill flag. |
| `SessionRepoTests.cs` | Upsert/Get/List, additive token accumulation, auto-link → confirm → remove, counter reset, prune + FK cascade. |
| `SessionDailyRepoTests.cs` | `SessionDailyTokens` accumulate/replace/delete + rolling-window sums. |
| `TokensWeeklyTests.cs` | `StatsHandlers.TokensWeeklySql` — spend-week attribution, bucket-less fallback, zero-filled quiet weeks. |
| `NonTicketProjectsTests.cs` | `StatsHandlers.NonTicketProjectsSql` — folder grouping (case-insensitive), link exclusion by any source, `(unknown folder)` label, top-10 cut. |
| `DataRepoTests.cs` | `TicketRepo` (incl. the `provider` column) / `ManualEntryRepo` round-trips. |
| `AppVersionTests.cs` | `AppVersion.Parse` — semver+commit split, bare semver, missing/blank input; sanity check that the built assembly's static fields are populated. |

Data-layer tests use `Helpers/TestDb` — a fresh in-memory SQLite DB migrated to the current schema
per test (repositories take an explicit connection, so no global `Db` state is touched → parallel-safe).

---

## Build output (not source, do not edit)

`bin/` and `obj/` — MSBuild output. The single-file published exe lands under
`bin/Release/net10.0/win-x64/publish/AIUsage.exe` (see `CLAUDE.md` for the publish command).
