using System.IO;
using System.Runtime.InteropServices;

namespace PaneDock.App;

internal enum FileCommand { NewFolder, NewFile, Rename, Cut, Copy, Paste, Delete, DeletePermanently }

internal sealed record ShellFileOperationResult(
    int RequestedItems, int CompletedItems, int FailedItems,
    bool WasAborted, bool WasCanceled, string? Error = null, int SkippedItems = 0)
{
    public bool Succeeded => !WasAborted && !WasCanceled && Error is null
        && FailedItems == 0 && SkippedItems == 0 && CompletedItems == RequestedItems;

    public string Describe(FileCommand command)
    {
        var operation = command switch
        {
            FileCommand.Cut => "이동",
            FileCommand.Copy => "복사",
            FileCommand.Delete or FileCommand.DeletePermanently => "삭제",
            FileCommand.Rename => "이름 변경",
            FileCommand.NewFolder => "폴더 생성",
            _ => "파일 작업"
        };
        if (Succeeded)
            return $"{CompletedItems:N0}개 항목 {operation} 완료";

        var remaining = Math.Max(0, RequestedItems - CompletedItems);
        var reason = FailedItems > 0 || Error is not null
            ? "오류 발생"
            : WasCanceled || WasAborted ? "취소 또는 중단됨"
            : SkippedItems > 0 ? "건너뛴 항목 포함" : "일부 항목 미완료";
        return $"{RequestedItems:N0}개 중 {CompletedItems:N0}개 {operation} 완료"
            + $" · {remaining:N0}개 미완료 ({reason})";
    }
}

internal static class ShellFileOperation
{
    private const uint AddUndoRecord = 0x20000000;
    private const uint RecycleOnDelete = 0x00080000;
    private const uint WarnBeforePermanentDelete = 0x4000;
    private static readonly Guid ShellItemId = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    public static async Task<bool> RunAsync(
        nint owner, FileCommand command, string folder, string[] paths, string? name = null)
        => (await RunDetailedAsync(owner, command, folder, paths, name)).Succeeded;

