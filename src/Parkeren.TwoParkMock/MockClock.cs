internal sealed class MockClock
{
    private DateTimeOffset? fixedUtcNow;

    public DateTimeOffset UtcNow => fixedUtcNow ?? DateTimeOffset.UtcNow;

    public void Set(DateTimeOffset utcNow) => fixedUtcNow = utcNow.ToUniversalTime();

    public void Advance(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
  throw new ArgumentOutOfRangeException(nameof(duration));
        fixedUtcNow = UtcNow.Add(duration);
    }

    public void Reset() => fixedUtcNow = null;
}
