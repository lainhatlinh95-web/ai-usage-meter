using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
    private readonly System.Windows.Forms.Timer _timer;
    private OverlayStats _stats;
    private string _error;
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
        BackColor = Color.Black;
        Opacity = 0.68;
        Width = 760;
        Height = 150;
        Left = Screen.PrimaryScreen.WorkingArea.Right - Width - 18;
        Top = Screen.PrimaryScreen.WorkingArea.Top + 18;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh", null, delegate { UpdateStats(); });
        menu.Items.Add("Close", null, delegate { Close(); });
        ContextMenuStrip = menu;

        MouseDown += StartDrag;
        MouseMove += MoveDrag;
        MouseUp += StopDrag;

        _timer = new System.Windows.Forms.Timer { Interval = Math.Max(2, _options.RefreshSeconds) * 1000 };
        _timer.Tick += delegate { UpdateStats(); };
        _timer.Start();
        UpdateStats();
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

    private void UpdateStats()
    {
        try
        {
            _stats = UsageReader.Read(_options);
            _error = null;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (!string.IsNullOrEmpty(_error))
        {
            using (var brush = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            using (var font = new Font("Segoe UI", 11f, FontStyle.Regular))
            {
                g.DrawString("AI Usage: " + _error, font, brush, 18, 18);
            }
            return;
        }

        if (_stats == null)
        {
            using (var brush = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            using (var font = new Font("Segoe UI", 11f, FontStyle.Regular))
            {
                g.DrawString("AI Usage: loading", font, brush, 18, 18);
            }
            return;
        }

        var gap = 14;
        var cardWidth = (ClientSize.Width - 28 - gap) / 2;
        var cardHeight = ClientSize.Height - 24;
        var left = new RectangleF(12, 12, cardWidth, cardHeight);
        var right = new RectangleF(12 + cardWidth + gap, 12, cardWidth, cardHeight);

        var shortLeft = RemainingPercent(_stats.Codex.ShortUsedPercent);
        var weekLeft = RemainingPercent(_stats.Codex.WeekUsedPercent);
        DrawQuotaCard(g, left, "5 hour usage limit", shortLeft, _stats.Codex.ShortReset);
        DrawQuotaCard(g, right, "Weekly usage limit", weekLeft, _stats.Codex.WeekReset);
    }

    private static void DrawQuotaCard(Graphics g, RectangleF rect, string title, double? remainingPercent, DateTime? reset)
    {
        using (var path = RoundedRect(rect, 18f))
        using (var fill = new SolidBrush(Color.FromArgb(220, 30, 30, 30)))
        using (var border = new Pen(Color.FromArgb(80, 255, 255, 255), 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        var x = rect.Left + 22;
        var y = rect.Top + 18;
        using (var titleFont = new Font("Segoe UI", 10.5f, FontStyle.Bold))
        using (var valueFont = new Font("Segoe UI", 18f, FontStyle.Bold))
        using (var smallFont = new Font("Segoe UI", 9.5f, FontStyle.Regular))
        using (var muted = new SolidBrush(Color.FromArgb(205, 220, 226, 235)))
        using (var white = new SolidBrush(Color.White))
        {
            g.DrawString(title, titleFont, muted, x, y);
            var value = remainingPercent.HasValue
                ? remainingPercent.Value.ToString("N0", CultureInfo.InvariantCulture) + "% left"
                : "-- left";
            g.DrawString(value, valueFont, white, x, y + 29);

            var barRect = new RectangleF(x, y + 76, rect.Width - 44, 11);
            DrawProgressBar(g, barRect, remainingPercent);
            g.DrawString("reset: " + FormatReset(reset), smallFont, muted, x, y + 101);
        }
    }

    private static void DrawProgressBar(Graphics g, RectangleF rect, double? remainingPercent)
    {
        using (var trackPath = RoundedRect(rect, rect.Height / 2f))
        using (var track = new SolidBrush(Color.FromArgb(230, 236, 238, 244)))
        {
            g.FillPath(track, trackPath);
        }

        if (!remainingPercent.HasValue) return;
        var pct = Math.Max(0, Math.Min(100, remainingPercent.Value));
        if (pct <= 0) return;

        var fillRect = new RectangleF(rect.Left, rect.Top, Math.Max(rect.Height, rect.Width * (float)(pct / 100.0)), rect.Height);
        var fillColor = pct <= 20
            ? Color.FromArgb(255, 255, 105, 115)
            : pct <= 35
                ? Color.FromArgb(255, 245, 181, 68)
                : Color.FromArgb(255, 35, 198, 100);
        using (var fillPath = RoundedRect(fillRect, rect.Height / 2f))
        using (var fill = new SolidBrush(fillColor))
        {
            g.FillPath(fill, fillPath);
        }
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var diameter = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static double? RemainingPercent(double? usedPercent)
    {
        return usedPercent.HasValue ? Math.Max(0, 100 - usedPercent.Value) : (double?)null;
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
