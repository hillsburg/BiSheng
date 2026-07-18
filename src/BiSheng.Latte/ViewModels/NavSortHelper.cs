using BiSheng.Latte.Data.Entities;

namespace BiSheng.Latte.ViewModels;

/// <summary>导航项排序：置顶 → 收藏 → 名称</summary>
internal static class NavSortHelper
{
    public static int CompareFolder(LocalFolder a, LocalFolder b)
    {
        var pin = b.IsPinned.CompareTo(a.IsPinned);
        if (pin != 0) return pin;
        var fav = b.IsFavorite.CompareTo(a.IsFavorite);
        if (fav != 0) return fav;
        return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
    }

    public static int CompareNote(LocalNote a, LocalNote b)
    {
        var pin = b.IsPinned.CompareTo(a.IsPinned);
        if (pin != 0) return pin;
        var fav = b.IsFavorite.CompareTo(a.IsFavorite);
        if (fav != 0) return fav;
        return b.UpdatedAt.CompareTo(a.UpdatedAt);
    }

    public static int CompareFavoriteItem(object a, object b)
    {
        var pinA = GetPinned(a);
        var pinB = GetPinned(b);
        var pin = pinB.CompareTo(pinA);
        if (pin != 0) return pin;

        var nameA = GetDisplayName(a);
        var nameB = GetDisplayName(b);
        return string.Compare(nameA, nameB, StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool GetPinned(object item) => item switch
    {
        FolderNode fn => fn.Folder.IsPinned,
        NoteItemViewModel ni => ni.Note.IsPinned,
        _ => false
    };

    private static string GetDisplayName(object item) => item switch
    {
        FolderNode fn => fn.Name,
        NoteItemViewModel ni => ni.Title,
        _ => string.Empty
    };
}
