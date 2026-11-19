using PaneDock.Core.Models;
using PaneDock.Core.Services;
using PaneDock.App;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

var checks = 0;
var expectedCounts = new Dictionary<PaneLayout, int>
{
    [PaneLayout.Single] = 1,
    [PaneLayout.TwoColumns] = 2,
    [PaneLayout.TwoRows] = 2,
    [PaneLayout.ThreeLeft] = 3,
    [PaneLayout.ThreeTop] = 3,
    [PaneLayout.FourGrid] = 4
};

foreach (var (layout, count) in expectedCounts)
{
    if (PaneLayoutInfo.GetPaneCount(layout) != count)
        throw new Exception($"{layout} 패널 개수 오류");
    checks++;
}

var file = new ExplorerEntry("C:\\temp\\test.log", "test.log", ".log", 2048, DateTime.Now, false);
if (file.DisplaySize != "2 KB" || file.Kind != "LOG 파일")
    throw new Exception("파일 항목 표시 형식 오류");
checks++;

void Assert(bool condition, string description)
{
    if (!condition)
        throw new Exception(description);
    checks++;
}

var hidden = file with { IsHidden = true };
Assert(ExplorerItemFilter.IsVisible(file, "", false), "일반 파일이 숨겨졌습니다.");
Assert(ExplorerItemFilter.IsVisible(file, "TEST", false), "대소문자 무시 필터 오류");
Assert(ExplorerItemFilter.IsVisible(file, " log ", false), "부분 문자열 공백 처리 오류");
Assert(!ExplorerItemFilter.IsVisible(file, "missing", false), "불일치 파일이 표시됩니다.");
Assert(!ExplorerItemFilter.IsVisible(hidden, "", false), "숨김 파일 기본 필터 오류");
Assert(ExplorerItemFilter.IsVisible(hidden, "test", true), "숨김 파일 보기 옵션 오류");

Assert(file.IconGlyph == "📄", "일반 파일 아이콘 분류 실패");
Assert((file with { IsDirectory = true }).IconGlyph == "📁", "폴더 아이콘 분류 실패");
Assert((file with { Extension = ".PNG" }).IconGlyph == "🖼", "대문자 이미지 확장자 분류 실패");
Assert((file with { Extension = ".zip" }).IconGlyph == "🗜", "압축 파일 아이콘 분류 실패");
Assert((file with { Extension = ".cs" }).IconGlyph == "⌘", "소스 파일 아이콘 분류 실패");
Assert(ExplorerLocations.IsThisPc(ExplorerLocations.ThisPc), "가상 내 PC 경로 판정 오류");
Assert(!ExplorerLocations.IsThisPc(@"C:\"), "실제 드라이브가 가상 경로로 인식됨");
Assert((file with { IsDrive = true }).Kind == "드라이브", "드라이브 유형 분류 오류");
Assert(ExplorerItemFilter.IsVisible(file with { IsDrive = true }, "does-not-match", false),
    "내 PC에서 기존 파일명 필터 때문에 드라이브가 사라짐");

var breadcrumbRoot = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory));
var breadcrumbPath = Path.Combine(breadcrumbRoot!, "PaneDock", "src", "PaneDock.App");
var breadcrumb = PathBreadcrumb.Build(breadcrumbPath);
Assert(breadcrumb.Count == 4, "Breadcrumb 구성 요소 수 오류");
Assert(breadcrumb[0].FullPath == breadcrumbRoot, "Breadcrumb 드라이브 루트 오류");
Assert(breadcrumb[1].Label == "PaneDock" && breadcrumb[2].Label == "src",
    "Breadcrumb 이름/순서 오류");
Assert(breadcrumb[^1].FullPath == breadcrumbPath && breadcrumb[^1].Label == "PaneDock.App",
    "Breadcrumb 목적지 경로 오류");

