using System.Collections.Concurrent;
using System.Reflection;

namespace Vali_Mediator.Core.General.Cache;

/// <summary>
/// Thread-safe cache for <see cref="MethodInfo"/> instances resolved via reflection.
/// Avoids repeated <c>GetMethod</c> calls on hot dispatch paths.
/// </summary>
internal static class ReflectionCache
{
    private static readonly ConcurrentDictionary<(Type Type, string Method), MethodInfo> Cache = new();

    /// <summary>
    /// Returns the <see cref="MethodInfo"/> for <paramref name="methodName"/> on <paramref name="type"/>,
    /// resolving it on the first call and serving it from cache on subsequent calls.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the method is not found on the type.</exception>
    internal static MethodInfo GetMethod(Type type, string methodName)
        => Cache.GetOrAdd((type, methodName), static key =>
            key.Type.GetMethod(key.Method)
            ?? throw new InvalidOperationException(
                $"Type '{key.Type.Name}' does not expose a '{key.Method}' method."));
}
