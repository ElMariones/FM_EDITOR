using System.ComponentModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FMOpenEditor.App.Services;
using FMOpenEditor.Core.Graphics;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.App.ViewModels;

/// <summary>
/// One entity's face or badge, loaded in the background. <see cref="HasImage"/> stays false when
/// the user has no picture for it, and views bind the picture's visibility to that so the slot
/// disappears and only the name remains.
/// </summary>
public sealed partial class EntityPicture : ObservableObject
{
    private readonly GraphicSize _size;
    private int _generation;

    public EntityKind Kind { get; }
    public int Id { get; }

    /// <summary>Faces are shown round; badges are shown whole.</summary>
    public bool IsPerson => Kind is EntityKind.Player or EntityKind.Staff;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasImage))] private Bitmap? _image;

    public bool HasImage => Image is not null;

    public EntityPicture(Entity entity, GraphicSize size) : this(entity.Kind, entity.Id, size) { }

    public EntityPicture(EntityKind kind, int id, GraphicSize size)
    {
        Kind = kind;
        Id = id;
        _size = size;
        Reload();
    }

    /// <summary>Loads again, after the graphics index changed.</summary>
    public void Reload()
    {
        var generation = ++_generation;
        Image = null;
        ImageService.Instance.Load(Kind, Id, _size, bitmap =>
        {
            if (generation == _generation) Image = bitmap;
        });
    }
}

/// <summary>
/// The pictures for one grid row, by column key: the row's own face or badge under the name
/// column, and the referenced club / nation / competition badge under reference columns.
/// Loading starts the first time a cell asks, so only rows that scroll into view cost anything.
/// </summary>
public sealed class RowImages : INotifyPropertyChanged
{
    private readonly Dictionary<string, (EntityKind Kind, int Id)> _targets = [];
    private readonly Dictionary<string, Bitmap?> _loaded = [];
    private readonly HashSet<string> _requested = [];
    private int _generation;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Bitmap? this[string key]
    {
        get
        {
            if (_loaded.TryGetValue(key, out var bitmap)) return bitmap;
            if (!_targets.TryGetValue(key, out var target)) return null;
            var images = ImageService.Instance;
            if (!images.HasAny(target.Kind)) return null;
            if (images.TryGetCached(target.Kind, target.Id, GraphicSize.Small, out bitmap)) return _loaded[key] = bitmap;
            if (!_requested.Add(key)) return null;

            var generation = _generation;
            images.Load(target.Kind, target.Id, GraphicSize.Small, loaded =>
            {
                if (generation != _generation) return;
                _loaded[key] = loaded;
                if (loaded is not null) Notify();
            });
            return _loaded.GetValueOrDefault(key);
        }
    }

    /// <summary>Sets which entity a column's picture shows; null clears it.</summary>
    public void Target(string key, EntityKind? kind, int id)
    {
        if (kind is null)
        {
            if (_targets.Remove(key)) Forget(key);
            return;
        }
        if (_targets.TryGetValue(key, out var existing) && existing == (kind.Value, id)) return;
        _targets[key] = (kind.Value, id);
        Forget(key);
    }

    /// <summary>Drops everything loaded, after the graphics index changed.</summary>
    public void Reset()
    {
        _generation++;
        _loaded.Clear();
        _requested.Clear();
        Notify();
    }

    private void Forget(string key)
    {
        if (_loaded.Remove(key) | _requested.Remove(key)) Notify();
    }

    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
}
