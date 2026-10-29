namespace Somning.OverlayPreview;

internal enum Desk { Wallpaper, Document, Code, Red }

/// <summary>Procedural fake desktops (virtual 1920x1080 screen, taskbar below y=1040) used as backdrops for headless previews.</summary>
internal static class FakeDesktops
{
    public const int ScreenWidth = 1920;
    public const int ScreenHeight = 1080;
    public const int WorkBottom = 1040;

    /// <summary>Pixel 0xAARRGGBB of a fake desktop at virtual screen coordinates.</summary>
    public static uint Pixel(Desk desk, int x, int y)
    {
        if (y >= WorkBottom) return Taskbar(x, y);
        var (r, g, b) = desk switch
        {
            Desk.Wallpaper => Wallpaper(x, y),
            Desk.Document => Document(x, y),
            Desk.Code => Code(x, y),
            _ => Red(x, y)
        };
        return Pack(r, g, b);
    }

    private static uint Pack(float r, float g, float b) =>
        0xFF000000u | ((uint)(Math.Clamp(r, 0f, 1f) * 255f + 0.5f) << 16) | ((uint)(Math.Clamp(g, 0f, 1f) * 255f + 0.5f) << 8) | (uint)(Math.Clamp(b, 0f, 1f) * 255f + 0.5f);

    private static uint Hash(int a, int b)
    {
        var h = (uint)(a * 374761393 + b * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    private static uint Taskbar(int x, int y)
    {
        var icon = (x - 700) / 56;
        var inIcon = x >= 700 && x < 1220 && y >= 1048 && y < 1072 && (x - 700) % 56 < 30;
        if (inIcon) return Pack(0.30f + 0.12f * (Hash(icon, 3) % 5), 0.45f, 0.75f - 0.08f * (Hash(icon, 9) % 4));
        return Pack(0.09f, 0.09f, 0.11f);
    }

    private static (float, float, float) Wallpaper(int x, int y)
    {
        var r = 0.5f + 0.5f * MathF.Sin(x * 0.0042f + y * 0.0021f);
        var g = 0.5f + 0.5f * MathF.Sin(x * 0.0031f + 2f - y * 0.0012f);
        var b = 0.5f + 0.5f * MathF.Sin(y * 0.0040f + x * 0.0011f + 4f);
        var sat = 0.88f;
        r = 0.18f + r * sat * 0.82f;
        g = 0.14f + g * sat * 0.78f;
        b = 0.20f + b * sat * 0.80f;
        // A bright sun and a deep teal pool near the drop so reflection and refraction have strong colors to pick up.
        var sun = Math.Clamp(1f - MathF.Sqrt((x - 820f) * (x - 820f) + (y - 860f) * (y - 860f)) / 90f, 0f, 1f);
        var teal = Math.Clamp(1f - MathF.Sqrt((x - 1130f) * (x - 1130f) + (y - 930f) * (y - 930f)) / 150f, 0f, 1f);
        r = r * (1f - sun) + 1.0f * sun;
        g = g * (1f - sun) + 0.85f * sun;
        b = b * (1f - sun) + 0.45f * sun;
        r *= 1f - 0.6f * teal;
        g = g * (1f - teal) + 0.7f * teal;
        b = b * (1f - teal) + 0.75f * teal;
        return (r, g, b);
    }

    private static (float, float, float) Document(int x, int y)
    {
        if (x < 400) return (0.90f, 0.91f, 0.93f);
        if (y < 780) return (0.97f, 0.97f, 0.98f);
        if (x >= 960 && x < 1100 && y >= 920 && y < 952) return (0.16f, 0.45f, 0.95f);   // a blue button
        var row = (y - 780) / 20;
        var inLine = (y - 780) % 20 is >= 5 and < 13 && x >= 560 && x < 560 + 260 + (int)(Hash(row, 1) % 640);
        if (inLine) return (0.18f, 0.19f, 0.22f);
        return (0.98f, 0.98f, 0.97f);
    }

    private static (float, float, float) Code(int x, int y)
    {
        var row = y / 20;
        var local = y % 20;
        if (local < 5 || local >= 14 || x < 80) return (0.118f, 0.118f, 0.118f);
        var col = (x - 80) / 14;
        var token = (int)(Hash(row, col / 6) % 6);
        var indent = (int)(Hash(row, 77) % 5) * 3;
        var length = 5 + (int)(Hash(row, 5) % 40);
        if (col < indent || col > indent + length || (x - 80) % 14 >= 11) return (0.118f, 0.118f, 0.118f);
        return token switch
        {
            0 => (0.34f, 0.61f, 0.84f),
            1 => (0.86f, 0.86f, 0.67f),
            2 => (0.81f, 0.57f, 0.47f),
            3 => (0.77f, 0.53f, 0.75f),
            4 => (0.42f, 0.60f, 0.33f),
            _ => (0.83f, 0.83f, 0.83f)
        };
    }

    private static (float, float, float) Red(int x, int y)
    {
        if (x is >= 520 and < 760 && y is >= 800 and < 990) return (0.97f, 0.97f, 0.97f);   // a white card on the left
        var t = Math.Clamp((x - 400f) / 1100f, 0f, 1f);
        return (0.88f + 0.10f * t, 0.06f + 0.22f * t, 0.10f);
    }
}
