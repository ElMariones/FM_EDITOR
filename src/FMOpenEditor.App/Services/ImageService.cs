using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FMOpenEditor.Core.Graphics;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.App.Services;

/// <summary>
/// Faces and badges for the UI, read from the user's FM graphics folder.
/// The folder is indexed in the background; pictures are decoded off the UI thread at the size
/// they're shown and cached. Anything that can't be found simply yields no picture, and the UI
/// collapses the slot.
/// </summary>
public sealed class ImageService
{
    private const int SmallPixels = 64;   // rows are ~28 dip; 64px stays crisp at 200% scaling
    private const int LargePixels = 256;
    private const int MaxCached = 900;

    private readonly Dictionary<(EntityKind, int, GraphicSize), Bitmap?> _cache = [];
    private readonly Dictionary<(EntityKind, int, GraphicSize), List<Action<Bitmap?>>> _waiting = [];
    private readonly LinkedList<(EntityKind, int, GraphicSize)> _order = new();
    private readonly SemaphoreSlim _decoders = new(4);
    private CancellationTokenSource? _indexing;
    private GraphicsLibrary _library = GraphicsLibrary.Empty;

    public static ImageService Instance { get; } = new();

    /// <summary>
    /// Ids only line up with graphics packs for a real save. The demo save invents its ids, so it
    /// shows no pictures unless a test opts in.
    /// </summary>
    public bool Active { get; private set; }

    public bool IsIndexing { get; private set; }
    public GraphicsLibrary Library => _library;

    /// <summary>Raised on the UI thread when the index or the on/off state changes.</summary>
    public event Action? Changed;

    /// <summary>True when a picture could exist for this kind; false lets a view drop the slot.</summary>
    public bool HasAny(EntityKind kind) => Active && _library.HasAny(kind);

    /// <summary>Points the service at a library that is already loaded.</summary>
    public void Use(GraphicsLibrary library, bool active)
    {
        _indexing?.Cancel();
        _library = library;
        Active = active;
        IsIndexing = false;
        ClearCache();
        Changed?.Invoke();
    }

    /// <summary>Turns pictures off (not connected, demo save, or disabled in Settings).</summary>
    public void Disable() => Use(GraphicsLibrary.Empty, false);

    /// <summary>Indexes the graphics folders in the background and switches pictures on when done.</summary>
    public async Task IndexAsync(IReadOnlyList<string> roots)
    {
        _indexing?.Cancel();
        var cancel = _indexing = new CancellationTokenSource();
        Active = true;
        IsIndexing = true;
        Changed?.Invoke();
        try
        {
            var library = await Task.Run(() => GraphicsLibrary.Load(roots, cancel.Token), cancel.Token);
            if (cancel.IsCancellationRequested) return;
            _library = library;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            _library = GraphicsLibrary.Empty;
        }
        IsIndexing = false;
        ClearCache();
        Changed?.Invoke();
    }

    /// <summary>A picture that is already decoded, without starting a load.</summary>
    public bool TryGetCached(EntityKind kind, int id, GraphicSize size, out Bitmap? bitmap) =>
        _cache.TryGetValue((kind, id, size), out bitmap);

    /// <summary>
    /// Loads a picture and calls <paramref name="done"/> on the UI thread: with the bitmap, or with
    /// null when there is none. Call from the UI thread.
    /// </summary>
    public void Load(EntityKind kind, int id, GraphicSize size, Action<Bitmap?> done)
    {
        var key = (kind, id, size);
        if (!Active || _library.IsEmpty) { done(null); return; }
        if (_cache.TryGetValue(key, out var cached)) { done(cached); return; }
        if (_waiting.TryGetValue(key, out var list)) { list.Add(done); return; }
        _waiting[key] = [done];

        var library = _library;
        _ = Task.Run(async () =>
        {
            Bitmap? bitmap = null;
            await _decoders.WaitAsync();
            try
            {
                var path = library.Find(kind, id, size);
                if (path is not null)
                {
                    using var stream = File.OpenRead(path);
                    bitmap = Bitmap.DecodeToHeight(stream, size == GraphicSize.Large ? LargePixels : SmallPixels);
                }
            }
            catch (Exception)
            {
                // Unreadable or corrupt picture: treated the same as no picture.
            }
            finally
            {
                _decoders.Release();
            }

            Dispatcher.UIThread.Post(() =>
            {
                // The library may have been swapped while this was decoding.
                if (ReferenceEquals(library, _library)) Remember(key, bitmap);
                if (_waiting.Remove(key, out var callbacks))
                    foreach (var callback in callbacks) callback(ReferenceEquals(library, _library) ? bitmap : null);
            });
        });
    }

    private void Remember((EntityKind, int, GraphicSize) key, Bitmap? bitmap)
    {
        _cache[key] = bitmap;
        _order.AddLast(key);
        while (_order.Count > MaxCached)
        {
            var oldest = _order.First!.Value;
            _order.RemoveFirst();
            // Not disposed: a row on screen may still be showing it. The GC reclaims it.
            _cache.Remove(oldest);
        }
    }

    private void ClearCache()
    {
        _cache.Clear();
        _order.Clear();
    }
}
