using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using AVATrek.Models;

namespace AVATrek.Engine;

public enum GameState
{
    StartScreen,
    Playing,
    Paused,
    GameOver,
    Attract,
    EnterInitials,
    HighScores
}

/// <summary>Where a round has got to: the briefing, the fight, or the tally afterwards.</summary>
public enum RoundPhase
{
    Intro,
    Fighting,
    Cleared
}

public class GameEngine : IDisposable
{
    private static readonly Random Random = new();

    public const int StartingLives = 3;
    public const int MaxLives = 6;
    public const int ExtraShipEvery = 25000;
    public const double LowShields = 25;

    private const double IntroTime = 2.8;
    private const double ClearedTime = 3.6;
    private const double RespawnDelay = 2.5;
    private const double KlingonSpawnInterval = 2.2;
    private const double SaucerSpawnInterval = 2.6;
    private const double SpawnDistance = 330;
    private const int MaxMines = 14;
    private const double ZeroShieldGrace = 1.0;     // seconds a drained, shieldless ship survives

    private const double GameOverInputDelay = 1.0;
    private const double AttractIdleDelay = 20.0;   // seconds on the title screen before the demo
    private const double AttractLength = 75.0;      // longest a demo runs
    private const double GameOverIdleDelay = 20.0;  // seconds on game over before the title returns
    private const double GameOverBeforeEntry = 2.5; // "GAME OVER" shows this long before initials entry
    private const double EntryInputGrace = 0.8;     // ignore input this long, so a mashed fire button doesn't enter letters
    private const double EntryTimeout = 45.0;       // idle this long in entry and the initials are taken as they are
    private const double HighScoresShowTime = 10.0; // the table stays up this long

    public Enterprise Ship { get; }
    public bool ShipAlive { get; private set; }
    public List<Klingon> Klingons { get; } = new();
    public List<Saucer> Saucers { get; } = new();
    public List<Mine> Mines { get; } = new();
    public Nomad? Nomad { get; private set; }
    public List<Starbase> Starbases { get; } = new();
    public List<Torpedo> Torpedoes { get; } = new();
    public List<Disruptor> Disruptors { get; } = new();
    public List<PhaserBeam> Phasers { get; } = new();
    public List<Explosion> Explosions { get; } = new();

    public int Score { get; private set; }
    public HighScoreTable Table { get; } = HighScoreTable.Load();
    public int HighScore => Table.TopScore;
    public int Lives { get; private set; }
    public int Sector { get; private set; } = 1;
    public int Round { get; private set; } = 1;
    public RoundPlan Plan { get; private set; } = Rounds.Plan(1, 1);
    public RoundPhase Phase { get; private set; }
    public GameState State { get; private set; }

    /// <summary>Enemies still to arrive this round, plus those in the sector now.</summary>
    public int EnemiesLeft => _klingonsToCome + _saucersToCome + (_nomadToCome ? 1 : 0) +
                              Klingons.Count + Saucers.Count + (Nomad != null ? 1 : 0);

    /// <summary>The initials being entered, during GameState.EnterInitials.</summary>
    public InitialsEntry? Entry { get; private set; }

    /// <summary>The place a just-entered score took in the table (0 = top), or -1.</summary>
    public int HighlightIndex { get; private set; } = -1;

    /// <summary>The game just ended with a score that makes the table: initials entry follows.</summary>
    public bool IsEntryPending => _entryPending;

    // What the screens show besides the objects themselves.
    public string? Message { get; private set; }
    public string? SubMessage { get; private set; }
    public double MessageTime { get; private set; }
    public double ShakeTime { get; private set; }
    public double HitFlash { get; private set; }
    public double DockingProgress => _dockTimer > 0 ? 1 - _dockTimer / Starbase.DockTime : 0;
    public bool IsDocking => _dockTimer > 0;
    public bool IsImpulse => ShipAlive && IsInPlay && _controls.Impulse && !IsDocking;
    public bool IsInPlay => State is GameState.Playing or GameState.Attract;
    public bool IsShieldWarning => ShipAlive && Ship.Shields < LowShields;

    /// <summary>A clock that runs on every screen, for things that turn and blink.</summary>
    public double Clock { get; private set; }

    public InputHandler Input { get; } = new();
    public SoundManager Sound { get; } = new();

    private int _klingonsToCome, _saucersToCome;
    private bool _nomadToCome;
    private double _klingonSpawnTimer, _saucerSpawnTimer, _phaseTimer;
    private double _respawnTimer;
    private double _dockTimer;
    private Starbase? _dockingAt;
    private double _zeroShieldTime;
    private bool _lowShieldWarned, _photonsOutWarned;
    private int _nextExtraShip;
    private bool _wasTorpedo, _wasWarping;

    private bool _wasStartPressed;
    private double _gameOverTimer;
    private bool _entryPending;
    private double _entryClock;
    private double _entryIdle;
    private double _padRepeat;
    private bool _wasPadUp, _wasPadDown, _wasPadLeft, _wasPadRight, _wasPadAccept, _wasPadDone, _wasLeftMouse;
    private double _tableTimer;
    private bool _tableLeadsToDemo;
    private double _idleTimer;
    private double _attractTimer;
    private bool _wasAnyInputLastFrame;
    private ShipControls _controls;
    private readonly AttractPilot _pilot = new();

