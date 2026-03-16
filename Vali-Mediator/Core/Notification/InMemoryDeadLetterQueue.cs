using System.Collections.Concurrent;

namespace Vali_Mediator.Core.Notification;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IDeadLetterQueue"/>.
/// Entries are held in a bounded <see cref="ConcurrentQueue{T}"/>.
/// Register via <c>services.AddInMemoryDeadLetterQueue()</c>.
/// </summary>
public sealed class InMemoryDeadLetterQueue : IDeadLetterQueue
{
    private readonly ConcurrentQueue<DeadLetterEntry> _entries = new ConcurrentQueue<DeadLetterEntry>();
    private readonly int _maxEntries;

    /// <param name="maxEntries">
    /// Maximum number of entries retained. Oldest entries are discarded when the limit is reached.
    /// Default: 1 000.
    /// </param>
    public InMemoryDeadLetterQueue(int maxEntries = 1_000)
    {
        if (maxEntries < 1) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        _maxEntries = maxEntries;
    }

    /// <inheritdoc/>
    public Task EnqueueAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        _entries.Enqueue(entry);
        while (_entries.Count > _maxEntries)
            _entries.TryDequeue(out _);
        return Task.CompletedTask;
    }

    /// <summary>Returns a snapshot of all retained entries (oldest first).</summary>
    public IReadOnlyList<DeadLetterEntry> GetEntries() => _entries.ToArray();

    /// <summary>Removes all entries from the queue.</summary>
    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
    }

    /// <summary>Total number of entries currently held.</summary>
    public int Count => _entries.Count;
}
