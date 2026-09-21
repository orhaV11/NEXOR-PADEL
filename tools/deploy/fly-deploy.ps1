<#
.SYNOPSIS
  Pull the latest branch and deploy it to Fly, in one command.

.DESCRIPTION
  Two facts belong to THIS deployment and not to the repository, and both have to be re-applied after every pull:

    * The production origin. tools/brand/set-origin.js writes it into nine files — the app's own link-preview tags,
      both landing pages, the phone wrapper's config, and the five docs that quote the address — because a crawler
      reading a canonical link never runs our code, so it cannot be a setting. Two of the nine are DEPLOY.md and
      README.md, the files that change most.
    * The Fly app's name in fly.toml. The repository ships "orevosh-yourname" so anybody can deploy their own.

  Committing either one locally is the obvious thing to do and the wrong one: every pull then becomes a merge, and a
  merge that stops half-way blocks everything behind it. So this script treats them as build steps instead:

      1. abandon a merge left half-finished by an earlier attempt
      2. make the working tree exactly the remote branch  (this DISCARDS local changes — see the warning)
      3. put this deployment's app name back into fly.toml, so `fly ...` typed by hand also finds the app
      4. put this deployment's origin back into the nine files
      5. deploy

  WARNING: step 2 throws away anything you changed here. That is the point — this folder is a deployment checkout, not
  a place to write code. If you ever do edit something here and want to keep it, commit and push it first.

.EXAMPLE
  .\tools\deploy\fly-deploy.ps1
#>
[CmdletBinding()]
param(
  [string] $App    = 'orevosh-or',
  [string] $Origin = 'https://orevosh-or.fly.dev',
  [string] $Branch = 'claude/fitcheck-phase-1-bxkyvx'
)

# NOT 'Stop': git and fly are native programs, and several of them write ordinary progress to stderr. Under 'Stop'
# PowerShell turns that into a fatal error even when the command succeeded — which is exactly what `git merge --abort`
# with no merge in progress did to the first version of this script. Native tools report through $LASTEXITCODE, and
# every call below checks it.
$ErrorActionPreference = 'Continue'

function Step([string] $text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Fail([string] $text) { Write-Host "`n$text" -ForegroundColor Red; exit 1 }

Set-Location (Resolve-Path (Join-Path $PSScriptRoot '..\..'))

foreach ($tool in 'git', 'node', 'fly') {
  if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
    Fail "$tool is not installed, or not on PATH. Open a new terminal and try again; if it still says this, reinstall $tool."
  }
}

Step 'Clearing anything left half-done'
# Ask whether a merge is in progress rather than trying and ignoring the failure: quieter, and it cannot mask a real one.
$gitDir = (git rev-parse --git-dir 2>$null)
if ($LASTEXITCODE -ne 0) { Fail 'This folder is not a git checkout. Are you in the right directory?' }
if (Test-Path (Join-Path $gitDir 'MERGE_HEAD')) {
  git merge --abort
  if ($LASTEXITCODE -ne 0) { Fail 'Could not abandon the half-finished merge. Send me the output above.' }
  Write-Host '    a half-finished merge was abandoned'
} else {
  Write-Host '    nothing to clear'
}

Step "Fetching $Branch"
git fetch origin $Branch
if ($LASTEXITCODE -ne 0) { Fail 'Could not reach GitHub. Check the internet connection and try again.' }

Step 'Matching the branch exactly (local changes here are discarded)'
git reset --hard FETCH_HEAD
if ($LASTEXITCODE -ne 0) { Fail 'git reset failed. Send me the output above.' }

Step "Naming this deployment's app in fly.toml: $App"
$tomlPath = Join-Path (Get-Location) 'fly.toml'
$toml = Get-Content -LiteralPath $tomlPath -Raw
$named = [regex]::Replace($toml, '(?m)^app\s*=\s*".*"\s*$', ('app = "' + $App + '"'))
if ($named -notmatch [regex]::Escape('app = "' + $App + '"')) {
  Fail "Could not find the app line in fly.toml. Send me the first few lines of that file."
}
Set-Content -LiteralPath $tomlPath -Value $named -NoNewline -Encoding utf8

Step "Pointing the app at $Origin"
node tools/brand/set-origin.js $Origin
if ($LASTEXITCODE -ne 0) { Fail 'set-origin failed. Send me the output above.' }

node tools/brand/set-origin.js --check
if ($LASTEXITCODE -ne 0) { Fail 'A placeholder host is still in the shipped pages. Send me the output above.' }

Step 'Deploying to Fly'
fly deploy --ha=false --app $App
if ($LASTEXITCODE -ne 0) {
  Fail @"
The deploy failed.

If it says 'unauthorized', your Fly session has expired — this happens every few hours. Run:

    fly auth login

and then this script again. Anything else: send me the output above.
"@
}

Write-Host "`nDone. https://$App.fly.dev/" -ForegroundColor Green
Write-Host "fly.toml now names $App again, so `fly volumes list`, `fly logs` and the rest work in this folder too."
