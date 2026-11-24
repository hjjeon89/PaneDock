using System.Runtime.InteropServices;
using System.Windows.Interop;
using PaneDock.Core.Models;

namespace PaneDock.App;

/// <summary>Hosts the native classic Shell menu on the WPF STA thread.</summary>
internal static class ShellContextMenuService
{
    private const uint ShellFirst = 1, ShellLast = 0x6fff, CustomFirst = 0x8000;
    private const uint MfString = 0, MfGrayed = 1, MfPopup = 0x10, MfSeparator = 0x800;
    private const uint TpmRightButton = 0x2, TpmReturnCmd = 0x100;
    private const uint CmfCanRename = 0x10, CmfItemMenu = 0x80, CmfExtendedVerbs = 0x100;
    private const uint GcsVerbW = 4, GcsVerbA = 0;
    private const uint CmicUnicode = 0x4000, CmicPtInvoke = 0x20000000;
    private const string ComputerParsingName = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
    private static readonly Guid FolderId = typeof(IShellFolder).GUID;
    private static readonly Guid MenuId = typeof(IContextMenu).GUID;

    internal record struct Command(FileCommand Action, string Text, bool Enabled);
    internal record struct Result(bool Shown, FileCommand? Action = null,
        string? Error = null, bool NativeInvoked = false, bool OpenFolder = false,
        bool PasteIntoSelection = false);

    internal static FileCommand? GetPaneDockAction(string? verb) =>
        verb?.ToLowerInvariant() switch
        {
            "cut" => FileCommand.Cut,
            "copy" => FileCommand.Copy,
            "paste" => FileCommand.Paste,
            "rename" => FileCommand.Rename,
            "delete" => FileCommand.Delete,
            _ => null
        };

