param(
  [switch]$Once,
  [int]$RefreshSeconds = 10,
  [string]$CodexRoot = (Join-Path $env:USERPROFILE ".codex"),
  [string]$ClaudeRoot = (Join-Path $env:USERPROFILE ".claude")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function ConvertFrom-UnixSeconds {
  param([object]$Value)
  if ($null -eq $Value) { return $null }
  try {
    return [DateTimeOffset]::FromUnixTimeSeconds([int64]$Value).LocalDateTime
  } catch {
    return $null
  }
}

function ConvertTo-LocalTime {
  param([object]$Value)
  if ($null -eq $Value) { return $null }
  try {
    return ([DateTimeOffset]::Parse([string]$Value)).LocalDateTime
  } catch {
    return $null
  }
}

function Format-CompactNumber {
  param([double]$Value)
  if ($Value -ge 1000000000) { return ("{0:N1}b" -f ($Value / 1000000000)).Replace(".0", "") }
  if ($Value -ge 1000000) { return ("{0:N1}m" -f ($Value / 1000000)).Replace(".0", "") }
  if ($Value -ge 1000) { return ("{0:N1}k" -f ($Value / 1000)).Replace(".0", "") }
  return ([int64]$Value).ToString()
}

function Format-ResetTime {
  param([object]$Value)
  if ($null -eq $Value) { return "--" }
  $now = Get-Date
  $reset = [DateTime]$Value
  if ($reset -lt $now) { return "now" }
  if ($reset.Date -eq $now.Date) { return $reset.ToString("HH:mm") }
  return $reset.ToString("ddd HH:mm")
}

function Get-JsonlFiles {
  param(
    [string[]]$Roots,
    [DateTime]$Since,
    [int]$MaxFiles = 80
  )

  $files = foreach ($root in $Roots) {
    if (Test-Path -LiteralPath $root) {
      Get-ChildItem -LiteralPath $root -Recurse -File -Filter "*.jsonl" -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -ge $Since }
    }
  }

  @($files | Sort-Object LastWriteTime -Descending | Select-Object -First $MaxFiles)
}

function Get-RegexInt64 {
  param(
    [string]$Text,
    [string]$Pattern
  )

  $match = [regex]::Match($Text, $Pattern)
  if (-not $match.Success) { return 0L }
  try {
    return [int64]$match.Groups[1].Value
  } catch {
    return 0L
  }
}

function Get-RegexDouble {
  param(
    [string]$Text,
    [string]$Pattern
  )

  $match = [regex]::Match($Text, $Pattern)
  if (-not $match.Success) { return $null }
  try {
    return [double]$match.Groups[1].Value
  } catch {
    return $null
  }
}

function Get-RegexText {
  param(
    [string]$Text,
    [string]$Pattern
  )

  $match = [regex]::Match($Text, $Pattern)
  if (-not $match.Success) { return "" }
  return $match.Groups[1].Value
}

function Read-JsonlLines {
  param([System.IO.FileInfo[]]$Files)

  foreach ($file in $Files) {
    try {
      foreach ($line in [System.IO.File]::ReadLines($file.FullName)) {
        if (-not [string]::IsNullOrWhiteSpace($line)) {
          $line
        }
      }
    } catch {
      continue
    }
  }
}