var unc = PathBreadcrumb.Build(@"\\server\share\reports\2026");
Assert(unc.Count == 3 && unc[0].FullPath.StartsWith(@"\\server\share", StringComparison.OrdinalIgnoreCase),
    "UNC 공유 경로의 Breadcrumb 생성 오류");

Assert(WindowsItemName.IsValid("새 폴더") && WindowsItemName.IsValid("report.txt"), "정상 이름 거부");
Assert(new[] { "", "..", @"nested\file", "report:stream", "report.", "report ", "NUL.txt", "COM¹.log" }
    .All(name => !WindowsItemName.IsValid(name)), "잘못된 Windows 이름 허용");

var temp = Path.Combine(Path.GetTempPath(), "PaneDock-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    File.WriteAllText(Path.Combine(temp, "sample.txt"), "sample");
    Directory.CreateDirectory(Path.Combine(temp, "subfolder"));
    var hiddenPath = Path.Combine(temp, "secret.txt");
    File.WriteAllText(hiddenPath, "hidden");
    File.SetAttributes(hiddenPath, File.GetAttributes(hiddenPath) | FileAttributes.Hidden);

    var entries = new ConcurrentBag<ExplorerEntry>();
    var reporter = new ImmediateProgress<IReadOnlyList<ExplorerEntry>>(
        batch => { foreach (var item in batch) entries.Add(item); });
    var service = new DirectoryListingService();
    await service.EnumerateAsync(temp, reporter, CancellationToken.None);
    Assert(entries.Count == 3, "디렉터리 열거 개수 오류");
    Assert(entries.Any(e => e.IsDirectory && e.Name == "subfolder"), "하위 폴더 조회 실패");
    Assert(entries.Any(e => e.Name == "sample.txt" && e.Size == 6), "파일 메타데이터 조회 실패");
    Assert(entries.Any(e => e.Name == "secret.txt" && e.IsHidden), "숨김 속성 조회 실패");
    Assert(entries.All(e => e.CreatedAt != default), "파일 생성 날짜 조회 실패");

    var snapshot = await service.GetSnapshotAsync(temp, CancellationToken.None);
    Assert(snapshot.Count == 3 && snapshot.Any(e => e.Name == "sample.txt"),
        "폴더 Snapshot 목록 조회 실패");
    var driveEntries = await service.GetSnapshotAsync(ExplorerLocations.ThisPc, CancellationToken.None);
    Assert(driveEntries.Count > 0 && driveEntries.All(e => e.IsDrive && e.IsDirectory
        && Path.IsPathRooted(e.FullPath)), "내 PC 목록에서 드라이브 조회 실패");

    var hiddenFolder = Directory.CreateDirectory(Path.Combine(temp, "Hidden Folder"));
    hiddenFolder.Attributes |= FileAttributes.Hidden;
    var visibleFolders = await service.GetFoldersAsync(temp, showHidden: false);
    Assert(visibleFolders.Select(folder => folder.Name).SequenceEqual(["subfolder"]), "트리 기본 폴더 조회 오류");
    var allFolders = await service.GetFoldersAsync(temp, showHidden: true);
    Assert(allFolders.Select(folder => folder.Name).SequenceEqual(["Hidden Folder", "subfolder"]), "트리 숨김 폴더 표시 또는 정렬 오류");

    using var canceled = new CancellationTokenSource();
    canceled.Cancel();
    var canceledAsExpected = false;
    try
    {
        await service.EnumerateAsync(temp, reporter, canceled.Token);
    }
    catch (OperationCanceledException)
    {
        canceledAsExpected = true;
    }
    Assert(canceledAsExpected, "취소된 디렉터리 열거가 정상적으로 끝났습니다.");

    // FileSystemWatcher notifications arrive asynchronously and can be repeated by
    // Windows. Wait on a debounced signal instead of assuming an exact event count.
    using (var watcher = new DirectoryChangeMonitor(temp, TimeSpan.FromMilliseconds(250)))
    {
        Assert(watcher.LastError is null, "변경 감시 시작 실패");

        async Task ExpectChange(Action operation, string description)
        {
            var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged(object? sender, EventArgs e) => seen.TrySetResult();
            watcher.Changed += OnChanged;
            try
            {
                operation();
                await seen.Task.WaitAsync(TimeSpan.FromSeconds(10));
                checks++;
            }
            catch (TimeoutException)
            {
                throw new Exception(description);
            }
            finally
            {
                watcher.Changed -= OnChanged;
            }
        }

        var external = Path.Combine(temp, "external.txt");
        await ExpectChange(() => File.WriteAllText(external, "one"), "외부 파일 생성 감지 실패");
        await ExpectChange(() => File.AppendAllText(external, " two"), "외부 파일 수정 감지 실패");
        var renamed = Path.Combine(temp, "renamed-external.txt");
        await ExpectChange(() => File.Move(external, renamed), "외부 파일 이름 변경 감지 실패");
        await ExpectChange(() => File.Delete(renamed), "외부 파일 삭제 감지 실패");
        await ExpectChange(() => Directory.CreateDirectory(Path.Combine(temp, "new-external-folder")),
            "외부 폴더 추가 감지 실패");

        var beforeDispose = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref beforeDispose);
        watcher.Dispose();
        File.WriteAllText(Path.Combine(temp, "after-dispose.txt"), "not observed");
        await Task.Delay(600);
        Assert(beforeDispose == 0, "감시 종료 이후 변경 알림 발생");
    }

    // A missing directory must not permanently disable monitoring: a later
    // creation should reconnect and trigger a complete rescan.
    var reconnectPath = Path.Combine(temp, "reconnect-test");
    using (var recovery = new DirectoryChangeMonitor(reconnectPath, TimeSpan.FromMilliseconds(100)))
    {
        Assert(recovery.LastError is not null, "없는 폴더 감시 오류가 누락됨");
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        recovery.MonitoringRestored += (_, _) => restored.TrySetResult();
        Directory.CreateDirectory(reconnectPath);
        await restored.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert(recovery.LastError is null, "감시 연결 복구 실패");
    }
}
finally
{
    File.SetAttributes(Path.Combine(temp, "secret.txt"), FileAttributes.Normal);
    Directory.Delete(temp, recursive: true);
}

