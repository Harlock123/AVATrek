using System;
using Avalonia;
using Avalonia.Media;
using AVATrek.Engine;
using AVATrek.Models;

namespace AVATrek.Views;

// The status panel, the tactical scanner, and the title, entry and high-score screens.
public partial class GameCanvas
{
    private static readonly Rect StatusRect = new(12, 12, 316, 290);
    private static readonly Rect ScannerRect = new(12, 314, 1000, 450);
    private static readonly double ScannerScale = ScannerRect.Width / World.Width;

    // ----- Status panel -----

    private void DrawStatusPanel(DrawingContext context)
    {
        context.FillRectangle(GetBrush(Color.FromRgb(2, 4, 12), 1), StatusRect);
        DrawFrame(context, StatusRect, FrameColor, 0.8);

        double x = StatusRect.X + 16, right = StatusRect.Right - 16;
        var ship = _engine.Ship;

        context.DrawText(Text("SCORE", 13, TextColor, 0.8), new Point(x, 22));
        context.DrawText(Text(_engine.Score.ToString("000000"), 30, Colors.White), new Point(x, 36));

        // A demo's score is the pilot's, not the player's: it never shows as the high score.
        int high = _engine.State == GameState.Attract ? _engine.HighScore : Math.Max(_engine.HighScore, _engine.Score);
        DrawRightText(context, "HIGH", 13, right, 22, TextColor);
        DrawRightText(context, high.ToString("000000"), 18, right, 44, Gold);

        double shields = _engine.ShipAlive ? ship.Shields / Enterprise.MaxShields : 0;
        var shieldColor = shields > 0.5 ? Color.FromRgb(90, 255, 140) : shields > 0.25 ? Gold : AlertRed;
        DrawGauge(context, "SHIELDS", 84, shields, shieldColor, _engine.IsShieldWarning && Blink(4));

        double warp = _engine.ShipAlive ? ship.WarpEnergy / Enterprise.MaxWarp : 0;
        DrawGauge(context, "WARP", 128, warp, Color.FromRgb(120, 190, 255), false);

        // Photon torpedoes, one icon each.
        context.DrawText(Text("PHOTONS", 13, TextColor, 0.8), new Point(x, 172));
        int photons = _engine.ShipAlive ? ship.Photons : 0;
        for (int i = 0; i < Enterprise.MaxPhotons; i++)
        {
            var c = new Point(x + 104 + i * 34, 181);
            if (i < photons)
            {
                context.DrawEllipse(GetBrush(TorpedoColor, 0.3), null, c, 8, 8);
                context.DrawEllipse(GetBrush(TorpedoColor, 0.95), null, c, 3.5, 3.5);
                DrawGlowLine(context, c + new Vector(-8, 0), c + new Vector(8, 0), TorpedoColor, 0.8, 1);
                DrawGlowLine(context, c + new Vector(0, -8), c + new Vector(0, 8), TorpedoColor, 0.8, 1);
            }
            else
            {
                context.DrawEllipse(null, GetPen(TorpedoColor, 0.25, 1), c, 4, 4);
            }
        }

        context.DrawText(Text($"SECTOR {_engine.Sector}", 18, Gold), new Point(x, 204));
        DrawRightText(context, $"ROUND {_engine.Round}/{Rounds.PerSector}", 18, right, 204, Gold);
        context.DrawText(Text(_engine.Plan.Name, 13, TextColor), new Point(x, 228));

        // Spare ships on the left, starbases standing on the right.
        int spare = Math.Max(0, _engine.Lives - (_engine.ShipAlive ? 1 : 0));
        for (int i = 0; i < spare && i < 6; i++)
            DrawModelTopDown(context, Wireframe.Enterprise, new Point(x + 14 + i * 32, 270), -Math.PI / 2, 12, ShipColor, 0.8, 1);
        for (int i = 0; i < _engine.Starbases.Count; i++)
            DrawModelTopDown(context, Wireframe.Starbase, new Point(right - 14 - i * 34, 270), Clock * 0.4, 12, GameEngine.StarbaseColor, 0.8, 1);

        var flags = (_engine.Settings.Spinner ? "SPINNER " : "") + (_engine.Sound.IsMuted ? "MUTED " : "") +
                    (!_engine.Sound.IsVoiceOn ? "VOICE OFF" : "");
        if (flags.Length > 0)
            DrawRightText(context, flags.Trim(), 11, right, 232, TextColor);

        if (_engine.IsShieldWarning && Blink(3))
        {
            var banner = new Rect(StatusRect.X + 60, 140, StatusRect.Width - 120, 0);
            DrawCentredText(context, "RED ALERT", 15, 150, AlertRed, banner.Center.X);
        }
    }

