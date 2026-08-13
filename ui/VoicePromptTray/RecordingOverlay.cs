using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace VoicePromptTray;

internal sealed class RecordingOverlay : Form
{
    internal const string WaveStyle = "wave";
    internal const string BarsStyle = "bars";
    internal const string OrbStyle = "orb";

    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private static readonly Color OverlayBackground = Color.FromArgb(17, 19, 22);
    private static readonly Color OverlayBorder = Color.FromArgb(58, 63, 70);
    private static readonly Color OverlaySignal = Color.FromArgb(218, 221, 226);
    private static readonly string[] StyleIds = [WaveStyle, BarsStyle, OrbStyle];

    private readonly AudioMeterReader _reader = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly byte[] _waveBytes = new byte[AudioMeterReader.WaveSamples];
    private readonly float[] _waveform = new float[AudioMeterReader.WaveSamples];
    private readonly PointF[] _wavePoints = new PointF[AudioMeterReader.WaveSamples];
    private string _style = "";
    private int _lastSequence;
    private long _lastSignal;
    private float _level;
    private bool _recording;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public RecordingOverlay(string? style = null)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = OverlayBackground;
        DoubleBuffered = true;
        Opacity = 0;

        SelectStyle(style);
        _timer = new System.Windows.Forms.Timer { Interval = 25 };
        _timer.Tick += (_, _) => UpdateMeter();
        _timer.Start();
    }

    internal static IReadOnlyList<string> SupportedStyles => StyleIds;

    internal static string NormalizeStyle(string? style) => style?.Trim().ToLowerInvariant() switch
    {
        BarsStyle => BarsStyle,
        OrbStyle => OrbStyle,
        _ => WaveStyle,
    };

    internal void SelectStyle(string? style)
    {
        string normalized = NormalizeStyle(style);
        if (_style == normalized && ClientSize.Width > 0)
            return;

        _style = normalized;
        ClientSize = normalized switch
        {
            BarsStyle => new Size(140, 42),
            OrbStyle => new Size(48, 48),
            _ => new Size(164, 42),
        };
        UpdateRegion();
        if (Visible)
            PositionOnActiveScreen();
        Invalidate();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_NCHITTEST)
            m.Result = (IntPtr)HTTRANSPARENT;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegion();
    }

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0)
            return;
        int radius = _style == OrbStyle ? Height / 2 : 13;
        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), radius);
        Region?.Dispose();
        Region = new Region(path);
    }

    private void UpdateMeter()
    {
        long now = Environment.TickCount64;
        if (!_reader.TryRead(_waveBytes, out AudioMeterSample meterSample))
        {
            // A writer can briefly hold the shared meter's sequence lock. Preserve
            // visible recording feedback through that normal transient and recover
            // automatically if the publisher actually disappears.
            if (_lastSignal == 0 || now - _lastSignal >= 3000)
            {
                _recording = false;
                _level = 0;
                Hide();
            }
            return;
        }

        if (meterSample.Sequence != _lastSequence)
        {
            _lastSequence = meterSample.Sequence;
            _lastSignal = now;
        }

        bool recording = meterSample.Recording && _lastSignal != 0 && now - _lastSignal < 3000;
        float targetLevel = recording ? meterSample.Level : 0f;
        float levelResponse = targetLevel > _level ? 0.46f : 0.20f;
        _level += (targetLevel - _level) * levelResponse;

        if (recording && !_recording)
        {
            Array.Clear(_waveform);
            PositionOnActiveScreen();
            Opacity = 0.98;
            Show();
        }
        _recording = recording;

        float response = recording ? 0.48f : 0.22f;
        float audibleLevel = Math.Clamp((_level - 0.06f) / 0.74f, 0f, 1f);
        float strength = MathF.Sqrt(audibleLevel);
        for (int i = 0; i < _waveform.Length; i++)
        {
            float sample = recording && audibleLevel > 0f
                ? (_waveBytes[i] - 128f) / 127f * strength
                : 0f;
            _waveform[i] += (sample - _waveform[i]) * response;
        }

        if (!Visible)
            return;

        Opacity = recording ? 0.98 : Math.Max(0, Opacity - 0.28);
        if (!recording && Opacity <= 0.01)
        {
            Hide();
            return;
        }
        Invalidate();
    }

    private void PositionOnActiveScreen()
    {
        IntPtr foreground = GetForegroundWindow();
        var area = foreground != IntPtr.Zero
            ? Screen.FromHandle(foreground).WorkingArea
            : Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - 32);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        DrawSurface(g);
        switch (_style)
        {
            case BarsStyle:
                DrawMicrophone(g, 20f, 15f, 0.92f);
                DrawBars(g);
                break;
            case OrbStyle:
                DrawOrb(g);
                break;
            default:
                DrawMicrophone(g, 20f, 15f, 0.92f);
                DrawWaveform(g);
                break;
        }
    }

    private void DrawSurface(Graphics g)
    {
        int radius = _style == OrbStyle ? (Height - 2) / 2 : 13;
        using var background = Theme.RoundedRect(new Rectangle(1, 1, Width - 3, Height - 3), radius);
        using var fill = new SolidBrush(OverlayBackground);
        using var border = new Pen(OverlayBorder, 1f);
        g.FillPath(fill, background);
        g.DrawPath(border, background);
    }

    private static void DrawMicrophone(Graphics g, float centerX, float centerY, float scale)
    {
        using var pen = new Pen(OverlaySignal, 1.65f * scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var body = Theme.RoundedRect(
            Rectangle.Round(new RectangleF(centerX - 4f * scale, centerY - 8f * scale, 8f * scale, 14f * scale)),
            Math.Max(3, (int)MathF.Round(4f * scale)));
        g.DrawPath(pen, body);
        g.DrawArc(pen, centerX - 8f * scale, centerY - 2f * scale, 16f * scale, 13f * scale, 0, 180);
        g.DrawLine(pen, centerX, centerY + 11f * scale, centerX, centerY + 16f * scale);
        g.DrawLine(pen, centerX - 3.5f * scale, centerY + 16f * scale, centerX + 3.5f * scale, centerY + 16f * scale);
    }

    private void DrawWaveform(Graphics g)
    {
        const float startX = 38f;
        float endX = Width - 13f;
        float centerY = Height / 2f;
        const float amplitude = 10.5f;

        for (int i = 0; i < _wavePoints.Length; i++)
        {
            float progress = i / (float)(_wavePoints.Length - 1);
            float edgeFade = MathF.Sqrt(MathF.Max(0f, MathF.Sin(MathF.PI * progress)));
            _wavePoints[i] = new PointF(
                startX + (endX - startX) * progress,
                centerY - _waveform[i] * amplitude * edgeFade);
        }

        using var line = new Pen(OverlaySignal, 1.55f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.DrawLines(line, _wavePoints);
    }

    private void DrawBars(Graphics g)
    {
        const int count = 11;
        const float startX = 42f;
        float step = (Width - startX - 14f) / (count - 1);
        float centerY = Height / 2f;
        using var line = new Pen(OverlaySignal, 3f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        for (int i = 0; i < count; i++)
        {
            int sampleIndex = i * (_waveform.Length - 1) / (count - 1);
            float activity = Math.Clamp(MathF.Abs(_waveform[sampleIndex]) * 0.82f + _level * 0.55f, 0f, 1f);
            float halfHeight = 1.5f + activity * 8.5f;
            float x = startX + i * step;
            g.DrawLine(line, x, centerY - halfHeight, x, centerY + halfHeight);
        }
    }

    private void DrawOrb(Graphics g)
    {
        float pulse = Math.Clamp(_level, 0f, 1f);
        using (var halo = new Pen(Color.FromArgb(95 + (int)(90 * pulse), OverlaySignal), 1.4f + pulse * 1.4f))
        {
            g.DrawEllipse(halo, 5f - pulse, 5f - pulse, Width - 11f + pulse * 2f, Height - 11f + pulse * 2f);
        }
        DrawMicrophone(g, Width / 2f, 18f, 0.86f);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
            _reader.Dispose();
        }
        base.Dispose(disposing);
    }
}
