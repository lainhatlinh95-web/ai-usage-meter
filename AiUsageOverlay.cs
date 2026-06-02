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
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        NativeUi.EnableDpiAwareness();
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

internal static class NativeUi
{
    private const int DpiAwarenessContextPerMonitorAwareV2 = -4;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("shcore.dll")]
    private static extern int SetProcessDpiAwareness(int value);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    public static void EnableDpiAwareness()
    {
        try
        {
            if (SetProcessDpiAwarenessContext(new IntPtr(DpiAwarenessContextPerMonitorAwareV2))) return;
        }
        catch { }

        try
        {
            SetProcessDpiAwareness(2);
        }
        catch { }
    }

    public static bool TryEnableRoundedCorners(IntPtr hwnd)
    {
        try
        {
            var preference = DwmwcpRound;
            return DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int)) == 0;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class OverlayTheme
{
    public string Name;
    public Color CardTop;
    public Color CardBottom;
    public Color Border;
    public Color Divider;
    public Color Text;
    public Color Muted;
    public Color Accent;
    public Color Track;
    public Color Ok;
    public Color Warning;
    public Color Critical;
    public Color ButtonFill;
    public string MonoFont;
    public bool UppercaseTitle;
    public bool Scanlines;
    public bool TopAccentLine;

    public static OverlayTheme DarkHud()
    {
        return new OverlayTheme
        {
            Name = "Dark HUD",
            CardTop = Color.FromArgb(255, 18, 18, 26),
            CardBottom = Color.FromArgb(255, 9, 9, 15),
            Border = Color.FromArgb(88, 255, 255, 255),
            Divider = Color.FromArgb(28, 255, 255, 255),
            Text = Color.FromArgb(245, 237, 236, 245),
            Muted = Color.FromArgb(195, 154, 153, 176),
            Accent = Color.FromArgb(255, 192, 132, 252),
            Track = Color.FromArgb(58, 255, 255, 255),
            Ok = Color.FromArgb(255, 52, 211, 153),
            Warning = Color.FromArgb(255, 251, 191, 36),
            Critical = Color.FromArgb(255, 251, 111, 111),
            ButtonFill = Color.FromArgb(34, 255, 255, 255),
            MonoFont = "Consolas",
            UppercaseTitle = true,
            Scanlines = true,
            TopAccentLine = true
        };
    }

    public static OverlayTheme Glass()
    {
        return new OverlayTheme
        {
            Name = "Glassmorphism",
            CardTop = Color.FromArgb(255, 72, 48, 104),
            CardBottom = Color.FromArgb(255, 38, 24, 70),
            Border = Color.FromArgb(128, 255, 255, 255),
            Divider = Color.FromArgb(42, 255, 255, 255),
            Text = Color.FromArgb(255, 255, 255, 255),
            Muted = Color.FromArgb(210, 255, 255, 255),
            Accent = Color.FromArgb(255, 233, 213, 255),
            Track = Color.FromArgb(72, 255, 255, 255),
            Ok = Color.FromArgb(255, 94, 234, 212),
            Warning = Color.FromArgb(255, 252, 211, 77),
            Critical = Color.FromArgb(255, 251, 113, 133),
            ButtonFill = Color.FromArgb(55, 255, 255, 255),
            MonoFont = "Consolas",
            UppercaseTitle = true,
            Scanlines = false,
            TopAccentLine = false
        };
    }

    public static OverlayTheme MacLight()
    {
        return new OverlayTheme
        {
            Name = "macOS Light",
            CardTop = Color.FromArgb(255, 250, 250, 252),
            CardBottom = Color.FromArgb(255, 242, 243, 246),
            Border = Color.FromArgb(38, 0, 0, 0),
            Divider = Color.FromArgb(24, 0, 0, 0),
            Text = Color.FromArgb(255, 29, 29, 31),
            Muted = Color.FromArgb(255, 108, 108, 114),
            Accent = Color.FromArgb(255, 0, 122, 255),
            Track = Color.FromArgb(34, 0, 0, 0),
            Ok = Color.FromArgb(255, 52, 199, 89),
            Warning = Color.FromArgb(255, 255, 159, 10),
            Critical = Color.FromArgb(255, 255, 59, 48),
            ButtonFill = Color.FromArgb(210, 255, 255, 255),
            MonoFont = "Consolas",
            UppercaseTitle = false,
            Scanlines = false,
            TopAccentLine = false
        };
    }
}

