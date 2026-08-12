using System.Text;
using System.Runtime.InteropServices;

namespace WorkstationAgent.Ubuntu;

internal static class Program
{
    private const string DefaultConfigPath = "/etc/avtoforward-agent/agentsettings.json";
    private const string DefaultStatePath = "/var/lib/avtoforward-agent/state.json";

    public static async Task<int> Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            var options = CommandLineOptions.Parse(args);
            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
            }

            var settings = new SettingsStore().Load(options.ConfigPath);
            var logger = new AgentLogger(settings.LogFilePath);
            logger.Info($"Avtoforward Agent for Ubuntu {AgentRuntime.Version} starting.");
            logger.Info($"Configuration loaded from {options.ConfigPath}.");

            if (options.ValidateOnly)
            {
                logger.Info("Configuration is valid.");
                return 0;
            }

            var printerService = new PrinterService(
                settings,
                logger,
                new PrintPayloadBuilder(new ImageMagickRasterizer()));
            var posTerminalService = new PosTerminalService(settings, logger);
            if (options.PrintTest is not null)
            {
                var requestId = Guid.NewGuid().ToString("N");
                if (PrinterRoles.IsLabel(options.PrintTest))
                {
                    var result = await printerService.PrintLabelTestAsync(requestId, CancellationToken.None);
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
                    return result.Success ? 0 : 1;
                }
                var receiptResult = await printerService.PrintReceiptTestAsync(requestId, CancellationToken.None);
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(receiptResult));
                return receiptResult.Success ? 0 : 1;
            }
            if (options.PosTest)
            {
                var result = await posTerminalService.TestConnectionAsync(CancellationToken.None);
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
                return string.Equals(result.Status, "approved", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            }

            var identityStore = new IdentityStore(options.StatePath);
            var hadStoredIdentity = identityStore.Exists;
            var identity = identityStore.Load(settings);
            if (!identity.IsComplete || options.ForceRegistration)
            {
                logger.Info("Registering workstation agent with the Autof API.");
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                identity = await new RegistrationClient(httpClient)
                    .RegisterAsync(settings, identity, CancellationToken.None);
                identityStore.Save(identity);
                logger.Info($"Registration completed. DeviceId={identity.DeviceId}, AgentName={identity.AgentName}.");
            }
            else if (!hadStoredIdentity)
            {
                identityStore.Save(identity);
            }

            using var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };
            using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                shutdown.Cancel();
            });
            using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
            {
                context.Cancel = true;
                shutdown.Cancel();
            });

            await using var socketClient = new AgentSocketClient(
                settings,
                identity,
                logger,
                printerService,
                posTerminalService);
            await socketClient.RunAsync(shutdown.Token);
            logger.Info("Agent stopped.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal agent error: {ex}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            Avtoforward Agent for Ubuntu

            Usage:
              WorkstationAgent.Ubuntu [options]

            Options:
              --config PATH          Configuration file (default: /etc/avtoforward-agent/agentsettings.json)
              --state PATH           Registration state file (default: /var/lib/avtoforward-agent/state.json)
              --validate             Validate configuration and exit
              --register             Register again using registrationToken, then run normally
              --print-test receipt   Print a test receipt and exit
              --print-test label     Print a test label and exit
              --pos-test             Test the PrivatBank POS connection and exit
              --help                 Show this help

            Environment:
              AVTOFORWARD_AGENT_CONFIG overrides the default configuration path.
              AVTOFORWARD_AGENT_STATE overrides the default registration state path.
            """);
    }

    private sealed record CommandLineOptions(
        string ConfigPath,
        string StatePath,
        bool ValidateOnly,
        bool ForceRegistration,
        string? PrintTest,
        bool PosTest,
        bool ShowHelp)
    {
        public static CommandLineOptions Parse(string[] args)
        {
            var configPath = Environment.GetEnvironmentVariable("AVTOFORWARD_AGENT_CONFIG") ?? DefaultConfigPath;
            var statePath = Environment.GetEnvironmentVariable("AVTOFORWARD_AGENT_STATE") ?? DefaultStatePath;
            var validate = false;
            var register = false;
            string? printTest = null;
            var posTest = false;
            var showHelp = false;

            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--config":
                        configPath = ReadValue(args, ref index, "--config");
                        break;
                    case "--state":
                        statePath = ReadValue(args, ref index, "--state");
                        break;
                    case "--validate":
                        validate = true;
                        break;
                    case "--register":
                        register = true;
                        break;
                    case "--print-test":
                        printTest = ReadValue(args, ref index, "--print-test").ToLowerInvariant();
                        if (printTest is not (PrinterRoles.Receipt or PrinterRoles.Label))
                        {
                            throw new ArgumentException("--print-test must be receipt or label.");
                        }
                        break;
                    case "--pos-test":
                        posTest = true;
                        break;
                    case "--help" or "-h":
                        showHelp = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument '{args[index]}'. Use --help for usage.");
                }
            }

            return new CommandLineOptions(
                Path.GetFullPath(configPath),
                Path.GetFullPath(statePath),
                validate,
                register,
                printTest,
                posTest,
                showHelp);
        }

        private static string ReadValue(string[] args, ref int index, string option)
        {
            index++;
            if (index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            {
                throw new ArgumentException($"{option} requires a value.");
            }
            return args[index];
        }
    }
}
