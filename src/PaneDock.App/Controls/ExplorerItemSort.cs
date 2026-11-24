using System.ComponentModel;
using PaneDock.Core.Models;

namespace PaneDock.App.Controls;

internal static class ExplorerItemSort
{
    // Keep directories first regardless of the selected column or direction.
    // Drives also have IsDirectory=true, so the This PC list stays sortable.
    public static void Apply(ICollectionView view, string column, bool descending)
    {
        var foldersFirst = new SortDescription(nameof(ExplorerEntry.IsDirectory),
            ListSortDirection.Descending);
        var secondary = new SortDescription(column,
            descending ? ListSortDirection.Descending : ListSortDirection.Ascending);
        var sorts = view.SortDescriptions;
        if (sorts.Count == 2 && sorts[0].Equals(foldersFirst) && sorts[1].Equals(secondary))
            return;

        using (view.DeferRefresh())
        {
            sorts.Clear();
            sorts.Add(foldersFirst);
            sorts.Add(secondary);
        }
    }
}
