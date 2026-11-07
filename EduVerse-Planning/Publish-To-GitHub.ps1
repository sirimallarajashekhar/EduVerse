# Publish EduVerse planning files and 19 phased GitHub issues.
# Requirements: Git and GitHub CLI (gh), with `gh auth login` completed.
# Run: powershell -ExecutionPolicy Bypass -File .\Publish-To-GitHub.ps1
$ErrorActionPreference = 'Stop'
$repo = 'sirimallarajashekhar/EduVerse'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
foreach ($app in @('git','gh')) {
  if (-not (Get-Command $app -ErrorAction SilentlyContinue)) { throw "$app is required. Install it before running this script." }
}
& gh auth status | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Run gh auth login first.' }
$repoInfo = & gh repo view $repo --json nameWithOwner,defaultBranchRef | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Repository cannot be accessed.' }
if ($repoInfo.defaultBranchRef.name -ne 'main') { throw 'Expected default branch main.' }
$temp = Join-Path $env:TEMP ('EduVerse-Planning-' + [guid]::NewGuid().ToString('N'))
try {
  & git clone "https://github.com/$repo.git" $temp
  if ($LASTEXITCODE -ne 0) { throw 'Could not clone repository.' }
  $docsDir = Join-Path $temp 'docs'
  New-Item -ItemType Directory -Path $docsDir -Force | Out-Null
  Copy-Item (Join-Path $root 'docs\ROADMAP.md') (Join-Path $docsDir 'ROADMAP.md') -Force
  $repoReadme = Join-Path $temp 'README.md'
  if (-not (Test-Path $repoReadme)) { Copy-Item (Join-Path $root 'README.md') $repoReadme }
  Push-Location $temp
  try {
    & git add README.md docs/ROADMAP.md
    & git diff --cached --quiet
    if ($LASTEXITCODE -ne 0) {
      & git commit -m 'docs: add EduVerse architecture and phase-wise roadmap'
      if ($LASTEXITCODE -ne 0) { throw 'Git commit failed. Check git user.name and user.email.' }
      & git push origin main
      if ($LASTEXITCODE -ne 0) { throw 'Could not push to main. Check branch protection/permissions.' }
    }
  } finally { Pop-Location }
  $roadmap = Get-Content (Join-Path $root 'docs\ROADMAP.md') -Raw -Encoding UTF8
  $sections = [regex]::Matches($roadmap, '(?ms)^## Phase (\d{2}) — (.+?)\r?\n(.*?)(?=^## Phase \d{2} — |\z)')
  foreach ($section in $sections) {
    $phaseNumber = $section.Groups[1].Value
    $title = "Phase $phaseNumber - " + $section.Groups[2].Value.Trim()
    $body = "## Tasks`n" + $section.Groups[3].Value.Trim() + "`n`nReference: docs/ROADMAP.md"
    $existing = & gh issue list --repo $repo --state all --limit 200 --json title | ConvertFrom-Json
    if ($existing.title -contains $title) { Write-Host "Skipping existing issue: $title"; continue }
    & gh issue create --repo $repo --title $title --body $body
    if ($LASTEXITCODE -ne 0) { throw "Issue creation failed: $title" }
  }
  Write-Host 'Finished publishing EduVerse roadmap and phase issues.'
} finally {
  if (Test-Path $temp) { Remove-Item -Path $temp -Force -Recurse }
}
