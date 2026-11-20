using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PaneDock.Core.Models;

namespace PaneDock.Core.Services;

/// <summary>
/// Queries voidtools Everything's already-maintained filename index via its
/// official 1.4-compatible IPC SDK. This does NOT build a second index.
/// The native SDK DLL is a small MIT-licensed IPC client, not Everything itself.
/// State in the SDK is process-wide, so all queries must hold the same lock.
/// </summary>
public sealed class EverythingIndexedSearchService
{
    private static readonly object QueryGate = new();
    private const uint RequestFlags = 0x01 | 0x02 | 0x10 | 0x40;

    public bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess
                || !File.Exists(Path.Combine(AppContext.BaseDirectory, "Everything64.dll")))
                return false;
            try
            {
                // Do not repeatedly perform a full filesystem scan as the user types
                // when the Everything background process is unavailable.
                return Process.GetProcessesByName("Everything").Length > 0;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Returns null if the Everything IPC endpoint/DLL is unavailable.
    /// When available, even zero matches are a complete and valid index result.
    /// </summary>
    public Task<FileSearchSummary?> TrySearchAsync(
        string query, string? folder, bool recursive, int maxResults,
        CancellationToken token) =>
        Task.Run(() => Search(query, folder, recursive, maxResults, token), token);

    private FileSearchSummary? Search(
        string query, string? folder, bool recursive, int maxResults, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsAvailable) return null;
        if (string.IsNullOrWhiteSpace(query)) return null;
        if (maxResults is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(maxResults));

        // The SDK uses global mutable query/result state. No other Everything
        // SDK call may run until the current results have been copied.
        lock (QueryGate)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                Native.Reset();
                Native.SetSearch(BuildQuery(query.Trim(), folder, recursive));
                Native.SetMatchPath(false);
                Native.SetMatchCase(false);
                Native.SetRegex(false);
                Native.SetMax((uint)maxResults);
                Native.SetOffset(0);
                Native.SetRequestFlags(RequestFlags);
                if (!Native.Query(true))
                    return null;

                var count = (int)Math.Min(Native.GetNumResults(), (uint)maxResults);
                var total = Native.GetTotResults();
                var list = new List<FileSearchResult>(count);
                for (uint index = 0; index < count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var needed = Native.GetResultFullPathLength(index, IntPtr.Zero, 0);
                    if (needed is 0 or > 32767) continue;
                    var buffer = new StringBuilder((int)needed + 1);
                    Native.GetResultFullPath(index, buffer, (uint)buffer.Capacity);
                    var path = buffer.ToString();
                    var parent = Path.GetDirectoryName(path);
                    if (string.IsNullOrWhiteSpace(parent)) continue;

                    var isFolder = Native.IsFolderResult(index);
                    long? size = null;
                    if (!isFolder && Native.GetResultSize(index, out var fileSize))
                        size = fileSize;
                    var modified = DateTime.MinValue;
                    if (Native.GetResultDateModified(index, out var fileTime)
                        && fileTime > 0)
                    {
                        try { modified = DateTime.FromFileTimeUtc(fileTime).ToLocalTime(); }
                        catch (ArgumentOutOfRangeException) { }
                    }

                    list.Add(new FileSearchResult(Path.GetFileName(path), path,
                        parent, isFolder, size, modified));
                }

                return new FileSearchSummary(list.Count, 0, 0,
                    total > maxResults, list, (int)Math.Min(total, int.MaxValue));
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
                                       or BadImageFormatException)
            {
                return null;
            }
            finally
            {
                try { Native.Reset(); }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
                                           or BadImageFormatException) { }
            }
        }
    }

    /// <summary>
    /// A quoted directory with a trailing separator restricts the Everything
    /// index to descendants. Non-recursive search uses the parent: operator.
    /// </summary>
    public static string BuildQuery(string query, string? folder, bool recursive)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return query.Trim();
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var scopedQuery = "<" + query.Trim() + ">";
        if (!recursive)
            return scopedQuery + " parent:\"" + normalized.Replace("\"", "") + "\"";

        var scoped = Path.EndsInDirectorySeparator(normalized)
            ? normalized : normalized + Path.DirectorySeparatorChar;
        return scopedQuery + " \"" + scoped.Replace("\"", "") + "\"";
    }

    private static class Native
    {
        [DllImport("Everything64.dll", EntryPoint = "Everything_Reset")]
        internal static extern void Reset();

        [DllImport("Everything64.dll", EntryPoint = "Everything_SetSearchW", CharSet = CharSet.Unicode)]
        internal static extern void SetSearch([MarshalAs(UnmanagedType.LPWStr)] string value);

        [DllImport("Everything64.dll", EntryPoint = "Everything_SetMatchPath")]
        internal static extern void SetMatchPath([MarshalAs(UnmanagedType.Bool)] bool value);

        [DllImport("Everything64.dll", EntryPoint = "Everything_SetMatchCase")]
        internal static extern void SetMatchCase([MarshalAs(UnmanagedType.Bool)] bool value);

        [DllImport("Everything64.dll", EntryPoint = "Everything_SetRegex")]
        internal static extern void SetRegex([MarshalAs(UnmanagedType.Bool)] bool value);

        [DllImport("Everything64.dll", EntryPoint = "Everything_SetMax")]
        internal static extern void SetMax(uint count);

        [DllImport("Everything64.dll", EntryPoint = "Everything_SetOffset")]
        internal static extern void SetOffset(uint offset);

        [DllImport("Everything64.dll", EntryPoint = "Everything_SetRequestFlags")]
        internal static extern void SetRequestFlags(uint flags);

        [DllImport("Everything64.dll", EntryPoint = "Everything_QueryW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Query([MarshalAs(UnmanagedType.Bool)] bool wait);

        [DllImport("Everything64.dll", EntryPoint = "Everything_GetNumResults")]
        internal static extern uint GetNumResults();

        [DllImport("Everything64.dll", EntryPoint = "Everything_GetTotResults")]
        internal static extern uint GetTotResults();

        [DllImport("Everything64.dll", EntryPoint = "Everything_IsFolderResult")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsFolderResult(uint index);

        [DllImport("Everything64.dll", EntryPoint = "Everything_GetResultFullPathNameW", CharSet = CharSet.Unicode)]
        internal static extern uint GetResultFullPathLength(uint index, IntPtr destination, uint maxCount);

        [DllImport("Everything64.dll", EntryPoint = "Everything_GetResultFullPathNameW", CharSet = CharSet.Unicode)]
        internal static extern uint GetResultFullPath(uint index, StringBuilder destination, uint maxCount);

        [DllImport("Everything64.dll", EntryPoint = "Everything_GetResultSize")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetResultSize(uint index, out long size);

        [DllImport("Everything64.dll", EntryPoint = "Everything_GetResultDateModified")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetResultDateModified(uint index, out long fileTime);
    }
}