    internal static Result Show(HwndSource source, string folder, string[] paths,
        int x, int y, bool extended, IReadOnlyList<Command> commands,
        bool singleFolder)
    {
        var pidls = new List<nint>();
        IShellFolder? desktop = null, parent = null;
        IContextMenu? context = null;
        nint menu = 0, custom = 0;
        var shown = false;
        try
        {
            nint Parse(string path)
            {
                var parsingName = ExplorerLocations.IsThisPc(path) ? ComputerParsingName : path;
                Marshal.ThrowExceptionForHR(SHParseDisplayName(parsingName, 0, out var pidl, 0, out _));
                pidls.Add(pidl);
                return pidl;
            }

            // A selection must belong to the same Shell folder. The relative
            // child PIDLs point into the absolute PIDLs, kept alive until invoke.
            var folderPidl = Parse(folder);
            Marshal.ThrowExceptionForHR(SHGetDesktopFolder(out desktop));
            Marshal.ThrowExceptionForHR(desktop.BindToObject(folderPidl, 0, in FolderId, out parent));
            if (paths.Length == 0)
                Marshal.ThrowExceptionForHR(parent.CreateViewObject(source.Handle, in MenuId, out context));
            else
            {
                var children = new nint[paths.Length];
                for (var i = 0; i < paths.Length; i++)
                {
                    var item = Parse(paths[i]);
                    if (!ILIsParent(folderPidl, item, true))
                        return new(false, Error: "선택 항목의 Windows Shell 상위 폴더가 다릅니다.");
                    children[i] = ILFindLastID(item);
                }
                Marshal.ThrowExceptionForHR(parent.GetUIObjectOf(source.Handle, (uint)children.Length,
                    children, in MenuId, 0, out context));
            }

            menu = CreatePopupMenu();
            if (menu == 0)
                return new(false, Error: "Windows 메뉴를 생성하지 못했습니다.");
            var flags = paths.Length > 0 ? CmfItemMenu | CmfCanRename : 0u;
            if (extended) flags |= CmfExtendedVerbs;
            Marshal.ThrowExceptionForHR(context.QueryContextMenu(menu, 0, ShellFirst, ShellLast, flags));
            if (GetMenuItemCount(menu) <= 0)
                return new(false, Error: "Windows Shell 메뉴 항목이 없습니다.");

            nint paneDockMenu = 0;
            if (commands.Count > 0)
            {
                custom = CreatePopupMenu();
                if (custom == 0)
                    return new(false, Error: "PaneDock 메뉴를 생성하지 못했습니다.");
                for (var i = 0; i < commands.Count; i++)
                    if (!AppendMenu(custom, MfString | (commands[i].Enabled ? 0 : MfGrayed),
                        CustomFirst + (uint)i, commands[i].Text))
                        return new(false, Error: "PaneDock 메뉴 항목을 추가하지 못했습니다.");
                if (!AppendMenu(menu, MfSeparator, 0, null) ||
                    !AppendMenu(menu, MfPopup, (nuint)custom, "PaneDock 기능"))
                    return new(false, Error: "PaneDock 메뉴를 연결하지 못했습니다.");
                paneDockMenu = custom;
                custom = 0; // The root menu now owns this submenu.
            }

            // The native menu can contain owner-drawn and lazily populated
            // extension submenus (e.g. Send To, archive tools).
            var context3 = context as IContextMenu3;
            var context2 = context3 is null ? context as IContextMenu2 : null;
            nint Forward(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
            {
                if (msg is not (0x0117 or 0x002b or 0x002c or 0x0120) ||
                    ((msg is 0x002b or 0x002c) && wParam != 0) ||
                    (paneDockMenu != 0 && (msg == 0x0117 && wParam == paneDockMenu ||
                        msg == 0x0120 && lParam == paneDockMenu)))
                    return 0;
                try
                {
                    if (context3 is not null &&
                        context3.HandleMenuMsg2((uint)msg, wParam, lParam, out var value) == 0)
                    {
                        handled = true;
                        return value;
                    }
                    if (context2 is not null && context2.HandleMenuMsg((uint)msg, wParam, lParam) == 0)
                    {
                        handled = true;
                        return msg is 0x002b or 0x002c ? 1 : 0;
                    }
                }
                catch (COMException) { }
                return 0;
            }

            uint selected;
            HwndSourceHook hook = Forward;
            source.AddHook(hook);
            try
            {
                shown = true;
                selected = TrackPopupMenuEx(menu, TpmReturnCmd | TpmRightButton,
                    x, y, source.Handle, 0);
            }
            finally { source.RemoveHook(hook); }

            if (selected == 0) return new(true); // Escape/outside click.
            if (selected >= CustomFirst && selected - CustomFirst < commands.Count)
            {
                var command = commands[(int)(selected - CustomFirst)];
                return new(true, command.Enabled ? command.Action : null);
            }
            if (selected < ShellFirst || selected > ShellLast) return new(true);

            var offset = selected - ShellFirst;
            var verb = GetVerb(context, offset);
            if (GetPaneDockAction(verb) is { } action)
                return new(true, action, PasteIntoSelection: action == FileCommand.Paste && singleFolder);
            if (singleFolder && string.Equals(verb, "open", StringComparison.OrdinalIgnoreCase))
                return new(true, OpenFolder: true);

            // Numeric verb offsets preserve extension-defined commands, even
            // when GetCommandString does not expose a canonical verb.
            Invoke(context, source.Handle, offset,
                ExplorerLocations.IsThisPc(folder) ? null : folder, x, y);
            return new(true, NativeInvoked: true);
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or
                                   InvalidOperationException or UnauthorizedAccessException)
        {
            return new(shown, Error: ex.Message);
        }
        finally
        {
            if (custom != 0) DestroyMenu(custom);
            if (menu != 0) DestroyMenu(menu);
            Release(context);
            Release(parent);
            Release(desktop);
            foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl);
        }
    }

