using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AVATrek.Engine;

namespace AVATrek.Views;

/// <summary>
/// The whole cabinet screen: status readouts and the main viewer across the top, the tactical scanner
/// below. Drawn on a fixed 1024×768 screen that is scaled to fit the window.
/// </summary>
public partial class GameCanvas : Control
{
    public const double ScreenWidth = 1024;
    public const double ScreenHeight = 768;

    private readonly GameEngine _engine;
    private readonly DispatcherTimer _gameTimer;
    private DateTime _lastUpdate;
    private bool _wasPausePressed;
    private bool _wasBackPressed;
    private Point _lastPointer;
    private bool _hasLastPointer;
    private bool _spinnerCursorHidden;

    public GameCanvas()
    {
        _engine = new GameEngine();
        _lastUpdate = DateTime.Now;

        _gameTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _gameTimer.Tick += GameLoop;

        Focusable = true;
        ClipToBounds = true;
        InitViewer();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _gameTimer.Start();
        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _gameTimer.Stop();
        _engine.Dispose();
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        // Handle Ctrl+S (or Cmd+S on macOS) for screenshot
        if (e.Key == Avalonia.Input.Key.S &&
            (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) ||
             e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta)))
        {
            TakeScreenshot();
            e.Handled = true;
            return;
        }

        // While entering initials every key belongs to the entry (M is a letter, not mute).
        if (_engine.State == GameState.EnterInitials)
        {
            _engine.InitialsKey(e.Key);
            e.Handled = true;
            return;
        }

        if (e.Key == Avalonia.Input.Key.M)
        {
            _engine.Sound.IsMuted = !_engine.Sound.IsMuted;
            if (_engine.Sound.IsMuted)
            {
                _engine.Sound.StopAllLoops();
                _engine.Sound.ClearVoice();
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Avalonia.Input.Key.Tab)
        {
            var settings = _engine.Settings;
            settings.Spinner = !settings.Spinner;
            settings.Save();
            _engine.Notify(settings.Spinner ? "MOUSE SPINNER ON" : "MOUSE SPINNER OFF",
                settings.Spinner ? "Move the mouse left and right to turn" : null);
            e.Handled = true;
            return;
        }

        if (e.Key is Avalonia.Input.Key.OemOpenBrackets or Avalonia.Input.Key.OemCloseBrackets)
        {
            var settings = _engine.Settings;
            int step = e.Key == Avalonia.Input.Key.OemCloseBrackets ? 1 : -1;
            settings.SpinnerSensitivity = Math.Clamp(settings.SpinnerSensitivity + step,
                Settings.MinSensitivity, Settings.MaxSensitivity);
            settings.Save();
            _engine.Notify($"SPINNER SENSITIVITY {settings.SpinnerSensitivity}", null);
            e.Handled = true;
            return;
        }

        if (e.Key == Avalonia.Input.Key.V)
        {
            _engine.Sound.IsVoiceOn = !_engine.Sound.IsVoiceOn;
            if (!_engine.Sound.IsVoiceOn)
                _engine.Sound.ClearVoice();
            e.Handled = true;
            return;
        }

        if (e.Key == Avalonia.Input.Key.Escape)
        {
            if (_engine.State == GameState.Playing)
            {
                _engine.Pause();
            }
            else if (_engine.State is GameState.Attract or GameState.HighScores)
            {
                _engine.ReturnToTitle();
            }
            else
            {
                var window = TopLevel.GetTopLevel(this) as Window;
                window?.Close();
            }
            e.Handled = true;
            return;
        }

        if (_engine.State == GameState.Paused)
        {
            _engine.Resume();
            e.Handled = true;
            return;
        }

        _engine.Input.OnKeyDown(e.Key);
        e.Handled = true;
    }

    protected override void OnKeyUp(Avalonia.Input.KeyEventArgs e)
    {
        _engine.Input.OnKeyUp(e.Key);
        e.Handled = true;
    }

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsLeftButtonPressed)
            _engine.Input.OnMouseDown(Avalonia.Input.MouseButton.Left);
        if (props.IsRightButtonPressed)
            _engine.Input.OnMouseDown(Avalonia.Input.MouseButton.Right);
        if (props.IsMiddleButtonPressed)
            _engine.Input.OnMouseDown(Avalonia.Input.MouseButton.Middle);
        e.Handled = true;
    }

    protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed)
            _engine.Input.OnMouseUp(Avalonia.Input.MouseButton.Left);
        if (!props.IsRightButtonPressed)
            _engine.Input.OnMouseUp(Avalonia.Input.MouseButton.Right);
        if (!props.IsMiddleButtonPressed)
            _engine.Input.OnMouseUp(Avalonia.Input.MouseButton.Middle);
        e.Handled = true;
    }

    // Spinner mode: only sideways movement counts. The pointer is kept near the middle of the window
    // (see KeepPointerCentred), so it never runs into a screen edge.
    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e)
    {
        var position = e.GetPosition(this);
        if (_engine.IsSpinnerActive && _hasLastPointer)
        {
            double dx = position.X - _lastPointer.X;
            // A jump this big is the pointer being moved back to the middle, not the player.
            if (Math.Abs(dx) < Bounds.Width * 0.2)
                _engine.Input.OnSpinner(dx);
        }
        _lastPointer = position;
        _hasLastPointer = true;
        KeepPointerCentred(position);
    }

    private void KeepPointerCentred(Point position)
    {
        if (!_engine.IsSpinnerActive || Bounds.Width <= 0) return;
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        if (Math.Abs(position.X - centre.X) < Bounds.Width * 0.2 && Math.Abs(position.Y - centre.Y) < Bounds.Height * 0.2)
            return;

        var screen = this.PointToScreen(centre);
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        if (MouseWarp.MoveTo(screen.X, screen.Y, scaling))
            _lastPointer = centre;
    }

    // The pointer disappears while the spinner is working, and comes back for menus and pauses.
    private void UpdateSpinnerCursor()
    {
        bool active = _engine.IsSpinnerActive;
        if (active == _spinnerCursorHidden) return;
        _spinnerCursorHidden = active;
        Cursor = active ? new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.None) : null;
        if (active)
            KeepPointerCentred(new Point(-1, -1));
    }

    protected override void OnPointerWheelChanged(Avalonia.Input.PointerWheelEventArgs e)
    {
        _engine.Input.OnMouseWheel(e.Delta.Y);
        e.Handled = true;
    }

    private void TakeScreenshot()
    {
        var pixelSize = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
            return;

        using var bitmap = new RenderTargetBitmap(pixelSize);
        bitmap.Render(this);

        var picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var filename = Path.Combine(picturesPath, $"AVATrek_{timestamp}.png");

        bitmap.Save(filename, PngBitmapEncoderOptions.Default);
    }

    private void GameLoop(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        double deltaTime = (now - _lastUpdate).TotalSeconds;
        _lastUpdate = now;

        deltaTime = Math.Min(deltaTime, 0.1);

        // Poll controller state
        _engine.Input.Update();

        // Handle controller pause/resume (with edge detection)
        HandleControllerPause();

        _engine.Update(deltaTime);
        UpdateSpinnerCursor();
        UpdateViewer(deltaTime);
        _engine.Input.PostUpdate();
        InvalidateVisual();
    }

    private void HandleControllerPause()
    {
        // Start button toggles pause (edge detection)
        bool pausePressed = _engine.Input.IsPausePressed;
        if (pausePressed && !_wasPausePressed)
        {
            if (_engine.State == GameState.Playing)
                _engine.Pause();
            else if (_engine.State == GameState.Paused)
                _engine.Resume();
        }
        _wasPausePressed = pausePressed;

        // Back button exits when paused (edge detection)
        bool backPressed = _engine.Input.IsBackPressed;
        if (backPressed && !_wasBackPressed && _engine.State == GameState.Paused)
        {
            var window = TopLevel.GetTopLevel(this) as Window;
            window?.Close();
        }
        _wasBackPressed = backPressed;
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Black, new Rect(0, 0, Bounds.Width, Bounds.Height));

        // Fit the fixed 4:3 screen to the window, centred, with black bars on the long sides.
        double scale = Math.Min(Bounds.Width / ScreenWidth, Bounds.Height / ScreenHeight);
        if (scale <= 0) return;
        double offsetX = (Bounds.Width - ScreenWidth * scale) / 2;
        double offsetY = (Bounds.Height - ScreenHeight * scale) / 2;

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
        using (context.PushClip(new Rect(0, 0, ScreenWidth, ScreenHeight)))
        {
            switch (_engine.State)
            {
                case GameState.StartScreen:
                    DrawTitleScreen(context, withText: true);
                    break;

                case GameState.HighScores:
                    DrawTitleScreen(context, withText: false);
                    DrawHighScoreTable(context);
                    break;

                default:
                    DrawBridge(context);
                    break;
            }
        }
    }

    /// <summary>The playing screen: status panel, viewer, scanner, and whatever is over them.</summary>
    private void DrawBridge(DrawingContext context)
    {
        // A hit shakes the whole bridge.
        var shake = new Vector();
        if (_engine.ShakeTime > 0)
        {
            double amount = Math.Min(1, _engine.ShakeTime * 3) * 6;
            shake = new Vector((_random.NextDouble() - 0.5) * amount, (_random.NextDouble() - 0.5) * amount);
        }

        using (context.PushTransform(Matrix.CreateTranslation(shake.X, shake.Y)))
        {
            DrawStatusPanel(context);
            DrawViewer(context);
            DrawScanner(context);
        }

        switch (_engine.State)
        {
            case GameState.Paused:
                DrawPanel(context, new Rect(262, 300, 500, 150));
                DrawCentredText(context, "PAUSED", 46, 320, Colors.White);
                DrawCentredText(context, "ESC to quit, any other key to resume", 18, 390, TextColor);
                break;

            case GameState.GameOver:
                DrawPanel(context, new Rect(222, 300, 580, _engine.IsEntryPending ? 170 : 200));
                DrawCentredText(context, "GAME OVER", 52, 315, AlertRed);
                DrawCentredText(context, "SIMULATION TERMINATED", 18, 385, TextColor);
                if (_engine.IsEntryPending)
                    DrawCentredText(context, NewEntryHeadline, 22, 420, Gold);
                else if (Blink(2))
                    DrawCentredText(context, "Press SPACE or X to play again", 22, 440, Colors.White);
                break;

            case GameState.EnterInitials:
                DrawInitialsEntry(context);
                break;

            case GameState.Attract:
                if (Blink(2))
                    DrawCentredText(context, "Press SPACE or X to play", 22, ScreenHeight - 40, Colors.White);
                break;
        }
    }
}
