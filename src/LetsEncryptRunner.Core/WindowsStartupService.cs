// Copyright Xeno Innovations, Inc. 2026
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace LetsEncryptRunner.Core;

public sealed class WindowsStartupService
{
  private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

  [SupportedOSPlatform("windows")]
  public void Install(string appName, string executablePath, string arguments)
  {
    EnsureWindows();

    using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
      ?? throw new InvalidOperationException("Unable to open current user Windows Run registry key.");

    key.SetValue(appName, $"\"{executablePath}\" {arguments}");
  }

  [SupportedOSPlatform("windows")]
  public void Uninstall(string appName)
  {
    EnsureWindows();

    using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
      ?? throw new InvalidOperationException("Unable to open current user Windows Run registry key.");

    key.DeleteValue(appName, throwOnMissingValue: false);
  }

  private static void EnsureWindows()
  {
    if (!OperatingSystem.IsWindows())
    {
      throw new PlatformNotSupportedException("Windows startup registration is only supported on Windows.");
    }
  }
}
