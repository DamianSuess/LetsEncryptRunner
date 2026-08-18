# LetsEncryptRunner

Two C# console applications for issuing Let's Encrypt certificates with HTTP-01 validation and renewing/uploading them on a configurable interval.

## Projects

- `LetsEncryptRunner.Setup`: creates/updates the JSON config and can issue certificates immediately.
- `LetsEncryptRunner.RenewalRunner`: runs once, renews due certificates, uploads them, updates state in the JSON config, then exits.
- `LetsEncryptRunner.Core`: shared config, ACME, certificate artifact, renewal, and deployment logic.

## Important GoDaddy Note

GoDaddy's public developer API manages domains/DNS and GoDaddy certificate orders. A third-party Let's Encrypt certificate is normally installed on the hosting account or server, not on the domain registration itself.

This project includes a `CPanel` deployment target because GoDaddy Linux hosting commonly exposes cPanel. It uploads to cPanel UAPI `SSL/install_ssl` with:

- `domain`
- `cert`
- `key`
- `cabundle`

If your GoDaddy product is not cPanel-based, keep `"deployment": { "type": "None" }` and add a deployment target for that hosting API.

## HTTP-01 Requirements

HTTP-01 validation requires every configured domain to serve:

```text
http://<domain>/.well-known/acme-challenge/<token>
```

The `httpChallenge.webRootPath` must point to the website root that serves that path publicly on port 80. Wildcard certificates are not supported by HTTP-01.

## Create A Config

Generate an example:

```powershell
dotnet run --project src\LetsEncryptRunner.Setup -- --config config.example.json --sample
```

Create your real `sites.json` interactively:

```powershell
dotnet run --project src\LetsEncryptRunner.Setup -- --config sites.json --add
```

The key inputs are:

- `Domain Name(s)`: comma-separated, for example `example.com,www.example.com`
- `Email address`: Let's Encrypt account/contact email
- `HTTP web root path`: website root where `/.well-known/acme-challenge` can be served

The default config uses Let's Encrypt staging first. After a successful staging test, set this in `sites.json`:

```json
"useLetsEncryptStaging": false
```

## Issue Now

Issue certificates without uploading:

```powershell
dotnet run --project src\LetsEncryptRunner.Setup -- --config sites.json --issue
```

Issue and upload for sites with a configured deployment target:

```powershell
dotnet run --project src\LetsEncryptRunner.Setup -- --config sites.json --issue --upload
```

Artifacts are saved under `certificateStorePath`:

- `cert.pem`
- `chain.pem`
- `fullchain.pem`
- `privkey.pem`
- `certificate.pfx`

Set an optional PFX password with:

```powershell
[Environment]::SetEnvironmentVariable("LETSENCRYPT_RUNNER_PFX_PASSWORD", "your-pfx-password", "User")
```

## Configure cPanel Upload

Create a cPanel API token in your hosting account and store it as an environment variable:

```powershell
[Environment]::SetEnvironmentVariable("CPANEL_API_TOKEN", "your-cpanel-token", "User")
```

Then configure each site's deployment:

```json
"deployment": {
  "type": "CPanel",
  "cPanel": {
    "baseUrl": "https://your-cpanel-host.example.com:2083",
    "username": "cpanel-user",
    "apiTokenEnvironmentVariable": "CPANEL_API_TOKEN",
    "domainNameOverride": "example.com",
    "allowInvalidTlsCertificate": false
  }
}
```

## Renewal Runner

Run once. It renews only when due, uploads configured sites, writes state to `sites.json`, and exits:

```powershell
dotnet run --project src\LetsEncryptRunner.RenewalRunner -- --config sites.json
```

Force renewal/upload regardless of interval:

```powershell
dotnet run --project src\LetsEncryptRunner.RenewalRunner -- --config sites.json --force
```

Upload the most recently saved certificate without issuing a new one:

```powershell
dotnet run --project src\LetsEncryptRunner.RenewalRunner -- --config sites.json --upload-only --force
```

The default interval is 87 days:

```json
"renewalIntervalDays": 87
```

You can override it per site with `website.renewalIntervalDays`.

## Start With Windows

Publish the renewal runner:

```powershell
dotnet publish src\LetsEncryptRunner.RenewalRunner -c Release -r win-x64 --self-contained false -o output\RenewalRunner
```

Register it for the current user's Windows Startup:

```powershell
.\output\RenewalRunner\LetsEncryptRunner.RenewalRunner.exe --config C:\dev\labs\LetsEncryptRunner\sites.json --install-startup
```

At login, the runner starts, checks whether any site is due based on `renewalIntervalDays`, performs renewal/upload if needed, and closes.

Remove the startup entry:

```powershell
.\output\RenewalRunner\LetsEncryptRunner.RenewalRunner.exe --uninstall-startup
```

## Run Every 87 Days With Task Scheduler

The runner can also create Windows Scheduled Tasks. It creates one task for `renewalIntervalDays` and one at-logon task. Both run once and exit.

```powershell
.\output\RenewalRunner\LetsEncryptRunner.RenewalRunner.exe --config C:\dev\labs\LetsEncryptRunner\sites.json --install-scheduled-task
```

Remove the scheduled tasks:

```powershell
.\output\RenewalRunner\LetsEncryptRunner.RenewalRunner.exe --uninstall-scheduled-task
```

## Future Improvements

* Remove System.Linq dependency
* Build AOT for device specific and zero-dependency builds

## References

- Let's Encrypt HTTP-01 challenge docs: https://letsencrypt.org/docs/challenge-types/
- Certes ACME client: https://github.com/fszlin/certes
- cPanel `SSL/install_ssl` API: https://api.docs.cpanel.net/openapi/cpanel/operation/install_ssl/
- GoDaddy Certificates API docs: https://developer.godaddy.com/doc/endpoint/certificates
