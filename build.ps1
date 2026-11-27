$ErrorActionPreference = 'Stop'

$solutionPath = Join-Path $PSScriptRoot 'LetsEncryptRunner.slnx'
$publishRoot = Join-Path $PSScriptRoot 'publish'
[xml]$solution = Get-Content -LiteralPath $solutionPath -Raw

foreach ($project in $solution.SelectNodes('//Project[@Path]'))
{
  $projectPath = Join-Path $PSScriptRoot $project.GetAttribute('Path')
  $projectName = [IO.Path]::GetFileNameWithoutExtension($projectPath)
  $publishPath = Join-Path $publishRoot $projectName

  dotnet publish $projectPath --configuration Release --self-contained false --output $publishPath

  if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
  }
}

exit 0
