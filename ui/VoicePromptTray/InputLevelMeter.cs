using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace VoicePromptTray;

internal sealed class InputLevelMeter : Control
{
    private readonly AudioMeterReader _reader = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly byte[] _waveform = new byte[AudioMeterReader.WaveSamples];
    private int _lastSequence;
    private long _lastSignal;
    private float _level;
    private bool _listening;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal float DisplayLevel => _level;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Listening => _listening;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Active => _timer.Enabled;

    public InputLevelMeter()
    {
        Height = 46;
        BackColor = Theme.Surface;
        ForeColor = Theme.TextSecondary;
        AccessibleName = "Live microphone input level";
        AccessibleRole = AccessibleRole.Indicator;
        TabStop = false;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        _timer = new System.Windows.Forms.Timer { Interval = 33 };
        _timer.Tick += (_, _) => UpdateLevel();
    }

    public void SetActive(bool active)
    {
        if (active)
        {
            if (!_timer.Enabled)
            {
                _lastSignal = 0;
                _timer.Start();
                UpdateLevel();
            }
        }
        else if (_timer.Enabled)
        {
            _timer.Stop();
            _listening = false;
            _level = 0;
            Invalidate();
        }
    }

    private void UpdateLevel()
    {
        long now = Environment.TickCount64;
        if (_reader.TryRead(_waveform, out AudioMeterSample sample))
        {
            if (sample.Sequence != _lastSequence)
            {
                _lastSequence = sample.Sequence;
                _lastSignal = now;
            }
            _listening = sample.Recording && _lastSignal != 0 && now - _lastSignal < 1500;
            float target = _listening ? sample.Level : 0f;
            _level += (target - _level) * (_listening ? 0.45f : 0.25f);
        }
        else
        {
            _listening = false;
            _level *= 0.75f;
        }
        if (_level < 0.002f)
            _level = 0;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 1, Math.Max(1, Width - 1), Math.Max(1, Height - 2));
        using (var path = Theme.RoundedRect(bounds, 9))
        using (var fill = new SolidBrush(Theme.Control))
        using (var border = new Pen(Theme.Border))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        string status = !_listening
            ? "Hold your hotkey to test"
            : _level < 0.14f
                ? "Listening · quiet"
                : _level > 0.92f
                    ? "Listening · very loud"
                    : "Listening · good signal";
        Color statusColor = !_listening
            ? Theme.TextSecondary
            : _level is >= 0.14f and <= 0.92f ? Theme.Ok : Theme.Warn;
        var textBounds = new Rectangle(13, 0, Math.Max(70, Width - 190), Height);
        TextRenderer.DrawText(
            e.Graphics,
            status,
            Theme.Font(8.8f, FontStyle.Bold),
            textBounds,
            statusColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        int meterWidth = Math.Min(154, Math.Max(84, Width / 3));
        var track = new Rectangle(Width - meterWidth - 14, (Height - 8) / 2, meterWidth, 8);
        using (var trackPath = Theme.RoundedRect(track, 4))
        using (var trackFill = new SolidBrush(Theme.SurfaceRaised))
            e.Graphics.FillPath(trackFill, trackPath);

        int signalWidth = (int)Math.Round(track.Width * Math.Clamp(_level, 0f, 1f));
        if (signalWidth > 1)
        {
            var signal = new Rectangle(track.X, track.Y, signalWidth, track.Height);
            using var signalPath = Theme.RoundedRect(signal, 4);
            using var signalFill = new SolidBrush(
                _level > 0.92f ? Theme.Warn : Theme.Ok);
            e.Graphics.FillPath(signalFill, signalPath);
        }
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
