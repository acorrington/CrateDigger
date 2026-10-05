# Emby API surface — verified against Emby Server 4.10.1.0

All signatures below were confirmed by compiling the plugin against the real DLLs
(`lib\`), cross-checked with a reflection probe (`tools/EmbyApiProbe`), first-party
plugin decompiles (MBBackup, OpenSubtitles, Emby.Webhooks), and the official docs at
[dev.emby.media](https://dev.emby.media/doc/plugins/dev/index.html).

Sources of truth used during discovery:
- Reflection probes → saved output: `docs/emby-api-probe*.txt`
- Official docs: [Plugin Development](https://dev.emby.media/doc/plugins/dev/index.html),
  [Creating Api Endpoints](https://dev.emby.media/doc/plugins/dev/Creating-Api-Endpoints.html),
  [Automatic Type Discovery](https://dev.emby.media/doc/plugins/dev/Automatic-Type-Discovery.html)
- First-party reference pages extracted to `docs/reference-pages/`

## Confirmed signature table (✅ = compiled + live-tested)

| # | Member | Confirmed shape | Status |
|---|--------|-----------------|--------|
| 1 | Plugin base | `BasePlugin<T>(IApplicationPaths, IXmlSerializer)`; `Name` abstract virtual; `Id` virtual; `Configuration {get;set}` | ✅ compiles, loads |
| 2 | Configuration class | `class PluginConfiguration : BasePluginConfiguration` (MediaBrowser.Model.Plugins) | ✅ |
| 3 | `IHasWebPages.GetPages()` | returns `PluginPageInfo { Name, DisplayName, EmbeddedResourcePath, EnableInMainMenu, MenuSection, MenuIcon }` — **`EmbeddedResourcePath`**, not `...Name` | ✅ |
| 4 | Page convention | html page (`Name="cratedigger"`) + paired JS module (`Name="cratediggerjs"`); markup root has `data-controller="__plugin/cratediggerjs"`; JS is an **AMD `define([...], fn)` returning a `View`** extending `baseView` | ✅ pattern from MBBackup |
| 5 | `IServerEntryPoint` | `void Run()` + `IDisposable` (NOT `RunAsync`) | ✅ runs at startup |
| 6 | Service interface | `MediaBrowser.Model.Services.IService` (marker) + `IRequiresRequest` (`IRequest Request {get;set;}` injected) — there is **no `IRestfulService`** | ✅ |
| 7 | Route declaration | `[Route("/path", "VERB")]` + `[Authenticated]` + `IReturn<TResponse>` **on the request DTO class**; service methods named after the verb (`Get`/`Post`) taking the DTO | ✅ ST-001/002 |
| 8 | Service discovery | automatic — implementing `IService` is enough (Automatic Type Discovery) | ✅ |
| 9 | Logger | `MediaBrowser.Model.Logging.ILogger` via `ILogManager.GetLogger(name)`; methods `Info/Warn/Error(msg, params)`, `ErrorException(msg, ex, params)` | ✅ |
| 10 | Library query | `BaseItem[] GetItemList(InternalItemsQuery)` with `IncludeItemTypes = string[]`, `Recursive` | ✅ 2830 items |
| 11 | Artists | on `Audio` (`MediaBrowser.Controller.Entities.Audio.Audio`): `string[] Artists`, `string[] AlbumArtists` — **not** on `BaseItem` | ✅ |
| 12 | Playlist creation | `Task<PlaylistCreationResult> CreatePlaylist(PlaylistCreationRequest { Name, ItemIdList : long[], MediaType, User, IsPublic })`; item keys are **`BaseItem.InternalId` (Int64)**; result `.Id` is string | ✅ playlist on disk |
| 13 | Playlist owner | `IAuthorizationContext.GetAuthorizationInfo(IRequest)` → `AuthorizationInfo.UserId (long)`/`.User` → `IUserManager.GetUserById(...)`; fallback: config `OwnerUserId` → first user | ✅ |
| 14 | Auth | DTOs carry `[Authenticated]` (MediaBrowser.Controller.Net) → unauthenticated = **HTTP 401** (ST-001 ✅). Client auth: header `Authorization: MediaBrowser Client="..", Device="..", DeviceId="..", Version=".."` + `POST /Users/AuthenticateByName` | ✅ |
| 15 | JSON serialization | responses are **PascalCase**; config endpoint `GET/POST /Plugins/{id}/Configuration` accepts JSON bodies | ✅ |
| 16 | Request body binding | service DTO bodies bind **`application/x-www-form-urlencoded`** reliably; plain `application/json` bodies did NOT bind on this server build (verified on `/Users/AuthenticateByName` and `/CrateDigger/Create`), while `/Plugins/{id}/Configuration` accepts JSON. Frontend uses form-encoding. | ✅ empirically |
| 17 | Dashboard page discovery | `GET /web/ConfigurationPages` lists plugin pages (built from `IHasWebPages.GetPages()` via `WebAppService.GetPluginPages`); `GET /web/ConfigurationPage?name=cratedigger` serves the HTML. Plugins → CrateDigger → Settings works; `EnableInMainMenu=true` also adds the Settings-menu entry. | ✅ curl-verified |
| 18 | Client auth for tests | `POST /Users/AuthenticateByName` with header `Authorization: MediaBrowser Client="..", Device="..", DeviceId="..", Version=".."` and **form-encoded** `Username`/`Pw` (JSON body does not bind) → `AccessToken`; send as `X-Emby-Token` | ✅ |
| 19 | Plugins-page thumbnail | implement `MediaBrowser.Common.Plugins.IHasThumbImage` on the plugin class: `ImageFormat ThumbImageFormat` (`MediaBrowser.Model.Drawing.ImageFormat.Png`) + `Stream GetThumbImage()` returning an embedded resource. The handler has **no null-guard** — without the interface `GET /Plugins/{Id}/Thumb` returns **500 NRE** (verified live); first-party plugins (MBBackup) implement it and embed `{Namespace}.thumb.png` | ✅ 200 image/png |
| 20 | Scheduled tasks | `MediaBrowser.Model.Tasks.IScheduledTask` = `Name/Key/Description/Category` + `Task Execute(CancellationToken, IProgress<double>)` + `IEnumerable<TaskTriggerInfo> GetDefaultTriggers()`; interval trigger = `{ Type = "IntervalTrigger", IntervalTicks = TimeSpan.FromMinutes(n).Ticks }`; auto-discovered like entry points; **run manually via `POST /ScheduledTasks/Running/{Id}` where Id = the GUID `Id` field from `GET /ScheduledTasks`** — NOT Key, NOT Name (404 "Task not found" otherwise) | ✅ E2E run |
| 21 | m3u-backed playlists are NOT tree-linked | any `InternalItemsQuery` parent scoping (`Parent`, `ParentIds[]`, `TopParentIds[]`, `HasParentId`, ± `SkipAncestorNormalization`) returns **0 children** for playlist items (10-shape diagnostic matrix). REST `?ParentId=` special-cases them server-side. Read playlist contents from the m3u instead: `{programdata}\data\userplaylists\{Name} [playlist]\{Name}.m3u` (programdata derived from `BasePlugin.ConfigurationFilePath`); entries carry `#EXTART`/`#EXTINF` metadata — resolve to library items with the fuzzy matcher, never via paths (they're relative and format-quirky) | ✅ E2E |
| 22 | Playlist REST DTOs | `AddToPlaylist`/`CreatePlaylist`/`RemoveFromPlaylist` take **comma-separated STRING fields** (`Ids`, `EntryIds` — `System.String`, not arrays!) and this server binds them from **form/query, not JSON bodies** (same quirk as `/Users/AuthenticateByName`): `--data 'Ids=11757,11764'` works, JSON silently binds nothing (`ItemAddedCount:0`) | ✅ empirically |
| 23 | Service routes & verbs | route lives **on the DTO class** (`[Route(path, verbs)]` + `[Authenticated]` + `IReturn<T>`) and the service method must be **verb-named** (`Get`/`Post` overloads — `GetDiagChildren` never dispatches); `GET /Items` itself accepts query params mirroring `InternalItemsQuery` names | ✅ |

## Environment facts

- Emby 4.10.1.0 installed at `%AppData%\Emby-Server\`, plugins at `programdata\plugins\`
- **Plugin assemblies load in isolated load contexts** — sibling DLLs are NOT resolvable at
  runtime (loader throws `FileNotFoundException` during type enumeration, silently dropping
  `IService` types while lazy entry points still work). **Therefore the plugin is built as a
  single DLL**: ILRepack merges `CrateDigger.Core` into `CrateDigger.dll` post-build.
- Log: `programdata\logs\embyserver.txt` — plugin logs under `Info CrateDigger:`
- Test music library: `\\amc-media\emby\Emby Server\Music` → 2,830 audio items

## Test results

| ID | Test | Result |
|----|------|--------|
| UT-001..NN | 58 unit tests (matcher, parser, prompts, payload, pipeline, m3u seeds) | ✅ 58/58 |
| ST-001 | POST `/CrateDigger/Create` without token | ✅ 401 |
| ST-001b | GET `/CrateDigger/Status` without token | ✅ 401 (same guard) |
| ST-002 | End-to-end generation (mock LLM): 2830-track library → artist shortlist → tracklist → fuzzy match → playlist | ✅ 5/5 matched, playlist `.m3u` written |
| ST-003 | Real local LLM (Unsloth `unsloth/Qwen3.8-27B-GGUF` @ `localhost:8888/v1`) — "backyard barbecue" prompt | ✅ 57s, playlist "Backyard Rock & Pop", 23/30 matched, 7 hallucinations/absent tracks correctly rejected |
| ST-004 | Seed-playlist trigger E2E (v0.2.0): 2 seeds added via API → `POST /ScheduledTasks/Running/{Id}` → 265s run → 45-track "CrateDigger Radio 10-05 15:33", 0 unmatched, seeds cleared + playlist recreated | ✅ |

## LLM integration lessons (local/Unsloth servers)

- `HttpClient.Timeout` defaults to **100s** and silently overrides a larger per-request
  budget — set it to `Timeout.InfiniteTimeSpan` and rely on the configurable CTS only.
- llama.cpp-family servers default `max_tokens` to **unlimited** — always send a cap
  (default 16384 since v0.1.5; 8192 proved too small when qwen rambled → truncation
  mid-JSON, now also backstopped by truncation salvage + one reinforced parse retry).
- Reasoning models (Qwen3 etc.) can burn the whole budget thinking; `chat_template_kwargs:
  {"enable_thinking": false}` (configurable as "Extra request JSON") cuts latency sharply.
- Timeout is **not retried** (a slow local model stays slow) — fail fast with a clear
  message pointing at the "LLM timeout" setting.
- Occasional non-JSON shortlist/tracklist replies happen — raw payloads are now logged
  (truncated to 2000 chars) for diagnosis.

## Open items

1. Dashboard UI eyeball by a human (page + AMD JS render): `http://localhost:8096` → Plugins → CrateDigger.
2. Match-threshold tuning: "Fleetwood Mac - Dreams" scored 0.70 (below 0.75) — lower the
   threshold in settings if absent-from-library vs borderline matches feel wrong.
3. API key at rest: plugin config XML is plaintext — DPAPI hardening planned.
4. Consider a "test connection" button on the settings page.
