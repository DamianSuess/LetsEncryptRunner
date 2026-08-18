// Copyright Xeno Innovations, Inc. 2026
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LetsEncryptRunner.Core.Certificates;
using LetsEncryptRunner.Core.Configuration;
using LetsEncryptRunner.Core.Deployment;

namespace LetsEncryptRunner.Core.Renewal;

public sealed class RenewalService
{
  private readonly ConfigStore _configStore;
  private readonly AcmeCertificateService _certificateService;
  private readonly CertificateDeployer _deployer;

  public RenewalService(
    ConfigStore? configStore = null,
    AcmeCertificateService? certificateService = null,
    CertificateDeployer? deployer = null)
  {
    _configStore = configStore ?? new ConfigStore();
    _certificateService = certificateService ?? new AcmeCertificateService();
    _deployer = deployer ?? new CertificateDeployer();
  }

  public async Task<RenewalSummary> RunAsync(
    string configPath,
    RenewalOptions options,
    Action<string>? log = null,
    CancellationToken cancellationToken = default)
  {
    var config = await _configStore.LoadAsync(configPath, cancellationToken);
    var summary = new RenewalSummary();

    foreach (var website in config.Websites.Where(site => site.Enabled))
    {
      cancellationToken.ThrowIfCancellationRequested();
      var label = string.IsNullOrWhiteSpace(website.Name) ? string.Join(", ", website.DomainNames) : website.Name;

      if (!options.Force && !IsDue(config, website))
      {
        log?.Invoke($"Skipping {label}; renewal is not due.");
        summary.Skipped++;
        continue;
      }

      try
      {
        website.State.LastRenewalAttemptUtc = DateTimeOffset.UtcNow;

        CertificateArtifacts artifacts;
        if (options.UploadOnly)
        {
          artifacts = LoadExistingArtifacts(website);
          log?.Invoke($"Using existing certificate artifacts for {label}: {artifacts.Directory}");
        }
        else
        {
          artifacts = await _certificateService.IssueAsync(config, website, configPath, log, cancellationToken);
          log?.Invoke($"Issued certificate for {label}; expires {artifacts.ExpiresUtc:u}.");
        }

        if (!options.SkipUpload)
        {
          await _deployer.DeployAsync(website, artifacts, log, cancellationToken);
          log?.Invoke($"Uploaded certificate for {label}.");
        }

        summary.Processed++;
      }
      catch (Exception ex)
      {
        website.State.LastError = ex.Message;
        summary.Failed++;
        log?.Invoke($"Failed {label}: {ex.Message}");

        if (!options.ContinueOnError)
        {
          await _configStore.SaveAsync(configPath, config, cancellationToken);
          throw;
        }
      }
    }

    await _configStore.SaveAsync(configPath, config, cancellationToken);
    return summary;
  }

  private static bool IsDue(RunnerConfig config, WebsiteConfig website)
  {
    var now = DateTimeOffset.UtcNow;
    var intervalDays = website.RenewalIntervalDays ?? config.RenewalIntervalDays;

    if (website.State.LastIssuedUtc is null)
      return true;

    if (website.State.LastIssuedUtc.Value.AddDays(intervalDays) <= now)
      return true;

    return website.State.ExpiresUtc is not null
        && website.State.ExpiresUtc.Value.AddDays(-config.RenewWhenExpiresWithinDays) <= now;
  }

  private static CertificateArtifacts LoadExistingArtifacts(WebsiteConfig website)
  {
    var directory = website.State.LastCertificateDirectory;
    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
    {
      throw new InvalidOperationException($"No existing certificate directory is recorded for '{website.Name}'.");
    }

    var certificatePath = Path.Combine(directory, "cert.pem");
    var chainPath = Path.Combine(directory, "chain.pem");
    var fullChainPath = Path.Combine(directory, "fullchain.pem");
    var privateKeyPath = Path.Combine(directory, "privkey.pem");
    var pfxPath = Path.Combine(directory, "certificate.pfx");

    foreach (var path in new[] { certificatePath, chainPath, fullChainPath, privateKeyPath })
    {
      if (!File.Exists(path))
        throw new FileNotFoundException($"Expected certificate artifact was not found: {path}", path);
    }

    return new CertificateArtifacts(
      directory,
      certificatePath,
      chainPath,
      fullChainPath,
      privateKeyPath,
      pfxPath,
      website.State.ExpiresUtc ?? DateTimeOffset.MinValue);
  }
}

public sealed record RenewalOptions(
  bool Force = false,
  bool UploadOnly = false,
  bool SkipUpload = false,
  bool ContinueOnError = true);

public sealed class RenewalSummary
{
  public int Processed { get; set; }

  public int Skipped { get; set; }

  public int Failed { get; set; }
}

