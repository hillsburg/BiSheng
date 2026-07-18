using BiSheng.Shared;

namespace BiSheng.Latte.Services.Navigation;

/// <summary>将本地 CRUD / 过滤 / 布局变更发布到读模型</summary>
public sealed class NavigationMutationPublisher : INavigationMutationPublisher
{
    private readonly INavigationReadModel _readModel;

    /// <summary>构造变更发布器</summary>
    public NavigationMutationPublisher(INavigationReadModel readModel)
    {
        _readModel = readModel;
    }

    /// <inheritdoc />
    public void NotifyFolderCreated(Guid folderId, Guid? parentFolderId) =>
        PublishDataChange(new NavigationChange
        {
            EntityType = EntityTypes.Folder,
            EntityId = folderId,
            Action = ChangeActions.Create,
            ParentFolderId = parentFolderId
        });

    /// <inheritdoc />
    public void NotifyFolderUpdated(
        Guid folderId,
        Guid? parentFolderId,
        bool flagsChanged = false,
        bool parentFolderChanged = false) =>
        PublishDataChange(new NavigationChange
        {
            EntityType = EntityTypes.Folder,
            EntityId = folderId,
            Action = ChangeActions.Update,
            ParentFolderId = parentFolderId,
            FlagsChanged = flagsChanged,
            ParentFolderChanged = parentFolderChanged
        });

    /// <inheritdoc />
    public void NotifyFolderDeleted(Guid folderId) =>
        PublishDataChange(new NavigationChange
        {
            EntityType = EntityTypes.Folder,
            EntityId = folderId,
            Action = ChangeActions.Delete
        });

    /// <inheritdoc />
    public void NotifyNoteCreated(Guid noteId, Guid folderId) =>
        PublishDataChange(new NavigationChange
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Create,
            FolderId = folderId
        });

    /// <inheritdoc />
    public void NotifyNoteUpdated(Guid noteId, Guid folderId, bool flagsChanged = false) =>
        PublishDataChange(new NavigationChange
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Update,
            FolderId = folderId,
            FlagsChanged = flagsChanged
        });

    /// <inheritdoc />
    public void NotifyNoteDeleted(Guid noteId, Guid folderId) =>
        PublishDataChange(new NavigationChange
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Delete,
            FolderId = folderId
        });

    /// <inheritdoc />
    public void NotifyChanges(IReadOnlyList<NavigationChange> changes)
    {
        if (changes.Count == 0)
        {
            return;
        }

        if (changes.Count > NavigationRefreshPolicy.MaxIncrementalChanges)
        {
            _readModel.Publish(NavigationProjectionUpdate.FullDataRebuild, waitForPresentation: true);
            return;
        }

        _readModel.Publish(
            NavigationProjectionUpdate.FromDelta(SyncNavigationDelta.FromChanges(changes)),
            waitForPresentation: true);
    }

    /// <inheritdoc />
    public void NotifyFilterChanged() =>
        _readModel.Publish(NavigationProjectionUpdate.Filter, waitForPresentation: true);

    /// <inheritdoc />
    public void NotifyLayoutRebuild() =>
        _readModel.Publish(NavigationProjectionUpdate.Layout, waitForPresentation: true);

    private void PublishDataChange(NavigationChange change) =>
        _readModel.Publish(
            NavigationProjectionUpdate.FromDelta(SyncNavigationDelta.FromChanges(new[] { change })),
            waitForPresentation: true);
}