    public static Task<ShellFileOperationResult> RunDetailedAsync(
        nint owner, FileCommand command, string folder, string[] paths, string? name = null,
        CancellationToken cancellation = default, Action<int>? onItemCompleted = null)
    {
        var completion = new TaskCompletionSource<ShellFileOperationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                completion.SetResult(Perform(owner, command, folder, paths, name,
                    cancellation, onItemCompleted));
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

    private static ShellFileOperationResult Perform(nint owner, FileCommand command,
        string folder, string[] paths, string? name,
        CancellationToken cancellation, Action<int>? onItemCompleted)
    {
        var requested = command == FileCommand.NewFolder ? 1 : paths.Length;
        if (cancellation.IsCancellationRequested)
            return new ShellFileOperationResult(requested, 0, 0, false, true);

        var operation = (IFileOperation)new FileOperation();
        var items = new List<IShellItem>();
        var sink = new ProgressSink(cancellation, onItemCompleted);
        uint adviseCookie = 0;
        try
        {
            operation.SetOwnerWindow(owner);
            operation.SetOperationFlags(GetOperationFlags(command));
            operation.Advise(sink, out adviseCookie);

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
                    case FileCommand.DeletePermanently:
                        operation.DeleteItem(item, 0);
                        break;
                }
            }

            Exception? operationError = null;
            var aborted = false;
            try
            {
                operation.PerformOperations();
            }
            catch (Exception ex) when (ex is COMException || IsUserCancellation(ex))
            {
                operationError = ex;
            }
            finally
            {
                // Windows can report success from PerformOperations even if
                // the user canceled partway through the queued items.
                try { operation.GetAnyOperationsAborted(out aborted); }
                catch (COMException) { aborted = true; }
            }

            var completed = Math.Min(requested, sink.Completed);
            var failed = Math.Min(requested - completed, sink.Failed);
            var canceled = cancellation.IsCancellationRequested || sink.WasCanceled
                || operationError is not null && IsUserCancellation(operationError);
            // PerformOperations can return S_OK even when one queued item
            // fails. Retain the first per-item/finish HRESULT so the UI can
            // explain a permission or locked-file failure instead of only
            // showing "items incomplete" in the status bar.
            var error = operationError is not null && !IsUserCancellation(operationError)
                ? operationError.Message
                : sink.FirstErrorHResult is { } hr ? DescribeShellFailure(hr) : null;
            return new ShellFileOperationResult(requested, completed, failed,
                aborted, canceled, error, Math.Min(requested - completed, sink.Skipped));
        }
        finally
        {
            if (adviseCookie != 0)
            {
                try { operation.Unadvise(adviseCookie); }
                catch (COMException) { /* Operation has already finished. */ }
            }
            Marshal.ReleaseComObject(operation);
            foreach (var item in items)
                Marshal.ReleaseComObject(item);
            GC.KeepAlive(sink);
        }
    }

    internal static uint GetOperationFlags(FileCommand command) => command switch
    {
        FileCommand.Delete => AddUndoRecord | RecycleOnDelete | WarnBeforePermanentDelete,
        // Shift+Delete must not request recycling or add a misleading undo record.
        FileCommand.DeletePermanently => WarnBeforePermanentDelete,
        _ => AddUndoRecord
    };

    internal static bool IsUserCancellation(Exception ex) =>
        ex is OperationCanceledException
        || ex is COMException { HResult: unchecked((int)0x800704C7) } // ERROR_CANCELLED
        || ex is COMException { HResult: unchecked((int)0x80004004) } // E_ABORT
        || ex is COMException { HResult: unchecked((int)0x80270000) } // COPYENGINE_E_USER_CANCELLED
        || ex is COMException { HResult: unchecked((int)0x80270001) }; // COPYENGINE_E_CANCELLED

    internal static string DescribeShellFailure(int hr)
    {
        var detail = Marshal.GetExceptionForHR(hr)?.Message;
        return $"{(string.IsNullOrWhiteSpace(detail) ? "Windows 파일 작업 실패" : detail)} (0x{unchecked((uint)hr):X8})";
    }

    // IFileOperationProgressSink receives the HRESULT for each completed
    // item, unlike PerformOperations (which can return S_OK on cancellation).
    [ComVisible(true)]
    [Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, nint item, [MarshalAs(UnmanagedType.LPWStr)] string? name);
        [PreserveSig] int PostRenameItem(uint flags, nint item, [MarshalAs(UnmanagedType.LPWStr)] string? name,
            int result, nint created);
        [PreserveSig] int PreMoveItem(uint flags, nint item, nint folder, [MarshalAs(UnmanagedType.LPWStr)] string? name);
        [PreserveSig] int PostMoveItem(uint flags, nint item, nint folder,
            [MarshalAs(UnmanagedType.LPWStr)] string? name, int result, nint created);
        [PreserveSig] int PreCopyItem(uint flags, nint item, nint folder, [MarshalAs(UnmanagedType.LPWStr)] string? name);
        [PreserveSig] int PostCopyItem(uint flags, nint item, nint folder,
            [MarshalAs(UnmanagedType.LPWStr)] string? name, int result, nint created);
        [PreserveSig] int PreDeleteItem(uint flags, nint item);
        [PreserveSig] int PostDeleteItem(uint flags, nint item, int result, nint created);
        [PreserveSig] int PreNewItem(uint flags, nint folder, [MarshalAs(UnmanagedType.LPWStr)] string? name);
        [PreserveSig] int PostNewItem(uint flags, nint folder,
            [MarshalAs(UnmanagedType.LPWStr)] string? name,
            [MarshalAs(UnmanagedType.LPWStr)] string? template, uint attributes, int result, nint created);
        [PreserveSig] int UpdateProgress(uint total, uint completed);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class ProgressSink(CancellationToken cancellation, Action<int>? onItemCompleted)
        : IFileOperationProgressSink
    {
        private const int Cancelled = unchecked((int)0x800704C7);
        private const int CopyEngineUserCancelled = unchecked((int)0x80270000);
        private const int CopyEngineCancelled = unchecked((int)0x80270001);
        internal int Completed { get; private set; }
        internal int Failed { get; private set; }
        internal int Skipped { get; private set; }
        internal bool WasCanceled { get; private set; }
        internal int? FirstErrorHResult { get; private set; }

        private int CheckCancellation()
        {
            if (!cancellation.IsCancellationRequested) return 0;
            WasCanceled = true;
            return Cancelled;
        }

        private int Record(int hr)
        {
            // Shell post-item HRESULTs need not be S_OK. A successful folder
            // transfer/rename often reports COPYENGINE_S_DONT_PROCESS_CHILDREN.
            // USER_IGNORED and PENDING, however, must never count as copied.
            if (IsCompletedItemResult(hr))
            {
                Completed++;
                // This notification is useful for deterministic cancellation
                // regression tests. Never let a client callback abort COM.
                try { onItemCompleted?.Invoke(Completed); }
                catch (Exception) { }
            }
            else if (hr is Cancelled or CopyEngineUserCancelled or CopyEngineCancelled
                     or unchecked((int)0x80004004))
                WasCanceled = true;
            else if (hr < 0)
            {
                Failed++;
                FirstErrorHResult ??= hr;
            }
            else
                Skipped++;
            return 0; // Callbacks must not accidentally cancel pending work.
        }

        internal static bool IsCompletedItemResult(int hr) => hr is 0
            or 0x00270001 // COPYENGINE_S_YES
            or 0x00270006 // COPYENGINE_S_MERGE
            or 0x00270008 // COPYENGINE_S_DONT_PROCESS_CHILDREN
            or 0x0027000A // COPYENGINE_S_ALREADY_DONE
            or 0x0027000C // COPYENGINE_S_KEEP_BOTH
            or 0x0027000E; // COPYENGINE_S_COLLISIONRESOLVED

        public int StartOperations() => CheckCancellation();
        public int FinishOperations(int result)
        {
            if (result is Cancelled or CopyEngineUserCancelled or CopyEngineCancelled)
                WasCanceled = true;
            else if (result < 0)
                FirstErrorHResult ??= result;
            return 0;
        }
        public int PreRenameItem(uint flags, nint item, string? name) => CheckCancellation();
        public int PostRenameItem(uint flags, nint item, string? name, int result, nint created) => Record(result);
        public int PreMoveItem(uint flags, nint item, nint folder, string? name) => CheckCancellation();
        public int PostMoveItem(uint flags, nint item, nint folder, string? name, int result, nint created) => Record(result);
        public int PreCopyItem(uint flags, nint item, nint folder, string? name) => CheckCancellation();
        public int PostCopyItem(uint flags, nint item, nint folder, string? name, int result, nint created) => Record(result);
        public int PreDeleteItem(uint flags, nint item) => CheckCancellation();
        public int PostDeleteItem(uint flags, nint item, int result, nint created) => Record(result);
        public int PreNewItem(uint flags, nint folder, string? name) => CheckCancellation();
        public int PostNewItem(uint flags, nint folder, string? name,
            string? template, uint attributes, int result, nint created) => Record(result);
        public int UpdateProgress(uint total, uint completed) => CheckCancellation();
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
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
        void Advise([MarshalAs(UnmanagedType.Interface)] IFileOperationProgressSink sink, out uint cookie);
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