function Get-CodexStats {
  param([string]$Root)

  $now = Get-Date
  $hourStart = $now.AddHours(-1)
  $weekStart = $now.AddDays(-7)
  $roots = @(
    (Join-Path $Root "sessions"),
    (Join-Path $Root "archived_sessions")
  )

  $hour = 0L
  $week = 0L
  $latestPrimary = $null
  $latestSecondary = $null
  $latestTimestamp = [DateTime]::MinValue

  $files = Get-JsonlFiles -Roots $roots -Since $weekStart
  foreach ($line in Read-JsonlLines -Files $files) {
    if ($line.IndexOf('"type":"token_count"', [StringComparison]::Ordinal) -lt 0) { continue }
    $timestampText = Get-RegexText $line '"timestamp":"([^"]+)"'
    $timestamp = ConvertTo-LocalTime $timestampText
    if ($null -eq $timestamp -or $timestamp -lt $weekStart) { continue }

    $tokens = Get-RegexInt64 $line '"last_token_usage":\{[^}]*"total_tokens":([0-9]+)'
    $week += $tokens
    if ($timestamp -ge $hourStart) { $hour += $tokens }

    if ($timestamp -ge $latestTimestamp -and $line.IndexOf('"rate_limits"', [StringComparison]::Ordinal) -ge 0) {
      $latestTimestamp = $timestamp
      $primaryMatch = [regex]::Match($line, '"primary":\{[^}]*"used_percent":([0-9.]+)[^}]*"resets_at":([0-9]+)')
      $secondaryMatch = [regex]::Match($line, '"secondary":\{[^}]*"used_percent":([0-9.]+)[^}]*"resets_at":([0-9]+)')
      $latestPrimary = if ($primaryMatch.Success) {
        [pscustomobject]@{ used = [double]$primaryMatch.Groups[1].Value; reset = [int64]$primaryMatch.Groups[2].Value }
      } else { $null }
      $latestSecondary = if ($secondaryMatch.Success) {
        [pscustomobject]@{ used = [double]$secondaryMatch.Groups[1].Value; reset = [int64]$secondaryMatch.Groups[2].Value }
      } else { $null }
    }
  }

  [pscustomobject]@{
    hourTokens = $hour
    weekTokens = $week
    shortUsedPercent = if ($null -ne $latestPrimary) { $latestPrimary.used } else { $null }
    weekUsedPercent = if ($null -ne $latestSecondary) { $latestSecondary.used } else { $null }
    shortReset = if ($null -ne $latestPrimary) { ConvertFrom-UnixSeconds $latestPrimary.reset } else { $null }
    weekReset = if ($null -ne $latestSecondary) { ConvertFrom-UnixSeconds $latestSecondary.reset } else { $null }
    updatedAt = if ($latestTimestamp -eq [DateTime]::MinValue) { $null } else { $latestTimestamp }
  }
}

function Get-ClaudeStats {
  param([string]$Root)

  $now = Get-Date
  $hourStart = $now.AddHours(-1)
  $weekStart = $now.AddDays(-7)
  $projectRoot = Join-Path $Root "projects"
  $hour = 0L
  $week = 0L
  $latestTimestamp = [DateTime]::MinValue
  $seen = [System.Collections.Generic.HashSet[string]]::new()

  $files = Get-JsonlFiles -Roots @($projectRoot) -Since $weekStart
  foreach ($line in Read-JsonlLines -Files $files) {
    if ($line.IndexOf('"type":"assistant"', [StringComparison]::Ordinal) -lt 0) { continue }
    if ($line.IndexOf('"usage":', [StringComparison]::Ordinal) -lt 0) { continue }
    $timestampText = Get-RegexText $line '"timestamp":"([^"]+)"'
    $timestamp = ConvertTo-LocalTime $timestampText
    if ($null -eq $timestamp -or $timestamp -lt $weekStart) { continue }

    $messageId = Get-RegexText $line '"id":"(msg_[^"]+)"'
    $requestId = Get-RegexText $line '"requestId":"([^"]+)"'
    $dedupeKey = "$requestId|$messageId"
    if (-not [string]::IsNullOrWhiteSpace($dedupeKey) -and -not $seen.Add($dedupeKey)) { continue }

    $tokens = 0L
    $tokens += Get-RegexInt64 $line '"input_tokens":([0-9]+)'
    $tokens += Get-RegexInt64 $line '"cache_creation_input_tokens":([0-9]+)'
    $tokens += Get-RegexInt64 $line '"cache_read_input_tokens":([0-9]+)'
    $tokens += Get-RegexInt64 $line '"output_tokens":([0-9]+)'
    $week += $tokens
    if ($timestamp -ge $hourStart) { $hour += $tokens }
    if ($timestamp -gt $latestTimestamp) { $latestTimestamp = $timestamp }
  }

  [pscustomobject]@{
    hourTokens = $hour
    weekTokens = $week
    updatedAt = if ($latestTimestamp -eq [DateTime]::MinValue) { $null } else { $latestTimestamp }
  }
}

