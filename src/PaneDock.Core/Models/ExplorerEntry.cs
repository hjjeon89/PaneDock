using System.Globalization;

namespace PaneDock.Core.Models;

public sealed record ExplorerEntry(
    string FullPath,
    string Name,
    string Extension,
    long? Size,
    DateTime ModifiedAt,
    bool IsDirectory,
    bool IsHidden = false,
    DateTime CreatedAt = default)
{
    public string Kind => IsDirectory ? "폴더" : string.IsNullOrEmpty(Extension) ? "파일" : Extension.TrimStart('.').ToUpperInvariant() + " 파일";

    public string DisplaySize
    {
        get
        {
            if (Size is null)
                return "—";

            var value = (double)Size.Value;
            var units = new[] { "B", "KB", "MB", "GB", "TB", "PB" };
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0
                ? $"{value.ToString("0", CultureInfo.InvariantCulture)} {units[unit]}"
                : $"{value.ToString("0.#", CultureInfo.InvariantCulture)} {units[unit]}";
        }
    }

    public string DisplayModifiedAt => ModifiedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
    public string DisplayCreatedAt => CreatedAt == default ? "—" : CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
}
