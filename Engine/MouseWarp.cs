using System;
using System.Runtime.InteropServices;

namespace AVATrek.Engine;

/// <summary>
/// Moves the mouse pointer, so the spinner can keep pulling it back to the middle of the window and
/// never run out of room at a screen edge. Avalonia has no call for this, so it goes to each platform
/// directly: SetCursorPos on Windows, XWarpPointer on Linux (X11 and XWayland), CGWarpMouseCursorPosition
/// on macOS. Where none of those works it quietly does nothing, and the spinner still turns the ship
/// until the pointer reaches the edge.
/// </summary>
public static class MouseWarp
{
    private static IntPtr _display;
    private static bool _failed;

    /// <summary>Moves the pointer to a point in screen pixels. Returns false if it couldn't.</summary>
    public static bool MoveTo(int x, int y, double scaling)
    {
        if (_failed) return false;
        try
        {
            if (OperatingSystem.IsWindows())
                return SetCursorPos(x, y);

            if (OperatingSystem.IsMacOS())
            {
                // Core Graphics works in points, not pixels.
                CGWarpMouseCursorPosition(new CGPoint { X = x / scaling, Y = y / scaling });
                CGAssociateMouseAndMouseCursorPosition(true);
                return true;
            }

            if (OperatingSystem.IsLinux())
            {
                if (_display == IntPtr.Zero)
                {
                    _display = XOpenDisplay(IntPtr.Zero);
                    if (_display == IntPtr.Zero)
                    {
                        _failed = true;
                        return false;
                    }
                }
                var root = XDefaultRootWindow(_display);
                XWarpPointer(_display, IntPtr.Zero, root, 0, 0, 0, 0, x, y);
                XFlush(_display);
                return true;
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            _failed = true;
        }
        return false;
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern int CGWarpMouseCursorPosition(CGPoint point);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern int CGAssociateMouseAndMouseCursorPosition(bool connected);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XWarpPointer(IntPtr display, IntPtr srcWindow, IntPtr destWindow, int srcX, int srcY,
        uint srcWidth, uint srcHeight, int destX, int destY);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);
}
