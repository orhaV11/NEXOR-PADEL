<#
.SYNOPSIS
  Runs the stylist steadiness pass (tools/eval/stylist.js) from a Windows machine against the live server and
  answers with one Hebrew line.

.DESCRIPTION
  Written for the PowerShell that ships with Windows (5.1); it also runs under pwsh 7, which is what CI uses for
  its dry run. It finds node, checks it is 18 or newer (stylist.js uses fetch and FormData, which Node 18 brought),
  takes every .jpg in -Photos, prints the arithmetic of what the run will cost, sends the photos through
  stylist.js against -Base (https://orevosh.com by default), echoes every line as it comes, files a dated report
  under tools/eval/reports/ (the tables, the masked command, the base, the count, plus the rows as JSON beside
  it), and ends with a Hebrew verdict in green or red. The exit code is stylist.js's own: 0 within the allowed
  spread, 1 too wide or no verdict, 2 the run could not be made.

  THIS SPENDS REAL MONEY. Every run is one model call on the owner's key: photos x runs calls, about one to two
  US cents each, and each call is one check out of the account's day. The account signed in as needs an
  allowance of at least photos x runs checks that day (a Pro account: fly ssh console -u app -C "dotnet
  /app/FitCheck.Api.dll --pro <handle> 1"), and Limits__SpendPerDayUsd on the server is the ceiling the app stops
  itself at. A Pro day is 30 checks at the server's default settings, so at 8 runs three photos fit in a day. The
  script prints the arithmetic and goes on: a script that stops to ask looks frozen. It stops before signing in
  only when the pass is more than a Pro day (-Force goes on, for a server whose caps were raised).

  The password is never taken on the command line unless you insist (-Password): a plain password there lands
  in PSReadLine's history file. Set OREVOSH_EVAL_PASSWORD, or let the script ask for it, masked. It reaches
  node through OREVOSH_EVAL_PASSWORD too, never as an argument: 5.1 does not quote a " inside an argument, and
  an argument is in the process list for as long as the run lasts.

.EXAMPLE
  tools\eval\calibrate.ps1 -Photos C:\looks\calibration -Occasion date -Language he

.EXAMPLE
  tools\eval\calibrate.ps1 -Photos .\looks -DryRun
  Prints the exact command with the password masked and exits 0 without signing in. A relative -Photos is read
  from where you are, and the script leaves you there.

.NOTES
  The file is saved as UTF-8 with a byte-order mark on purpose: Windows PowerShell 5.1 reads a .ps1 without one
  in the system code page and the Hebrew below would come out as boxes. The console must be on UTF-8 as well
  ([Console]::OutputEncoding is set below) and the window's font must carry Hebrew glyphs; the report file is
  the durable record either way, written UTF-8 without a mark.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$Photos,
  [ValidateSet('everyday', 'date', 'office', 'party', 'formal', 'sport')][string]$Occasion = 'everyday',
  [ValidateSet('none', 'streetwear', 'old-money', 'minimal', 'classic')][string]$Style = 'none',
  [ValidateSet('en', 'he', 'ar', 'ru')][string]$Language = 'en',
  [int]$Runs = 8,
  [double]$MaxSpread = 2,
  [string]$Base = 'https://orevosh.com',
  [string]$Handle = $env:OREVOSH_EVAL_HANDLE,
  [string]$Password = $env:OREVOSH_EVAL_PASSWORD,
  [string]$Note = '',
  [int]$Delay = 0,
  [switch]$Force,
  [switch]$DryRun
)

# A native command's failure is read off $LASTEXITCODE after each call, never thrown half-way (fly-deploy.ps1
# does the same): stderr lines from node are output to be shown, not errors to stop on.
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Utf8NoBom = New-Object System.Text.UTF8Encoding $false

function Stop-Run([string]$Message, [int]$Code = 1) {
  Write-Host ("הריצה לא יצאה לפועל: " + $Message) -ForegroundColor Red
  exit $Code
}

# 1. The repository root, from where this file lives, so the paths below hold wherever it is called from.
#    Path.Combine, not a typed backslash: the same file dry-runs under pwsh on CI's Linux, where a backslash is a letter.
#    The location is never changed: a relative -Photos is the caller's, and the caller's window stays where it was.
$RepoRoot = (Resolve-Path ([System.IO.Path]::Combine($PSScriptRoot, '..', '..'))).Path
$Stylist = [System.IO.Path]::Combine($RepoRoot, 'tools', 'eval', 'stylist.js')
if (-not (Test-Path -LiteralPath $Stylist)) { Stop-Run "tools\eval\stylist.js is not beside this script ($RepoRoot)." }

# 2. Node, 18 or newer.
$NodeCmd = Get-Command node -ErrorAction SilentlyContinue
if (-not $NodeCmd) { Stop-Run "node is not installed or not on the PATH. Install the LTS from https://nodejs.org and open a new window." }
$NodeVersion = (& node --version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { Stop-Run "node --version failed: $NodeVersion" }
$NodeMajor = 0
if ($NodeVersion -match '^v(\d+)\.') { $NodeMajor = [int]$Matches[1] }
if ($NodeMajor -lt 18) {
  Stop-Run "node $NodeVersion is too old. stylist.js uses fetch and FormData, which Node 18 brought; install the LTS from https://nodejs.org."
}

# 3. The photos: every .jpg in the folder, by name, so the report reads in one order every time. A relative
#    folder is read from the caller's location, and the report names it in full.
if (-not (Test-Path -LiteralPath $Photos -PathType Container)) { Stop-Run "the folder $Photos does not exist." }
$Photos = (Resolve-Path -LiteralPath $Photos).ProviderPath
$Files = @(Get-ChildItem -LiteralPath $Photos -File | Where-Object { $_.Extension -in '.jpg', '.jpeg' } | Sort-Object Name)
if ($Files.Count -eq 0) { Stop-Run "no .jpg in $Photos. The pass sends JPEG photographs; put the looks in that folder." }

# How many checks the pass spends. Every call is one check out of the account's rolling day, and a Pro day is
# Plans:ProChecksPerDay (never above Limits:ChecksPerDay): 30 at the server's defaults. A pass longer than that is
# refused part-way (429) and its verdict read on fewer runs than asked, so a real run stops here, before anyone is
# asked for a password or signed in, unless -Force says the server's caps were raised. A dry run only says so.
$ProDay = 30
$Calls = $Files.Count * $Runs
$Over = $Calls -gt $ProDay
if ($Over -and -not $Force -and -not $DryRun) {
  Stop-Run ("{0} photos x {1} runs = {2} checks, more than a Pro day ({3}). Split the folder, or pass -Force on a server whose Plans__ProChecksPerDay and Limits__ChecksPerDay were raised." -f $Files.Count, $Runs, $Calls, $ProDay)
}

# 4. The account. The handle is required; the password is asked for, masked, when neither -Password nor the
#    environment gave one. A dry run signs in as nobody and needs neither.
if (-not $Handle) {
  if ($DryRun) { $Handle = '<handle>' }
  else { Stop-Run "no account to sign in as. Set OREVOSH_EVAL_HANDLE (and OREVOSH_EVAL_PASSWORD), or pass -Handle." }
}
if (-not $Password -and -not $DryRun) {
  $Secure = Read-Host "Password for $Handle" -AsSecureString
  $Bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
  try { $Password = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($Bstr) }
  finally { [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($Bstr) }
  if (-not $Password) { Stop-Run "no password given." }
}
if (-not $Password) { $Password = '<password>' }

# 5. What it costs, said before it is spent: the calls, and the checks they take out of the account's day.
Write-Host ""
Write-Host ("  {0} photos x {1} runs = {2} model calls, about 1-2 US cents each, on the owner's key." -f $Files.Count, $Runs, $Calls)
Write-Host ("  The account {0} needs an allowance of at least {1} checks today (Pro: --pro <handle> 1 on the server)," -f $Handle, $Calls)
Write-Host "  and Limits__SpendPerDayUsd is the ceiling the app stops itself at."
if ($Over) {
  $Fit = [Math]::Floor($ProDay / $Runs)
  Write-Host ("  That is more than a Pro day ({0} checks at the default settings): at {1} runs, {2} photos fit in a day." -f $ProDay, $Runs, $Fit) -ForegroundColor Yellow
}
if ($Over -and -not $Force) { Write-Host "  a real run stops before signing in, unless -Force." -ForegroundColor Yellow }
Write-Host ""

# 6. The command. --report keeps the rows as JSON beside the text report; the tables still print.
$Stamp = Get-Date -Format 'yyyy-MM-dd-HHmm'
$ReportsDir = [System.IO.Path]::Combine($RepoRoot, 'tools', 'eval', 'reports')
$ReportBase = Join-Path $ReportsDir ("{0}-{1}-{2}-{3}" -f $Stamp, $Occasion, $Style, $Language)
$ReportTxt = $ReportBase + '.txt'
$ReportJson = $ReportBase + '.json'

$NodeArgs = New-Object System.Collections.ArrayList
[void]$NodeArgs.Add($Stylist)
foreach ($File in $Files) { [void]$NodeArgs.Add('--photo'); [void]$NodeArgs.Add($File.FullName) }
[void]$NodeArgs.AddRange(@('--occasion', $Occasion, '--style', $Style, '--language', $Language,
    '--runs', "$Runs", '--max-spread', "$MaxSpread", '--base', $Base, '--handle', $Handle))
if ($Note) { [void]$NodeArgs.AddRange(@('--note', $Note)) }
if ($Delay -gt 0) { [void]$NodeArgs.AddRange(@('--delay', "$Delay")) }
[void]$NodeArgs.AddRange(@('--report', $ReportJson))

# The same line with the password masked: printed, and written into the report. The password is not an argument:
# it goes to node in OREVOSH_EVAL_PASSWORD (stylist.js reads it there), so the line shows that, masked.
$Masked = New-Object System.Collections.ArrayList
for ($i = 0; $i -lt $NodeArgs.Count; $i++) {
  if ("$($NodeArgs[$i])" -match '[\s"]') { [void]$Masked.Add('"' + $NodeArgs[$i] + '"') }
  else { [void]$Masked.Add($NodeArgs[$i]) }
}
$MaskedLine = '$env:OREVOSH_EVAL_PASSWORD = ''********''; node ' + ($Masked -join ' ')
Write-Host "  $MaskedLine"
Write-Host ""

if ($DryRun) {
  Write-Host "  dry run: nothing was sent and nobody was signed in."
  Write-Host ("  the report would be written to {0}" -f $ReportTxt)
  exit 0
}

# 7. The run, every line echoed as it arrives and kept for the report. stderr lines come through as records
#    under 2>&1; ToString() gives the text of either kind. The password rides in the environment only while node
#    runs, and the window's own OREVOSH_EVAL_PASSWORD (set or not) is put back afterwards.
$Lines = New-Object System.Collections.ArrayList
$HadPassword = Test-Path Env:OREVOSH_EVAL_PASSWORD
$PriorPassword = $env:OREVOSH_EVAL_PASSWORD
$env:OREVOSH_EVAL_PASSWORD = $Password
try {
  & node @NodeArgs 2>&1 | ForEach-Object {
    $Text = $_.ToString()
    Write-Host $Text
    [void]$Lines.Add($Text)
  }
  $Code = $LASTEXITCODE
} finally {
  if ($HadPassword) { $env:OREVOSH_EVAL_PASSWORD = $PriorPassword }
  else { Remove-Item Env:OREVOSH_EVAL_PASSWORD -ErrorAction SilentlyContinue }
}
if ($null -eq $Code) { $Code = 2 }

# 8. The report: UTF-8 without a mark (5.1's Out-File would write one), the folder created if it is missing.
if (-not (Test-Path -LiteralPath $ReportsDir)) { New-Item -ItemType Directory -Path $ReportsDir | Out-Null }
$Header = @(
  ("OREVOSH stylist eval - {0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm')),
  ("base:      {0}" -f $Base),
  ("account:   {0}" -f $Handle),
  ("photos:    {0} in {1}" -f $Files.Count, $Photos),
  ("runs:      {0} per photo ({1} model calls)" -f $Runs, $Calls),
  ("occasion:  {0}   style: {1}   language: {2}   max spread: {3}" -f $Occasion, $Style, $Language, $MaxSpread),
  ("command:   {0}" -f $MaskedLine),
  ("exit code: {0}" -f $Code),
  ""
)
$ReportText = (($Header + $Lines) -join "`r`n") + "`r`n"
[System.IO.File]::WriteAllText($ReportTxt, $ReportText, $Utf8NoBom)

# 9. The verdict, from the exit code and the last line stylist.js printed.
$Last = ''
for ($i = $Lines.Count - 1; $i -ge 0; $i--) { if ($Lines[$i].Trim()) { $Last = $Lines[$i].Trim(); break } }
$FirstError = ''
foreach ($L in $Lines) { if ($L.Trim()) { $FirstError = $L.Trim(); break } }
Write-Host ""
if ($Code -eq 0) {
  Write-Host ("בתוך {0}: הציון של כל תמונה נשאר בטווח המותר." -f $MaxSpread) -ForegroundColor Green
} elseif ($Code -eq 1 -and $Last -match '^TOO WIDE: (.*) \(allowed: ([^)]*)\)\.?$') {
  Write-Host ("רחב מדי: {0} (מותר: {1})." -f $Matches[1], $Matches[2]) -ForegroundColor Red
} elseif ($Code -eq 1 -and $Last -match '^NO VERDICT: (\d+) photo') {
  Write-Host ("אין פסק דין: {0} תמונות לא חזרו עם ציון. לא נמדד כלום." -f $Matches[1]) -ForegroundColor Red
} elseif ($Code -eq 1) {
  Write-Host ("הריצה נכשלה: {0}" -f $Last) -ForegroundColor Red
} else {
  Write-Host ("הריצה לא יצאה לפועל: {0}" -f $FirstError) -ForegroundColor Red
}
Write-Host ("הדו""ח: {0}" -f $ReportTxt)
exit $Code
