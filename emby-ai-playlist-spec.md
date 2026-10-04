# Spec: Emby AI Playlist Generator Plugin

## 1. Executive Summary & Architecture Overview
This specification details a native **Emby Server Plugin** targeting **Windows 11 (.NET 8.0+)**. The plugin extends the Emby Dashboard with a custom **Simple UI** layout containing a prompt text box. When submitted, the text box queries an AI service (e.g., OpenAI API) to match the prompt against the user's local music library and automatically construct an Emby playlist.

```
[ User Input (Dashboard UI) ] 
       │ (JavaScript POST)
       ▼
[ Custom Emby API Endpoint (IRestfulService) ]
       │
       ├─► [ 1. Fetch Local Music Schema/Sample ]
       ├─► [ 2. Send Payload to LLM Completion API ]
       │         │ (Returns structured JSON tracklist)
       │         ▼
       ├─► [ 3. Fuzzy-match Tracklist against Emby Library ]
       └─► [ 4. Execute IPlaylistManager.CreatePlaylist ]
                 │
                 ▼
       [ Natively Saved Emby Playlist ]
```

---

## 2. Prerequisites & Setup Requirements

### 2.1 Developer Environment
- **Operating System:** Windows 11
- **IDE:** Visual Studio 2022 (with .NET Desktop Development workload) or VS Code
- **SDK:** .NET 8.0 SDK (or the version corresponding to your target Emby Server build)

### 2.2 External Dependencies
- **Emby Server DLL References:** (Must be copied or referenced from the active Emby installation directory `%AppData%\Emby-Server\system\`)
  - `MediaBrowser.Controller.dll`
  - `MediaBrowser.Model.dll`
  - `MediaBrowser.Common.dll`
- **LLM Token Provider:** OpenAI API Key (or equivalent compatible local/cloud endpoint like Ollama or Anthropic).

---

## 3. Core Functional Requirements

### 3.1 Configuration Page (Frontend UI)
- **Implementation Mechanism:** Must use a custom HTML file embedded as a resource or served dynamically using a class implementing `IHasWebPages`.
- **UI Components:**
  - **API Key Configuration:** A secure password-masked text box to save the OpenAI API key.
  - **Prompt Input Field:** A multiline text area labeled "Describe your playlist theme...".
  - **Generation Button:** A button triggering the generation sequence.
  - **Visual Status indicator:** Loading animations or text updates stating current execution state (*"Analyzing library..."*, *"Thinking..."*, *"Creating playlist..."*).

### 3.2 Backend Service Implementation
- **Plugin Entry Point:** Inherit from `IServerEntryPoint` to orchestrate lifecycle hooks.
- **Custom Rest API:** Inherit from `IRestfulService` to establish a custom web pathway (e.g., `/Plugin/AIPlaylistGenerator/Create`).
- **Music Catalog Extraction:** Use `ILibraryManager` to query items filtered by type `Audio`.
- **AI Orchestration Framework:** 
  - Construct a system prompt embedding either the full track list (if small) or structural genres/artists present in the library.
  - Require structured JSON back from the LLM endpoint specifying a list of matching `{"Artist": "...", "Title": "..."}` objects.
- **Matching & Consolidation:** Implement a string-distance algorithm (e.g., Levenshtein or fuzzy matching) to resolve the AI’s text suggestions to true internal Emby library `Audio` metadata object unique identifiers (`Guid`).
- **Playlist Matrix Assembly:** Invoke `IPlaylistManager.CreatePlaylist` using the collection of resolved IDs.

---

## 4. Test Specifications

### 4.1 Unit Tests (Mock Environment)
- **UT-001: String Matcher Validation**
  * *Objective:* Verify the fuzzy-matching utility resolves typos.
  * *Input:* AI returns `"The Beatles - Norweigan Wood"`. Library contains `"The Beatles - Norwegian Wood"`.
  * *Expected Output:* True match assignment.
- **UT-002: OpenAI Parser Robustness**
  * *Objective:* Ensure valid handling of malformed or unexpectedly formatted JSON responses from the LLM.
  * *Input:* Raw text block wrapping the JSON payload.
  * *Expected Output:* Graceful parsing extraction or defined exception handling.

### 4.2 Integration & System Tests
- **ST-001: API Endpoint Availability**
  * *Objective:* Assert that the custom endpoint registers with Emby's internal web routing gateway.
  * *Input:* HTTP POST to `/Plugin/AIPlaylistGenerator/Create` with placeholder payload.
  * *Expected Output:* HTTP 401 (Unauthorized without valid session token) or HTTP 200/400.
- **ST-002: End-to-End Generation Sequence**
  * *Objective:* Verify a playlist is written to disk.
  * *Input:* Prompt text: *"Upbeat 80s synth music for working out"*.
  * *Expected Output:* Emby reflects a newly generated playlist entity accessible by users.

---

## 5. Deployment Instructions

1. Compile the project target as a Class Library outputting a single `.dll` bundle.
2. Ensure the output directory configuration redirects or manually drop the `.dll` file into:
   `%AppData%\Emby-Server\programdata\plugins`
3. Restart Emby Server entirely via the Windows 11 System Tray.
4. Navigate to **Emby Dashboard -> Plugins** to confirm instantiation and load the configuration viewport.