    private void DrawGauge(DrawingContext context, string label, double y, double fraction, Color color, bool flash)
    {
        double x = StatusRect.X + 16;
        context.DrawText(Text(label, 13, TextColor, 0.8), new Point(x, y));
        var bar = new Rect(x, y + 18, StatusRect.Width - 32, 14);
        DrawFrame(context, bar, color, flash ? 1 : 0.5);

        // A bar of segments, like a lit readout.
        const int segments = 20;
        double w = bar.Width / segments;
        int lit = (int)Math.Ceiling(fraction * segments - 0.001);
        for (int i = 0; i < lit; i++)
            context.FillRectangle(GetBrush(color, flash ? 1 : 0.85), new Rect(bar.X + i * w + 1.5, bar.Y + 2.5, w - 3, bar.Height - 5));
    }

    // ----- Tactical scanner -----

    private void DrawScanner(DrawingContext context)
    {
        context.FillRectangle(GetBrush(Color.FromRgb(1, 3, 10), 1), ScannerRect);

        using (context.PushClip(ScannerRect))
        using (context.PushTransform(Matrix.CreateScale(ScannerScale, ScannerScale) * Matrix.CreateTranslation(ScannerRect.X, ScannerRect.Y)))
        {
            // A faint grid, so movement reads even with nothing near.
            var grid = GetPen(FrameColor, 0.12, 1);
            for (double gx = 100; gx < World.Width; gx += 100)
                context.DrawLine(grid, new Point(gx, 0), new Point(gx, World.Height));
            for (double gy = 90; gy < World.Height; gy += 90)
                context.DrawLine(grid, new Point(0, gy), new Point(World.Width, gy));

            DrawScannerObjects(context);
        }

        DrawFrame(context, ScannerRect, FrameColor, 0.8);
        context.DrawText(Text("TACTICAL SCANNER", 12, TextColor, 0.7), new Point(ScannerRect.X + 8, ScannerRect.Y + 5));
        if (_engine.Phase == RoundPhase.Fighting || _engine.Phase == RoundPhase.Intro)
            DrawRightText(context, $"ENEMY CONTACTS {_engine.EnemiesLeft}", 12, ScannerRect.Right - 8, ScannerRect.Y + 5, TextColor);
    }

    /// <summary>Draws something at its place in the sector, and again across the wrap if it overlaps an edge.</summary>
    private static void ForEachImage(Point p, double radius, Action<Point> draw)
    {
        draw(p);
        double dx = p.X < radius ? World.Width : p.X > World.Width - radius ? -World.Width : 0;
        double dy = p.Y < radius ? World.Height : p.Y > World.Height - radius ? -World.Height : 0;
        if (dx != 0) draw(new Point(p.X + dx, p.Y));
        if (dy != 0) draw(new Point(p.X, p.Y + dy));
        if (dx != 0 && dy != 0) draw(new Point(p.X + dx, p.Y + dy));
    }

    private void DrawScannerModel(DrawingContext context, GameObject obj, IModelled modelled, Color color, double alpha, double width = 1.6)
    {
        ForEachImage(obj.Position, modelled.ModelSize * 1.4, p =>
            DrawModelTopDown(context, modelled.Model, p, modelled.ModelYaw, modelled.ModelSize, color, alpha, width));
    }

