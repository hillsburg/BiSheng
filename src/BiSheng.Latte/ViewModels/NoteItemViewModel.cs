using BiSheng.Latte.Data.Entities;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiSheng.Latte.ViewModels;

/// <summary>
/// 笔记列表项视图模型：包装 LocalNote，增加 UI 状态（是否正在重命名）
/// 用于 ListBox 的 ItemTemplate 绑定，支持内联重命名
/// </summary>
public partial class NoteItemViewModel : ObservableObject
{
    /// <summary>底层笔记实体</summary>
    public LocalNote Note { get; }

    /// <summary>是否正在重命名（控制 TextBox 显隐）</summary>
    [ObservableProperty]
    private bool _isRenaming;

    /// <summary>重命名输入框的当前文本</summary>
    [ObservableProperty]
    private string _renameText = string.Empty;

    public NoteItemViewModel(LocalNote note)
    {
        Note = note;
    }

    // ===== 转发常用属性以便 XAML 直接绑定 =====
    public Guid Id => Note.Id;
    public string Title => Note.Title;
    public Guid FolderId => Note.FolderId;
    public bool IsDeleted => Note.IsDeleted;
    public bool IsFavorite => Note.IsFavorite;
    public bool IsPinned => Note.IsPinned;
    public DateTime CreatedAt => Note.CreatedAt;
    public DateTime UpdatedAt => Note.UpdatedAt;

    /// <summary>底层 Note 字段变更后通知 UI 绑定刷新</summary>
    public void NotifyDisplayChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(FolderId));
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(UpdatedAt));
    }
}
