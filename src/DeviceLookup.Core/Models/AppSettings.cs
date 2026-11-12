using System.Text.Json.Serialization;

namespace DeviceLookup.Core.Models;

public enum DellMode { PublicOnly, ApiPreferred, ApiOnly }

public sealed class AppSettings
{
    public bool LenovoEnabled { get; set; } = true;
    public bool DellEnabled { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DellMode DellMode { get; set; } = DellMode.ApiPreferred;

    public string DellClientId { get; set; } = "";

    /// <summary>Stored encrypted (Windows DPAPI). Never written to disk in plain text.</summary>
    public string DellClientSecretProtected { get; set; } = "";

    public int ConcurrentLookups { get; set; } = 4;
    public int CacheDays { get; set; } = 7;
    public bool UseCache { get; set; } = true;

    [JsonIgnore] public string DellClientSecret { get; set; } = "";

    [JsonIgnore]
    public bool DellConfigured =>
        !string.IsNullOrWhiteSpace(DellClientId) && !string.IsNullOrWhiteSpace(DellClientSecret);
}
