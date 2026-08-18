// Copyright Xeno Innovations, Inc. 2026
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using LetsEncryptRunner.Core;
using LetsEncryptRunner.Core.Renewal;

const string StartupAppName = "LetsEncryptRunner.RenewalRunner";
const string ScheduledTaskName = "LetsEncryptRunner.RenewalRunner";

var options = CliOptions.Parse(args);

if (options.Has("--help") || options.Has("-h"))
{
  PrintHelp();
  return 0;
}

var configPath = Path.GetFullPath(options.Value("--config") ?? "sites.json");
var startupService = new WindowsStartupService();
var scheduledTaskService = new WindowsScheduledTaskService();

if (options.Has("--install-startup"))
{
  EnsureWindowsForStartup();

  var (executablePath, runnerArguments) = GetRunnerInvocation(configPath);
  startupService.Install(StartupAppName, executablePath, runnerArguments);
  Console.WriteLine($"Installed Windows startup entry for {StartupAppName}.");
  Console.WriteLine($"Config: {configPath}");
  return 0;
}

if (options.Has("--uninstall-startup"))
{
  EnsureWindowsForStartup();

  startupService.Uninstall(StartupAppName);
  Console.WriteLine($"Removed Windows startup entry for {StartupAppName}.");
  return 0;
}

if (options.Has("--install-scheduled-task"))
{
  EnsureWindowsForStartup();

  var config = File.Exists(configPath)
      ? await new LetsEncryptRunner.Core.Configuration.ConfigStore().LoadAsync(configPath)
      : new LetsEncryptRunner.Core.Configuration.RunnerConfig();
  var (executablePath, runnerArguments) = GetRunnerInvocation(configPath);

  await scheduledTaskService.InstallAsync(
      ScheduledTaskName,
      executablePath,
      runnerArguments,
      config.RenewalIntervalDays);

  Console.WriteLine($"Installed Windows scheduled tasks for {ScheduledTaskName} every {config.RenewalIntervalDays} day(s) and at logon.");
  Console.WriteLine($"Config: {configPath}");
  return 0;
}

if (options.Has("--uninstall-scheduled-task"))
{
  EnsureWindowsForStartup();

  await scheduledTaskService.UninstallAsync(ScheduledTaskName);
  Console.WriteLine($"Removed Windows scheduled tasks for {ScheduledTaskName}.");
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
      --install-scheduled-task
                           Create Windows tasks that run every renewalIntervalDays and at logon.
      --uninstall-scheduled-task
                           Remove those Windows scheduled tasks.
      --help                Show help.
    """);
}

static void EnsureWindowsForStartup()
{
  if (!OperatingSystem.IsWindows())
  {
    throw new PlatformNotSupportedException("Windows startup registration is only supported on Windows.");
  }
}

static (string ExecutablePath, string Arguments) GetRunnerInvocation(string configPath)
{
  var executablePath = Environment.ProcessPath
      ?? throw new InvalidOperationException("Unable to determine executable path.");
  var entryAssemblyPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
  var runnerArguments = $"--config \"{configPath}\" --run-once";

  if (Path.GetFileNameWithoutExtension(executablePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
      && !string.IsNullOrWhiteSpace(entryAssemblyPath))
  {
    runnerArguments = $"\"{entryAssemblyPath}\" {runnerArguments}";
  }

  return (executablePath, runnerArguments);
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
