using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace VoicePromptTray;

internal sealed class RecordingOverlay : Form
{
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int CS_DROPSHADOW = 0x00020000;
    private static readonly Color OverlayBackground = Color.FromArgb(18, 20, 24);
    private static readonly Color OverlayBorder = Color.FromArgb(47, 53, 62);
    private static readonly Color OverlaySignal = Color.FromArgb(198, 204, 212);

    private readonly AudioMeterReader _reader = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly byte[] _waveBytes = new byte[AudioMeterReader.WaveSamples];
    private readonly float[] _waveform = new float[AudioMeterReader.WaveSamples];
    private int _lastSequence;
    private long _lastSignal;
    private float _level;
    private bool _recording;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public RecordingOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ClientSize = new Size(176, 44);
        BackColor = OverlayBackground;
        DoubleBuffered = true;
        Opacity = 0;

        UpdateRegion();
        _timer = new System.Windows.Forms.Timer { Interval = 25 };
        _timer.Tick += (_, _) => UpdateMeter();
        _timer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            cp.ClassStyle |= CS_DROPSHADOW;
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
        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), 14);
        Region?.Dispose();
        Region = new Region(path);
    }

    private void UpdateMeter()
    {
        if (!_reader.TryRead(_waveBytes, out AudioMeterSample meterSample))
        {
            Hide();
            return;
        }

        long now = Environment.TickCount64;
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
            Opacity = 0.97;
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

        Opacity = recording ? 0.97 : Math.Max(0, Opacity - 0.28);
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

        using (var background = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 14))
        using (var fill = new SolidBrush(OverlayBackground))
        using (var border = new Pen(OverlayBorder))
        {
            g.FillPath(fill, background);
            g.DrawPath(border, background);
        }

        DrawMicrophone(g);
        DrawWaveform(g);
    }

    private static void DrawMicrophone(Graphics g)
    {
        using var pen = new Pen(OverlaySignal, 1.65f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var body = Theme.RoundedRect(new Rectangle(17, 7, 8, 15), 4);
        g.DrawPath(pen, body);
        g.DrawArc(pen, 13, 13, 16, 14, 0, 180);
        g.DrawLine(pen, 21, 27, 21, 33);
        g.DrawLine(pen, 17.5f, 33, 24.5f, 33);
    }

    private void DrawWaveform(Graphics g)
    {
        const float startX = 39f;
        const float endX = 163f;
        const float centerY = 22f;
        const float amplitude = 11.5f;

        var points = new PointF[_waveform.Length];
        for (int i = 0; i < points.Length; i++)
        {
            float progress = i / (float)(points.Length - 1);
            float edgeFade = MathF.Sqrt(MathF.Max(0f, MathF.Sin(MathF.PI * progress)));
            points[i] = new PointF(
                startX + (endX - startX) * progress,
                centerY - _waveform[i] * amplitude * edgeFade);
        }

        using var line = new Pen(OverlaySignal, 1.55f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.DrawLines(line, points);
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
