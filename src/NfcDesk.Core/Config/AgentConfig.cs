namespace NfcDesk.Core.Config;

public sealed class TagMapping
{
    public string Tag { get; set; } = "";
    public string Label { get; set; } = "";
    public string Script { get; set; } = "";
    public DateTimeOffset? LastTapped { get; set; }
    public int TapCount { get; set; }

    public TagMapping Clone() => (TagMapping)MemberwiseClone();
}

public sealed class AgentConfig
{
    public const int DefaultPort = 8765;

    public int Port { get; set; } = DefaultPort;
    public bool Notifications { get; set; } = true;
    public bool FirewallPrompted { get; set; }
    public List<TagMapping> Tags { get; set; } = new();
    public Dictionary<string, long> LastTs { get; set; } = new();

    public AgentConfig Clone() => new()
    {
        Port = Port,
        Notifications = Notifications,
        FirewallPrompted = FirewallPrompted,
        Tags = Tags.Select(t => t.Clone()).ToList(),
        LastTs = new Dictionary<string, long>(LastTs),
    };
}
