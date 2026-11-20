using System.IO;
using System.Runtime.InteropServices;

namespace PaneDock.App;

internal enum FileCommand { NewFolder, NewFile, Rename, Cut, Copy, Paste, Delete }

internal static class ShellFileOperation
{
    private const uint AddUndoRecord = 0x20000000;
    private const uint RecycleOnDelete = 0x00080000;
    private const uint WarnBeforePermanentDelete = 0x4000;
    private static readonly Guid ShellItemId = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    public static Task<bool> RunAsync(
        nint owner, FileCommand command, string folder, string[] paths, string? name = null)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                completion.SetResult(Perform(owner, command, folder, paths, name));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return completion.Task;
    }

    private static bool Perform(nint owner, FileCommand command, string folder, string[] paths, string? name)
    {
        var operation = (IFileOperation)new FileOperation();
        var items = new List<IShellItem>();
        try
        {
            operation.SetOwnerWindow(owner);
            operation.SetOperationFlags(AddUndoRecord
                | (command == FileCommand.Delete ? RecycleOnDelete | WarnBeforePermanentDelete : 0));

            IShellItem GetItem(string path)
            {
                if (!Path.IsPathFullyQualified(path))
                    throw new IOException($"절대 경로가 필요합니다. {path}");
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, in ShellItemId, out var item));
                items.Add(item);
                return item;
            }

            var destination = command is FileCommand.Copy or FileCommand.Cut or FileCommand.NewFolder
                ? GetItem(folder) : null;

            if (command == FileCommand.NewFolder)
                operation.NewItem(destination!, (uint)FileAttributes.Directory, name!, null, 0);

            foreach (var path in paths)
            {
                var item = GetItem(path);
                switch (command)
                {
                    case FileCommand.Rename:
                        operation.RenameItem(item, name!, 0);
                        break;
                    case FileCommand.Copy:
                        operation.CopyItem(item, destination!, null, 0);
                        break;
                    case FileCommand.Cut:
                        operation.MoveItem(item, destination!, null, 0);
                        break;
                    case FileCommand.Delete:
                        operation.DeleteItem(item, 0);
                        break;
                }
            }

            operation.PerformOperations();
            operation.GetAnyOperationsAborted(out var aborted);
            return !aborted;
        }
        finally
        {
            Marshal.ReleaseComObject(operation);
            foreach (var item in items)
                Marshal.ReleaseComObject(item);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(
        string path, nint bindContext, in Guid id, out IShellItem item);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem { }

    [ComImport, Guid("3ad05575-8857-4850-9277-11b85bdb8e09")]
    private class FileOperation { }

    // The declarations follow the Windows SDK vtable order.
    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(nint sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(nint dialog);
        void SetProperties(nint properties);
        void SetOwnerWindow(nint owner);
        void ApplyPropertiesToItem(nint item);
        void ApplyPropertiesToItems(nint items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, nint sink);
        void RenameItems(nint items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string? name, nint sink);
        void MoveItems(nint items, nint folder);
        void CopyItem(IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string? name, nint sink);
        void CopyItems(nint items, nint folder);
        void DeleteItem(IShellItem item, nint sink);
        void DeleteItems(nint items);
        void NewItem(IShellItem folder, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? template, nint sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
}
