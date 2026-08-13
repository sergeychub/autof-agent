using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkstationAgent.Ubuntu.Tests;

[TestClass]
public sealed class SettingsStoreTests
{
    [TestMethod]
    public void DeviceSerialAllowsDeviceTransportWithoutDevicePath()
    {
        var settings = LoadSettings(
            """
            {
              "agentName": "ubuntu-test",
              "apiBaseUrl": "https://api.example.com",
              "receiptPrinter": {
                "enabled": true,
                "transportMode": "device",
                "deviceSerial": "809444052203"
              },
              "labelPrinter": { "enabled": false }
            }
            """);

        Assert.AreEqual("809444052203", settings.ReceiptPrinter.DeviceSerial);
        Assert.IsNull(settings.ReceiptPrinter.DevicePath);
    }

    [TestMethod]
    public void DeviceTransportRequiresSerialOrAbsolutePath()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            LoadSettings(
                """
                {
                  "agentName": "ubuntu-test",
                  "apiBaseUrl": "https://api.example.com",
                  "receiptPrinter": {
                    "enabled": true,
                    "transportMode": "device"
                  },
                  "labelPrinter": { "enabled": false }
                }
                """));

        StringAssert.Contains(exception.Message, "deviceSerial or an absolute devicePath");
    }

    private static AgentSettings LoadSettings(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"autof-agent-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            return new SettingsStore().Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
