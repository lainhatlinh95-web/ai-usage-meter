# AI Usage Overlay

Lightweight Windows overlay for Codex and Claude usage. Codex quota and reset
times are read from Codex's authenticated app-server status endpoint; this is a
status read, not a model/API call. Local JSONL logs under `%USERPROFILE%\.codex`
and `%USERPROFILE%\.claude` are still used for rolling token totals and fallback
data.

This repository is intentionally small: a compiled WinForms HUD plus a
PowerShell fallback. It does not use Electron, Tauri, Docker, Grafana, browser
automation, or terminal screen scraping.

## Run

```cmd
run-overlay.cmd
```

`run-overlay.cmd` starts `AiUsageOverlay.exe` when it exists. If you edit the C#
source, rebuild it with:

```cmd
build.cmd
```

Click the palette button or right-click the overlay to switch between Dark HUD,
Glassmorphism, and macOS Light styles. Drag it with the left mouse button.

## What It Shows

- `CODEX 5h / 7d`: local rolling token totals from Codex `token_count` events.
- `CODEX 5h left / 7d left`: live Codex account quota and reset times from
  `account/rateLimits/read`, cached for 60 seconds. If that read fails, the
  overlay falls back to the latest local Codex JSONL rate-limit entry.
- `CLAUDE 1h / 7d`: local rolling token totals from Claude assistant usage
  records, deduped by request and message id.

Claude CLI logs inspected here contain token usage, but not subscription reset
timestamps. The widget reports Claude rolling usage totals only unless those
reset fields appear in future local logs. It intentionally does not call Claude
or Anthropic APIs to fetch them.

## Privacy

- No raw prompts, transcripts, screenshots, or terminal output are stored.
- Claude usage is read from local Claude JSONL records only.
- Codex rolling token totals are read from local Codex JSONL records only.
- Codex quota percentages and reset times are read through Codex's local
  authenticated app-server path, `account/rateLimits/read`.
- No API keys are required by this overlay.

## Avoided in v1 / Non-goals

- No Electron, Tauri, Docker, Grafana, cloud sync, database, or background
  browser session.
- No browser scraping of the ChatGPT analytics page.
- No terminal screen scraping of Codex `/status`.
- No claim that local Codex token totals exactly match billing or quota
  accounting.
- No raw Claude or Codex conversation content collection.

## Verify Without Opening the Overlay

```powershell
.\AiUsageOverlay.exe -Once
```

## Low-Resource Design

- Compiled WinForms executable, no Electron/browser runtime.
- Polls every 10 seconds by default; Codex account status is cached for 60
  seconds.
- Reads only recent `.jsonl` files modified in the last 7 days.
- No API keys and no model calls.
- PowerShell implementation remains as a fallback in `Start-AiUsageOverlay.ps1`.
