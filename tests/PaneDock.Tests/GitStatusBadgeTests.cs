using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.App.Controls;
using PaneDock.Core.Services;

[TestClass]
public sealed class GitStatusBadgeTests
{
    [TestMethod]
    public void UnmanagedOrIgnoredPathsDoNotGetEmptyBadges()
    {
        var root = Path.Combine(Path.GetTempPath(), "PaneDock-git-badge-test");
        var snapshot = GitStatusService.Parse(root,
            "## main\0?? new.txt\0", "tracked.txt\0");
        var converter = new GitStatusBadgeConverter();

        Assert.IsNull(converter.Convert([Path.Combine(root, "ignored.tmp"), false, snapshot],
            typeof(object), null!, CultureInfo.InvariantCulture));
        Assert.IsNull(converter.Convert([Path.Combine(root, "untracked-empty-folder"), true, snapshot],
            typeof(object), null!, CultureInfo.InvariantCulture));
        Assert.IsNull(converter.Convert([Path.Combine(root, "new.txt"), false, null!],
            typeof(object), null!, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void GitUntrackedFilesAndTheirFoldersDisplayQuestionMark()
    {
        var root = Path.Combine(Path.GetTempPath(), "PaneDock-git-badge-test");
        var snapshot = GitStatusService.Parse(root, "## main\0?? new-folder/new.txt\0", "");
        var converter = new GitStatusBadgeConverter();

        var fileBadge = converter.Convert([Path.Combine(root, "new-folder", "new.txt"),
                false, snapshot], typeof(object), null!, CultureInfo.InvariantCulture)
            as GitStatusBadge;
        var folderBadge = converter.Convert([Path.Combine(root, "new-folder"),
                true, snapshot], typeof(object), null!, CultureInfo.InvariantCulture)
            as GitStatusBadge;

        Assert.IsNotNull(fileBadge);
        Assert.IsNotNull(folderBadge);
        Assert.AreEqual("?", fileBadge.Symbol);
        Assert.AreEqual("?", folderBadge.Symbol);
        StringAssert.Contains(fileBadge.Description, "추적되지 않음");
    }
}
