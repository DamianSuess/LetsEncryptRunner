$ErrorActionPreference = 'Stop'

$solutionPath = Join-Path $PSScriptRoot 'LetsEncryptRunner.slnx'
$publishRoot = Join-Path $PSScriptRoot 'publish'
[xml]$solution = Get-Content -LiteralPath $solutionPath -Raw

if (Test-Path -LiteralPath $publishRoot) {
  $publishDirectory = Get-Item -LiteralPath $publishRoot -Force
  if ($publishDirectory.FullName -ne $publishRoot -or !$publishDirectory.PSIsContainer -or
      ($publishDirectory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw "Expected a local publish directory at '$publishRoot'."
  }

  foreach ($item in Get-ChildItem -LiteralPath $publishDirectory.FullName -Force) {
    if ($item.PSIsContainer) {
      # Directory.Delete skips the targets of junctions and symbolic links.
      [IO.Directory]::Delete($item.FullName, $true)
    } else {
      Remove-Item -LiteralPath $item.FullName -Force
    }
  }
}

foreach ($project in $solution.SelectNodes('//Project[@Path]'))
{
  $projectPath = Join-Path $PSScriptRoot $project.GetAttribute('Path')

  dotnet publish $projectPath --configuration Release --self-contained false --output $publishRoot -p:CopyOutputSymbolsToPublishDirectory=false

  if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
  }
}

# Project references can still contribute symbols to the shared publish folder.
Get-ChildItem -LiteralPath $publishRoot -Filter '*.pdb' -File | ForEach-Object {
  Remove-Item -LiteralPath $_.FullName
}

exit 0
