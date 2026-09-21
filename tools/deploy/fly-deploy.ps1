<#
.SYNOPSIS
  Pull the latest branch and deploy it to Fly, in one command.

.DESCRIPTION
  The production origin is baked into nine files by tools/brand/set-origin.js — the app's own link-preview tags, both
  landing pages, the phone wrapper's config, and the docs that quote the address — because a link-preview tag and a
  canonical link cannot read a setting at runtime. That rewrite is a LOCAL edit: it belongs to this one deployment and
  not in the repository, which ships a placeholder so anybody else's deployment can do the same.

  Two of those nine are DEPLOY.md and README.md, which change often. That is why the collision was not a one-off.

  Committing that rewrite locally is what makes `git pull` collide, again and again, for the rest of the project. So
  this script treats it as a build step instead of as history:

      1. abandon any half-finished merge left over from a previous attempt
      2. make the working tree exactly the remote branch  (this DISCARDS local changes — see the warning below)
      3. re-apply the origin
      4. deploy

  WARNING: step 2 throws away anything you changed here. That is the point — this folder is a deployment checkout, not
  a place to write code. If you ever do edit something locally and want to keep it, commit and push it first.

.EXAMPLE
  .\tools\deploy\fly-deploy.ps1
#>
[CmdletBinding()]
param(
  [string] $Origin = 'https://orevosh-or.fly.dev',
  [string] $Branch = 'claude/fitcheck-phase-1-bxkyvx'
)

$ErrorActionPreference = 'Stop'

function Step([string] $text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Fail([string] $text) { Write-Host "`n$text" -ForegroundColor Red; exit 1 }

# Run from the repository root however the script was invoked.
Set-Location (Resolve-Path (Join-Path $PSScriptRoot '..\..'))

foreach ($tool in 'git', 'node', 'fly') {
  if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
    Fail "$tool is not installed, or not on PATH. Open a new terminal and try again; if it still says this, reinstall $tool."
  }
}

Step 'Clearing anything left half-done'
# A merge left unfinished by an earlier run blocks everything after it. No merge in progress is not an error here.
git merge --abort 2>$null | Out-Null

Step "Fetching $Branch"
git fetch origin $Branch
if ($LASTEXITCODE -ne 0) { Fail 'Could not reach GitHub. Check the internet connection and try again.' }

Step 'Matching the branch exactly (local changes here are discarded)'
git reset --hard FETCH_HEAD
if ($LASTEXITCODE -ne 0) { Fail 'git reset failed. Send me the output above.' }

Step "Pointing the app at $Origin"
node tools/brand/set-origin.js $Origin
if ($LASTEXITCODE -ne 0) { Fail 'set-origin failed. Send me the output above.' }

node tools/brand/set-origin.js --check
if ($LASTEXITCODE -ne 0) { Fail 'A placeholder host is still in the shipped pages. Send me the output above.' }

Step 'Deploying to Fly'
fly deploy --ha=false
if ($LASTEXITCODE -ne 0) {
  Fail @"
The deploy failed.

If it says 'unauthorized', your Fly session has expired — this happens every few hours. Run:

    fly auth login

and then this script again. Anything else: send me the output above.
"@
}

Write-Host "`nDone. https://orevosh-or.fly.dev/" -ForegroundColor Green
Write-Host 'The origin rewrite stays uncommitted on purpose: next time, just run this script again.'