    private void DrawScannerObjects(DrawingContext context)
    {
        foreach (var starbase in _engine.Starbases)
        {
            var color = starbase.HitFlash > 0 ? Colors.White : GameEngine.StarbaseColor;
            DrawScannerModel(context, starbase, starbase, color, 0.9, 1.4);
            // The docking point at its heart pulses.
            double pulse = 0.4 + 0.4 * Math.Sin(Clock * 4);
            context.DrawEllipse(GetBrush(GameEngine.StarbaseColor, pulse), null, starbase.Position, 3, 3);
        }

        foreach (var mine in _engine.Mines)
        {
            double alpha = mine.IsFading && Blink(8) ? 0.3 : 1;
            DrawScannerModel(context, mine, mine, GameEngine.MineColor, alpha, 1.3);
        }

        if (_engine.Nomad is { } nomad)
            DrawScannerModel(context, nomad, nomad, nomad.HitFlash > 0 ? Colors.White : GameEngine.NomadColor, 1, 1.8);

        foreach (var klingon in _engine.Klingons)
        {
            double alpha = klingon.HasArrived ? 1 : ArrivalAlpha(klingon);
            DrawScannerModel(context, klingon, klingon, klingon.HitFlash > 0 ? Colors.White : GameEngine.KlingonColor, alpha, 1.7);
        }

        foreach (var saucer in _engine.Saucers)
            DrawScannerModel(context, saucer, saucer, GameEngine.SaucerColor, 1, 1.4);

        foreach (var bolt in _engine.Disruptors)
        {
            var tail = bolt.Position - bolt.Velocity / Math.Max(bolt.Velocity.Length, 1) * 12;
            DrawGlowLine(context, tail, bolt.Position, DisruptorColor, 1, 2.5);
        }

        foreach (var torpedo in _engine.Torpedoes)
        {
            double r = 4 + Math.Sin(Clock * 30) * 1.5;
            context.DrawEllipse(GetBrush(TorpedoColor, 0.3), null, torpedo.Position, r * 2, r * 2);
            context.DrawEllipse(GetBrush(TorpedoColor, 1), null, torpedo.Position, r * 0.7, r * 0.7);
        }

        foreach (var beam in _engine.Phasers)
        {
            double alpha = 1 - beam.Age / PhaserBeam.ShowTime;
            DrawGlowLine(context, beam.Start, beam.End, PhaserColor, alpha, 2.5);
        }

        foreach (var explosion in _engine.Explosions)
        {
            double alpha = explosion.GetOpacity();
            foreach (var (a, b) in explosion.GetParticleLines())
                DrawGlowLine(context, a, b, explosion.Color, alpha, 2);
        }

        DrawScannerShip(context);
    }

    private void DrawScannerShip(DrawingContext context)
    {
        if (!_engine.ShipAlive) return;
        var ship = _engine.Ship;

        // The viewer's field of view, faintly, so the two screens can be matched up.
        foreach (double side in new[] { -1.0, 1.0 })
        {
            double a = ship.Rotation + side * FieldOfView / 2;
            DrawGlowLine(context, ship.Position + new Vector(Math.Cos(a), Math.Sin(a)) * 22,
                ship.Position + new Vector(Math.Cos(a), Math.Sin(a)) * 170, ShipColor, 0.12, 1);
        }

        if (ship.InvulnerableTime > 0 && !ship.IsWarping && Blink(10) && !_engine.IsDocking)
            return;

        var color = ship.IsWarping ? Color.FromRgb(170, 210, 255) : ShipColor;
        ForEachImage(ship.Position, 30, p =>
        {
            if (ship.IsWarping)
            {
                // A streak of light behind the ship at warp.
                for (int i = 1; i <= 4; i++)
                {
                    var back = p - ship.Heading * (14 + i * 14);
                    DrawGlowLine(context, back - ship.Heading * 10, back, color, 0.5 - i * 0.1, 3);
                }
            }
            DrawModelTopDown(context, ship.Model, p, ship.Rotation, ship.ModelSize, color, 1, 1.6);

            if (_engine.IsImpulse && Blink(20))
            {
                double c = Math.Cos(ship.Rotation), s = Math.Sin(ship.Rotation);
                foreach (double y in new[] { -0.44, 0.44 })
                {
                    var exhaust = new Point(p.X + (-1.05 * c - y * s) * ship.ModelSize, p.Y + (-1.05 * s + y * c) * ship.ModelSize);
                    context.DrawEllipse(GetBrush(PhaserColor, 0.8), null, exhaust, 2.5, 2.5);
                }
            }

            // The shields light up round the ship when they take a hit.
            if (_engine.HitFlash > 0)
                context.DrawEllipse(null, GetPen(Color.FromRgb(120, 200, 255), Math.Min(1, _engine.HitFlash * 3), 2), p, 24, 24);
        });
    }

    // ----- Title screen -----