internal sealed class OverlayForm : Form
{
    private readonly Options _options;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly OverlayTheme[] _themes = new[] { OverlayTheme.DarkHud(), OverlayTheme.Glass(), OverlayTheme.MacLight() };
    private OverlayStats _stats;
    private string _error;
    private bool _dragging;
    private Point _dragStart;
    private RectangleF _themeButton;
    private RectangleF _minimizeButton;
    private RectangleF _weeklyButton;
    private RectangleF _pillButton;
    private int _themeIndex;
    private bool _showWeekly;
    private bool _minimized;
    private bool _usingDwmCorners;
    private ToolStripMenuItem[] _themeMenuItems;

    public OverlayForm(Options options)
    {
        _options = options;
        Text = "AI Usage Overlay";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(18, 18, 26);
        Width = 344;
        Height = OverlayHeight();
        Left = Screen.PrimaryScreen.WorkingArea.Right - Width - 18;
        Top = Screen.PrimaryScreen.WorkingArea.Top + 18;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        ContextMenuStrip = BuildMenu();
        ApplyShape();

        MouseDown += StartDrag;
        MouseMove += MoveDrag;
        MouseUp += StopDrag;

        _timer = new System.Windows.Forms.Timer { Interval = Math.Max(2, _options.RefreshSeconds) * 1000 };
        _timer.Tick += delegate { UpdateStats(); };
        _timer.Start();
        UpdateStats();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _usingDwmCorners = NativeUi.TryEnableRoundedCorners(Handle);
        ApplyShape();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyShape();
    }

    private void StartDrag(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (_minimized)
        {
            if (_pillButton.Contains(e.Location))
            {
                _minimized = false;
                ApplyOverlaySize();
                Invalidate();
                return;
            }
        }
        if (_themeButton.Contains(e.Location))
        {
            ContextMenuStrip.Show(this, e.Location);
            return;
        }
        if (_minimizeButton.Contains(e.Location))
        {
            _minimized = true;
            ApplyOverlaySize();
            Invalidate();
            return;
        }
        if (_weeklyButton.Contains(e.Location))
        {
            _showWeekly = !_showWeekly;
            ApplyOverlaySize();
            Invalidate();
            return;
        }

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
            ApplyOverlaySize();
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
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (!string.IsNullOrEmpty(_error))
        {
            using (var font = new Font("Segoe UI", 11f, FontStyle.Regular))
                DrawText(g, "AI Usage: " + _error, font, Color.FromArgb(235, 255, 255, 255), 18, 18);
            return;
        }

        if (_stats == null)
        {
            using (var font = new Font("Segoe UI", 11f, FontStyle.Regular))
                DrawText(g, "AI Usage: loading", font, Color.FromArgb(235, 255, 255, 255), 18, 18);
            return;
        }

        if (_minimized) DrawPill(g);
        else DrawOverlayCard(g);
    }

