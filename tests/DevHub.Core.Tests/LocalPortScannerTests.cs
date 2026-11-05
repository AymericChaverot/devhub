using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class LocalPortScannerTests
{
    [Fact]
    public void ParseNetstatOutput_ExtractsListeningPortsAndIdentifiesCommonDevPorts()
    {
        var sample = """
            Active Connections

              Proto  Local Address          Foreign Address        State           PID
              TCP    0.0.0.0:135            0.0.0.0:0              LISTENING       1120
              TCP    127.0.0.1:5173         0.0.0.0:0              LISTENING       24800
              TCP    127.0.0.1:5173         127.0.0.1:54122        ESTABLISHED     24800
              TCP    [::1]:3000             [::]:0                 ÉCOUTE          19200
            """;

        var ports = LocalPortScanner.ParseNetstatOutput(sample, pid => pid switch
        {
            24800 => "node",
            19200 => "bun",
            1120 => "svchost",
            _ => null,
        });

        Assert.Equal(3, ports.Count);

        // Common dev ports (3000, 5173) are sorted first, ascending by port.
        Assert.Equal(3000, ports[0].Port);
        Assert.Equal("bun", ports[0].ProcessName);
        Assert.True(ports[0].IsCommonDevPort);

        Assert.Equal(5173, ports[1].Port);
        Assert.Equal("node", ports[1].ProcessName);
        Assert.True(ports[1].IsCommonDevPort);

        Assert.Equal(135, ports[2].Port);
        Assert.Equal("svchost", ports[2].ProcessName);
        Assert.False(ports[2].IsCommonDevPort);
    }
}
