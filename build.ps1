$ErrorActionPreference = 'Stop'

$solutionPath = Join-Path $PSScriptRoot 'LetsEncryptRunner.slnx'
$publishRoot = Join-Path $PSScriptRoot 'publish'
[xml]$solution = Get-Content -LiteralPath $solutionPath -Raw

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