    private void DrawTitleScreen(DrawingContext context, bool withText)
    {
        // Stars streaming out from the centre, as if at warp.
        var centre = new Point(ScreenWidth / 2, 250);
        for (int i = 0; i < _farStars.Count; i++)
        {
            var (azimuth, _, brightness) = _farStars[i];
            double t = (Clock * (0.12 + brightness * 0.18) + i * 0.618) % 1;
            double r = Math.Pow(t, 2.2) * 760;
            var dir = new Vector(Math.Cos(azimuth), Math.Sin(azimuth));
            var p = centre + dir * r;
            double len = 4 + r * 0.08;
            DrawGlowLine(context, p, p + dir * len, Color.FromRgb(190, 210, 255), brightness * Math.Min(1, t * 3), 1);
        }

        // The ship herself, turning slowly.
        DrawModelShowcase(context, Wireframe.Enterprise, centre, 150, Clock * 0.45 + 2.2, 0.32, ShipColor, 1, 1.6);

        if (!withText) return;

        DrawCentredText(context, "AVATREK", 72, 14, Gold);
        DrawCentredText(context, "STRATEGIC OPERATIONS SIMULATOR", 20, 98, TextColor);

        // Who's who, with what each is worth.
        var legend = new (Wireframe Model, Color Color, string Name, string Value, double Size)[]
        {
            (Wireframe.Klingon, GameEngine.KlingonColor, "KLINGON", Klingon.ScoreValue.ToString(), 34),
            (Wireframe.Saucer, GameEngine.SaucerColor, "ANTI-MATTER SAUCER", Saucer.ScoreValue.ToString(), 24),
            (Wireframe.Nomad, GameEngine.NomadColor, "NOMAD", Nomad.ScoreValue.ToString(), 26),
            (Wireframe.Mine, GameEngine.MineColor, "MINE", Mine.ScoreValue.ToString(), 20),
            (Wireframe.Starbase, GameEngine.StarbaseColor, "STARBASE", "DOCK", 32)
        };
        for (int i = 0; i < legend.Length; i++)
        {
            var (model, color, name, value, size) = legend[i];
            double cx = 112 + i * 200;
            DrawModelShowcase(context, model, new Point(cx, 418), size, Clock * 0.8 + i, 0.5, color, 1, 1.3);
            DrawCentredText(context, name, 13, 456, color, cx);
            DrawCentredText(context, value, 15, 474, Colors.White, cx);
        }

        var controls = new (string Keys, string Action)[]
        {
            ("A / D, LEFT / RIGHT", "Turn"),
            ("W or UP", "Impulse"),
            ("SPACE", "Phasers"),
            ("F or Z", "Photon torpedo"),
            ("SHIFT or E", "Warp"),
            ("Mouse", "Left phasers, right impulse,"),
            ("", "middle torpedo, wheel turns"),
            ("TAB   [  ]", "Mouse spinner, sensitivity"),
            ("ESC  M  V", "Pause, mute, voice")
        };
        string[] orders =
        {
            "Defend the sector through six rounds:",
            "Klingons, anti-matter saucers, Nomad.",
            "Shields, photons and warp are limited.",
            "Fly into a starbase to dock and refill,",
            "but every starbase left unused at the",
            "end of a sector earns a bonus.",
            "Saucers latch on and drain the shields:",
            "go to warp to shake them off.",
            ""
        };
        for (int i = 0; i < controls.Length; i++)
        {
            double y = 504 + i * 19;
            context.DrawText(Text(controls[i].Keys, 14, Colors.White), new Point(48, y));
            context.DrawText(Text(controls[i].Action, 14, TextColor), new Point(230, y));
            context.DrawText(Text(orders[i], 14, TextColor), new Point(580, y));
        }

        var best = _engine.Table.Entries.Count > 0 ? _engine.Table.Entries[0] : null;
        DrawCentredText(context, best == null ? "HIGH SCORE 0" : $"HIGH SCORE  {best.Initials.TrimEnd()}  {best.Score}", 18, 686, Gold);

        if (Blink(2))
            DrawCentredText(context, "Press SPACE or X to start", 26, 718, Colors.White);

        var settings = _engine.Settings;
        DrawRightText(context, settings.Spinner ? $"MOUSE SPINNER ON  ·  SENSITIVITY {settings.SpinnerSensitivity}" : "MOUSE SPINNER OFF",
            12, ScreenWidth - 14, 748, settings.Spinner ? Gold : TextColor);
    }

