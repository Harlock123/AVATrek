using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace AVATrek.Engine;

public class InputHandler : IDisposable
{
    private readonly HashSet<Key> _pressedKeys = new();
    private readonly ControllerManager _controller = new();

    // Threshold for analog stick to register as direction
    private const float StickThreshold = 0.3f;
    // Threshold for trigger to register as pressed
    private const float TriggerThreshold = 0.3f;

    // Mouse state
    private bool _leftMousePressed;
    private bool _rightMousePressed;
    private bool _middleMousePressed;
    private double _mouseWheelDelta;

    public void OnKeyDown(Key key)
    {
        _pressedKeys.Add(key);
    }

    public void OnKeyUp(Key key)
    {
        _pressedKeys.Remove(key);
    }

    public bool IsKeyPressed(Key key)
    {
        return _pressedKeys.Contains(key);
    }

    public void OnMouseDown(MouseButton button)
    {
        if (button == MouseButton.Left)
            _leftMousePressed = true;
        else if (button == MouseButton.Right)
            _rightMousePressed = true;
        else if (button == MouseButton.Middle)
            _middleMousePressed = true;
    }

    public void OnMouseUp(MouseButton button)
    {
        if (button == MouseButton.Left)
            _leftMousePressed = false;
        else if (button == MouseButton.Right)
            _rightMousePressed = false;
        else if (button == MouseButton.Middle)
            _middleMousePressed = false;
    }

    public void OnMouseWheel(double delta)
    {
        _mouseWheelDelta += delta;
    }

    public void Update()
    {
        _controller.Update();
    }

    public void PostUpdate()
    {
        // Reset mouse wheel delta after each frame
        _mouseWheelDelta = 0;
    }

    public bool IsLeftMousePressed => _leftMousePressed;
    public bool IsRightMousePressed => _rightMousePressed;
    public double MouseWheelDelta => _mouseWheelDelta;

    public bool IsControllerConnected => _controller.IsConnected;

    // Controller-only menu inputs, for initials entry (the keyboard is handled key by key there).
    public bool PadUp => _controller.DPadUp || _controller.LeftStickY < -0.5f;
    public bool PadDown => _controller.DPadDown || _controller.LeftStickY > 0.5f;
    public bool PadLeft => _controller.DPadLeft || _controller.LeftStickX < -0.5f;
    public bool PadRight => _controller.DPadRight || _controller.LeftStickX > 0.5f;
    public bool PadAccept => _controller.ButtonX || _controller.ButtonB || _controller.ButtonA;
    public bool PadDone => _controller.ButtonStart;

    /// <summary>Any key, mouse button, wheel turn or controller input at all this frame.</summary>
    public bool IsAnyInput =>
        _pressedKeys.Count > 0 ||
        _leftMousePressed || _rightMousePressed || _middleMousePressed || _mouseWheelDelta != 0 ||
        IsTurningLeft || IsTurningRight || IsImpulse || IsPhaser || IsTorpedo || IsWarp ||
        IsPausePressed || IsBackPressed;

    public bool IsTurningLeft =>
        IsKeyPressed(Key.A) ||
        IsKeyPressed(Key.Left) ||
        _controller.DPadLeft ||
        _controller.LeftStickX < -StickThreshold;

    public bool IsTurningRight =>
        IsKeyPressed(Key.D) ||
        IsKeyPressed(Key.Right) ||
        _controller.DPadRight ||
        _controller.LeftStickX > StickThreshold;

    public bool IsImpulse =>
        IsKeyPressed(Key.W) ||
        IsKeyPressed(Key.Up) ||
        _controller.DPadUp ||
        _controller.LeftStickY < -StickThreshold ||  // Up is negative Y
        _controller.RightTrigger > TriggerThreshold ||
        _controller.ButtonA ||
        _rightMousePressed;

    public bool IsPhaser =>
        IsKeyPressed(Key.Space) ||
        _controller.ButtonX ||
        _controller.RightBumper ||
        _leftMousePressed;

    public bool IsTorpedo =>
        IsKeyPressed(Key.F) ||
        IsKeyPressed(Key.Z) ||
        _controller.ButtonB ||
        _controller.LeftBumper ||
        _middleMousePressed;

    public bool IsWarp =>
        IsKeyPressed(Key.LeftShift) ||
        IsKeyPressed(Key.RightShift) ||
        IsKeyPressed(Key.E) ||
        _controller.ButtonY ||
        _controller.LeftTrigger > TriggerThreshold;

    public bool IsPausePressed =>
        _controller.ButtonStart;

    public bool IsBackPressed =>
        _controller.ButtonBack;

    public void Dispose()
    {
        _controller.Dispose();
    }
}