    public GameEngine()
    {
        Ship = new Enterprise(World.Center, -Math.PI / 2);
        State = GameState.StartScreen;
    }

    public void Dispose()
    {
        // Closing the window mid-entry still keeps the score, under whatever initials are showing.
        if (_entryPending)
            CommitEntry();

        Sound.Dispose();
        Input.Dispose();
    }

    public void StartNewGame()
    {
        BeginRun(GameState.Playing);
        Sound.Say(Voice.Welcome);
    }

    /// <summary>
    /// The game plays itself, silently, until it runs out of ships or time, then goes back to the
    /// title. Fire starts a real game; anything else returns to the title.
    /// </summary>
    public void StartAttract()
    {
        BeginRun(GameState.Attract);
        _pilot.Reset();
        _attractTimer = AttractLength;
    }

    public void ReturnToTitle()
    {
        State = GameState.StartScreen;
        Sound.StopAllLoops();
        Sound.ClearVoice();
        Sound.IsSilenced = false;
        ShipAlive = false;
        ClearSector();
        Starbases.Clear();
        _idleTimer = 0;
        HighlightIndex = -1;
        Message = SubMessage = null;
    }

    private void ClearSector()
    {
        Klingons.Clear();
        Saucers.Clear();
        Mines.Clear();
        Nomad = null;
        Torpedoes.Clear();
        Disruptors.Clear();
        Phasers.Clear();
        Explosions.Clear();
        _dockTimer = 0;
        _dockingAt = null;
    }

    /// <summary>
    /// Shows the table: after a new entry (with it highlighted), or as part of the idle cycle on the
    /// way from the title screen to the demo.
    /// </summary>
    private void ShowHighScores(bool leadsToDemo)
    {
        State = GameState.HighScores;
        _tableTimer = HighScoresShowTime;
        _tableLeadsToDemo = leadsToDemo;
        if (leadsToDemo)
            HighlightIndex = -1;
    }

    private void BeginInitials()
    {
        State = GameState.EnterInitials;
        Entry = new InitialsEntry(Table.LastInitials);
        _entryClock = 0;
        _entryIdle = 0;
        _wasPadUp = _wasPadDown = _wasPadLeft = _wasPadRight = _wasPadAccept = _wasPadDone = true;
        _wasLeftMouse = true;
    }

    private void CommitEntry()
    {
        _entryPending = false;
        string initials = Entry?.Initials ?? Table.LastInitials;
        HighlightIndex = Table.Insert(initials, Score, Sector);
        Table.Save();
        Entry = null;
    }

    private bool EntryAcceptsInput => State == GameState.EnterInitials && _entryClock >= EntryInputGrace && Entry != null;

    /// <summary>Keyboard input during initials entry, passed straight from the window.</summary>
    public void InitialsKey(Avalonia.Input.Key key)
    {
        if (!EntryAcceptsInput) return;
        var entry = Entry!;
        _entryIdle = 0;

        if (key >= Avalonia.Input.Key.A && key <= Avalonia.Input.Key.Z)
            entry.Type((char)('A' + (key - Avalonia.Input.Key.A)));
        else if (key >= Avalonia.Input.Key.D0 && key <= Avalonia.Input.Key.D9)
            entry.Type((char)('0' + (key - Avalonia.Input.Key.D0)));
        else if (key >= Avalonia.Input.Key.NumPad0 && key <= Avalonia.Input.Key.NumPad9)
            entry.Type((char)('0' + (key - Avalonia.Input.Key.NumPad0)));
        else if (key is Avalonia.Input.Key.OemPeriod or Avalonia.Input.Key.Decimal)
            entry.Type('.');
        else if (key == Avalonia.Input.Key.Up)
            entry.Cycle(1);
        else if (key == Avalonia.Input.Key.Down)
            entry.Cycle(-1);
        else if (key == Avalonia.Input.Key.Left || key == Avalonia.Input.Key.Back)
            entry.Back();
        else if (key == Avalonia.Input.Key.Right)
            entry.Move(1);
        else if (key == Avalonia.Input.Key.Space)
            entry.Advance();
        else if (key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Escape)
            entry.Finish();
    }

