using System.IO.MemoryMappedFiles;

namespace VoicePromptTray;

internal readonly record struct AudioMeterSample(
    int Sequence,
    bool Recording,
    float Level,
    int PublisherPid);

internal sealed class AudioMeterReader : IDisposable
{
    internal const string MapName = "VoicePrompt.AudioMeter.v2";
    internal const int WaveSamples = 48;
    internal const int MapSize = 16 + WaveSamples;

    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _view;
    private long _nextOpenAttempt;

    public AudioMeterReader()
    {
        EnsureOpen();
    }

    public bool TryRead(byte[] waveform, out AudioMeterSample sample)
    {
        sample = default;
        if (waveform.Length < WaveSamples || !EnsureOpen())
            return false;

        try
        {
            MemoryMappedViewAccessor view = _view!;
            int sequence = view.ReadInt32(0);
            if ((sequence & 1) != 0)
                return false;
            int state = view.ReadInt32(4);
            float level = view.ReadSingle(8);
            int publisherPid = view.ReadInt32(12);
            view.ReadArray(16, waveform, 0, WaveSamples);
            if (sequence != view.ReadInt32(0))
                return false;

            sample = new AudioMeterSample(
                sequence,
                state == 1,
                float.IsFinite(level) ? Math.Clamp(level, 0f, 1f) : 0f,
                publisherPid);
            return true;
        }
        catch
        {
            CloseMap();
            _nextOpenAttempt = Environment.TickCount64 + 500;
            return false;
        }
    }

    private bool EnsureOpen()
    {
        if (_view is not null)
            return true;

        long now = Environment.TickCount64;
        if (now < _nextOpenAttempt)
            return false;

        try
        {
            _map = MemoryMappedFile.CreateOrOpen(MapName, MapSize, MemoryMappedFileAccess.ReadWrite);
            _view = _map.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.ReadWrite);
            _nextOpenAttempt = 0;
            return true;
        }
        catch
        {
            CloseMap();
            _nextOpenAttempt = now + 500;
            return false;
        }
    }

    private void CloseMap()
    {
        _view?.Dispose();
        _view = null;
        _map?.Dispose();
        _map = null;
    }

    public void Dispose()
    {
        CloseMap();
    }
}
