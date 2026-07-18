using BiSheng.Latte.Data;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services;
using BiSheng.Latte.Services.Navigation;
using BiSheng.Latte.Services.Search;
using BiSheng.Latte.Services.Shell;
using BiSheng.Latte.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BiSheng.Latte.DependencyInjection;

/// <summary>BiSheng Latte 客户端依赖注入</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>注册 Latte 应用服务图（单例组合根）</summary>
    public static IServiceCollection AddBiShengLatte(this IServiceCollection services)
    {
        services.AddSingleton<ILocalDbContextFactory, LocalDbContextFactory>();
        services.AddSingleton<Func<LocalDbContext>>(sp =>
            sp.GetRequiredService<ILocalDbContextFactory>().Create);

        services.AddSingleton<AuthService>();
        services.AddSingleton<AppUpdateService>();
        services.AddSingleton<ApiClient>();
        services.AddSingleton<SignalRService>();
        services.AddSingleton(_ => SyncSettings.Load());

        services.AddSingleton<LocalEditJournalService>();
        services.AddSingleton<LocalChangeTracker>();

        services.AddSingleton<INavigationReadModel, NavigationReadModel>();
        services.AddSingleton<INavigationMutationPublisher, NavigationMutationPublisher>();
        services.AddSingleton<INavigationLayoutMode, NavigationLayoutContext>();
        services.AddSingleton<SyncService>();
        services.AddSingleton<ImageStorageService>();
        services.AddSingleton<ImageSyncService>();
        services.AddSingleton<NoteRevisionService>();
        services.AddSingleton<WebView2PdfExportHost>();
        services.AddSingleton<ExportService>();
        services.AddSingleton<TrashService>();
        services.AddSingleton<IDialogNavigationService, DialogNavigationService>();

        services.AddSingleton<INoteContentSearchService, NoteContentSearchService>();
        services.AddSingleton<INavigationFilterState, NavigationFilterState>();
        services.AddSingleton<NavigationViewModel>();
        services.AddSingleton<FolderTreeViewModel>();
        services.AddSingleton<NoteListViewModel>();
        services.AddSingleton<EditorViewModel>();
        services.AddSingleton<IEditorSessionService, EditorSessionService>();
        services.AddSingleton<INavigationStore, NavigationStore>();
        services.AddSingleton<INavigationPresentationCoordinator, NavigationPresentationCoordinator>();
        services.AddSingleton<NavigationFilterBridge>();
        services.AddSingleton<MainViewModel>();

        return services;
    }
}
