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

    private readonly MemoryMappedFile? _map;
    private readonly MemoryMappedViewAccessor? _view;

    public AudioMeterReader()
    {
        try
        {
            _map = MemoryMappedFile.CreateOrOpen(MapName, MapSize, MemoryMappedFileAccess.ReadWrite);
            _view = _map.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.ReadWrite);
        }
        catch
        {
            _view?.Dispose();
            _map?.Dispose();
        }
    }

    public bool TryRead(byte[] waveform, out AudioMeterSample sample)
    {
        sample = default;
        if (_view is null || waveform.Length < WaveSamples)
            return false;

        try
        {
            int sequence = _view.ReadInt32(0);
            if ((sequence & 1) != 0)
                return false;
            int state = _view.ReadInt32(4);
            float level = _view.ReadSingle(8);
            int publisherPid = _view.ReadInt32(12);
            _view.ReadArray(16, waveform, 0, WaveSamples);
            if (sequence != _view.ReadInt32(0))
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
            return false;
        }
    }

    public void Dispose()
    {
        _view?.Dispose();
        _map?.Dispose();
    }
}
