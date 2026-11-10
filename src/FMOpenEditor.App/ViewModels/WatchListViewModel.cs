using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.App.ViewModels;

public sealed record WatchedEntity(Entity Entity, string Name, string Mode, int Count, bool IsFrozen)
{
    public EntityPicture Picture { get; } = new(Entity, FMOpenEditor.Core.Graphics.GraphicSize.Small);
    public string Icon => IsFrozen ? "snow" : "up";
}

/// <summary>Everyone whose attributes are frozen or only allowed to go up.</summary>
public sealed partial class WatchListViewModel
{
    private readonly EditorSession _session;

    public ObservableCollection<WatchedEntity> Items { get; } = [];

    public WatchListViewModel(EditorSession session)
    {
        _session = session;
        session.Watcher.Changed += Reload;
        Reload();
    }

    public bool IsEmpty => Items.Count == 0;

    private void Reload()
    {
        Items.Clear();
        foreach (var g in _session.Watcher.Watches.GroupBy(w => w.Entity))
        {
            var mode = g.First().Mode == WatchMode.Freeze ? "Frozen" : "Only up";
            Items.Add(new WatchedEntity(g.Key, _session.TryRead(g.Key, PropertyCatalog.DisplayName) as string ?? g.Key.ToString(),
                mode, g.Count(), g.First().Mode == WatchMode.Freeze));
        }
    }

    [RelayCommand] private void Remove(WatchedEntity item) => _session.Watcher.Unwatch(item.Entity);
    [RelayCommand] private void Clear() => _session.Watcher.Clear();

    public void Detach() => _session.Watcher.Changed -= Reload;
}
