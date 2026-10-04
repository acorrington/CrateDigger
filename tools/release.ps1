<#
.SYNOPSIS
    Builds, tests, tags, and publishes a CrateDigger GitHub release.

.DESCRIPTION
    Why local instead of GitHub Actions: the deployable CrateDigger.dll must be
    compiled against Emby's proprietary MediaBrowser.*.dll (present in lib\ on a
    dev machine with Emby installed) and ILRepack-merged into a single DLL.
    Those assemblies are intentionally NOT in the repository, so CI can only
    build in stub mode. This script runs on a machine with Emby installed.

    Pipeline: unit tests -> Release build (real Emby refs + ILRepack merge)
    -> annotated tag -> push tag -> gh release create with the merged DLL.

.EXAMPLE
    pwsh -File tools/release.ps1            # version from Directory.Build.props
    pwsh -File tools/release.ps1 -Version 0.2.0
#>
param(
    [string]$Version = ""
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

# 1. Version: explicit arg, else read Directory.Build.props
if (-not $Version) {
    $props = Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw
    if ($props -match '<Version>([^<]+)</Version>') { $Version = $Matches[1] }
    else { throw "Could not read <Version> from Directory.Build.props; pass -Version." }
}
$tag = "v$Version"
Write-Host "==> Releasing CrateDigger $tag" -ForegroundColor Cyan

# 2. Preflight
Push-Location $repoRoot
try {
    if (git status --porcelain) { throw "Working tree is not clean - commit or stash first." }
    foreach ($dll in 'MediaBrowser.Controller.dll', 'MediaBrowser.Model.dll', 'MediaBrowser.Common.dll') {
        if (-not (Test-Path "lib/$dll")) { throw "lib/$dll missing - real Emby references required for a deployable build." }
    }
    if (git tag -l $tag) { throw "Tag $tag already exists." }

    # 3. Test gate
    Write-Host '==> Running unit tests' -ForegroundColor Cyan
    dotnet test tests/CrateDigger.Core.Tests/CrateDigger.Core.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw "Tests failed - aborting release." }

    # 4. Release build (runs ILRepack merge + local deploy target)
    Write-Host '==> Building Release (real Emby refs, ILRepack merge)' -ForegroundColor Cyan
    dotnet build CrateDigger.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed - aborting release." }

    $dllPath = Join-Path $repoRoot 'src/CrateDigger.Plugin/bin/Release/net8.0/CrateDigger.dll'
    if (-not (Test-Path $dllPath)) { throw "Expected merged DLL not found: $dllPath" }
    $dllSize = (Get-Item $dllPath).Length
    if ($dllSize -lt 60KB) {
        throw "CrateDigger.dll is only $dllSize bytes - ILRepack merge likely did not run (Core not merged)."
    }

    # Sanity: the merged assembly must NOT reference CrateDigger.Core separately.
    $refs = [System.Reflection.Assembly]::LoadFile((Resolve-Path $dllPath)).GetReferencedAssemblies().Name
    if ($refs -contains 'CrateDigger.Core') {
        throw 'Merged DLL still references CrateDigger.Core - release would break on Emby (isolated load contexts).'
    }
    Write-Host "==> Merged DLL OK ($([math]::Round($dllSize/1KB)) KB, $(($refs | Where-Object { $_ -like 'MediaBrowser*' }).Count) MediaBrowser refs, no Core ref)" -ForegroundColor Green

    # 5. Tag + push
    Write-Host "==> Tagging $tag" -ForegroundColor Cyan
    git tag -a $tag -m "CrateDigger $tag"
    git push origin $tag

    # 6. GitHub release with the single-DLL asset
    $notes = @"
## Install

1. Download ``CrateDigger.dll`` below.
2. Copy it into ``%AppData%\Emby-Server\programdata\plugins\``.
3. Restart Emby Server.
4. Open **Dashboard → Plugins → CrateDigger** to configure.

## Configure

Settings live in the dashboard: API key, OpenAI-compatible base URL
(OpenAI, Unsloth/llama.cpp, Ollama, OpenRouter...), model id, playlist length,
match threshold, LLM timeout, max output tokens, and extra request JSON
(e.g. ``{"chat_template_kwargs":{"enable_thinking":false}}`` for reasoning models).

## What's new in $tag

- Full end-to-end pipeline verified against Emby 4.10.1.0 (ST-001 401 auth gate,
  ST-002 mock-LLM end-to-end, ST-003 real local-LLM generation)
- Multi-variant fuzzy matcher: typos, diacritics, feat./remaster decorations,
  parenthetical asymmetry (45 matcher/parser tests of 50 total)
- Local-model hardening: configurable timeout (HttpClient's hidden 100s default
  removed), max_tokens cap, extra-JSON passthrough, no retry on timeout
- Staged dashboard status UI with defensive response parsing + console breadcrumbs
- Single-DLL build (ILRepack) — required because Emby isolates plugin load contexts

## Notes

- Built against Emby Server **4.10.1.0** references (``lib\`` is gitignored —
  Emby assemblies are proprietary and not redistributed).
- The API key is stored as plain XML in the plugin configuration folder.
"@
    Write-Host '==> Creating GitHub release' -ForegroundColor Cyan
    gh release create $tag $dllPath `
        --title "CrateDigger $tag" `
        --notes $notes
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed." }

    Write-Host "==> Done: https://github.com/acorrington/CrateDigger/releases/tag/$tag" -ForegroundColor Green
}
finally {
    Pop-Location
}