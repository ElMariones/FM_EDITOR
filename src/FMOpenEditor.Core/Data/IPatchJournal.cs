namespace FMOpenEditor.Core.Data;

/// <summary>Bytes the editor wrote at one address, with the bytes that were there before.</summary>
public sealed record MemoryPatch(nint Address, byte[] Before, byte[] After);

/// <summary>
/// A database that can record the exact bytes a batch of writes changed, and undo them only while the
/// game still holds what the editor wrote. Anything the game changed since makes the undo refuse,
/// rather than writing stale values over the game's own.
/// </summary>
public interface IPatchJournal
{
    /// <summary>Starts recording every write until <see cref="StopRecording"/>.</summary>
    void StartRecording();

    /// <summary>Stops recording and returns the writes made, in order.</summary>
    IReadOnlyList<MemoryPatch> StopRecording();

    /// <summary>
    /// Puts back what <paramref name="patches"/> overwrote, in reverse order, when every byte they wrote is
    /// still there. Otherwise writes nothing and throws <see cref="UndoRefusedException"/>.
    /// </summary>
    void Revert(IReadOnlyList<MemoryPatch> patches);
}

public sealed class UndoRefusedException(int changed)
    : Exception($"The game has changed {changed} of the values this save wrote since (it plays on, or edited them itself), " +
                "so undoing it would overwrite the game's own data. Nothing was undone.")
{
    public int Changed { get; } = changed;
}

/// <summary>Thrown when the game has loaded another save or gone back to its menu since the data was read.</summary>
public sealed class StaleDataException()
    : InvalidOperationException("The game has loaded another save or gone back to its menu since the editor read it. " +
                                "Reload the data before editing; nothing was written.");
