using NfcDesk.Core;
using NfcDesk.Core.Config;

namespace NfcDesk.App;

public sealed class AgentState : IAgentState, IDisposable
{
    private static readonly TimeSpan LearnDuration = TimeSpan.FromMinutes(2);

    private readonly ConfigStore _config;
    private readonly object _lock = new();
    private System.Threading.Timer? _timer;
    private DateTime _expiryUtc = DateTime.MinValue;
    private bool _paused;

    public AgentState(ConfigStore config) => _config = config;

    public event Action? Changed;

    public bool IsTagRegistered(string tag) => _config.FindTag(tag) != null;

    public bool Paused
    {
        get { lock (_lock) return _paused; }
        set
        {
            lock (_lock)
            {
                if (_paused == value) return;
                _paused = value;
            }
            Changed?.Invoke();
        }
    }

    public bool LearningMode
    {
        get { lock (_lock) return DateTime.UtcNow < _expiryUtc; }
    }

    public void StartLearning()
    {
        lock (_lock)
        {
            _expiryUtc = DateTime.UtcNow + LearnDuration;
            _timer?.Dispose();
            _timer = new System.Threading.Timer(_ => OnExpired(), null, LearnDuration + TimeSpan.FromMilliseconds(100), Timeout.InfiniteTimeSpan);
        }
        Changed?.Invoke();
    }

    public void StopLearning()
    {
        bool was;
        lock (_lock)
        {
            was = DateTime.UtcNow < _expiryUtc;
            _expiryUtc = DateTime.MinValue;
            _timer?.Dispose();
            _timer = null;
        }
        if (was) Changed?.Invoke();
    }

    private void OnExpired() => Changed?.Invoke();

    public void Dispose()
    {
        lock (_lock) { _timer?.Dispose(); _timer = null; }
    }
}