    // Gamepad and mouse input during initials entry: up/down dial the letter (holding repeats),
    // left/right move, a fire button accepts a letter, Start finishes. The wheel dials, a click accepts.
    private void UpdateInitials(double deltaTime)
    {
        _entryClock += deltaTime;
        _entryIdle += deltaTime;
        var entry = Entry!;

        bool up = Input.PadUp, down = Input.PadDown, left = Input.PadLeft, right = Input.PadRight;
        bool accept = Input.PadAccept, done = Input.PadDone, click = Input.IsLeftMousePressed;

        if (EntryAcceptsInput)
        {
            if (up || down || left || right || accept || done || click || Input.MouseWheelDelta != 0)
                _entryIdle = 0;

            int dial = 0;
            if ((up && !_wasPadUp) || (down && !_wasPadDown))
            {
                dial = up ? 1 : -1;
                _padRepeat = 0.4;
            }
            else if (up || down)
            {
                _padRepeat -= deltaTime;
                if (_padRepeat <= 0)
                {
                    dial = up ? 1 : -1;
                    _padRepeat = 0.12;
                }
            }
            if (Input.MouseWheelDelta != 0)
                dial = Math.Sign(Input.MouseWheelDelta);
            if (dial != 0)
                entry.Cycle(dial);

            if (left && !_wasPadLeft) entry.Back();
            if (right && !_wasPadRight) entry.Move(1);
            if ((accept && !_wasPadAccept) || (click && !_wasLeftMouse)) entry.Advance();
            if (done && !_wasPadDone) entry.Finish();
        }

        _wasPadUp = up; _wasPadDown = down; _wasPadLeft = left; _wasPadRight = right;
        _wasPadAccept = accept; _wasPadDone = done; _wasLeftMouse = click;

        if (_entryIdle >= EntryTimeout)
            entry.Finish();

        if (entry.IsDone)
        {
            CommitEntry();
            ShowHighScores(leadsToDemo: false);
        }
    }

    private void BeginRun(GameState state)
    {
        State = state;
        Sound.StopAllLoops();
        Sound.ClearVoice();
        Sound.IsSilenced = state == GameState.Attract;
        Score = 0;
        Lives = StartingLives;
        Sector = 1;
        Round = 1;
        _nextExtraShip = ExtraShipEvery;
        ClearSector();
        Message = SubMessage = null;

        SpawnShip(World.Center, -Math.PI / 2);
        BeginSector();
    }

    // ----- Rounds and sectors -----

    private void BeginSector()
    {
        Starbases.Clear();
        int count = Rounds.StarbasesFor(Sector);
        for (int i = 0; i < count; i++)
            Starbases.Add(new Starbase(FindStarbaseSpot()));
        BeginRound();
    }

    // Starbases go well apart from each other and from the ship.
    private Point FindStarbaseSpot()
    {
        Point best = World.Center;
        double bestScore = -1;
        for (int i = 0; i < 40; i++)
        {
            var p = new Point(80 + Random.NextDouble() * (World.Width - 160), 70 + Random.NextDouble() * (World.Height - 140));
            double score = Starbases.Select(b => World.Distance(b.Position, p)).DefaultIfEmpty(1000).Min();
            score = Math.Min(score, World.Distance(Ship.Position, p) * 1.5);
            if (score > bestScore)
            {
                bestScore = score;
                best = p;
            }
        }
        return best;
    }

    private void BeginRound()
    {
        Plan = Rounds.Plan(Sector, Round);
        Phase = RoundPhase.Intro;
        _phaseTimer = IntroTime;
        _klingonsToCome = Plan.Klingons;
        _saucersToCome = Plan.Saucers;
        _nomadToCome = Plan.Nomad;
        _klingonSpawnTimer = 0.3;
        _saucerSpawnTimer = 1.5;
        ShowMessage($"SECTOR {Sector}   ROUND {Round}", Plan.Name, IntroTime);

        if (Plan.Nomad)
        {
            Sound.PlayRedAlert();
            Sound.Say(Voice.Nomad);
        }
        else if (Plan.Saucers > Plan.Klingons)
        {
            Sound.PlayRedAlert();
            Sound.Say(Voice.Saucers);
        }
        else
        {
            Sound.Say(Voice.Klingons);
        }
    }

    private void UpdateRound(double deltaTime)
    {
        _phaseTimer -= deltaTime;
        switch (Phase)
        {
            case RoundPhase.Intro:
                if (_phaseTimer <= 0)
                    Phase = RoundPhase.Fighting;
                break;

            case RoundPhase.Fighting:
                SpawnEnemies(deltaTime);
                if (EnemiesLeft == 0)
                    ClearRound();
                break;

            case RoundPhase.Cleared:
                if (_phaseTimer <= 0 && ShipAlive && !IsDocking)
                {
                    if (Round >= Rounds.PerSector)
                    {
                        Sector++;
                        Round = 1;
                        BeginSector();
                    }
                    else
                    {
                        Round++;
                        BeginRound();
                    }
                }
                break;
        }
    }

    private void ClearRound()
    {
        Phase = RoundPhase.Cleared;
        _phaseTimer = ClearedTime;

        // Whatever Nomad left behind is swept up.
        foreach (var mine in Mines)
            Explosions.Add(Explosion.CreateSmall(mine.Position, MineColor));
        Mines.Clear();
        Disruptors.Clear();

        int bonus = Rounds.RoundBonus(Sector);
        if (Round >= Rounds.PerSector)
        {
            int bases = Starbases.Count;
            bonus += bases * Rounds.StarbaseBonus(Sector);
            ShowMessage($"SECTOR {Sector} SECURED",
                bases > 0 ? $"BONUS {bonus}   ({bases} UNUSED STARBASE{(bases == 1 ? "" : "S")})" : $"BONUS {bonus}",
                ClearedTime);
            Sound.Say(Voice.SectorSecured);
        }
        else
        {
            ShowMessage("ROUND COMPLETE", $"BONUS {bonus}", ClearedTime);
            Sound.Say(Voice.RoundComplete);
        }
        Sound.PlayRoundClear();
        AddScore(bonus);
    }

