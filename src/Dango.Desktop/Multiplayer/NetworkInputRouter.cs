namespace Dango.Desktop.Multiplayer;

/// <summary>Window-local input only. Its callback has no actor/pet selector and can only send self intents.</summary>
public sealed class NetworkInputRouter(Action<string> setMoveIntent, Action startWave, Func<bool> canSend)
{
    public static TimeSpan RenewalInterval { get; } = TimeSpan.FromMilliseconds(250);
    private TimeSpan now;
    private TimeSpan lastMove;
    private string? held;
    private bool active;
    public bool IsActive => active;
    public string? HeldDirection => held;
    public void MoveLeft() => Move("left");
    public void MoveRight() => Move("right");
    private void Move(string direction)
    {
        if (!active || !canSend()) { held = null; return; }
        if (held == direction) return;
        held = direction; lastMove = now; setMoveIntent(direction);
    }
    public void Stop()
    {
        held = null;
        if (active && canSend()) setMoveIntent("stopped");
    }
    public void Wave() { if (active && canSend()) startWave(); }
    public void SetActive(bool value)
    {
        if (active == value) return;
        active = value;
        if (!value) Clear();
    }
    public void Clear()
    {
        var wasHeld = held is not null; held = null;
        if (wasHeld && canSend())
        {
            try { setMoveIntent("stopped"); }
            catch (InvalidOperationException) { /* Disconnection/disposal races cannot keep local input held. */ }
        }
    }
    public void Tick(TimeSpan monotonicTime)
    {
        if (monotonicTime < now) throw new ArgumentOutOfRangeException(nameof(monotonicTime));
        var suspended = monotonicTime - now > TimeSpan.FromSeconds(1);
        now = monotonicTime;
        if (!active || !canSend()) { held = null; return; }
        if (suspended) { Clear(); return; }
        if (held is not null && now - lastMove >= RenewalInterval)
        { lastMove = now; setMoveIntent(held); }
    }
}
