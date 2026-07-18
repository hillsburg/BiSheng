namespace BiSheng.Latte.Services.Navigation;

/// <summary>导航布局模式（并列 / 归纳），供展示协调器读取，避免依赖 MainViewModel</summary>
public interface INavigationLayoutMode
{
    /// <summary>是否为归纳模式（笔记嵌在文件夹树内）</summary>
    bool IsTreeMode { get; set; }
}
