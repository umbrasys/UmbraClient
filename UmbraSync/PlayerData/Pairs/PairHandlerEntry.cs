namespace UmbraSync.PlayerData.Pairs;
internal sealed class PairHandlerEntry : IDisposable
{
    private readonly HashSet<string> _pairUids = new(StringComparer.Ordinal);
    private CancellationTokenSource? _gracePeriodCts;
    private bool _disposed;
    public string Ident { get; }
    public IPairHandlerAdapter Handler { get; }
    public bool HasPairs => _pairUids.Count > 0;
    public int PairCount => _pairUids.Count;
    public IReadOnlySet<string> PairUids => _pairUids;
    public DateTime? GracePeriodExpiresAt { get; private set; }
    public bool IsInGracePeriod => GracePeriodExpiresAt.HasValue;

    public PairHandlerEntry(string ident, IPairHandlerAdapter handler)
    {
        Ident = ident ?? throw new ArgumentNullException(nameof(ident));
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public void AddPair(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return;
        _pairUids.Add(uid);
        CancelGracePeriod();
    }
    
    public bool RemovePair(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return false;
        return _pairUids.Remove(uid);
    }
    
    public void StartGracePeriod(TimeSpan duration, Action<PairHandlerEntry> onExpired)
    {
        if (_disposed) return;
        if (HasPairs) return; 

        CancelGracePeriod();

        GracePeriodExpiresAt = DateTime.UtcNow + duration;
        Handler.ScheduledForDeletion = true;

        _gracePeriodCts = new CancellationTokenSource();
        var token = _gracePeriodCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(duration, token).ConfigureAwait(false);

                if (!token.IsCancellationRequested && !HasPairs)
                {
                    onExpired(this);
                }
            }
            catch (OperationCanceledException)
            {
                // Grace period annulée
            }
        }, token);
    }
    
    public void CancelGracePeriod()
    {
        GracePeriodExpiresAt = null;
        Handler.ScheduledForDeletion = false;

        if (_gracePeriodCts != null)
        {
            _gracePeriodCts.Cancel();
            _gracePeriodCts.Dispose();
            _gracePeriodCts = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CancelGracePeriod();
        Handler.Dispose();
    }
}