    private void DrawOverlayCard(Graphics g)
    {
        var theme = _themes[_themeIndex];
        if (Width != OverlayWidth() || Height != OverlayHeight()) ApplyOverlaySize();
        var rect = new RectangleF(0.5f, 0.5f, ClientSize.Width - 1, ClientSize.Height - 1);
        using (var path = RoundedRect(rect, 18f))
        using (var fill = new LinearGradientBrush(rect, theme.CardTop, theme.CardBottom, LinearGradientMode.Vertical))
        using (var border = new Pen(theme.Border, 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        if (theme.Scanlines)
        {
            using (var scan = new Pen(Color.FromArgb(8, 255, 255, 255), 1f))
            {
                for (var yScan = 3f; yScan < ClientSize.Height; yScan += 4f)
                    g.DrawLine(scan, 1, yScan, ClientSize.Width - 2, yScan);
            }
        }

        if (theme.TopAccentLine)
        {
            using (var accent = new Pen(theme.Accent, 2f))
                g.DrawLine(accent, 18, 2, ClientSize.Width - 18, 2);
        }
        if (theme.Scanlines) DrawCornerTicks(g, theme, rect);

        DrawHeader(g, theme);

        var y = 46f;
        y = DrawToolBlock(
            g,
            theme,
            y,
            "Codex",
            "OpenAI",
            "C",
            Color.FromArgb(45, 212, 191),
            _stats.Codex.ShortUsedPercent,
            _stats.Codex.WeekUsedPercent,
            RemainingPercent(_stats.Codex.ShortUsedPercent),
            RemainingPercent(_stats.Codex.WeekUsedPercent),
            true,
            "5-HR LEFT",
            FormatResetLabel(_stats.Codex.ShortReset),
            "WEEKLY LEFT",
            FormatResetLabel(_stats.Codex.WeekReset),
            string.Format(CultureInfo.InvariantCulture, "{0} tokens", FormatCompact(_stats.Codex.HourTokens)),
            string.Format(CultureInfo.InvariantCulture, "{0} tokens", FormatCompact(_stats.Codex.WeekTokens)));

        if (HasClaudeData())
        {
            using (var line = new Pen(theme.Divider, 1f))
                g.DrawLine(line, 14, y, ClientSize.Width - 14, y);

            y = DrawToolBlock(
                g,
                theme,
                y,
                "Claude",
                "Anthropic",
                "C",
                Color.FromArgb(224, 138, 95),
                null,
                null,
                null,
                null,
                false,
                "1-HR TOKENS",
                FormatCompact(_stats.Claude.HourTokens),
                "WEEKLY TOKENS",
                FormatCompact(_stats.Claude.WeekTokens),
                "local log total",
                "local log total");
        }

        DrawFooter(g, theme, y);
    }

    private void DrawHeader(Graphics g, OverlayTheme theme)
    {
        using (var grip = new Pen(theme.Muted, 1.6f))
        {
            for (var i = 0; i < 3; i++)
                g.DrawLine(grip, 14, 17 + i * 4, 26, 17 + i * 4);
        }

        using (var headerFont = new Font("Segoe UI", 10f, FontStyle.Bold))
        using (var liveFont = new Font("Segoe UI", 7f, FontStyle.Bold))
        {
            DrawText(g, "AI ", headerFont, theme.Text, 32, 13);
            DrawText(g, theme.UppercaseTitle ? "USAGE" : "Usage", headerFont, theme.Accent, 53, 13);
            using (var dot = new SolidBrush(theme.Ok))
                g.FillEllipse(dot, 125, 22, 6, 6);
            DrawText(g, "LIVE", liveFont, theme.Muted, 136, 17);
        }

        DrawThemeButton(g, theme, ClientSize.Width - 64, 11);
        DrawMinimizeButton(g, theme, ClientSize.Width - 36, 11);
    }

    private float DrawToolBlock(
        Graphics g,
        OverlayTheme theme,
        float y,
        string name,
        string sub,
        string letter,
        Color tint,
        double? primaryStatusUsed,
        double? weeklyStatusUsed,
        double? primaryDisplayPercent,
        double? weeklyDisplayPercent,
        bool showWeekly,
        string primaryLabel,
        string primaryRight,
        string weeklyLabel,
        string weeklyRight,
        string primaryDetail,
        string weeklyDetail)
    {
        y += 11;
        DrawBadge(g, theme, 14, y, letter, tint);

        using (var nameFont = new Font("Segoe UI", 10.5f, FontStyle.Bold))
        using (var subFont = new Font("Segoe UI", 7.25f, FontStyle.Regular))
        {
            DrawText(g, name, nameFont, theme.Text, 54, y - 1);
            DrawText(g, sub.ToUpperInvariant(), subFont, theme.Muted, 55, y + 18);
        }

        DrawStatus(g, theme, ClientSize.Width - 14, y + 15, primaryStatusUsed);

        y += 46;
        DrawMeter(g, theme, 14, y, ClientSize.Width - 28, primaryLabel, primaryDisplayPercent, primaryStatusUsed, primaryRight, primaryDetail, tint);
        y += 46;

        if (showWeekly)
        {
            using (var labelFont = new Font("Segoe UI", 7f, FontStyle.Bold))
            using (var line = new Pen(theme.Divider, 1f))
            {
                DrawText(g, "WEEKLY", labelFont, theme.Muted, 14, y + 2);
                g.DrawLine(line, 63, y + 9, ClientSize.Width - 14, y + 9);
            }
            y += 22;
            DrawMeter(g, theme, 14, y, ClientSize.Width - 28, weeklyLabel, weeklyDisplayPercent, weeklyStatusUsed, weeklyRight, weeklyDetail, tint);
            y += 46;
        }

        return y + 10;
    }

    private static void DrawMeter(Graphics g, OverlayTheme theme, float x, float y, float w, string label, double? displayPercent, double? statusUsed, string right, string detail, Color tint)
    {
        using (var pctFont = new Font(theme.MonoFont, 12.75f, FontStyle.Bold))
        using (var pctSmall = new Font(theme.MonoFont, 7.5f, FontStyle.Bold))
        using (var labelFont = new Font("Segoe UI", 7.25f, FontStyle.Regular))
        using (var rightFont = new Font(theme.MonoFont, 9f, FontStyle.Regular))
        {
            var valueText = displayPercent.HasValue ? ((int)Math.Round(displayPercent.Value)).ToString(CultureInfo.InvariantCulture) : right;
            DrawText(g, valueText, pctFont, theme.Text, x, y);
            var valueWidth = MeasureText(valueText, pctFont).Width;
            if (displayPercent.HasValue) DrawText(g, "%", pctSmall, theme.Muted, x + valueWidth - 1, y + 7);
            DrawText(g, label, labelFont, theme.Muted, x + valueWidth + (displayPercent.HasValue ? 17 : 10), y + 6);
            var rs = MeasureText(right, rightFont);
            if (displayPercent.HasValue) DrawText(g, right, rightFont, theme.Accent, x + w - rs.Width, y + 5);
            else
            {
                var detailSize = MeasureText(detail, labelFont);
                DrawText(g, detail, labelFont, theme.Muted, x + w - detailSize.Width, y + 7);
            }
        }

        DrawProgressBar(g, theme, new RectangleF(x, y + 27, w, 8), displayPercent, statusUsed, tint);
    }

    private void DrawThemeButton(Graphics g, OverlayTheme theme, float x, float y)
    {
        _themeButton = new RectangleF(x, y, 24, 22);
        using (var path = RoundedRect(_themeButton, 7f))
        using (var fill = new SolidBrush(theme.ButtonFill))
        using (var border = new Pen(theme.Border, 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        using (var pen = new Pen(theme.Muted, 1.4f))
        using (var brush = new SolidBrush(theme.Muted))
        {
            g.DrawArc(pen, x + 5, y + 5, 13, 11, 135, 292);
            g.FillEllipse(brush, x + 8, y + 9, 2, 2);
            g.FillEllipse(brush, x + 11, y + 7, 2, 2);
            g.FillEllipse(brush, x + 14, y + 10, 2, 2);
        }
    }

    private void DrawMinimizeButton(Graphics g, OverlayTheme theme, float x, float y)
    {
        _minimizeButton = new RectangleF(x, y, 24, 22);
        using (var path = RoundedRect(_minimizeButton, 7f))
        using (var fill = new SolidBrush(theme.ButtonFill))
        using (var border = new Pen(theme.Border, 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        using (var pen = new Pen(theme.Muted, 1.7f))
            g.DrawLine(pen, x + 6, y + 12, x + 18, y + 12);
    }

    private static void DrawBadge(Graphics g, OverlayTheme theme, float x, float y, string letter, Color tint)
    {
        var rect = new RectangleF(x, y, 30, 30);
        using (var path = RoundedRect(rect, 9f))
        using (var fill = new SolidBrush(Color.FromArgb(theme.Name == "macOS Light" ? 255 : 36, 255, 255, 255)))
        using (var border = new Pen(Color.FromArgb(130, tint), 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        using (var font = new Font("Segoe UI", 10.5f, FontStyle.Bold))
            DrawText(g, letter, font, tint, rect, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static void DrawStatus(Graphics g, OverlayTheme theme, float right, float centerY, double? used)
    {
        var pct = used.HasValue ? used.Value : 0;
        var label = !used.HasValue ? "LOCAL" : pct >= 90 ? "CRITICAL" : pct >= 70 ? "LOW" : "HEALTHY";
        var color = !used.HasValue ? theme.Muted : pct >= 90 ? theme.Critical : pct >= 70 ? theme.Warning : theme.Ok;
        using (var font = new Font("Segoe UI", 7.25f, FontStyle.Bold))
        {
            var size = MeasureText(label, font);
            var rect = new RectangleF(right - size.Width - 14, centerY - 9, size.Width + 14, 18);
            using (var path = RoundedRect(rect, 6f))
            using (var fill = new SolidBrush(Color.FromArgb(42, color)))
                g.FillPath(fill, path);
            DrawText(g, label, font, color, rect.Left + 7, rect.Top + 2);
        }
    }

    private static void DrawCornerTicks(Graphics g, OverlayTheme theme, RectangleF rect)
    {
        using (var pen = new Pen(theme.Accent, 1.5f))
        {
            var len = 9f;
            var off = 8f;
            g.DrawLine(pen, rect.Left + off, rect.Top + off, rect.Left + off + len, rect.Top + off);
            g.DrawLine(pen, rect.Left + off, rect.Top + off, rect.Left + off, rect.Top + off + len);
            g.DrawLine(pen, rect.Right - off, rect.Top + off, rect.Right - off - len, rect.Top + off);
            g.DrawLine(pen, rect.Right - off, rect.Top + off, rect.Right - off, rect.Top + off + len);
            g.DrawLine(pen, rect.Left + off, rect.Bottom - off, rect.Left + off + len, rect.Bottom - off);
            g.DrawLine(pen, rect.Left + off, rect.Bottom - off, rect.Left + off, rect.Bottom - off - len);
            g.DrawLine(pen, rect.Right - off, rect.Bottom - off, rect.Right - off - len, rect.Bottom - off);
            g.DrawLine(pen, rect.Right - off, rect.Bottom - off, rect.Right - off, rect.Bottom - off - len);
        }
    }

    private static void DrawChevron(Graphics g, Color color, float cx, float cy, bool up)
    {
        using (var brush = new SolidBrush(color))
        {
            var points = up
                ? new[] { new PointF(cx - 4, cy + 2), new PointF(cx + 4, cy + 2), new PointF(cx, cy - 2) }
                : new[] { new PointF(cx - 4, cy - 2), new PointF(cx + 4, cy - 2), new PointF(cx, cy + 2) };
            g.FillPolygon(brush, points);
        }
    }

    private static void DrawProgressBar(Graphics g, OverlayTheme theme, RectangleF rect, double? displayPercent, double? statusUsed, Color tint)
    {
        using (var trackPath = RoundedRect(rect, rect.Height / 2f))
        using (var track = new SolidBrush(theme.Track))
        {
            g.FillPath(track, trackPath);
        }

        if (!displayPercent.HasValue) return;
        var pct = Math.Max(0, Math.Min(100, displayPercent.Value));
        if (pct <= 0) return;

        var fillRect = new RectangleF(rect.Left, rect.Top, Math.Max(rect.Height, rect.Width * (float)(pct / 100.0)), rect.Height);
        var used = statusUsed.HasValue ? statusUsed.Value : 0;
        var fillColor = used >= 90
            ? theme.Critical
            : used >= 70
                ? theme.Warning
                : tint;
        using (var fillPath = RoundedRect(fillRect, rect.Height / 2f))
        using (var fill = new SolidBrush(fillColor))
        {
            g.FillPath(fill, fillPath);
        }
    }

    private void DrawPill(Graphics g)
    {
        var theme = _themes[_themeIndex];
        var rect = new RectangleF(0.5f, 0.5f, ClientSize.Width - 1, ClientSize.Height - 1);
        _pillButton = rect;
        using (var path = RoundedRect(rect, 18f))
        using (var fill = new LinearGradientBrush(rect, theme.CardTop, theme.CardBottom, LinearGradientMode.Vertical))
        using (var border = new Pen(theme.Border, 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        if (theme.TopAccentLine)
        {
            using (var accent = new Pen(theme.Accent, 2f))
                g.DrawLine(accent, 18, 2, ClientSize.Width - 18, 2);
        }

        DrawMiniRing(g, theme, 13, 13, _stats.Codex.ShortUsedPercent, Color.FromArgb(45, 212, 191));
        DrawMiniRing(g, theme, 58, 13, null, Color.FromArgb(224, 138, 95));

        using (var labelFont = new Font("Segoe UI", 7f, FontStyle.Bold))
        using (var valueFont = new Font(theme.MonoFont, 10.5f, FontStyle.Regular))
        {
            DrawText(g, "CODEX 5H RESET", labelFont, theme.Muted, 112, 14);
            DrawText(g, FormatRemaining(_stats.Codex.ShortReset), valueFont, theme.Accent, 112, 28);
        }
    }

    private static void DrawMiniRing(Graphics g, OverlayTheme theme, float x, float y, double? used, Color tint)
    {
        var rect = new RectangleF(x + 2, y + 2, 34, 34);
        using (var track = new Pen(theme.Track, 4f))
            g.DrawArc(track, rect, 0, 360);
        if (used.HasValue)
        {
            using (var fill = new Pen(used.Value >= 90 ? theme.Critical : used.Value >= 70 ? theme.Warning : tint, 4f))
                g.DrawArc(fill, rect, -90, (float)(360.0 * Math.Max(0, Math.Min(100, used.Value)) / 100.0));
        }
        using (var font = new Font(theme.MonoFont, 7.5f, FontStyle.Bold))
            DrawText(g, used.HasValue ? ((int)Math.Round(used.Value)).ToString(CultureInfo.InvariantCulture) : "C", font, used.HasValue ? tint : theme.Muted, new RectangleF(x, y, 38, 38), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private int OverlayHeight()
    {
        if (_minimized) return 64;
        return HasClaudeData() ? 326 : 226;
    }

    private int OverlayWidth()
    {
        return _minimized ? 284 : 344;
    }

    private void ApplyOverlaySize()
    {
        var right = Right;
        Width = OverlayWidth();
        Height = OverlayHeight();
        Left = right - Width;
        ApplyShape();
    }

    private void ApplyShape()
    {
        if (_usingDwmCorners)
        {
            if (Region != null)
            {
                Region.Dispose();
                Region = null;
            }
            return;
        }

        var radius = _minimized ? 18f : 18f;
        using (var path = RoundedRect(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), radius))
            Region = new Region(path);
    }

    private void DrawFooter(Graphics g, OverlayTheme theme, float y)
    {
        _weeklyButton = RectangleF.Empty;
        using (var line = new Pen(theme.Divider, 1f))
            g.DrawLine(line, 14, y, ClientSize.Width - 14, y);

        using (var smallFont = new Font("Segoe UI", 7.5f, FontStyle.Regular))
        {
            DrawText(g,
                string.Format("Synced {0} - {1}", _stats.SampledAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture), _stats.Codex.Source),
                smallFont,
                theme.Muted,
                14,
                y + 16);
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        _themeMenuItems = new ToolStripMenuItem[_themes.Length];
        for (var i = 0; i < _themes.Length; i++)
        {
            var index = i;
            _themeMenuItems[i] = new ToolStripMenuItem(_themes[i].Name, null, delegate { SetTheme(index); });
            menu.Items.Add(_themeMenuItems[i]);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Refresh", null, delegate { UpdateStats(); });
        menu.Items.Add("Close", null, delegate { Close(); });
        UpdateThemeChecks();
        return menu;
    }

    private void SetTheme(int index)
    {
        _themeIndex = index;
        UpdateThemeChecks();
        Invalidate();
    }

    private void UpdateThemeChecks()
    {
        if (_themeMenuItems == null) return;
        for (var i = 0; i < _themeMenuItems.Length; i++)
            _themeMenuItems[i].Checked = i == _themeIndex;
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

    private static double? TokenPercent(long tokens, long softLimit)
    {
        if (tokens <= 0 || softLimit <= 0) return 0;
        return Math.Max(0, Math.Min(100, tokens * 100.0 / softLimit));
    }

    private static string FormatRemaining(DateTime? value)
    {
        if (!value.HasValue) return "--";
        var remaining = value.Value - DateTime.Now;
        if (remaining <= TimeSpan.Zero) return "now";
        if (remaining.TotalDays >= 1) return string.Format(CultureInfo.InvariantCulture, "{0}d {1:00}h {2:00}m", (int)remaining.TotalDays, remaining.Hours, remaining.Minutes);
        return string.Format(CultureInfo.InvariantCulture, "{0}h {1:00}m {2:00}s", (int)remaining.TotalHours, remaining.Minutes, remaining.Seconds);
    }

    private static string FormatResetLabel(DateTime? value)
    {
        return value.HasValue
            ? "reset " + value.Value.ToString("yyyy/MM/dd H:mm", CultureInfo.InvariantCulture)
            : "reset --";
    }

    private bool HasClaudeData()
    {
        return _stats != null &&
            (_stats.Claude.HourTokens > 0 ||
             _stats.Claude.WeekTokens > 0 ||
             _stats.Claude.ShortUsedPercent.HasValue ||
             _stats.Claude.WeekUsedPercent.HasValue ||
             _stats.Claude.ShortReset.HasValue ||
             _stats.Claude.WeekReset.HasValue);
    }

    private static void DrawText(Graphics g, string text, Font font, Color color, float x, float y)
    {
        TextRenderer.DrawText(g, text, font, new Point(Round(x), Round(y)), color, TextFlags());
    }

    private static void DrawText(Graphics g, string text, Font font, Color color, RectangleF rect, TextFormatFlags extraFlags)
    {
        TextRenderer.DrawText(g, text, font, Rectangle.Round(rect), color, TextFlags() | extraFlags);
    }

    private static Size MeasureText(string text, Font font)
    {
        return TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), TextFlags());
    }

    private static TextFormatFlags TextFlags()
    {
        return TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping;
    }

    private static int Round(float value)
    {
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static string FormatPercent(double? value)
    {
        return value.HasValue
            ? value.Value.ToString("N0", CultureInfo.InvariantCulture) + "%"
            : "--";
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
