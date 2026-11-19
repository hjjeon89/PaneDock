using PaneDock.Core.Models;

namespace PaneDock.Core.Services;

/// <summary>Commands accepted by the path entry box, separate from folder paths.</summary>
public static class ExplorerAddressCommand
{
    public static bool IsCommandPrompt(string? value) =>
        string.Equals(value?.Trim(), "cmd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value?.Trim(), "cmd.exe", StringComparison.OrdinalIgnoreCase);

    public static string GetWorkingDirectory(string? currentPath, string userProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfile);
        return string.IsNullOrWhiteSpace(currentPath) || ExplorerLocations.IsThisPc(currentPath)
            ? userProfile : currentPath;
    }
}
