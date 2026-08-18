// Copyright Xeno Innovations, Inc. 2026
// See the LICENSE file in the project root for more information.

namespace LetsEncryptRunner.Core.Configuration;

public sealed class RunnerConfig
{
  public int RenewalIntervalDays { get; set; } = 87;

  public int RenewWhenExpiresWithinDays { get; set; } = 14;

  public bool UseLetsEncryptStaging { get; set; } = true;

  public string AccountKeyPath { get; set; } = "accounts/letsencrypt-account.pem";

  public string CertificateStorePath { get; set; } = "certificates";

  public string PfxPasswordEnvironmentVariable { get; set; } = "LETSENCRYPT_RUNNER_PFX_PASSWORD";

  public List<WebsiteConfig> Websites { get; set; } = [];
}

public sealed class WebsiteConfig
{
  public string Name { get; set; } = string.Empty;

  public bool Enabled { get; set; } = true;

  public List<string> DomainNames { get; set; } = [];

  public string EmailAddress { get; set; } = string.Empty;

  public int? RenewalIntervalDays { get; set; }

  public HttpChallengeConfig HttpChallenge { get; set; } = new();

  public DeploymentConfig Deployment { get; set; } = new();

  public CertificateState State { get; set; } = new();
}

public sealed class HttpChallengeConfig
{
  public string WebRootPath { get; set; } = string.Empty;

  public bool ValidateChallengeFileBeforeSubmit { get; set; } = true;

  public bool CleanUpChallengeFiles { get; set; } = true;
}

public sealed class DeploymentConfig
{
  public string Type { get; set; } = DeploymentTypes.None;

  public CPanelDeploymentConfig CPanel { get; set; } = new();
}

public sealed class CPanelDeploymentConfig
{
  public string BaseUrl { get; set; } = "https://your-cpanel-host.example.com:2083";

  public string Username { get; set; } = string.Empty;

  public string ApiTokenEnvironmentVariable { get; set; } = "CPANEL_API_TOKEN";

  public string? DomainNameOverride { get; set; }

  public bool AllowInvalidTlsCertificate { get; set; }
}

public sealed class CertificateState
{
  public DateTimeOffset? LastIssuedUtc { get; set; }

  public DateTimeOffset? ExpiresUtc { get; set; }

  public DateTimeOffset? LastRenewalAttemptUtc { get; set; }

  public DateTimeOffset? LastSuccessfulUploadUtc { get; set; }

  public string? LastCertificateDirectory { get; set; }

  public string? LastError { get; set; }
}

public static class DeploymentTypes
{
  public const string None = "None";
  public const string CPanel = "CPanel";
}
