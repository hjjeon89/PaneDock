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
    DateTime CreatedAt = default,
    bool IsDrive = false)
{
    // Lightweight category glyphs: no file reads, Shell calls or bitmap allocations per row.
    // Native per-file Shell icons can be implemented separately when the icon cache is added.
    public string IconGlyph => IsDrive ? "💽" : IsDirectory ? "📁" : Extension.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp"
            or ".ico" or ".svg" or ".tif" or ".tiff" => "🖼",
        ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".tgz" => "🗜",
        ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" => "♫",
        ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" => "🎞",
        ".pdf" => "📕",
        ".xls" or ".xlsx" or ".csv" => "📊",
        ".cs" or ".xaml" or ".xml" or ".json" or ".py" or ".js"
            or ".ts" or ".html" or ".css" or ".ps1" or ".bat" or ".cmd" => "⌘",
        _ => "📄"
    };

    public string Kind => IsDrive ? "드라이브" : IsDirectory ? "폴더" : string.IsNullOrEmpty(Extension) ? "파일" : Extension.TrimStart('.').ToUpperInvariant() + " 파일";

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

    public string DisplayModifiedAt => ModifiedAt == DateTime.MinValue
        ? "—" : ModifiedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
    public string DisplayCreatedAt => CreatedAt == default ? "—" : CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
}
