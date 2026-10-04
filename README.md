# CrateDigger

[![CI](https://github.com/acorrington/CrateDigger/actions/workflows/ci.yml/badge.svg)](https://github.com/acorrington/CrateDigger/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/acorrington/CrateDigger)](https://github.com/acorrington/CrateDigger/releases)

**AI playlist generator plugin for Emby Server.** Describe a vibe in plain English —
CrateDigger digs the matching tracks out of your local music library with an LLM and
builds a native Emby playlist.

> **Quick install:** download `CrateDigger.dll` from [Releases](https://github.com/acorrington/CrateDigger/releases),
> copy it into `%AppData%\Emby-Server\programdata\plugins\`, restart Emby, then open
> **Dashboard → Plugins → CrateDigger**.

> "Crate digging" — flipping through record crates to find the perfect tracks. CrateDigger
> does that for your library, only the crate is your entire collection and the DJ is an AI.

```
[ Dashboard UI ] --POST--> [ /CrateDigger/Create ]
                               ├─ 1. Read library (ILibraryManager, type=Audio)
                               ├─ 2. Prompt → LLM (OpenAI-compatible endpoint)
                               ├─ 3. Parse JSON tracklist (fence/prose tolerant)
                               ├─ 4. Fuzzy-match → real library Guids (Levenshtein + token set)
                               └─ 5. IPlaylistManager.CreatePlaylist
                                     ↓
                          [ Native Emby playlist ]
```

## Features

- 🎛 **Dashboard config page** — API key, endpoint, model, match threshold, playlist length
- 💬 **Prompt box** — "Upbeat 80s synth music for working out"
- 📊 **Live staged status** — *Analyzing library… → Thinking… → Matching tracks… → Creating playlist…*
- 🧠 **Any OpenAI-compatible endpoint** — OpenAI, Ollama, LM Studio, OpenRouter
- 🧩 **Fuzzy matching** — typo/diacritics/decoration-tolerant resolution of AI suggestions to your
  real tracks (never invents items that aren't in the library)
- 🪜 **Scales to big libraries** — ≤300 tracks embedded verbatim; larger libraries go through an
  artist-shortlist pass first, then a summary-based prompt
- ✅ **43 unit tests** covering matching (UT-001), parser robustness (UT-002), prompt building,
  and the full pipeline against a fake LLM

## Repository layout

```
CrateDigger.slnx
├── src/
│   ├── CrateDigger.Core/          Pure logic — no Emby dependencies (fully unit-testable)
│   │   ├── Models/                TrackRef, LibraryTrack, ResolveReport
│   │   ├── Llm/                   LlmClient, PromptBuilder, ResponseParser
│   │   └── Matching/              FuzzyMatcher (normalize → score → threshold)
│   ├── CrateDigger.EmbyStubs/     Compile-time stand-ins for MediaBrowser.*.dll
│   └── CrateDigger.Plugin/        Emby integration: entry point, REST service, config page
│       └── Resources/configPage.html (embedded)
├── tests/CrateDigger.Core.Tests/  xUnit
├── lib/                           ← drop real Emby DLLs here (see lib/README.md)
└── docs/api-surface.md            Emby API verification checklist
```

## Building

```powershell
dotnet build CrateDigger.slnx        # builds against stubs if lib/ is empty
dotnet test  tests/CrateDigger.Core.Tests    # 50 unit tests
```

The plugin project **auto-switches** its references: with `MediaBrowser.Controller.dll`,
`MediaBrowser.Model.dll` and `MediaBrowser.Common.dll` present in `lib\` it compiles
against the real Emby API; without them it compiles against `CrateDigger.EmbyStubs`.
Deploying (and cutting releases) therefore requires a machine with Emby installed —
that's why CI runs tests in stub mode while `tools/release.ps1` builds the shipped DLL.

### Deploy

Copy the single merged **`CrateDigger.dll`** (ILRepack folds `CrateDigger.Core` into it —
Emby's isolated plugin load contexts can't resolve sibling DLLs) to:

```
%AppData%\Emby-Server\programdata\plugins\
```

and restart Emby Server. (The build auto-copies when that folder exists.) Verify under
**Dashboard → Plugins**, page **CrateDigger**.

### Release (maintainers, machine with Emby installed)

```powershell
pwsh -File tools/release.ps1          # tests -> Release build -> tag -> gh release + DLL
```

## Configuration

| Setting | Default | Notes |
|---|---|---|
| OpenAI API key | — | Not needed for Ollama/LM Studio |
| API base URL | `https://api.openai.com/v1` | Any OpenAI-compatible endpoint |
| Model | `gpt-4o-mini` | e.g. `llama3.1` for Ollama |
| Playlist length | 30 | Sent to the model as target size |
| Match threshold | 0.75 | Lower = looser fuzzy matching |

## Testing strategy

| ID | Layer | Status |
|----|-------|--------|
| UT-001 fuzzy matcher typo resolution | `FuzzyMatcherTests` | ✅ automated (50/50 total) |
| UT-002 malformed LLM JSON handling | `ResponseParserTests` | ✅ automated |
| ST-001 endpoint registers / 401 unauthenticated | live Emby 4.10.1.0 | ✅ 401 confirmed |
| ST-002 end-to-end playlist creation | live Emby + mock LLM | ✅ 5/5 tracks matched, playlist written |
| ST-003 real local-LLM generation | live Emby + Unsloth Qwen3.8-27B | ✅ 57s, "Backyard Rock & Pop", 23/30 matched |

## Local model notes (Unsloth / llama.cpp)

Settings all live in **Dashboard → Plugins → CrateDigger**: API key, base URL
(e.g. `http://localhost:8888/v1`), model id, playlist length, match threshold, **LLM timeout**,
**max output tokens**, and **extra request JSON** (e.g.
`{"chat_template_kwargs":{"enable_thinking":false}}` to silence a reasoning model).
Defaults suit a quantized 27B local model: 300s timeout, 8192 token cap.

## Known gaps / roadmap

- **ST-003 real-LLM run**: ✅ done — local Unsloth endpoint (see "Local model notes").
- **Single-DLL build**: Emby loads plugin assemblies in isolated contexts, so ILRepack
  merges `CrateDigger.Core.dll` into `CrateDigger.dll` post-build — deploy exactly one file.
- **API key at rest**: stored unencrypted in the plugin config XML; DPAPI protection planned.
- Context-window guard: summary path tops out at 300 embedded artists — chunking may be
  needed for very large libraries.
- "Test connection" button on the settings page.