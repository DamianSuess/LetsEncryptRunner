using LetsEncryptRunner.Core;
using LetsEncryptRunner.Core.Renewal;

const string StartupAppName = "LetsEncryptRunner.RenewalRunner";

var options = CliOptions.Parse(args);

if (options.Has("--help") || options.Has("-h"))
{
    PrintHelp();
    return 0;
}

var configPath = Path.GetFullPath(options.Value("--config") ?? "sites.json");
var startupService = new WindowsStartupService();

if (options.Has("--install-startup"))
{
    var executablePath = Environment.ProcessPath
        ?? throw new InvalidOperationException("Unable to determine executable path.");

    startupService.Install(StartupAppName, executablePath, $"--config \"{configPath}\" --run-once");
    Console.WriteLine($"Installed Windows startup entry for {StartupAppName}.");
    Console.WriteLine($"Config: {configPath}");
    return 0;
}

if (options.Has("--uninstall-startup"))
{
    startupService.Uninstall(StartupAppName);
    Console.WriteLine($"Removed Windows startup entry for {StartupAppName}.");
    return 0;
}

var renewalOptions = new RenewalOptions(
    Force: options.Has("--force"),
    UploadOnly: options.Has("--upload-only"),
    SkipUpload: options.Has("--skip-upload"),
    ContinueOnError: !options.Has("--stop-on-error"));

try
{
    var service = new RenewalService();
    var summary = await service.RunAsync(configPath, renewalOptions, Console.WriteLine);
    Console.WriteLine($"Done. Processed: {summary.Processed}, skipped: {summary.Skipped}, failed: {summary.Failed}");
    return summary.Failed == 0 ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("""
    LetsEncryptRunner.RenewalRunner

    Runs once, renews sites that are due, uploads to configured deployment targets, then exits.
    The due interval comes from renewalIntervalDays in the JSON config; default is 87 days.

    Options:
      --config <path>       Config file path. Default: sites.json
      --force               Renew/upload even when interval is not due.
      --upload-only         Upload the last saved certificate without issuing a new one.
      --skip-upload         Issue certificates but do not upload.
      --stop-on-error       Stop after the first site failure.
      --install-startup     Register this runner in HKCU Windows Startup.
      --uninstall-startup   Remove the Windows Startup entry.
      --help                Show help.
    """);
}

internal sealed class CliOptions
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith('-'))
            {
                continue;
            }

            var nextIsValue = i + 1 < args.Length && !args[i + 1].StartsWith('-');
            options._values[arg] = nextIsValue ? args[++i] : null;
        }

        return options;
    }

    public bool Has(string name) => _values.ContainsKey(name);

    public string? Value(string name) => _values.TryGetValue(name, out var value) ? value : null;
}

