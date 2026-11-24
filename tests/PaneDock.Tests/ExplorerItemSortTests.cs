using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows.Controls;
using System.Windows.Data;
using PaneDock.App.Controls;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

[TestClass]
public sealed class ExplorerItemSortTests
{
    [TestMethod]
    public void FoldersStayFirstForBothDirectionsAndOtherColumns()
    {
        RunOnSta(() =>
        {
            var entries = new BatchObservableCollection<ExplorerEntry>();
            entries.AddRange([
                Entry("a.txt", false, 20, 1),
                Entry("Z folder", true, null, 3),
                Entry("z.txt", false, 10, 4),
                Entry("A folder", true, null, 2)
            ]);
            var view = CollectionViewSource.GetDefaultView(entries);

            ExplorerItemSort.Apply(view, nameof(ExplorerEntry.Name), false);
            AssertNames(view, "A folder", "Z folder", "a.txt", "z.txt");
            ExplorerItemSort.Apply(view, nameof(ExplorerEntry.Name), true);
            AssertNames(view, "Z folder", "A folder", "z.txt", "a.txt");
            ExplorerItemSort.Apply(view, nameof(ExplorerEntry.ModifiedAt), true);
            AssertNames(view, "Z folder", "A folder", "z.txt", "a.txt");
            ExplorerItemSort.Apply(view, nameof(ExplorerEntry.ModifiedAt), false);
            AssertNames(view, "A folder", "Z folder", "a.txt", "z.txt");
            ExplorerItemSort.Apply(view, nameof(ExplorerEntry.Size), false);
            CollectionAssert.AreEqual(new[] { true, true, false, false },
                view.Cast<ExplorerEntry>().Select(e => e.IsDirectory).ToArray());
            CollectionAssert.AreEqual(new[] { "z.txt", "a.txt" },
                view.Cast<ExplorerEntry>().Where(e => !e.IsDirectory).Select(e => e.Name).ToArray());
            ExplorerItemSort.Apply(view, nameof(ExplorerEntry.Size), true);
            CollectionAssert.AreEqual(new[] { true, true, false, false },
                view.Cast<ExplorerEntry>().Select(e => e.IsDirectory).ToArray());
            CollectionAssert.AreEqual(new[] { "a.txt", "z.txt" },
                view.Cast<ExplorerEntry>().Where(e => !e.IsDirectory).Select(e => e.Name).ToArray());
        });
    }

    [TestMethod]
    public void SortingSurvivesDataGridSourceSwitchAndNewFolderInsertion()
    {
        RunOnSta(() =>
        {
            var items = new BatchObservableCollection<ExplorerEntry>();
            items.AddRange([Entry("a.txt", false, 1, 1), Entry("Z folder", true, null, 2)]);
            var view = CollectionViewSource.GetDefaultView(items);
            var other = CollectionViewSource.GetDefaultView(
                new[] { Entry("other.txt", false, 1, 1) });
            var grid = new DataGrid { ItemsSource = view };

            ExplorerItemSort.Apply(view, nameof(ExplorerEntry.Name), true);
            grid.ItemsSource = other;
            grid.ItemsSource = view;
            ExplorerItemSort.Apply(view, nameof(ExplorerEntry.Name), true);
            AssertNames(view, "Z folder", "a.txt");

            items.Add(Entry("A folder", true, null, 3));
            items.AddRange([Entry("z.txt", false, 1, 4)]);
            AssertNames(view, "Z folder", "A folder", "z.txt", "a.txt");
            view.Filter = item => ((ExplorerEntry)item).Name != "Z folder";
            AssertNames(view, "A folder", "z.txt", "a.txt");
            grid.ItemsSource = null;
        });
    }

    private static ExplorerEntry Entry(string name, bool directory, long? size, int day) =>
        new(name, name, directory ? "" : ".txt", size, new DateTime(2026, 11, day), directory);

    private static void AssertNames(System.ComponentModel.ICollectionView view, params string[] expected) =>
        CollectionAssert.AreEqual(expected, view.Cast<ExplorerEntry>().Select(e => e.Name).ToArray());

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "STA sort test timed out.");
        if (error is not null)
            throw new AssertFailedException("WPF sorting check failed.", error);
    }
}
