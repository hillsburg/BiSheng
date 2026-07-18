using System.Collections.ObjectModel;
using BiSheng.Latte.Data.Entities;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiSheng.Latte.ViewModels;

/// <summary>
/// 文件夹树节点：包装 LocalFolder，增加子节点集合、展开状态和重命名状态
/// 用于 TreeView 的层级绑定
/// </summary>
public partial class FolderNode : ObservableObject
{
    /// <summary>节点对应的文件夹数据</summary>
    public LocalFolder Folder { get; }

    /// <summary>子节点集合（支持 TreeView 的 HierarchicalDataTemplate 绑定）
    ///  可包含 FolderNode 和 NoteItemViewModel（归纳模式）</summary>
    public ObservableCollection<object> Children { get; } = new();

    /// <summary>节点是否展开（绑定到 TreeViewItem.IsExpanded）</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>是否正在重命名（控制 TreeView 中 TextBox 显隐）</summary>
    [ObservableProperty]
    private bool _isRenaming;

    /// <summary>重命名输入框的当前文本</summary>
    [ObservableProperty]
    private string _renameText = string.Empty;

    public FolderNode(LocalFolder folder)
    {
        Folder = folder;
    }

    // 转发常用属性以便 XAML 直接绑定
    public Guid Id => Folder.Id;
    public string Name => Folder.Name;
    public Guid? ParentId => Folder.ParentId;
    public bool IsDeleted => Folder.IsDeleted;
    public bool IsFavorite => Folder.IsFavorite;
    public bool IsPinned => Folder.IsPinned;
    public DateTime CreatedAt => Folder.CreatedAt;
    public DateTime UpdatedAt => Folder.UpdatedAt;

    /// <summary>底层 Folder 字段变更后通知 UI 绑定刷新</summary>
    public void NotifyDisplayChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ParentId));
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(UpdatedAt));
    }
}
