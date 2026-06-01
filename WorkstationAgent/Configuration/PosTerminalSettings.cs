using System.Text.Json.Serialization;

namespace WorkstationAgent.Configuration;

internal sealed class PosTerminalSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("host")]
    public string Host { get; set; } = "192.168.0.103";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 2000;

    [JsonPropertyName("merchantId")]
    public string MerchantId { get; set; } = "1";

    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 180;

    public PosTerminalSettings Clone()
    {
        return new PosTerminalSettings
        {
            Enabled = Enabled,
            Host = Host,
            Port = Port,
            MerchantId = MerchantId,
            TimeoutSeconds = TimeoutSeconds
        };
    }
}