    // ----- High scores -----

    private string NewEntryHeadline => _engine.Score > _engine.HighScore ? "NEW HIGH SCORE" : "YOU MADE THE TOP TEN";

    private void DrawInitialsEntry(DrawingContext context)
    {
        var entry = _engine.Entry;
        if (entry == null) return;

        DrawPanel(context, new Rect(152, 110, 720, 560));
        DrawCentredText(context, NewEntryHeadline, 40, 140, Gold);
        DrawCentredText(context, $"SCORE {_engine.Score}    SECTOR {_engine.Sector}", 22, 200, TextColor);
        DrawCentredText(context, "ENTER YOUR INITIALS, CAPTAIN", 24, 260, Colors.White);

        bool blinkOn = Blink(3);
        const double cell = 100;
        double left = (ScreenWidth - cell * HighScoreTable.InitialsLength) / 2;
        string initials = entry.Initials;

        for (int i = 0; i < initials.Length; i++)
        {
            bool current = i == entry.Cursor;
            char c = initials[i];
            var letter = Text(c == ' ' ? "_" : c.ToString(), 76, current ? Colors.White : Gold);
            double x = left + i * cell + (cell - letter.Width) / 2;
            context.DrawText(letter, new Point(x, 315));

            if (current)
            {
                var up = Text("^", 22, TextColor);
                var down = Text("v", 22, TextColor);
                context.DrawText(up, new Point(left + i * cell + (cell - up.Width) / 2, 300));
                context.DrawText(down, new Point(left + i * cell + (cell - down.Width) / 2, 418));
                if (blinkOn)
                    DrawGlowLine(context, new Point(left + i * cell + 18, 412), new Point(left + (i + 1) * cell - 18, 412), Colors.White, 1, 2.5);
            }
        }

        string[] help =
        {
            "Type your initials, or UP/DOWN to choose a letter",
            "and LEFT/RIGHT to move.  ENTER when done.",
            "Gamepad: D-pad to choose, X to accept, START when done",
            "Mouse: wheel to choose, click to accept"
        };
        double y = 480;
        foreach (var line in help)
        {
            DrawCentredText(context, line, 16, y, TextColor);
            y += 26;
        }
    }

    private void DrawHighScoreTable(DrawingContext context)
    {
        DrawPanel(context, new Rect(192, 60, 640, 640));
        DrawCentredText(context, "HIGH SCORES", 44, 80, Gold);

        const double rankX = 250, nameX = 340, scoreRight = 640, sectorX = 680;
        double y = 165;
        context.DrawText(Text("RANK", 16, TextColor), new Point(rankX, y));
        context.DrawText(Text("NAME", 16, TextColor), new Point(nameX, y));
        DrawRightText(context, "SCORE", 16, scoreRight, y, TextColor);
        context.DrawText(Text("SECTOR", 16, TextColor), new Point(sectorX, y));

        bool blinkOn = Blink(3);
        var entries = _engine.Table.Entries;
        y = 200;
        for (int i = 0; i < HighScoreTable.Size; i++)
        {
            bool highlight = i == _engine.HighlightIndex;
            var color = highlight ? (blinkOn ? Colors.White : Gold) : i == 0 ? Gold : Colors.White;

            string name = i < entries.Count ? entries[i].Initials : "---";
            string score = i < entries.Count ? entries[i].Score.ToString() : "0";
            string sector = i < entries.Count && entries[i].Sector > 0 ? entries[i].Sector.ToString() : "-";

            context.DrawText(Text($"{i + 1}.", 24, color), new Point(rankX, y));
            context.DrawText(Text(name, 24, color), new Point(nameX, y));
            DrawRightText(context, score, 24, scoreRight, y, color);
            context.DrawText(Text(sector, 24, color), new Point(sectorX + 26, y));

            if (highlight)
                DrawGlowLine(context, new Point(rankX - 20, y + 15), new Point(rankX - 8, y + 15), color, 1, 2);
            y += 42;
        }

        if (Blink(2))
            DrawCentredText(context, "Press SPACE or X to play", 24, 650, Colors.White);
    }
}
