param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('win-x64','win-arm64')]
    [string]$Target
)
$ErrorActionPreference = 'Stop'
$ProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'release-targets.ps1')
. (Join-Path $PSScriptRoot 'release-provenance.ps1')

if ($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_EVENT_NAME -ne 'pull_request') {
    throw 'QA artifact export is restricted to GitHub pull-request CI.'
}
if ($env:GITHUB_HEAD_REF -notlike 'qa/*') {
    throw 'QA artifact export requires an internal qa/ head branch.'
}
$sourceCommit = Get-DeskMcpGitSourceCommit $ProjectRoot
if ($sourceCommit -ne ([string]$env:GITHUB_SHA).Trim().ToLowerInvariant()) {
    throw 'GitHub head and source provenance differ; refusing artifact export.'
}
$version = [string]((Get-Content -LiteralPath (Join-Path $ProjectRoot 'package.json') -Raw | ConvertFrom-Json).version)
$targetConfig = Get-DeskMcpReleaseTarget $Target
$setupName = Get-DeskMcpSetupName $version $targetConfig
$releaseDir = Join-Path $ProjectRoot 'runtime\release'
$setupFile = Join-Path $releaseDir $setupName
if (-not (Test-Path -LiteralPath $setupFile -PathType Leaf)) {
    throw ('Expected QA Setup is absent: ' + $setupFile)
}
if ((Get-Item -LiteralPath $setupFile).Length -lt 1MB) {
    throw 'QA Setup is unexpectedly small.'
}
$embeddedCommit = Get-DeskMcpSetupSourceCommit $setupFile
if ($embeddedCommit -ne $sourceCommit) {
    throw 'QA Setup embedded source commit does not match checked-out source.'
}
$hash = (Get-FileHash -LiteralPath $setupFile -Algorithm SHA256).Hash.ToLowerInvariant()
$shaName = 'QA-SHA256SUMS-' + $Target + '.txt'
$manifestName = 'QA-ARTIFACT-' + $Target + '.json'
$shaPath = Join-Path $releaseDir $shaName
$manifestPath = Join-Path $releaseDir $manifestName
[IO.File]::WriteAllText($shaPath, ($hash + '  ' + $setupName + [Environment]::NewLine), [Text.Encoding]::ASCII)
$meta = [ordered]@{
    qa_only = $true
    for_distribution = $false
    release_candidate = $false
    target = $Target
    version = $version
    source_commit = $sourceCommit
    artifact_name = $setupName
    sha256 = $hash
}
[IO.File]::WriteAllText($manifestPath, ($meta | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
if ((Get-Content -LiteralPath $shaPath -Raw).Trim() -ne ($hash + '  ' + $setupName)) {
    throw 'Generated QA SHA256SUMS verification failed.'
}
$roundTrip = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($roundTrip.qa_only -ne $true -or $roundTrip.for_distribution -ne $false -or
    $roundTrip.source_commit -ne $sourceCommit -or $roundTrip.sha256 -ne $hash) {
    throw 'QA metadata round-trip verification failed.'
}
Write-Output ('QA_ARTIFACT_SOURCE=' + $sourceCommit)
Write-Output ('QA_ARTIFACT_TARGET=' + $Target)
Write-Output ('QA_ARTIFACT_SHA256=' + $hash)
Write-Output ('QA_ARTIFACT_METADATA=PASS')
