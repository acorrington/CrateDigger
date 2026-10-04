# lib/ — drop real Emby DLLs here

Copy these files from your Emby Server installation (`%AppData%\Emby-Server\system\`):

- `MediaBrowser.Controller.dll`
- `MediaBrowser.Model.dll`
- `MediaBrowser.Common.dll`

When all three are present, `CrateDigger.Plugin` automatically references the **real** Emby API
instead of the compile-time stubs in `src/CrateDigger.EmbyStubs`.

This directory is intentionally otherwise empty. The DLLs are **not** committed to the repo
(they are proprietary to your Emby install and provided at runtime by the server).

After switching, run `dotnet build` — any signature mismatch between the stubs and the real
API will surface immediately as compile errors. Record the resolved real signatures in
`docs/api-surface.md`.