    private void SpawnEnemies(double deltaTime)
    {
        _klingonSpawnTimer -= deltaTime;
        if (_klingonsToCome > 0 && _klingonSpawnTimer <= 0 && Klingons.Count < Plan.MaxKlingons)
        {
            var spot = FindSpawnSpot();
            var klingon = new Klingon(spot, World.AngleTo(spot, Ship.Position), Sector);
            if (Starbases.Count > 0 && Random.NextDouble() < 0.35)
                klingon.TargetBase = Starbases[Random.Next(Starbases.Count)];
            Klingons.Add(klingon);
            _klingonsToCome--;
            _klingonSpawnTimer = KlingonSpawnInterval;
            Sound.PlayKlingonArrive();
        }

        _saucerSpawnTimer -= deltaTime;
        if (_saucersToCome > 0 && _saucerSpawnTimer <= 0 && Saucers.Count < Plan.MaxSaucers)
        {
            // Saucers come in twos and threes from one direction.
            var spot = FindSpawnSpot();
            int group = Math.Min(_saucersToCome, 2 + Random.Next(2));
            for (int i = 0; i < group; i++)
            {
                double a = Random.NextDouble() * Math.PI * 2;
                Saucers.Add(new Saucer(World.Wrap(spot + new Vector(Math.Cos(a), Math.Sin(a)) * 25), Sector));
            }
            _saucersToCome -= group;
            _saucerSpawnTimer = SaucerSpawnInterval;
            Sound.PlaySaucerAttach();
        }

        if (_nomadToCome)
        {
            Nomad = new Nomad(FindSpawnSpot(), Sector);
            _nomadToCome = false;
            Sound.PlayKlingonArrive();
        }
    }

    // Somewhere well away from the ship, so nothing appears on top of it.
    private Point FindSpawnSpot()
    {
        Point best = World.Center;
        double bestDistance = -1;
        for (int i = 0; i < 25; i++)
        {
            var p = new Point(Random.NextDouble() * World.Width, Random.NextDouble() * World.Height);
            double d = World.Distance(p, Ship.Position);
            if (d >= SpawnDistance)
                return p;
            if (d > bestDistance)
            {
                bestDistance = d;
                best = p;
            }
        }
        return best;
    }

    private void ShowMessage(string message, string? sub, double time)
    {
        Message = message;
        SubMessage = sub;
        MessageTime = time;
    }

    // ----- The frame -----

    public void Update(double deltaTime)
    {
        Clock += deltaTime;
        bool start = Input.IsPhaser;
        bool startPressed = start && !_wasStartPressed;
        _wasStartPressed = start;
        bool newInput = Input.IsAnyInput && !_wasAnyInputLastFrame;
        _wasAnyInputLastFrame = Input.IsAnyInput;

        switch (State)
        {
            case GameState.Paused:
                return;

            case GameState.StartScreen:
                if (startPressed)
                {
                    StartNewGame();
                    return;
                }
                _idleTimer = Input.IsAnyInput ? 0 : _idleTimer + deltaTime;
                if (_idleTimer >= AttractIdleDelay)
                    ShowHighScores(leadsToDemo: true);
                return;

            case GameState.HighScores:
                if (startPressed)
                {
                    StartNewGame();
                    return;
                }
                if (newInput)
                {
                    ReturnToTitle();
                    return;
                }
                _tableTimer -= deltaTime;
                if (_tableTimer <= 0)
                {
                    if (_tableLeadsToDemo)
                        StartAttract();
                    else
                        ReturnToTitle();
                }
                return;

            case GameState.EnterInitials:
                UpdateExplosions(deltaTime);
                UpdateInitials(deltaTime);
                return;

            case GameState.GameOver:
                UpdateWorld(deltaTime, default);
                _gameOverTimer -= deltaTime;
                if (_entryPending)
                {
                    if (_gameOverTimer <= GameOverInputDelay - GameOverBeforeEntry)
                        BeginInitials();
                    return;
                }
                if (startPressed && _gameOverTimer <= 0)
                {
                    StartNewGame();
                    return;
                }
                _idleTimer = Input.IsAnyInput ? 0 : _idleTimer + deltaTime;
                if (_idleTimer >= GameOverIdleDelay)
                    ReturnToTitle();
                return;

            case GameState.Attract:
                if (startPressed)
                {
                    StartNewGame();
                    return;
                }
                if (newInput)
                {
                    ReturnToTitle();
                    return;
                }
                _attractTimer -= deltaTime;
                if (_attractTimer <= 0)
                {
                    ReturnToTitle();
                    return;
                }
                _controls = _pilot.Fly(this, deltaTime);
                break;

            default:
                _controls = new ShipControls(Input.IsTurningLeft, Input.IsTurningRight, Input.IsImpulse,
                    Input.IsPhaser, Input.IsTorpedo, Input.IsWarp);
                break;
        }

        UpdateWorld(deltaTime, _controls);
        if (State == GameState.Playing)
            Sound.StartAmbience();
    }

