using System.Drawing.Drawing2D;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace VoicePromptTray;

internal sealed class RecordingOverlay : Form
{
    private const string MapName = "VoicePrompt.AudioMeter.v2";
    private const int WaveSamples = 48;
    private const int MapSize = 16 + WaveSamples;
    private const int RecordingState = 1;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int CS_DROPSHADOW = 0x00020000;

    private readonly MemoryMappedFile? _map;
    private readonly MemoryMappedViewAccessor? _view;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly byte[] _waveBytes = new byte[WaveSamples];
    private readonly float[] _waveform = new float[WaveSamples];
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
        ClientSize = new Size(220, 48);
        BackColor = Theme.Bar;
        DoubleBuffered = true;
        Opacity = 0;

        try
        {
            _map = MemoryMappedFile.CreateOrOpen(MapName, MapSize, MemoryMappedFileAccess.ReadWrite);
            _view = _map.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.ReadWrite);
            _lastSequence = _view.ReadInt32(0);
        }
        catch
        {
            _view?.Dispose();
            _map?.Dispose();
        }

        UpdateRegion();
        _timer = new System.Windows.Forms.Timer { Interval = 33 };
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
        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), 15);
        Region?.Dispose();
        Region = new Region(path);
    }

    private void UpdateMeter()
    {
        if (_view == null)
            return;

        int sequence;
        int state;
        float level;
        try
        {
            sequence = _view.ReadInt32(0);
            if ((sequence & 1) != 0)
                return;
            state = _view.ReadInt32(4);
            level = _view.ReadSingle(8);
            _view.ReadArray(16, _waveBytes, 0, WaveSamples);
            if (sequence != _view.ReadInt32(0))
                return;
        }
        catch
        {
            Hide();
            return;
        }

        long now = Environment.TickCount64;
        if (sequence != _lastSequence)
        {
            _lastSequence = sequence;
            _lastSignal = now;
        }

        bool recording = state == RecordingState && _lastSignal != 0 && now - _lastSignal < 500;
        _timer.Interval = recording || Visible ? 33 : 100;
        _level = float.IsFinite(level) ? Math.Clamp(level, 0f, 1f) : 0f;

        if (recording && !_recording)
        {
            Array.Clear(_waveform);
            PositionOnActiveScreen();
            Opacity = 0;
            Show();
        }
        _recording = recording;

        float response = recording ? 0.52f : 0.24f;
        float strength = 0.58f + 0.42f * MathF.Sqrt(_level);
        for (int i = 0; i < _waveform.Length; i++)
        {
            float sample = recording ? (_waveBytes[i] - 128f) / 127f * strength : 0f;
            _waveform[i] += (sample - _waveform[i]) * response;
        }

        if (!Visible)
            return;

        Opacity = recording ? Math.Min(0.98, Opacity + 0.24) : Math.Max(0, Opacity - 0.22);
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
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - 34);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var background = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 15))
        using (var fill = new SolidBrush(Theme.Bar))
        using (var border = new Pen(Theme.Border))
        {
            g.FillPath(fill, background);
            g.DrawPath(border, background);
        }

        DrawMicrophone(g);
        DrawWaveform(g);
    }

    private static void DrawMicrophone(Graphics g)
    {
        using var pen = new Pen(Theme.Accent, 1.8f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var body = Theme.RoundedRect(new Rectangle(20, 8, 8, 16), 4);
        g.DrawPath(pen, body);
        g.DrawArc(pen, 16, 14, 16, 15, 0, 180);
        g.DrawLine(pen, 24, 29, 24, 35);
        g.DrawLine(pen, 20.5f, 35, 27.5f, 35);
    }

    private void DrawWaveform(Graphics g)
    {
        const float startX = 45f;
        const float endX = 207f;
        const float centerY = 24f;
        const float amplitude = 15f;

        var points = new PointF[_waveform.Length];
        for (int i = 0; i < points.Length; i++)
        {
            float progress = i / (float)(points.Length - 1);
            float edgeFade = MathF.Sqrt(MathF.Max(0f, MathF.Sin(MathF.PI * progress)));
            points[i] = new PointF(
                startX + (endX - startX) * progress,
                centerY - _waveform[i] * amplitude * edgeFade);
        }

        using var line = new Pen(Theme.Accent, 1.7f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.DrawCurve(line, points, 0.22f);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
            _view?.Dispose();
            _map?.Dispose();
        }
        base.Dispose(disposing);
    }
}