// File operations are exercised only against a freshly created, isolated temporary folder.
// No test file from the user workspace is renamed, moved or deleted.
if (OperatingSystem.IsWindows())
{
    var shellRoot = Path.Combine(Path.GetTempPath(), "PaneDock-ShellTests-" + Guid.NewGuid().ToString("N"));
    var origin = Path.Combine(shellRoot, "origin");
    var destination = Path.Combine(shellRoot, "destination");
    Directory.CreateDirectory(origin);
    Directory.CreateDirectory(destination);
    try
    {
        Assert(await ShellFileOperation.RunAsync(0, FileCommand.NewFolder, origin, [], "New Folder")
            && Directory.Exists(Path.Combine(origin, "New Folder")), "Shell 새 폴더 생성 실패");

        var originalFile = Path.Combine(origin, "report.txt");
        File.WriteAllText(originalFile, "PaneDock temporary regression test");
        var renamedFile = Path.Combine(origin, "renamed.txt");
        Assert(await ShellFileOperation.RunAsync(0, FileCommand.Rename, origin, [originalFile], "renamed.txt")
            && File.Exists(renamedFile) && !File.Exists(originalFile), "Shell 파일명 변경 실패");

        Assert(await ShellFileOperation.RunAsync(0, FileCommand.Copy, destination, [renamedFile])
            && File.Exists(Path.Combine(destination, "renamed.txt"))
            && File.Exists(renamedFile), "Shell 파일 복사 실패");

        var moveSource = Path.Combine(origin, "move.txt");
        File.WriteAllText(moveSource, "isolated test");
        Assert(await ShellFileOperation.RunAsync(0, FileCommand.Cut, destination, [moveSource])
            && File.Exists(Path.Combine(destination, "move.txt"))
            && !File.Exists(moveSource), "Shell 파일 이동 실패");

        var missingSourceFailed = false;
        try
        {
            await ShellFileOperation.RunAsync(0, FileCommand.Copy, destination,
                [Path.Combine(origin, "does-not-exist.txt")]);
        }
        catch (Exception ex) when (ex is COMException or IOException)
        {
            missingSourceFailed = true;
        }
        Assert(missingSourceFailed && !File.Exists(Path.Combine(destination, "does-not-exist.txt")),
            "없는 원본 파일을 잘못 복사 성공 처리");
    }
    finally
    {
        Directory.Delete(shellRoot, recursive: true);
    }
}

