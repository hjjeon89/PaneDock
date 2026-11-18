namespace PaneDock.Core.Services;

public static class WindowsItemName
{
    public static bool IsValid(string name)
    {
        if (name.Length is 0 or > 255 || name[^1] is ' ' or '.'
            || name.Any(character => character < ' ' || "<>:\"/\\|?*".Contains(character)))
            return false;

        var stem = name.Split('.', 2)[0].TrimEnd().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$")
            return false;

        return stem.Length != 4
            || !(stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
            || !"123456789¹²³".Contains(stem[3]);
    }
}
