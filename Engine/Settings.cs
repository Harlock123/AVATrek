using System;
using System.IO;

namespace AVATrek.Engine;

/// <summary>Player preferences kept between games, next to the high-score table.</summary>
public class Settings
{
    public const int MinSensitivity = 1;
    public const int MaxSensitivity = 10;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVATrek", "settings.txt");

    /// <summary>Mouse spinner mode: moving the mouse left and right turns the ship.</summary>
    public bool Spinner { get; set; }

    /// <summary>How far the ship turns for a given mouse movement, 1 to 10.</summary>
    public int SpinnerSensitivity { get; set; } = 5;

    /// <summary>Radians of turn per pixel of mouse movement.</summary>
    public double RadiansPerPixel => 0.0012 * SpinnerSensitivity;

    public static Settings Load()
    {
        var settings = new Settings();
        try
        {
            if (!File.Exists(FilePath)) return settings;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var parts = line.Split('=', 2);
                if (parts.Length != 2) continue;
                string key = parts[0].Trim(), value = parts[1].Trim();
                if (key == "spinner")
                    settings.Spinner = value == "on";
                else if (key == "spinner-sensitivity" && int.TryParse(value, out int s))
                    settings.SpinnerSensitivity = Math.Clamp(s, MinSensitivity, MaxSensitivity);
            }
        }
        catch
        {
            // An unreadable file just means the defaults.
        }
        return settings;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllLines(FilePath, new[]
            {
                $"spinner={(Spinner ? "on" : "off")}",
                $"spinner-sensitivity={SpinnerSensitivity}"
            });
        }
        catch
        {
            // A read-only home folder just means the settings aren't remembered.
        }
    }
}