    public void Pause()
    {
        if (State == GameState.Playing)
        {
            State = GameState.Paused;
            Sound.StopAllLoops();
        }
    }

    public void Resume()
    {
        if (State == GameState.Paused)
        {
            State = GameState.Playing;
            _wasTorpedo = true;
        }
    }

    private void UpdateWorld(double deltaTime, ShipControls controls)
    {
        MessageTime = Math.Max(0, MessageTime - deltaTime);
        ShakeTime = Math.Max(0, ShakeTime - deltaTime);
        HitFlash = Math.Max(0, HitFlash - deltaTime);

        foreach (var starbase in Starbases)
            starbase.Update(deltaTime);

        UpdateShip(deltaTime, controls);
        if (State != GameState.GameOver)
            UpdateRound(deltaTime);
        UpdateKlingons(deltaTime);
        UpdateSaucers(deltaTime);
        UpdateNomad(deltaTime);
        UpdateProjectiles(deltaTime);
        UpdateExplosions(deltaTime);
        CheckCollisions();

        Klingons.RemoveAll(k => !k.IsAlive);
        Saucers.RemoveAll(s => !s.IsAlive);
        Mines.RemoveAll(m => !m.IsAlive);
        if (Nomad is { IsAlive: false })
            Nomad = null;
    }

    // ----- The Enterprise -----

    private void SpawnShip(Point position, double rotation)
    {
        Ship.Respawn(position, rotation);
        ShipAlive = true;
        _zeroShieldTime = 0;
        _lowShieldWarned = false;
        _photonsOutWarned = false;
        _wasWarping = false;
        _wasTorpedo = true;
    }

    // The ship comes back at whichever of a grid of spots is furthest from trouble.
    private void RespawnShip()
    {
        var threats = Klingons.Select(k => k.Position)
            .Concat(Saucers.Select(s => s.Position))
            .Concat(Mines.Select(m => m.Position))
            .Concat(Disruptors.Select(d => d.Position))
            .Concat(Nomad != null ? new[] { Nomad.Position } : Array.Empty<Point>())
            .ToList();

        Point best = World.Center;
        double bestDistance = -1;
        for (int gx = 0; gx < 4; gx++)
        for (int gy = 0; gy < 3; gy++)
        {
            var p = new Point(World.Width * (gx + 0.5) / 4, World.Height * (gy + 0.5) / 3);
            double d = threats.Count == 0 ? Random.NextDouble() : threats.Min(t => World.Distance(p, t));
            if (d > bestDistance)
            {
                bestDistance = d;
                best = p;
            }
        }

        SpawnShip(best, World.AngleTo(best, World.Center));
    }

    private void UpdateShip(double deltaTime, ShipControls controls)
    {
        if (!ShipAlive)
        {
            _respawnTimer -= deltaTime;
            if (_respawnTimer <= 0 && Lives > 0 && State != GameState.GameOver)
                RespawnShip();
            return;
        }

        if (IsDocking)
        {
            UpdateDocking(deltaTime);
            return;
        }

        if (controls.TurnLeft) Ship.Turn(-1, deltaTime);
        if (controls.TurnRight) Ship.Turn(1, deltaTime);
        if (State == GameState.Playing && Input.MouseWheelDelta != 0)
            Ship.Rotation += Input.MouseWheelDelta * 0.25;

        bool warping = Ship.Fly(controls.Impulse, controls.Warp, deltaTime);

        if (warping && !_wasWarping)
        {
            Sound.PlayWarpEngage();
            Sound.StartWarp();
            ShakeOffSaucers();
        }
        else if (!warping && _wasWarping)
        {
            Sound.StopWarp();
        }
        _wasWarping = warping;

        if (controls.Impulse && !warping)
            Sound.StartImpulse();
        else
            Sound.StopImpulse();

        // Phasers repeat while the button is held; torpedoes need a press each.
        if (controls.Phaser && Ship.PhaserTimer <= 0 && !warping)
            FirePhaser();

        bool torpedoPressed = controls.Torpedo && !_wasTorpedo;
        _wasTorpedo = controls.Torpedo;
        if (torpedoPressed && !warping)
            FireTorpedo();

        // Docking: glide into a starbase's centre at a sensible speed.
        if (!warping)
        {
            foreach (var starbase in Starbases)
            {
                if (World.Distance(starbase.Position, Ship.Position) < Starbase.DockRadius && Ship.Velocity.Length < 220)
                {
                    BeginDocking(starbase);
                    break;
                }
            }
        }

        // Saucers on the hull drain the shields; with none left the ship can't last long.
        int attached = Saucers.Count(s => s.IsAttached);
        if (attached > 0)
        {
            if (Ship.Shields > 0)
            {
                Ship.Shields = Math.Max(0, Ship.Shields - Saucer.DrainRate * attached * deltaTime);
                CheckLowShields();
            }
            else
            {
                _zeroShieldTime += deltaTime;
                if (_zeroShieldTime >= ZeroShieldGrace)
                    DestroyShip();
            }
        }
        else
        {
            _zeroShieldTime = 0;
        }
    }

