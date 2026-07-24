using System;
using NAudio.Wave;

namespace GI_Subtitles.Services.Audio
{
    /// <summary>
    /// Reinterprets decoded PCM samples at a different sample rate.
    /// This changes playback speed (and pitch) without copying or buffering audio.
    /// </summary>
    internal sealed class PlaybackRateWaveProvider : IWaveProvider
    {
        private readonly IWaveProvider _source;

        public PlaybackRateWaveProvider(IWaveProvider source, double playbackRate)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            if (playbackRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(playbackRate));
            }

            WaveFormat sourceFormat = source.WaveFormat;
            int sampleRate = Math.Max(1, (int)Math.Round(sourceFormat.SampleRate * playbackRate));
            int averageBytesPerSecond = sampleRate * sourceFormat.BlockAlign;
            WaveFormat = WaveFormat.CreateCustomFormat(
                sourceFormat.Encoding,
                sampleRate,
                sourceFormat.Channels,
                averageBytesPerSecond,
                sourceFormat.BlockAlign,
                sourceFormat.BitsPerSample);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            return _source.Read(buffer, offset, count);
        }
    }
}
