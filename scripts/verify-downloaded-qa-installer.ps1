param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('win-x64','win-arm64')]
    [string]$Target,
    [Parameter(Mandatory=$true)]
    [string]$ArtifactDirectory
)
$ErrorActionPreference = 'Stop'
$ProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'release-targets.ps1')
. (Join-Path $PSScriptRoot 'release-provenance.ps1')

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

# This script is for independent GitHub-hosted PR validation, never for a live installation.
Require ($env:GITHUB_ACTIONS -eq 'true' -and $env:GITHUB_EVENT_NAME -eq 'pull_request') 'QA artifact verification requires a GitHub PR runner.'
Require ($env:GITHUB_HEAD_REF -like 'qa/*') 'QA artifact verification requires an internal qa/ PR.'
Require (-not [string]::IsNullOrWhiteSpace($env:GITHUB_SHA)) 'GITHUB_SHA is unavailable.'
$ExpectedCommit = ([string]$env:GITHUB_SHA).Trim().ToLowerInvariant()
Require ($ExpectedCommit -match '^[0-9a-f]{40}$') 'GitHub source commit is invalid.'
$CurrentCommit = Get-DeskMcpGitSourceCommit $ProjectRoot
Require ($CurrentCommit -eq $ExpectedCommit) 'Checkout does not match GitHub workflow source.'

$ExpectedArchitecture = if ($Target -eq 'win-arm64') { 'ARM64' } else { 'AMD64' }
Require ([string]$env:PROCESSOR_ARCHITECTURE -eq $ExpectedArchitecture) 'Downloaded setup must be verified by a native Windows runner.'
$ExpectedVersion = [string]((Get-Content -LiteralPath (Join-Path $ProjectRoot 'package.json') -Raw | ConvertFrom-Json).version)
$TargetConfig = Get-DeskMcpReleaseTarget $Target
$SetupName = Get-DeskMcpSetupName $ExpectedVersion $TargetConfig
$Directory = [IO.Path]::GetFullPath($ArtifactDirectory)
Require (Test-Path -LiteralPath $Directory -PathType Container) 'Downloaded QA artifact folder is missing.'
$ManifestName = 'QA-ARTIFACT-' + $Target + '.json'
$SumsName = 'QA-SHA256SUMS-' + $Target + '.txt'
$ExpectedFiles = @($SetupName, $ManifestName, $SumsName) | Sort-Object
$ActualFiles = @(Get-ChildItem -LiteralPath $Directory -Force -File | Select-Object -ExpandProperty Name | Sort-Object)
Require ($ActualFiles.Count -eq 3) 'QA archive must contain exactly three expected files.'
Require (([string]::Join('|', $ActualFiles)) -ceq ([string]::Join('|', $ExpectedFiles))) 'QA archive file list is unexpected.'
Require (@(Get-ChildItem -LiteralPath $Directory -Force -Directory).Count -eq 0) 'QA archive has unexpected directories.'
$Setup = Join-Path $Directory $SetupName
$ManifestPath = Join-Path $Directory $ManifestName
$SumsPath = Join-Path $Directory $SumsName
Require ((Get-Item -LiteralPath $Setup).Length -gt 1MB) 'Downloaded setup is unexpectedly small.'

$Metadata = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
Require ($Metadata.qa_only -eq $true -and $Metadata.for_distribution -eq $false -and $Metadata.release_candidate -eq $false) 'QA-only metadata flags are invalid.'
Require ([string]$Metadata.target -ceq $Target) 'QA target mismatch.'
Require ([string]$Metadata.version -ceq $ExpectedVersion) 'QA release version mismatch.'
Require ([string]$Metadata.artifact_name -ceq $SetupName) 'QA filename mismatch.'
Require ([string]$Metadata.source_commit -ceq $ExpectedCommit) 'QA source provenance mismatch.'
$ExpectedHash = ([string]$Metadata.sha256).Trim().ToLowerInvariant()
Require ($ExpectedHash -match '^[0-9a-f]{64}$') 'QA metadata SHA256 is invalid.'
$ActualHash = (Get-FileHash -LiteralPath $Setup -Algorithm SHA256).Hash.ToLowerInvariant()
Require ($ActualHash -ceq $ExpectedHash) 'Downloaded setup SHA256 mismatches QA metadata.'
$SumsText = (Get-Content -LiteralPath $SumsPath -Raw).Trim()
Require ($SumsText -ceq ($ExpectedHash + '  ' + $SetupName)) 'Downloaded checksum file disagrees with setup SHA256.'
$EmbeddedCommit = Get-DeskMcpSetupSourceCommit $Setup
Require ($EmbeddedCommit -ceq $ExpectedCommit) 'Downloaded setup embedded commit differs from its build source.'

Write-Output ('QA_DOWNLOADED_TARGET=' + $Target)
Write-Output ('QA_DOWNLOADED_SOURCE=' + $ExpectedCommit)
Write-Output ('QA_DOWNLOADED_SETUP_SHA256=' + $ActualHash)
Write-Output 'QA_DOWNLOADED_INSTALLER_VERIFIED=PASS'
