using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaneDock.Core.Models;

namespace PaneDock.App.Controls;

/// <summary>
/// Gets native Windows-associated 16px icons, cached by file extension.
/// SHGFI_USEFILEATTRIBUTES avoids touching each file on a slow network path.
/// </summary>
public sealed class ShellIconConverter : IValueConverter
{
    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, ImageSource?> Cache =
        new(StringComparer.OrdinalIgnoreCase);
    private const int MaxCachedIcons = 256;
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeNormal = 0x80;
    private const uint Icon = 0x100;
    private const uint SmallIcon = 0x1;
    private const uint UseFileAttributes = 0x10;

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ExplorerEntry entry || !OperatingSystem.IsWindows())
            return null;

        var extension = entry.Extension;
        var key = entry.IsDrive ? "drive:" + entry.FullPath
            : entry.IsDirectory ? "folder" : "type:" + extension;
        lock (CacheGate)
        {
            if (Cache.TryGetValue(key, out var cached))
                return cached;
            if (Cache.Count >= MaxCachedIcons)
                Cache.Clear();

            var image = GetSystemIcon(entry);
            Cache[key] = image;
            return image;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter,
        CultureInfo culture) => Binding.DoNothing;

    private static ImageSource? GetSystemIcon(ExplorerEntry entry)
    {
        var iconInfo = new ShellFileInfo();
        var folder = entry.IsDirectory || entry.IsDrive;
        var name = entry.IsDrive ? entry.FullPath
            : folder ? "Folder" : "sample" + entry.Extension;
        var flags = Icon | SmallIcon | (entry.IsDrive ? 0 : UseFileAttributes);
        try
        {
            if (SHGetFileInfo(name, folder ? FileAttributeDirectory : FileAttributeNormal,
                    ref iconInfo, (uint)Marshal.SizeOf<ShellFileInfo>(), flags) == 0
                || iconInfo.IconHandle == 0)
                return null;

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    iconInfo.IconHandle, Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(16, 16));
                source.Freeze();
                return source;
            }
            finally { DestroyIcon(iconInfo.IconHandle); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
                                   or ArgumentException or COMException or InvalidOperationException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint IconHandle;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
    private static extern nint SHGetFileInfo(string path, uint attributes,
        ref ShellFileInfo info, uint size, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}