    private void FirePhaser()
    {
        Ship.PhaserTimer = Enterprise.PhaserCooldown;
        var origin = Ship.Position;
        var heading = Ship.Heading;

        // The first thing along the beam takes the hit.
        Enemy? hit = null;
        double hitAt = Enterprise.PhaserRange;
        foreach (var enemy in Targets())
        {
            var d = World.Delta(origin, enemy.Position);
            double along = d.X * heading.X + d.Y * heading.Y;
            double across = Math.Abs(d.X * heading.Y - d.Y * heading.X);
            double r = enemy.Radius + 3;
            if (along < 0 || across > r) continue;
            double entry = along - Math.Sqrt(r * r - across * across);
            if (entry < hitAt)
            {
                hitAt = Math.Max(0, entry);
                hit = enemy;
            }
        }

        Phasers.Add(new PhaserBeam(origin, Ship.Rotation, hitAt, hit != null));
        Sound.PlayPhaser();
        if (hit != null)
            Damage(hit, 1);
    }

    private void FireTorpedo()
    {
        if (Ship.TorpedoTimer > 0) return;

        if (Ship.Photons <= 0)
        {
            Sound.PlayEmpty();
            if (!_photonsOutWarned)
            {
                _photonsOutWarned = true;
                Sound.Say(Voice.PhotonsOut);
            }
            return;
        }

        Ship.Photons--;
        Ship.TorpedoTimer = Enterprise.TorpedoCooldown;
        Torpedoes.Add(new Torpedo(Ship.Nose, Ship.Rotation, Ship.Velocity));
        Sound.PlayTorpedo();
    }

    private void BeginDocking(Starbase starbase)
    {
        _dockingAt = starbase;
        _dockTimer = Starbase.DockTime;
        Ship.Velocity = default;
        Sound.StopImpulse();
        Sound.PlayDocking();
        ShowMessage("DOCKING", null, Starbase.DockTime);

        // The base's own defences see off anything on the hull.
        foreach (var saucer in Saucers.Where(s => s.IsAttached))
            Kill(saucer);
    }

    private void UpdateDocking(double deltaTime)
    {
        var starbase = _dockingAt!;
        var pull = World.Delta(Ship.Position, starbase.Position);
        Ship.Position = World.Wrap(Ship.Position + pull * Math.Min(1, deltaTime * 6));
        Ship.Rotation += deltaTime * 0.8;
        _dockTimer -= deltaTime;
        if (_dockTimer > 0) return;

        // Everything topped up, and the base is spent.
        _dockTimer = 0;
        Ship.Shields = Enterprise.MaxShields;
        Ship.Photons = Enterprise.MaxPhotons;
        Ship.WarpEnergy = Enterprise.MaxWarp;
        _lowShieldWarned = _photonsOutWarned = false;
        _zeroShieldTime = 0;
        Starbases.Remove(starbase);
        _dockingAt = null;
        foreach (var klingon in Klingons.Where(k => k.TargetBase == starbase))
            klingon.TargetBase = null;
        Ship.InvulnerableTime = 1.0;
        ShowMessage("DOCKING COMPLETE", "SHIELDS  PHOTONS  WARP  REPLENISHED", 2.2);
        Sound.Say(Voice.Docking);
    }

    private void ShakeOffSaucers()
    {
        foreach (var saucer in Saucers.Where(s => s.IsAttached).ToList())
            Kill(saucer);
    }

    private void CheckLowShields()
    {
        if (Ship.Shields < LowShields && !_lowShieldWarned)
        {
            _lowShieldWarned = true;
            Sound.PlayRedAlert();
            Sound.Say(Voice.ShieldsLow);
        }
    }

    /// <summary>A hit on the Enterprise. The shields take it while they last; with them gone, it's the ship.</summary>
    private void HitShip(double damage)
    {
        if (!ShipAlive || Ship.IsInvulnerable || IsDocking) return;

        ShakeTime = 0.35;
        HitFlash = 0.3;
        if (Ship.Shields <= 0)
        {
            DestroyShip();
            return;
        }

        Ship.Shields = Math.Max(0, Ship.Shields - damage);
        Sound.PlayShieldHit();
        CheckLowShields();
    }

