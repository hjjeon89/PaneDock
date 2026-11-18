using PaneDock.Core.Models;
using PaneDock.Core.Services;
using System.Collections.Concurrent;

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
}
finally
{
    File.SetAttributes(Path.Combine(temp, "secret.txt"), FileAttributes.Normal);
    Directory.Delete(temp, recursive: true);
}

Console.WriteLine($"PaneDock.Core 기본 검증 통과 ({checks}개 확인)");

sealed class ImmediateProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}
