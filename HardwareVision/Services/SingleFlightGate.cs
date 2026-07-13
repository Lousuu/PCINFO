namespace HardwareVision.Services;

public sealed class SingleFlightGate
{
    private int running;

    public bool IsRunning => Volatile.Read(ref running) != 0;

    public bool TryEnter()
    {
        return Interlocked.CompareExchange(ref running, 1, 0) == 0;
    }

    public void Exit()
    {
        Volatile.Write(ref running, 0);
    }
}