function Get-OverlayStats {
  [pscustomobject]@{
    codex = Get-CodexStats -Root $CodexRoot
    claude = Get-ClaudeStats -Root $ClaudeRoot
    sampledAt = Get-Date
  }
}

if ($Once) {
  Get-OverlayStats | ConvertTo-Json -Depth 8
  exit 0
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

[System.Windows.Forms.Application]::EnableVisualStyles()

$form = [System.Windows.Forms.Form]::new()
$form.Text = "AI Usage Overlay"
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$form.TopMost = $true
$form.ShowInTaskbar = $false
$form.BackColor = [System.Drawing.Color]::FromArgb(10, 10, 10)
$form.Opacity = 0.86
$form.Width = 310
$form.Height = 126
$form.Left = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea.Right - $form.Width - 18
$form.Top = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea.Top + 18
$form.Font = [System.Drawing.Font]::new("Consolas", 9.5, [System.Drawing.FontStyle]::Regular)

$label = [System.Windows.Forms.Label]::new()
$label.Dock = [System.Windows.Forms.DockStyle]::Fill
$label.ForeColor = [System.Drawing.Color]::FromArgb(225, 245, 225)
$label.BackColor = [System.Drawing.Color]::Transparent
$label.Padding = [System.Windows.Forms.Padding]::new(10, 8, 10, 8)
$label.TextAlign = [System.Drawing.ContentAlignment]::MiddleLeft
$form.Controls.Add($label)

$menu = [System.Windows.Forms.ContextMenuStrip]::new()
$closeItem = $menu.Items.Add("Close")
$closeItem.add_Click({ $form.Close() })
$form.ContextMenuStrip = $menu
$label.ContextMenuStrip = $menu

$dragging = $false
$dragStart = [System.Drawing.Point]::Empty
$startDrag = {
  if ($_.Button -eq [System.Windows.Forms.MouseButtons]::Left) {
    $script:dragging = $true
    $script:dragStart = $_.Location
  }
}
$moveDrag = {
  if ($script:dragging) {
    $form.Left += $_.X - $script:dragStart.X
    $form.Top += $_.Y - $script:dragStart.Y
  }
}
$stopDrag = { $script:dragging = $false }
$form.add_MouseDown($startDrag)
$form.add_MouseMove($moveDrag)
$form.add_MouseUp($stopDrag)
$label.add_MouseDown($startDrag)
$label.add_MouseMove($moveDrag)
$label.add_MouseUp($stopDrag)

function Set-OverlayText {
  try {
    $stats = Get-OverlayStats
    $codex = $stats.codex
    $claude = $stats.claude
    $codexShort = if ($null -ne $codex.shortUsedPercent) { "{0:N0}%" -f $codex.shortUsedPercent } else { "--" }
    $codexWeek = if ($null -ne $codex.weekUsedPercent) { "{0:N0}%" -f $codex.weekUsedPercent } else { "--" }

    $label.Text = @(
      "AI USAGE"
      ("CODEX  1h {0,7}  7d {1,7}" -f (Format-CompactNumber $codex.hourTokens), (Format-CompactNumber $codex.weekTokens))
      ("       short {0,4} reset {1}" -f $codexShort, (Format-ResetTime $codex.shortReset))
      ("       week  {0,4} reset {1}" -f $codexWeek, (Format-ResetTime $codex.weekReset))
      ("CLAUDE 1h {0,7}  7d {1,7}" -f (Format-CompactNumber $claude.hourTokens), (Format-CompactNumber $claude.weekTokens))
      ("poll {0}s  {1}" -f $RefreshSeconds, $stats.sampledAt.ToString("HH:mm:ss"))
    ) -join [Environment]::NewLine
  } catch {
    $label.Text = "AI USAGE" + [Environment]::NewLine + "read error: " + $_.Exception.Message
  } finally {
    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()
  }
}

Set-OverlayText
$timer = [System.Windows.Forms.Timer]::new()
$timer.Interval = [Math]::Max(2, $RefreshSeconds) * 1000
$timer.add_Tick({ Set-OverlayText })
$timer.Start()

[System.Windows.Forms.Application]::Run($form)
