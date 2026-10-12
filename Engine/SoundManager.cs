using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NetCoreAudio;

namespace AVATrek.Engine;

/// <summary>The ship's voice: short spoken reports, one at a time.</summary>
public enum Voice
{
    Welcome,
    Klingons,
    Saucers,
    Nomad,
    Docking,
    ShieldsLow,
    StarbaseLost,
    RoundComplete,
    SectorSecured,
    PhotonsOut,
    ShipLost,
    GameOver
}

public class SoundManager : IDisposable
{
    private readonly string _soundsPath;
    private readonly Dictionary<string, Loop> _loops = new();

    // Voice reports queue up so they never talk over each other; one that has waited too long is dropped.
    private readonly Queue<(Voice Line, DateTime Queued)> _voiceQueue = new();
    private bool _voiceBusy;
    private static readonly TimeSpan VoiceStale = TimeSpan.FromSeconds(4);

    public bool IsMuted { get; set; } = false;
    public bool IsVoiceOn { get; set; } = true;

    /// <summary>Set during attract mode: the game is silent without touching the player's mute setting.</summary>
    public bool IsSilenced { get; set; }

    private bool IsQuiet => IsMuted || IsSilenced;

    public SoundManager()
    {
        _soundsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sounds");
    }

    public void PlayPhaser() => PlaySound("Phaser.wav");
    public void PlayTorpedo() => PlaySound("Torpedo.wav");
    public void PlayTorpedoBlast() => PlaySound("TorpedoBlast.wav");
    public void PlayDisruptor() => PlaySound("Disruptor.wav");
    public void PlayKlingonBoom() => PlaySound("KlingonBoom.wav");
    public void PlayKlingonArrive() => PlaySound("WarpIn.wav");
    public void PlayShieldHit() => PlaySound("ShieldHit.wav");
    public void PlaySaucerAttach() => PlaySound("SaucerAttach.wav");
    public void PlaySaucerBoom() => PlaySound("SaucerBoom.wav");
    public void PlayMineLaid() => PlaySound("MineLaid.wav");
    public void PlayMineBoom() => PlaySound("MineBoom.wav");
    public void PlayNomadHit() => PlaySound("NomadHit.wav");
    public void PlayBigBoom() => PlaySound("BigBoom.wav");
    public void PlayShipExplosion() => PlaySound("ShipExplosion.wav");
    public void PlayWarpEngage() => PlaySound("WarpEngage.wav");
    public void PlayDocking() => PlaySound("Docking.wav");
    public void PlayExtraShip() => PlaySound("ExtraShip.wav");
    public void PlayRedAlert() => PlaySound("RedAlert.wav");
    public void PlayRoundClear() => PlaySound("RoundClear.wav");
    public void PlayEmpty() => PlaySound("Empty.wav");

    public void StartImpulse() => StartLoop("Impulse.wav");
    public void StopImpulse() => StopLoop("Impulse.wav");
    public void StartWarp() => StartLoop("Warp.wav");
    public void StopWarp() => StopLoop("Warp.wav");

    /// <summary>The bridge's background hum and computer chatter, while a game is on.</summary>
    public void StartAmbience() => StartLoop("Bridge.wav");
    public void StopAmbience() => StopLoop("Bridge.wav");

    public void StopAllLoops()
    {
        foreach (var name in _loops.Keys)
            StopLoop(name);
    }

    public void Say(Voice line)
    {
        if (IsQuiet || !IsVoiceOn) return;

        _voiceQueue.Enqueue((line, DateTime.Now));
        if (!_voiceBusy)
            SpeakQueue();
    }

    public void ClearVoice() => _voiceQueue.Clear();

    private async void SpeakQueue()
    {
        _voiceBusy = true;
        try
        {
            while (_voiceQueue.Count > 0)
            {
                var (line, queued) = _voiceQueue.Dequeue();
                if (DateTime.Now - queued > VoiceStale || IsQuiet || !IsVoiceOn) continue;

                var path = Path.Combine(_soundsPath, $"Voice{line}.wav");
                if (!File.Exists(path)) continue;

                var player = new Player();
                await player.Play(path);
                while (player.Playing)
                    await Task.Delay(30);
                await Task.Delay(150);
            }
        }
        catch
        {
            // Silently ignore playback errors
        }
        finally
        {
            _voiceBusy = false;
        }
    }

    // A sound that repeats while something is held (the engines) or going on (the bridge hum). Each start
    // gets its own generation, so a loop left over from a quick stop and start ends instead of doubling up.
    private sealed class Loop
    {
        public bool Playing;
        public int Generation;
        public Player? Player;
    }

    private void StartLoop(string filename)
    {
        if (IsQuiet) return;
        if (!_loops.TryGetValue(filename, out var loop))
            _loops[filename] = loop = new Loop();
        if (loop.Playing) return;

        loop.Playing = true;
        RunLoop(filename, loop, ++loop.Generation);
    }

    private async void RunLoop(string filename, Loop loop, int generation)
    {
        var path = Path.Combine(_soundsPath, filename);
        if (!File.Exists(path))
        {
            loop.Playing = false;
            return;
        }

        var player = new Player();
        loop.Player = player;
        try
        {
            while (loop.Playing && generation == loop.Generation)
            {
                await player.Play(path);
                while (player.Playing && loop.Playing && generation == loop.Generation)
                    await Task.Delay(20);
            }
        }
        catch
        {
            // Silently ignore playback errors
        }
        finally
        {
            if (generation == loop.Generation)
                loop.Playing = false;
            else
                StopQuietly(player);
        }
    }

    private void StopLoop(string filename)
    {
        if (!_loops.TryGetValue(filename, out var loop)) return;
        loop.Playing = false;
        if (loop.Player != null)
            StopQuietly(loop.Player);
    }

    private static void StopQuietly(Player player)
    {
        try
        {
            player.Stop();
        }
        catch
        {
            // Silently ignore stop errors
        }
    }

    private async void PlaySound(string filename)
    {
        if (IsQuiet) return;

        var path = Path.Combine(_soundsPath, filename);
        if (!File.Exists(path)) return;

        try
        {
            // Create new player for each sound to allow overlapping
            var player = new Player();
            await player.Play(path);
        }
        catch
        {
            // Silently ignore playback errors
        }
    }

    public void Dispose()
    {
        _voiceQueue.Clear();
        StopAllLoops();
    }
}
