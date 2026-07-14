namespace DealerOS.IntegrationTests;

public sealed class TestTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private DateTimeOffset _utcNow = initial;
    public override DateTimeOffset GetUtcNow() => _utcNow;
    public void Advance(TimeSpan value) => _utcNow = _utcNow.Add(value);
}
