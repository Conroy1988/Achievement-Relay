using System.IO;
using System.Media;
using AchievementRelay.Core.Models;

namespace AchievementRelay.App.Services;

/// <summary>Original, local one-second two-note chime. No media downloads or system-volume changes.</summary>
public sealed class UnlockChime : IDisposable
{
    private SoundPlayer? _player;
    private MemoryStream? _stream;

    public static byte[] CreateWave(int volume, RelayRarityTier tier = RelayRarityTier.Unranked, UnlockSoundPack pack = UnlockSoundPack.Signal)
    {
        const int rate = 22050;
        const int samples = rate;
        var gain = Math.Clamp(volume, 0, 100) / 100.0;
        using var output = new MemoryStream(44 + samples * 2);
        using var writer = new BinaryWriter(output);
        writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(samples * 2);
        for (var i = 0; i < samples; i++)
        {
            var t = i / (double)rate;
            static double Bell(double t, double start, double hz)
            {
                var age = t - start;
                if (age < 0) return 0;
                var envelope = Math.Min(age / .012, 1) * Math.Exp(-age * 6);
                return envelope * (Math.Sin(2 * Math.PI * hz * age) + .22 * Math.Sin(2 * Math.PI * hz * 2.01 * age));
            }
            var impact = .10 * Math.Sin(2 * Math.PI * 110 * t) * Math.Min(t / .008, 1) * Math.Exp(-t * 35);
            var rare = tier is RelayRarityTier.Gold or RelayRarityTier.Platinum;
            var tuning = pack == UnlockSoundPack.Glass ? 1.5 : pack == UnlockSoundPack.Arcade ? .75 : 1;
            var signal = .35 * Bell(t, .02, (rare ? 783.99 : 659.25) * tuning) + .40 * Bell(t, .18, (rare ? 1174.66 : 987.77) * tuning) + impact;
            if (pack == UnlockSoundPack.Arcade) signal += .12 * Bell(t, .32, 1318.51);
            if (tier == RelayRarityTier.Platinum) signal += .20 * Bell(t, .38, 1567.98);
            var tail = Math.Clamp((1 - t) / .08, 0, 1);
            writer.Write((short)(Math.Clamp(signal * gain * tail, -1, 1) * short.MaxValue));
        }
        return output.ToArray();
    }

    public void Play(int volume, RelayRarityTier tier = RelayRarityTier.Unranked, UnlockSoundPack pack = UnlockSoundPack.Signal)
    {
        if (volume <= 0) return;
        try
        {
            Dispose();
            _stream = new MemoryStream(CreateWave(volume, tier, pack), writable: false);
            _player = new SoundPlayer(_stream);
            _player.Load();
            _player.Play();
        }
        catch (Exception) { Dispose(); } // Sound failure must never block the strip or Discord.
    }

    public void Dispose()
    {
        try { _player?.Stop(); } catch (Exception) { }
        _player?.Dispose(); _player = null;
        _stream?.Dispose(); _stream = null;
    }
}
