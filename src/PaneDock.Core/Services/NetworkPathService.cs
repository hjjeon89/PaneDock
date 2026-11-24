using System.IO;

namespace PaneDock.Core.Services;

/// <summary>
/// Recognizes UNC shares and mapped network drives without opening them.
/// An unreachable network path should remain in the tab for reconnection,
/// unlike a missing local directory.
/// </summary>
public static class NetworkPathService
{
    public static bool IsNetworkPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return false;
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return true;
        try
        {
            var root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root)
                && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException
                                   or NotSupportedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool ShouldWaitForReconnect(string path, bool directoryExists) =>
        !directoryExists && IsNetworkPath(path);
}
