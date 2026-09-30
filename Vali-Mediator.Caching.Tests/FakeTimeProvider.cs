namespace Vali_Mediator_Caching.Tests;

internal sealed class FakeTimeProvider : TimeProvider
{
    private readonly object _gate = new object();
    private DateTimeOffset _now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _now;
    }

    public void Advance(TimeSpan delta)
    {
        lock (_gate) _now += delta;
    }
}
