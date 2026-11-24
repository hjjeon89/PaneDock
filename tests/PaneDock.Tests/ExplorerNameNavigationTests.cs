using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.App.Controls;
using PaneDock.Core.Models;

[TestClass]
public sealed class ExplorerNameNavigationTests
{
    [TestMethod]
    public void OnlyEnglishLettersWithOptionalShiftInFileListAreAccepted()
    {
        for (var key = Key.A; key <= Key.Z; key++)
        {
            foreach (var modifiers in new[] { ModifierKeys.None, ModifierKeys.Shift })
            {
                Assert.IsTrue(ExplorerNameNavigation.TryGetLetter(key, modifiers,
                    true, false, out var letter));
                Assert.AreEqual((char)('A' + (int)key - (int)Key.A), letter);
            }
        }
        foreach (var modifiers in new[] { ModifierKeys.Control, ModifierKeys.Alt,
                     ModifierKeys.Windows, ModifierKeys.Control | ModifierKeys.Shift })
            Assert.IsFalse(ExplorerNameNavigation.TryGetLetter(Key.A, modifiers, true, false, out _));
        Assert.IsFalse(ExplorerNameNavigation.TryGetLetter(Key.A, ModifierKeys.Shift, false, false, out _));
        Assert.IsFalse(ExplorerNameNavigation.TryGetLetter(Key.A, ModifierKeys.None, true, true, out _));
        Assert.IsFalse(ExplorerNameNavigation.TryGetLetter(Key.Up, ModifierKeys.None, true, false, out _));
        Assert.IsFalse(ExplorerNameNavigation.TryGetLetter(Key.Delete, ModifierKeys.Shift, true, false, out _));
    }

    [TestMethod]
    public void PrefixRefinesSelectionAcrossFoldersAndFilesIgnoringCase()
    {
        var entries = new[] { Entry("Backup", true), Entry("Data", true),
            Entry("config.json"), Entry("Report.csv") };
        var navigation = new ExplorerNameNavigation();
        Assert.AreEqual(0, navigation.FindNext(entries, -1, 'b', 0));
        Assert.AreEqual(0, navigation.FindNext(entries, 0, 'A', 100));
        Assert.AreEqual(0, navigation.FindNext(entries, 0, 'C', 200));
        Assert.AreEqual(-1, navigation.FindNext(entries, 0, 'Z', 300));
        Assert.AreEqual(2, navigation.FindNext(entries, 0, 'C', 1500));
        Assert.AreEqual(2, navigation.FindNext(entries, 2, 'o', 1600));
        navigation.Reset();
        Assert.AreEqual(1, navigation.FindNext(entries, 2, 'D', 1700));
    }

    [TestMethod]
    public void RepeatedLetterCyclesAndWrapsButSupportsDoubleLetterPrefixes()
    {
        var entries = new[] { Entry("Backup", true), Entry("BMS.csv"),
            Entry("Build.txt"), Entry("AA-data.txt"), Entry("Apple.txt") };
        var navigation = new ExplorerNameNavigation();
        Assert.AreEqual(0, navigation.FindNext(entries, -1, 'B', 0));
        Assert.AreEqual(1, navigation.FindNext(entries, 0, 'B', 100));
        Assert.AreEqual(2, navigation.FindNext(entries, 1, 'B', 200));
        Assert.AreEqual(0, navigation.FindNext(entries, 2, 'B', 300));
        navigation.Reset();
        Assert.AreEqual(4, navigation.FindNext(entries, 3, 'A', 400));
        Assert.AreEqual(3, navigation.FindNext(entries, 4, 'A', 500));
        Assert.AreEqual(-1, navigation.FindNext(entries, 3, 'D', 600),
            "An unmatched prefix does not select a different file.");
    }

    [TestMethod]
    public void LargeListSearchAndTimeoutUseOnlyCurrentItems()
    {
        var entries = Enumerable.Range(0, 100000).Select(i => Entry($"file-{i:D5}.txt"))
            .Append(Entry("Z-last.txt")).ToArray();
        var navigation = new ExplorerNameNavigation();
        Assert.AreEqual(100000, navigation.FindNext(entries, 0, 'z', 0));
        Assert.AreEqual(-1, navigation.FindNext(Array.Empty<ExplorerEntry>(), -1, 'A', 100));
        Assert.AreEqual(0, navigation.FindNext(entries, -1, 'F', 200));
        Assert.AreEqual(1, navigation.FindNext(entries, 0, 'F', 1200));
    }

    [TestMethod]
    public void WpfGridSelectionAndCurrentRowFollowPrefixWithoutChangingFilter()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var entries = new[] { Entry("Backup", true), Entry("Data", true),
                    Entry("config.json"), Entry("Hidden.csv"), Entry("Report.csv") };
                var view = CollectionViewSource.GetDefaultView(entries);
                view.Filter = value => ((ExplorerEntry)value).Name != "Hidden.csv";
                var grid = new DataGrid
                {
                    AutoGenerateColumns = false, SelectionMode = DataGridSelectionMode.Extended,
                    IsReadOnly = true, ItemsSource = view
                };
                grid.Columns.Add(new DataGridTextColumn { Binding = new Binding("Name") });
                grid.SelectedItems.Add(entries[0]);
                grid.SelectedItems.Add(entries[1]);
                var navigation = new ExplorerNameNavigation();
                navigation.SelectNext(grid, 'C', 0);
                Assert.AreEqual(1, grid.SelectedItems.Count);
                Assert.AreEqual(entries[2], grid.SelectedItem);
                Assert.AreEqual(entries[2], grid.CurrentCell.Item);
                navigation.SelectNext(grid, 'O', 100);
                Assert.AreEqual(entries[2], grid.SelectedItem);
                navigation.SelectNext(grid, 'H', 1200);
                Assert.AreEqual(entries[2], grid.SelectedItem);
                Assert.AreEqual(4, grid.Items.Count);
                navigation.SelectNext(grid, 'R', 2300);
                Assert.AreEqual(entries[4], grid.SelectedItem);
                Assert.AreEqual(entries[4], grid.CurrentCell.Item);
                grid.ItemsSource = null;
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "WPF selection test timed out.");
        if (error is not null)
            throw new AssertFailedException("WPF name navigation failed.", error);
    }

    private static ExplorerEntry Entry(string name, bool directory = false) =>
        new(name, name, directory ? "" : ".txt", null, DateTime.MinValue, directory);
}