    private static string? GetVerb(IContextMenu menu, uint offset)
    {
        const int capacity = 512;
        var buffer = Marshal.AllocCoTaskMem(capacity * 2);
        try
        {
            Marshal.WriteInt16(buffer, 0);
            if (menu.GetCommandString(offset, GcsVerbW, 0, buffer, capacity) >= 0)
            {
                Marshal.WriteInt16(buffer, (capacity - 1) * 2, 0);
                var verb = Marshal.PtrToStringUni(buffer);
                if (!string.IsNullOrEmpty(verb)) return verb;
            }
            Marshal.WriteByte(buffer, 0);
            if (menu.GetCommandString(offset, GcsVerbA, 0, buffer, capacity) < 0)
                return null;
            Marshal.WriteByte(buffer, capacity - 1, 0);
            return Marshal.PtrToStringAnsi(buffer);
        }
        catch (COMException) { return null; }
        finally { Marshal.FreeCoTaskMem(buffer); }
    }

    private static void Invoke(IContextMenu menu, nint owner, uint offset,
        string? folder, int x, int y)
    {
        var ansi = folder is null ? 0 : Marshal.StringToCoTaskMemAnsi(folder);
        var unicode = folder is null ? 0 : Marshal.StringToCoTaskMemUni(folder);
        try
        {
            var info = new InvokeInfo
            {
                Size = Marshal.SizeOf<InvokeInfo>(), Mask = CmicUnicode | CmicPtInvoke,
                Owner = owner, Verb = (nint)offset, VerbW = (nint)offset,
                Directory = ansi, DirectoryW = unicode, Show = 1,
                InvokePoint = new NativePoint(x, y)
            };
            Marshal.ThrowExceptionForHR(menu.InvokeCommand(ref info));
        }
        finally
        {
            Marshal.FreeCoTaskMem(ansi);
            Marshal.FreeCoTaskMem(unicode);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.ReleaseComObject(value);
    }

    [StructLayout(LayoutKind.Sequential)]
    private record struct NativePoint(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct InvokeInfo
    {
        public int Size;
        public uint Mask;
        public nint Owner, Verb, Parameters, Directory;
        public int Show;
        public uint HotKey;
        public nint Icon, Title, VerbW, ParametersW, DirectoryW, TitleW;
        public NativePoint InvokePoint;
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        // Unused slots are retained in native vtable order.
        [PreserveSig] int ParseDisplayName();
        [PreserveSig] int EnumObjects();
        [PreserveSig] int BindToObject(nint pidl, nint bindContext, in Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IShellFolder folder);
        [PreserveSig] int BindToStorage();
        [PreserveSig] int CompareIDs();
        [PreserveSig] int CreateViewObject(nint owner, in Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IContextMenu menu);
        [PreserveSig] int GetAttributesOf();
        [PreserveSig] int GetUIObjectOf(nint owner, uint count,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] nint[] items,
            in Guid iid, nint reserved, [MarshalAs(UnmanagedType.Interface)] out IContextMenu menu);
        [PreserveSig] int GetDisplayNameOf();
        [PreserveSig] int SetNameOf();
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeInfo info);
        [PreserveSig] int GetCommandString(nuint offset, uint flags, nint reserved, nint text, uint count);
    }

    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeInfo info);
        [PreserveSig] int GetCommandString(nuint offset, uint flags, nint reserved, nint text, uint count);
        [PreserveSig] int HandleMenuMsg(uint message, nint wParam, nint lParam);
    }

    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeInfo info);
        [PreserveSig] int GetCommandString(nuint offset, uint flags, nint reserved, nint text, uint count);
        [PreserveSig] int HandleMenuMsg(uint message, nint wParam, nint lParam);
        [PreserveSig] int HandleMenuMsg2(uint message, nint wParam, nint lParam, out nint result);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint context, out nint pidl,
        uint attributes, out uint actualAttributes);
    [DllImport("shell32.dll")]
    private static extern int SHGetDesktopFolder([MarshalAs(UnmanagedType.Interface)] out IShellFolder folder);
    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ILIsParent(nint parent, nint child, [MarshalAs(UnmanagedType.Bool)] bool immediate);
    [DllImport("shell32.dll")] private static extern nint ILFindLastID(nint pidl);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, nint parameters);
}
