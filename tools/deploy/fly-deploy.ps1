<#
.SYNOPSIS
  Pull the latest branch and deploy it to Fly, in one command.

.DESCRIPTION
  Two facts belong to THIS deployment and not to the repository, and both have to be re-applied after every pull:

    * The production origin. tools/brand/set-origin.js writes it into nine files - the app's own link-preview tags,
      both landing pages, the phone wrapper's config, and the five docs that quote the address - because a crawler
      reading a canonical link never runs our code, so it cannot be a setting. Two of the nine are DEPLOY.md and
      README.md, the files that change most.
    * The Fly app's name in fly.toml. The repository ships "orevosh-yourname" so anybody can deploy their own.

  Committing either one locally is the obvious thing to do and the wrong one: every pull then becomes a merge, and a
  merge that stops half-way blocks everything behind it. So this script treats them as build steps instead:

      1. check the Fly session is alive, before spending three minutes finding out it is not
      2. abandon a merge left half-finished by an earlier attempt
      3. make the working tree exactly the remote branch  (this DISCARDS local changes - see the warning)
      4. put this deployment's app name back into fly.toml, so `fly ...` typed by hand also finds the app
      5. put this deployment's origin back into the nine files
      6. deploy

  WARNING: step 2 throws away anything you changed here. That is the point - this folder is a deployment checkout, not
  a place to write code. If you ever do edit something here and want to keep it, commit and push it first.

.EXAMPLE
  .\tools\deploy\fly-deploy.ps1
#>
[CmdletBinding()]
param(
  [string] $App    = 'orevosh-or',
  # The real domain, since Round 17. The app still ANSWERS on orevosh-or.fly.dev (AllowedHosts is *), but this is the
  # address it puts in its own link-preview tags, its canonical links, the landing pages and the mail it sends - and
  # those have to be the name people actually see, or a shared link previews as somebody else's host.
  [string] $Origin = 'https://orevosh.com',
  [string] $Branch = 'claude/fitcheck-phase-1-bxkyvx'
)

# NOT 'Stop': git and fly are native programs, and several of them write ordinary progress to stderr. Under 'Stop'
# PowerShell turns that into a fatal error even when the command succeeded - which is exactly what `git merge --abort`
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

# The Fly session, BEFORE anything expensive. flyctl makes you log in again 30 days after your last login whatever
# else is true (internal/command.TokenTimeout), and a token can also stop working earlier, so this comes up. This is not a guess about which check is meaningful: flyctl signs the
# calls that set up and finish a build (internal/uiex/builders.go - CreateBuild, FinishBuild, EnsureDepotBuilder) with
# cfg.Tokens.GraphQL(), and `fly auth whoami` asks the same API with the same token. So a whoami that works means those
# calls will authenticate, and a whoami that fails means the deploy was going to fail three minutes from now instead.
#
# It also explains a failure that looks impossible: the image can build and every layer can PUSH successfully and the
# deploy still ends in 401, because the push runs on a separate build token the builder handed out earlier
# (depotbuild.FromExistingBuild with the BuildToken), while the calls around it use the session token. A dead session
# with a live build token gives you exactly that - a perfect build and a 401 at the end.
Step 'Checking the Fly session'

# A token in the environment REPLACES the one `fly auth login` writes (flyctl's config.applyEnv: FLY_ACCESS_TOKEN,
# then FLY_API_TOKEN, either one wins over the file). If a stale one is set, logging in again changes nothing and the
# 401 comes back looking identical - so say it here rather than let it be debugged twice.
foreach ($name in 'FLY_ACCESS_TOKEN', 'FLY_API_TOKEN') {
  if ([Environment]::GetEnvironmentVariable($name)) {
    Write-Host "    WARNING: $name is set in this terminal. flyctl uses it INSTEAD of the account you log in as." -ForegroundColor Yellow
    Write-Host "    If the deploy fails with 'unauthorized', clear it first:  Remove-Item Env:\$name" -ForegroundColor Yellow
  }
}