    private void DestroyShip()
    {
        Explosions.Add(Explosion.CreateShip(Ship.Position));
        Explosions.Add(Explosion.CreateSmall(Ship.Position, Color(255, 200, 120)));
        Sound.PlayShipExplosion();
        Sound.StopImpulse();
        Sound.StopWarp();
        ShakeTime = 0.8;
        HitFlash = 0.6;

        ShipAlive = false;
        Ship.IsAlive = false;
        Ship.DropOutOfWarp();
        _dockTimer = 0;
        _dockingAt = null;
        // Marked rather than cleared: this can happen in the middle of a pass over these lists.
        foreach (var bolt in Disruptors) bolt.IsAlive = false;
        foreach (var torpedo in Torpedoes) torpedo.IsAlive = false;
        foreach (var saucer in Saucers.Where(s => s.IsAttached))
            saucer.Release(Ship);

        Lives--;
        if (Lives > 0)
        {
            _respawnTimer = RespawnDelay;
            Sound.Say(Voice.ShipLost);
            return;
        }

        // A demo that runs out of ships lingers on the explosion, then hands back to the title.
        // It never ends in Game Over and never touches the high score.
        if (State == GameState.Attract)
        {
            _attractTimer = Math.Min(_attractTimer, 3);
            return;
        }

        State = GameState.GameOver;
        Sound.StopAllLoops();
        Sound.Say(Voice.GameOver);
        Message = SubMessage = null;
        _gameOverTimer = GameOverInputDelay;
        _idleTimer = 0;
        _entryPending = Table.Qualifies(Score);
    }

    private void AddScore(int points)
    {
        Score += points;
        while (Score >= _nextExtraShip)
        {
            _nextExtraShip += ExtraShipEvery;
            if (Lives < MaxLives)
            {
                Lives++;
                Sound.PlayExtraShip();
            }
        }
    }

    // ----- Enemies -----

    /// <summary>Everything the ship's weapons can hit right now.</summary>
    private IEnumerable<Enemy> Targets()
    {
        foreach (var k in Klingons)
            if (k.IsAlive && k.HasArrived) yield return k;
        foreach (var s in Saucers)
            if (s.IsAlive && !s.IsAttached) yield return s;
        foreach (var m in Mines)
            if (m.IsAlive) yield return m;
        if (Nomad is { IsAlive: true })
            yield return Nomad;
    }

    private void Damage(Enemy enemy, int amount)
    {
        if (!enemy.IsAlive) return;
        enemy.Health -= amount;
        if (enemy.Health <= 0)
        {
            Kill(enemy);
            return;
        }

        enemy.HitFlash = 0.15;
        Sound.PlayNomadHit();
    }

    private void Kill(Enemy enemy)
    {
        if (!enemy.IsAlive) return;
        enemy.IsAlive = false;
        AddScore(enemy.Points);

        switch (enemy)
        {
            case Klingon:
                Explosions.Add(Explosion.CreateKlingon(enemy.Position));
                Sound.PlayKlingonBoom();
                break;
            case Saucer:
                Explosions.Add(Explosion.CreateSmall(enemy.Position, SaucerColor));
                Sound.PlaySaucerBoom();
                break;
            case Mine:
                Explosions.Add(Explosion.CreateSmall(enemy.Position, MineColor));
                Sound.PlayMineBoom();
                break;
            case Models.Nomad:
                Explosions.Add(Explosion.CreateBig(enemy.Position, NomadColor));
                Explosions.Add(Explosion.CreateBig(enemy.Position, Color(255, 255, 255)));
                Sound.PlayBigBoom();
                ShakeTime = 0.6;
                break;
        }
    }

    private void UpdateKlingons(double deltaTime)
    {
        foreach (var klingon in Klingons)
        {
            if (klingon.TargetBase is { } b && (!Starbases.Contains(b) ||
                (ShipAlive && World.Distance(Ship.Position, klingon.Position) < 200)))
                klingon.TargetBase = null;

            // With the Enterprise gone, they go after the starbases, or just keep circling.
            Point target = klingon.TargetBase?.Position ??
                           (ShipAlive ? Ship.Position : Starbases.FirstOrDefault()?.Position ?? World.Center);
            bool canFire = State != GameState.GameOver && (ShipAlive || klingon.TargetBase != null);

            if (klingon.Think(target, deltaTime) && canFire && Phase == RoundPhase.Fighting)
            {
                double bearing = World.AngleTo(klingon.Position, target);
                var muzzle = World.Wrap(klingon.Position + klingon.Heading * 18);
                Disruptors.Add(new Disruptor(muzzle, bearing, klingon.DisruptorSpeed));
                Sound.PlayDisruptor();
            }
        }
    }

    private void UpdateSaucers(double deltaTime)
    {
        foreach (var saucer in Saucers)
            saucer.Think(Ship, ShipAlive && !IsDocking, deltaTime);
    }

    private void UpdateNomad(double deltaTime)
    {
        if (Nomad == null) return;
        if (Nomad.Think(deltaTime) && Mines.Count < MaxMines && State != GameState.GameOver)
        {
            Mines.Add(new Mine(Nomad.Tail));
            Sound.PlayMineLaid();
        }

        foreach (var mine in Mines)
            mine.Update(deltaTime);
    }

