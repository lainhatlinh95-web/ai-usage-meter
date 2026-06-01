using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options.Once)
        {
            var stats = UsageReader.Read(options);
            Console.WriteLine(stats.ToJson());
            return 0;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new OverlayForm(options));
        return 0;
    }
}

internal sealed class Options
{
    public bool Once;
    public int RefreshSeconds = 10;
    public string CodexRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex");
    public string ClaudeRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude");

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (EqualsAny(arg, "-Once", "--once")) options.Once = true;
            else if (EqualsAny(arg, "-RefreshSeconds", "--refresh-seconds") && i + 1 < args.Length)
            {
                int seconds;
                if (int.TryParse(args[++i], out seconds)) options.RefreshSeconds = Math.Max(2, seconds);
            }
            else if (EqualsAny(arg, "-CodexRoot", "--codex-root") && i + 1 < args.Length)
            {
                options.CodexRoot = args[++i];
            }
            else if (EqualsAny(arg, "-ClaudeRoot", "--claude-root") && i + 1 < args.Length)
            {
                options.ClaudeRoot = args[++i];
            }
        }
        return options;
    }

    private static bool EqualsAny(string value, params string[] names)
    {
        return names.Any(name => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class OverlayForm : Form
{
    private readonly Options _options;
    private readonly Label _label;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _dragging;
    private Point _dragStart;

    public OverlayForm(Options options)
    {
        _options = options;
        Text = "AI Usage Overlay";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(10, 10, 10);
        Opacity = 0.86;
        Width = 310;
        Height = 126;
        Left = Screen.PrimaryScreen.WorkingArea.Right - Width - 18;
        Top = Screen.PrimaryScreen.WorkingArea.Top + 18;
        Font = new Font("Consolas", 9.5f, FontStyle.Regular);

        _label = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(225, 245, 225),
            BackColor = Color.Transparent,
            Padding = new Padding(10, 8, 10, 8),
            TextAlign = ContentAlignment.MiddleLeft
        };
        Controls.Add(_label);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh", null, delegate { UpdateText(); });
        menu.Items.Add("Close", null, delegate { Close(); });
        ContextMenuStrip = menu;
        _label.ContextMenuStrip = menu;

        MouseDown += StartDrag;
        MouseMove += MoveDrag;
        MouseUp += StopDrag;
        _label.MouseDown += StartDrag;
        _label.MouseMove += MoveDrag;
        _label.MouseUp += StopDrag;

        _timer = new System.Windows.Forms.Timer { Interval = Math.Max(2, _options.RefreshSeconds) * 1000 };
        _timer.Tick += delegate { UpdateText(); };
        _timer.Start();
        UpdateText();
    }

    private void StartDrag(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragStart = e.Location;
    }

    private void MoveDrag(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        Left += e.X - _dragStart.X;
        Top += e.Y - _dragStart.Y;
    }

    private void StopDrag(object sender, MouseEventArgs e)
    {
        _dragging = false;
    }

    private void UpdateText()
    {
        try
        {
            var stats = UsageReader.Read(_options);
            var codexShort = stats.Codex.ShortUsedPercent.HasValue
                ? Math.Max(0, 100 - stats.Codex.ShortUsedPercent.Value).ToString("N0", CultureInfo.InvariantCulture) + "%"
                : "--";
            var codexWeek = stats.Codex.WeekUsedPercent.HasValue
                ? Math.Max(0, 100 - stats.Codex.WeekUsedPercent.Value).ToString("N0", CultureInfo.InvariantCulture) + "%"
                : "--";

            _label.Text = string.Join(Environment.NewLine, new[]
            {
                "AI USAGE",
                string.Format("CODEX  {0,2} {1,7}  7d {2,7}", FormatWindow(stats.Codex.ShortWindowMinutes), FormatCompact(stats.Codex.HourTokens), FormatCompact(stats.Codex.WeekTokens)),
                string.Format("       5h left {0,4} reset {1}", codexShort, FormatReset(stats.Codex.ShortReset)),
                string.Format("       7d left {0,4} reset {1}", codexWeek, FormatReset(stats.Codex.WeekReset)),
                string.Format("CLAUDE 1h {0,7}  7d {1,7}", FormatCompact(stats.Claude.HourTokens), FormatCompact(stats.Claude.WeekTokens)),
                string.Format("poll {0}s {1} {2}", _options.RefreshSeconds, stats.Codex.Source, stats.SampledAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
            });
        }
        catch (Exception ex)
        {
            _label.Text = "AI USAGE" + Environment.NewLine + "read error: " + ex.Message;
        }
    }

    private static string FormatCompact(long value)
    {
        if (value >= 1000000000L) return (value / 1000000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "b";
        if (value >= 1000000L) return (value / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "m";
        if (value >= 1000L) return (value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k";
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatWindow(int minutes)
    {
        if (minutes > 0 && minutes % 60 == 0) return (minutes / 60).ToString(CultureInfo.InvariantCulture) + "h";
        return minutes.ToString(CultureInfo.InvariantCulture) + "m";
    }

    private static string FormatReset(DateTime? value)
    {
        if (!value.HasValue) return "--";
        var now = DateTime.Now;
        if (value.Value < now) return "now";
        if (value.Value.Date == now.Date) return value.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        return value.Value.ToString("ddd HH:mm", CultureInfo.InvariantCulture);
    }
}

internal static class UsageReader
{
    private static readonly Regex TimestampRegex = new Regex("\"timestamp\":\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex LastTokenRegex = new Regex("\"last_token_usage\":\\{[^}]*\"total_tokens\":([0-9]+)", RegexOptions.Compiled);
    private static readonly Regex PrimaryRegex = new Regex("\"primary\":\\{[^}]*\"used_percent\":([0-9.]+)[^}]*\"window_minutes\":([0-9]+)[^}]*\"resets_at\":([0-9]+)", RegexOptions.Compiled);
    private static readonly Regex SecondaryRegex = new Regex("\"secondary\":\\{[^}]*\"used_percent\":([0-9.]+)[^}]*\"resets_at\":([0-9]+)", RegexOptions.Compiled);
    private static readonly Regex AppPrimaryRegex = new Regex("\"primary\":\\{[^}]*\"usedPercent\":([0-9.]+)[^}]*\"windowDurationMins\":([0-9]+)[^}]*\"resetsAt\":([0-9]+)", RegexOptions.Compiled);
    private static readonly Regex AppSecondaryRegex = new Regex("\"secondary\":\\{[^}]*\"usedPercent\":([0-9.]+)[^}]*\"windowDurationMins\":([0-9]+)[^}]*\"resetsAt\":([0-9]+)", RegexOptions.Compiled);
    private static readonly Regex ClaudeMessageIdRegex = new Regex("\"id\":\"(msg_[^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex RequestIdRegex = new Regex("\"requestId\":\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex InputTokenRegex = new Regex("\"input_tokens\":([0-9]+)", RegexOptions.Compiled);
    private static readonly Regex CacheCreateRegex = new Regex("\"cache_creation_input_tokens\":([0-9]+)", RegexOptions.Compiled);
    private static readonly Regex CacheReadRegex = new Regex("\"cache_read_input_tokens\":([0-9]+)", RegexOptions.Compiled);
    private static readonly Regex OutputTokenRegex = new Regex("\"output_tokens\":([0-9]+)", RegexOptions.Compiled);
    private static readonly object CodexAccountLock = new object();
    private static ProviderStats cachedCodexAccountStats;
    private static DateTime cachedCodexAccountStatsAt = DateTime.MinValue;

    public static OverlayStats Read(Options options)
    {
        return new OverlayStats
        {
            Codex = ReadCodex(options.CodexRoot),
            Claude = ReadClaude(options.ClaudeRoot),
            SampledAt = DateTime.Now
        };
    }

    private static ProviderStats ReadCodex(string root)
    {
        var now = DateTime.Now;
        var shortWindowMinutes = 300;
        var shortWindowStart = now.AddMinutes(-shortWindowMinutes);
        var weekStart = now.AddDays(-7);
        var stats = new ProviderStats();
        stats.ShortWindowMinutes = shortWindowMinutes;
        var latest = DateTime.MinValue;

        foreach (var line in ReadRecentLines(new[]
        {
            Path.Combine(root, "sessions"),
            Path.Combine(root, "archived_sessions")
        }, weekStart, 80))
        {
            if (line.IndexOf("\"type\":\"token_count\"", StringComparison.Ordinal) < 0) continue;
            var timestamp = ParseTimestamp(line);
            if (!timestamp.HasValue || timestamp.Value < weekStart) continue;

            var tokens = MatchInt64(LastTokenRegex, line);
            stats.WeekTokens += tokens;
            if (timestamp.Value >= shortWindowStart) stats.HourTokens += tokens;

            if (timestamp.Value >= latest && line.IndexOf("\"rate_limits\"", StringComparison.Ordinal) >= 0)
            {
                latest = timestamp.Value;
                stats.UpdatedAt = timestamp.Value;
                var primary = PrimaryRegex.Match(line);
                var secondary = SecondaryRegex.Match(line);
                if (primary.Success)
                {
                    stats.ShortUsedPercent = ParseDouble(primary.Groups[1].Value);
                    stats.ShortWindowMinutes = ParseInt(primary.Groups[2].Value, shortWindowMinutes);
                    stats.ShortReset = FromUnixSeconds(primary.Groups[3].Value);
                }
                if (secondary.Success)
                {
                    stats.WeekUsedPercent = ParseDouble(secondary.Groups[1].Value);
                    stats.WeekReset = FromUnixSeconds(secondary.Groups[2].Value);
                }
            }
        }

        var accountStats = ReadCodexAccountRateLimits();
        if (accountStats != null)
        {
            stats.ShortUsedPercent = accountStats.ShortUsedPercent;
            stats.WeekUsedPercent = accountStats.WeekUsedPercent;
            stats.ShortWindowMinutes = accountStats.ShortWindowMinutes;
            stats.ShortReset = accountStats.ShortReset;
            stats.WeekReset = accountStats.WeekReset;
            stats.Source = accountStats.Source;
            stats.UpdatedAt = accountStats.UpdatedAt;
        }

        return stats;
    }

    private static ProviderStats ReadCodexAccountRateLimits()
    {
        lock (CodexAccountLock)
        {
            if (cachedCodexAccountStats != null &&
                DateTime.Now - cachedCodexAccountStatsAt < TimeSpan.FromSeconds(60))
            {
                return cachedCodexAccountStats.Clone();
            }

            var fresh = TryReadCodexAccountRateLimits();
            if (fresh != null)
            {
                cachedCodexAccountStats = fresh.Clone();
                cachedCodexAccountStatsAt = DateTime.Now;
                return fresh;
            }

            return cachedCodexAccountStats != null ? cachedCodexAccountStats.Clone() : null;
        }
    }

    private static ProviderStats TryReadCodexAccountRateLimits()
    {
        var codexExe = FindCodexExe();
        if (string.IsNullOrEmpty(codexExe)) return null;

        var response = string.Empty;
        using (var done = new AutoResetEvent(false))
        using (var process = new Process())
        {
            process.StartInfo = new ProcessStartInfo
            {
                FileName = codexExe,
                Arguments = "app-server --listen stdio://",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                if (e.Data.IndexOf("\"id\":2", StringComparison.Ordinal) >= 0 &&
                    e.Data.IndexOf("\"rateLimits\"", StringComparison.Ordinal) >= 0)
                {
                    response = e.Data;
                    done.Set();
                }
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.StandardInput.WriteLine("{\"method\":\"initialize\",\"id\":1,\"params\":{\"clientInfo\":{\"name\":\"ai-usage-overlay\",\"version\":\"0.1.0\"},\"capabilities\":{\"experimentalApi\":true}}}");
                process.StandardInput.WriteLine("{\"method\":\"initialized\",\"params\":{}}");
                process.StandardInput.WriteLine("{\"method\":\"account/rateLimits/read\",\"id\":2,\"params\":null}");
                process.StandardInput.Flush();

                done.WaitOne(TimeSpan.FromSeconds(8));
            }
            catch
            {
                return null;
            }
            finally
            {
                try
                {
                    if (!process.HasExited) process.Kill();
                }
                catch
                {
                }
            }
        }

        if (string.IsNullOrWhiteSpace(response)) return null;

        var primary = AppPrimaryRegex.Match(response);
        var secondary = AppSecondaryRegex.Match(response);
        if (!primary.Success && !secondary.Success) return null;

        var stats = new ProviderStats();
        stats.Source = "acct";
        stats.UpdatedAt = DateTime.Now;
        if (primary.Success)
        {
            stats.ShortUsedPercent = ParseDouble(primary.Groups[1].Value);
            stats.ShortWindowMinutes = ParseInt(primary.Groups[2].Value, 300);
            stats.ShortReset = FromUnixSeconds(primary.Groups[3].Value);
        }
        else
        {
            stats.ShortWindowMinutes = 300;
        }
        if (secondary.Success)
        {
            stats.WeekUsedPercent = ParseDouble(secondary.Groups[1].Value);
            stats.WeekReset = FromUnixSeconds(secondary.Groups[3].Value);
        }

        return stats;
    }

    private static string FindCodexExe()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var candidates = new[]
        {
            Path.Combine(appData, "npm", "node_modules", "@openai", "codex", "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe"),
            Path.Combine(appData, "npm", "codex.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static ProviderStats ReadClaude(string root)
    {
        var now = DateTime.Now;
        var hourStart = now.AddHours(-1);
        var weekStart = now.AddDays(-7);
        var stats = new ProviderStats();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var projectRoot = Path.Combine(root, "projects");

        foreach (var line in ReadRecentLines(new[] { projectRoot }, weekStart, 80))
        {
            if (line.IndexOf("\"type\":\"assistant\"", StringComparison.Ordinal) < 0) continue;
            if (line.IndexOf("\"usage\":", StringComparison.Ordinal) < 0) continue;
            var timestamp = ParseTimestamp(line);
            if (!timestamp.HasValue || timestamp.Value < weekStart) continue;

            var requestId = MatchText(RequestIdRegex, line);
            var messageId = MatchText(ClaudeMessageIdRegex, line);
            var dedupeKey = requestId + "|" + messageId;
            if (dedupeKey.Length > 1 && !seen.Add(dedupeKey)) continue;

            var tokens =
                MatchInt64(InputTokenRegex, line) +
                MatchInt64(CacheCreateRegex, line) +
                MatchInt64(CacheReadRegex, line) +
                MatchInt64(OutputTokenRegex, line);
            stats.WeekTokens += tokens;
            if (timestamp.Value >= hourStart) stats.HourTokens += tokens;
            if (!stats.UpdatedAt.HasValue || timestamp.Value > stats.UpdatedAt.Value) stats.UpdatedAt = timestamp.Value;
        }

        return stats;
    }

    private static IEnumerable<string> ReadRecentLines(IEnumerable<string> roots, DateTime since, int maxFiles)
    {
        IEnumerable<FileInfo> files = Enumerable.Empty<FileInfo>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                files = files.Concat(new DirectoryInfo(root)
                    .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                    .Where(file => file.LastWriteTime >= since));
            }
            catch
            {
            }
        }

        foreach (var file in files.OrderByDescending(file => file.LastWriteTime).Take(maxFiles))
        {
            IEnumerable<string> lines;
            try
            {
                lines = File.ReadLines(file.FullName);
            }
            catch
            {
                continue;
            }

            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line)) yield return line;
            }
        }
    }

    private static DateTime? ParseTimestamp(string line)
    {
        var value = MatchText(TimestampRegex, line);
        DateTimeOffset parsed;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed))
        {
            return parsed.LocalDateTime;
        }
        return null;
    }

    private static long MatchInt64(Regex regex, string text)
    {
        var match = regex.Match(text);
        long value;
        return match.Success && long.TryParse(match.Groups[1].Value, out value) ? value : 0L;
    }

    private static string MatchText(Regex regex, string text)
    {
        var match = regex.Match(text);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static double? ParseDouble(string value)
    {
        double parsed;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : (double?)null;
    }

    private static int ParseInt(string value, int fallback)
    {
        int parsed;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
    }

    private static DateTime? FromUnixSeconds(string value)
    {
        long seconds;
        if (!long.TryParse(value, out seconds)) return null;
        return DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
    }
}

internal sealed class OverlayStats
{
    public ProviderStats Codex = new ProviderStats();
    public ProviderStats Claude = new ProviderStats();
    public DateTime SampledAt;

    public string ToJson()
    {
        var builder = new StringBuilder();
        builder.AppendLine("{");
        AppendProvider(builder, "codex", Codex, true);
        AppendProvider(builder, "claude", Claude, true);
        builder.Append("  \"sampledAt\": \"").Append(SampledAt.ToString("o", CultureInfo.InvariantCulture)).AppendLine("\"");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendProvider(StringBuilder builder, string name, ProviderStats stats, bool comma)
    {
        builder.Append("  \"").Append(name).AppendLine("\": {");
        builder.Append("    \"hourTokens\": ").Append(stats.HourTokens.ToString(CultureInfo.InvariantCulture)).AppendLine(",");
        builder.Append("    \"weekTokens\": ").Append(stats.WeekTokens.ToString(CultureInfo.InvariantCulture)).AppendLine(",");
        builder.Append("    \"shortWindowMinutes\": ").Append(stats.ShortWindowMinutes.ToString(CultureInfo.InvariantCulture)).AppendLine(",");
        builder.Append("    \"shortUsedPercent\": ").Append(JsonNullable(stats.ShortUsedPercent)).AppendLine(",");
        builder.Append("    \"weekUsedPercent\": ").Append(JsonNullable(stats.WeekUsedPercent)).AppendLine(",");
        builder.Append("    \"shortReset\": ").Append(JsonNullable(stats.ShortReset)).AppendLine(",");
        builder.Append("    \"weekReset\": ").Append(JsonNullable(stats.WeekReset)).AppendLine(",");
        builder.Append("    \"source\": \"").Append(stats.Source).AppendLine("\",");
        builder.Append("    \"updatedAt\": ").Append(JsonNullable(stats.UpdatedAt)).AppendLine();
        builder.Append("  }");
        if (comma) builder.AppendLine(",");
        else builder.AppendLine();
    }

    private static string JsonNullable(double? value)
    {
        return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "null";
    }

    private static string JsonNullable(DateTime? value)
    {
        return value.HasValue ? "\"" + value.Value.ToString("o", CultureInfo.InvariantCulture) + "\"" : "null";
    }
}

internal sealed class ProviderStats
{
    public long HourTokens;
    public long WeekTokens;
    public double? ShortUsedPercent;
    public double? WeekUsedPercent;
    public int ShortWindowMinutes = 60;
    public DateTime? ShortReset;
    public DateTime? WeekReset;
    public DateTime? UpdatedAt;
    public string Source = "logs";

    public ProviderStats Clone()
    {
        return new ProviderStats
        {
            HourTokens = HourTokens,
            WeekTokens = WeekTokens,
            ShortUsedPercent = ShortUsedPercent,
            WeekUsedPercent = WeekUsedPercent,
            ShortWindowMinutes = ShortWindowMinutes,
            ShortReset = ShortReset,
            WeekReset = WeekReset,
            UpdatedAt = UpdatedAt,
            Source = Source
        };
    }
}
