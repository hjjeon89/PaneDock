namespace PaneDock.Core.Services;

public sealed class DriveChangeDetector
{
    private string? _last;

    public bool Update(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var current = string.Join("|", names
            .Select(path => path.TrimEnd(Path.DirectorySeparatorChar))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        var changed = _last is not null && !string.Equals(_last, current,
            StringComparison.OrdinalIgnoreCase);
        _last = current;
        return changed;
    }
}
