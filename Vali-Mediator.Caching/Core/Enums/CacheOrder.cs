namespace Vali_Mediator_Caching.Core.Enums;

/// <summary>
/// Controls how the caching pipeline behavior reads and writes to the cache store.
/// </summary>
public enum CacheOrder
{
    /// <summary>
    /// First attempts to read the cached result; on a miss, executes the handler and writes
    /// the result to the cache. This is the default mode.
    /// </summary>
    ReadThenWrite = 0,

    /// <summary>
    /// Skips the cache read entirely. The handler always executes and the result is written
    /// to the cache, replacing any existing entry.
    /// </summary>
    WriteOnly = 1,

    /// <summary>
    /// Reads from the cache if available. If the entry is missing, the handler executes but
    /// the result is NOT written to the cache.
    /// </summary>
    ReadOnly = 2,
}