// Session persistence never touches the real user's AppData file.
var sessionRoot = Path.Combine(Path.GetTempPath(), "PaneDock-session-tests-" + Guid.NewGuid().ToString("N"));
var sessionFile = Path.Combine(sessionRoot, "workspace.json");
try
{
    var store = new WorkspaceSessionStore(sessionFile);
    Assert(store.Load() is null, "첫 실행 시 세션 파일이 없어도 정상 처리해야 함");

    var data = new WorkspaceSession
    {
        Layout = PaneLayout.FourGrid,
        ActivePaneNumber = 3,
        Panes =
        [
            new PaneSession
            {
                TabPaths = [ExplorerLocations.ThisPc, Path.GetFullPath(sessionRoot)],
                ActiveTabIndex = 1
            },
            new PaneSession { TabPaths = [Path.GetFullPath(Path.GetTempPath())] }
        ]
    };
    store.Save(data);
    Assert(File.Exists(sessionFile), "세션 JSON 저장 실패");
    var restored = store.Load();
    Assert(restored is not null, "세션 JSON 다시 읽기 실패");
    Assert(restored!.Layout == PaneLayout.FourGrid && restored.ActivePaneNumber == 3,
        "패널 구성과 활성 패널 복원 실패");
    Assert(restored.Panes.Count == 4 && restored.Panes[0].TabPaths.Count == 2
        && restored.Panes[0].ActiveTabIndex == 1, "패널별 탭 경로/활성 탭 복원 실패");
    Assert(restored.Panes[0].TabPaths[1] == Path.GetFullPath(sessionRoot),
        "저장한 폴더 경로가 달라짐");
    Assert(restored.Panes[3].TabPaths.SequenceEqual([ExplorerLocations.ThisPc]),
        "저장하지 않은 패널의 기본 위치 오류");
    Assert(Directory.GetFiles(sessionRoot).Length == 1, "원자적 저장 임시 파일이 남아 있음");

    var invalid = WorkspaceSessionStore.Normalize(new WorkspaceSession
    {
        ActivePaneNumber = 4,
        Layout = PaneLayout.Single,
        Panes = [new PaneSession { TabPaths = ["relative/path", ""], ActiveTabIndex = 99 }]
    });
    Assert(invalid.ActivePaneNumber == 1 && invalid.Panes[0].ActiveTabIndex == 1,
        "잘못된 활성 패널 또는 탭 인덱스 정규화 실패");
    Assert(invalid.Panes[0].TabPaths.All(ExplorerLocations.IsThisPc),
        "상대 경로 또는 빈 경로는 내 PC로 복원되어야 함");

    File.WriteAllText(sessionFile, "{invalid json");
    Assert(store.Load() is null, "손상된 JSON에서 안전하게 기본 화면으로 복구하지 못함");
    File.WriteAllText(sessionFile, "{\"Version\":999}");
    Assert(store.Load() is null, "지원하지 않는 저장 형식 버전 처리 실패");
}
finally
{
    if (Directory.Exists(sessionRoot))
        Directory.Delete(sessionRoot, recursive: true);
}

Console.WriteLine($"PaneDock 검증 통과 ({checks}개 확인; Windows Shell 조작은 임시 폴더만 사용)");

sealed class ImmediateProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}
