using LetsEncryptRunner.Core.Configuration;
using LetsEncryptRunner.Core.Renewal;

var options = CliOptions.Parse(args);

if (options.Has("--help") || options.Has("-h"))
{
    PrintHelp();
    return 0;
}

var configPath = options.Value("--config") ?? "sites.json";
var store = new ConfigStore();

if (options.Has("--sample"))
{
    var sample = CreateSampleConfig();
    await store.SaveAsync(configPath, sample);
    Console.WriteLine($"Sample config written to {Path.GetFullPath(configPath)}");
    return 0;
}

RunnerConfig config;
if (File.Exists(configPath))
{
    config = await store.LoadAsync(configPath);
}
else
{
    config = store.CreateDefault();
    Console.WriteLine($"Creating new config: {Path.GetFullPath(configPath)}");
}

var shouldAdd = options.Has("--add") || args.Length == 0;
var shouldIssue = options.Has("--issue");
var shouldUpload = options.Has("--upload");

if (shouldAdd)
{
    var website = ReadWebsiteFromConsole(config);
    config.Websites.Add(website);
    await store.SaveAsync(configPath, config);
    Console.WriteLine($"Saved '{website.Name}' to {Path.GetFullPath(configPath)}");

    if (!shouldIssue)
    {
        shouldIssue = ReadYesNo("Issue the certificate now?", defaultValue: false);
    }
}

if (shouldIssue)
{
    var service = new RenewalService();
    var summary = await service.RunAsync(
        configPath,
        new RenewalOptions(Force: true, SkipUpload: !shouldUpload),
        Console.WriteLine);

    Console.WriteLine($"Done. Processed: {summary.Processed}, skipped: {summary.Skipped}, failed: {summary.Failed}");
    return summary.Failed == 0 ? 0 : 1;
}

if (!shouldAdd)
{
    Console.WriteLine("No action requested. Use --add, --issue, --sample, or --help.");
}

return 0;

static WebsiteConfig ReadWebsiteFromConsole(RunnerConfig config)
{
    Console.WriteLine();
    Console.WriteLine("Add website");

    var domains = ReadRequired("Domain Name(s), comma-separated")
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    var email = ReadRequired("Email address");
    var webRoot = ReadRequired("HTTP web root path for /.well-known/acme-challenge");
    var name = ReadOptional("Friendly site name", domains[0]);

    if (config.Websites.Count == 0)
    {
        config.UseLetsEncryptStaging = ReadYesNo("Use Let's Encrypt staging first?", defaultValue: true);
        config.RenewalIntervalDays = ReadInt("Renewal interval days", config.RenewalIntervalDays);
    }

    var website = new WebsiteConfig
    {
        Name = name,
        DomainNames = domains,
        EmailAddress = email,
        HttpChallenge = new HttpChallengeConfig
        {
            WebRootPath = webRoot
        }
    };

    if (ReadYesNo("Configure GoDaddy/cPanel upload for this site?", defaultValue: false))
    {
        website.Deployment.Type = DeploymentTypes.CPanel;
        website.Deployment.CPanel.BaseUrl = ReadRequired("cPanel base URL, e.g. https://example.com:2083");
        website.Deployment.CPanel.Username = ReadRequired("cPanel username");
        website.Deployment.CPanel.ApiTokenEnvironmentVariable = ReadOptional("API token environment variable", "CPANEL_API_TOKEN");
        website.Deployment.CPanel.DomainNameOverride = ReadOptional("cPanel install domain", domains[0]);
    }

    return website;
}

static RunnerConfig CreateSampleConfig()
{
    return new RunnerConfig
    {
        UseLetsEncryptStaging = true,
        Websites =
        [
            new WebsiteConfig
            {
                Name = "example.com",
                DomainNames = ["example.com", "www.example.com"],
                EmailAddress = "admin@example.com",
                HttpChallenge = new HttpChallengeConfig
                {
                    WebRootPath = "C:\\inetpub\\wwwroot"
                },
                Deployment = new DeploymentConfig
                {
                    Type = DeploymentTypes.CPanel,
                    CPanel = new CPanelDeploymentConfig
                    {
                        BaseUrl = "https://your-cpanel-host.example.com:2083",
                        Username = "cpanel-user",
                        ApiTokenEnvironmentVariable = "CPANEL_API_TOKEN",
                        DomainNameOverride = "example.com"
                    }
                }
            }
        ]
    };
}

static string ReadRequired(string label)
{
    while (true)
    {
        Console.Write($"{label}: ");
        var value = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
    }
}

static string ReadOptional(string label, string defaultValue)
{
    Console.Write($"{label} [{defaultValue}]: ");
    var value = Console.ReadLine();
    return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
}

static bool ReadYesNo(string label, bool defaultValue)
{
    var suffix = defaultValue ? "Y/n" : "y/N";
    Console.Write($"{label} [{suffix}]: ");
    var value = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(value))
    {
        return defaultValue;
    }

    return value.Trim().StartsWith('y', StringComparison.OrdinalIgnoreCase);
}

static int ReadInt(string label, int defaultValue)
{
    while (true)
    {
        Console.Write($"{label} [{defaultValue}]: ");
        var value = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (int.TryParse(value, out var parsed) && parsed > 0)
        {
            return parsed;
        }
    }
}

static void PrintHelp()
{
    Console.WriteLine("""
    LetsEncryptRunner.Setup

    Creates or updates the JSON website config and can issue certificates immediately.

    Options:
      --config <path>  Config file path. Default: sites.json
      --add            Add a website interactively.
      --issue          Issue/renew certificates for enabled websites.
      --upload         Upload after issue when deployment is configured.
      --sample         Write a sample config.
      --help           Show help.
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

