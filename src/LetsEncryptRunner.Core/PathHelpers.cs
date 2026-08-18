// Copyright Xeno Innovations, Inc. 2026
// See the LICENSE file in the project root for more information.

namespace LetsEncryptRunner.Core;

public static class PathHelpers
{
  public static string GetConfigDirectory(string configPath)
  {
    var fullPath = Path.GetFullPath(configPath);
    return Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
  }

  public static string ResolveFromConfig(string configPath, string path)
  {
    if (string.IsNullOrWhiteSpace(path))
      return path;

    return Path.IsPathFullyQualified(path)
      ? path
      : Path.GetFullPath(Path.Combine(GetConfigDirectory(configPath), path));
  }

  public static string SafeFileName(string value)
  {
    var invalid = Path.GetInvalidFileNameChars();
    var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
    return new string(chars).Replace('*', '_');
  }
}