# --json is not for the output, it is to keep this non-interactive. Without it, flyctl's RequireSession asks "Would
# you like to sign in?" on a terminal and waits - and since this captures the command's output, the question would be
# invisible and the script would look frozen. With --json it returns an error instead, which is what a check wants.
$whoLines = @(fly auth whoami --json 2>&1 | ForEach-Object { $_.ToString() })
$whoExit = $LASTEXITCODE
$who = (($whoLines -join ' ').Trim())
if ($whoExit -ne 0) {
  Fail @"
Your Fly session has expired, so the deploy would have failed at the end. Nothing has been changed. Run:

    fly auth login

(a browser opens; approve it) and then this script again.

Fly said: $who
"@
}
# {"email":"you@example.com"} -> you@example.com, and the raw line if it ever stops looking like that.
$email = if ($who -match '"email"\s*:\s*"([^"]+)"') { $Matches[1] } else { $who }
Write-Host "    signed in to Fly as $email"

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
# NOT Set-Content -Encoding utf8: in Windows PowerShell 5.1 that writes a UTF-8 BOM, and fly's TOML parser reads those
# three bytes as the first characters of the first key ("invalid character at start of key: U+00EF"). .NET's own writer
# with encoderShouldEmitUTF8Identifier = $false is the one way to say UTF-8 and mean it on both PowerShell 5 and 7.
[System.IO.File]::WriteAllText($tomlPath, $named, (New-Object System.Text.UTF8Encoding $false))

# Read it back the way fly will. A byte-order mark here cost a deploy once; it does not get a second chance.
$written = [System.IO.File]::ReadAllBytes($tomlPath)
if ($written.Length -ge 3 -and $written[0] -eq 0xEF -and $written[1] -eq 0xBB -and $written[2] -eq 0xBF) {
  Fail 'fly.toml was written with a byte-order mark, which fly cannot parse. Send me this message.'
}
if (-not ((Get-Content -LiteralPath $tomlPath -Raw) -match [regex]::Escape('app = "' + $App + '"'))) {
  Fail 'fly.toml does not name the app after writing it. Send me this message.'
}

Step "Pointing the app at $Origin"
node tools/brand/set-origin.js $Origin
if ($LASTEXITCODE -ne 0) { Fail 'set-origin failed. Send me the output above.' }

node tools/brand/set-origin.js --check
if ($LASTEXITCODE -ne 0) { Fail 'A placeholder host is still in the shipped pages. Send me the output above.' }

Step 'Deploying to Fly'
fly deploy --ha=false --app $App
if ($LASTEXITCODE -ne 0) {
  # The session was good a minute ago - this script just checked it - so an 'unauthorized' here is not simply a stale
  # login. Two things end a deploy in 401 after a clean whoami: the token expiring or failing to re-discharge during
  # the deploy (flyctl refreshes discharge tokens in the background and gives up quietly when the network drops one),
  # and Fly's build API being unhappy on its side. Both are usually gone on a second run, which is why that is first.
  Fail @"
The deploy failed. The build itself may well have worked - look for 'pushing layer' above. If it is there, the image
was built and uploaded and only the last step failed.

1. Run this script again. Most of these clear on a second run, and the build is cached, so it is quick.

2. Still 'unauthorized'? The session died mid-deploy. Run:

       fly auth login

   and then this script again.

3. Still failing, and the error mentions 'depot builder'? That is Fly's shared build service, not your app. Skip it:

       fly deploy --ha=false --app $App --depot=false

   This builds on a builder machine in your own Fly account instead (Fly creates one the first time, and it costs a
   little). Use it to get unstuck; go back to this script afterwards.

Anything else: send me the output above.
"@
}

Write-Host "`nDone. https://$App.fly.dev/" -ForegroundColor Green
Write-Host "fly.toml now names $App again, so `fly volumes list`, `fly logs` and the rest work in this folder too."