    private void UpdateProjectiles(double deltaTime)
    {
        foreach (var torpedo in Torpedoes)
        {
            torpedo.Update(deltaTime);
            bool contact = Targets().Any(e => World.Distance(e.Position, torpedo.Position) < e.Radius + torpedo.Radius);
            if (contact || !torpedo.IsAlive)
            {
                torpedo.IsAlive = false;
                Detonate(torpedo.Position);
            }
        }
        Torpedoes.RemoveAll(t => !t.IsAlive);

        foreach (var bolt in Disruptors)
        {
            bolt.Update(deltaTime);
            foreach (var starbase in Starbases)
            {
                if (World.Distance(bolt.Position, starbase.Position) < starbase.Radius * 0.8)
                {
                    bolt.IsAlive = false;
                    HitStarbase(starbase);
                    break;
                }
            }
        }
        Starbases.RemoveAll(b => b.Health <= 0);

        foreach (var beam in Phasers)
            beam.Update(deltaTime);
        Phasers.RemoveAll(p => p.IsFinished);
    }

    private void Detonate(Point position)
    {
        Explosions.Add(Explosion.CreateTorpedo(position));
        Sound.PlayTorpedoBlast();
        foreach (var enemy in Targets().ToList())
        {
            if (World.Distance(enemy.Position, position) < Torpedo.BlastRadius + enemy.Radius)
                Damage(enemy, Torpedo.Damage);
        }
    }

    private void HitStarbase(Starbase starbase)
    {
        if (starbase == _dockingAt) return;
        starbase.Health--;
        starbase.HitFlash = 0.2;
        Explosions.Add(Explosion.CreateSmall(starbase.Position, Color(255, 120, 90)));
        if (starbase.Health > 0)
        {
            Sound.PlayShieldHit();
            return;
        }

        Explosions.Add(Explosion.CreateBig(starbase.Position, StarbaseColor));
        Sound.PlayBigBoom();
        Sound.Say(Voice.StarbaseLost);
        ShowMessage("STARBASE DESTROYED", null, 2.2);
    }

    private void UpdateExplosions(double deltaTime)
    {
        foreach (var explosion in Explosions)
            explosion.Update(deltaTime);

        Explosions.RemoveAll(e => e.IsFinished);
    }

    private void CheckCollisions()
    {
        foreach (var bolt in Disruptors)
        {
            if (bolt.IsAlive && ShipAlive && !Ship.IsWarping &&
                World.Distance(bolt.Position, Ship.Position) < Ship.Radius + bolt.Radius)
            {
                bolt.IsAlive = false;
                HitShip(10 + Math.Min(Sector - 1, 10));
            }
        }
        Disruptors.RemoveAll(d => !d.IsAlive);

        if (!ShipAlive || IsDocking) return;

        // At warp the Enterprise ploughs straight through anything small.
        if (Ship.IsWarping)
        {
            foreach (var enemy in Targets().ToList())
            {
                if (!Ship.CollidesWith(enemy)) continue;
                if (enemy is Nomad nomad)
                {
                    Damage(nomad, 3);
                    Ship.DropOutOfWarp();
                    BounceOff(nomad);
                    HitShip(20);
                }
                else
                {
                    Kill(enemy);
                }
            }
            return;
        }

        foreach (var klingon in Klingons)
        {
            if (klingon.IsAlive && klingon.HasArrived && Ship.CollidesWith(klingon))
            {
                bool shielded = Ship.IsInvulnerable;
                Kill(klingon);
                if (!shielded) HitShip(25);
            }
        }

        foreach (var saucer in Saucers)
        {
            if (saucer.IsAlive && saucer.CanAttach && !Ship.IsInvulnerable && Ship.CollidesWith(saucer))
            {
                saucer.Attach(Ship);
                Sound.PlaySaucerAttach();
            }
        }

        foreach (var mine in Mines)
        {
            if (mine.IsAlive && Ship.CollidesWith(mine))
            {
                mine.IsAlive = false;
                Explosions.Add(Explosion.CreateSmall(mine.Position, MineColor));
                Sound.PlayMineBoom();
                HitShip(30);
                if (!ShipAlive) return;
            }
        }

        if (Nomad is { IsAlive: true } n && Ship.CollidesWith(n))
        {
            BounceOff(n);
            HitShip(40);
        }
    }

    private void BounceOff(GameObject obstacle)
    {
        var away = World.Delta(obstacle.Position, Ship.Position);
        double d = Math.Max(away.Length, 1);
        var normal = away / d;
        Ship.Position = World.Wrap(obstacle.Position + normal * (obstacle.Radius + Ship.Radius + 1));
        Ship.Velocity = normal * 160;
    }

    // Colours shared by the screens and the explosions.
    public static readonly Avalonia.Media.Color KlingonColor = Color(255, 90, 70);
    public static readonly Avalonia.Media.Color SaucerColor = Color(255, 220, 80);
    public static readonly Avalonia.Media.Color NomadColor = Color(230, 120, 255);
    public static readonly Avalonia.Media.Color MineColor = Color(150, 255, 170);
    public static readonly Avalonia.Media.Color StarbaseColor = Color(90, 170, 255);

    private static Avalonia.Media.Color Color(byte r, byte g, byte b) => Avalonia.Media.Color.FromRgb(r, g, b);
}
