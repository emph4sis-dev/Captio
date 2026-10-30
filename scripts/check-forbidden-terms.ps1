<#
.SYNOPSIS
  Fails if any tracked or untracked file in the repository contains a forbidden term.

.DESCRIPTION
  The term list lives OUTSIDE the repository so the terms themselves never get
  committed. One term per line; blank lines and lines starting with # are ignored.
  Matching is case-insensitive and also checks file paths.

  Default list location: $env:USERPROFILE\.captio-private\forbidden-terms.txt
  Override with -TermsFile or the CAPTIO_FORBIDDEN_TERMS_FILE environment variable.
#>
param(
    [string]$TermsFile = $(if ($env:CAPTIO_FORBIDDEN_TERMS_FILE) { $env:CAPTIO_FORBIDDEN_TERMS_FILE } else { Join-Path $env:USERPROFILE '.captio-private\forbidden-terms.txt' }),
    # Commits about to be published (used by the pre-push hook). Their committed
    # content and history are checked in addition to the working tree.
    [string[]]$Commits = @()
)

$ErrorActionPreference = 'Stop'
# powershell -File passes "a,b" as one string.
$Commits = @($Commits | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not (Test-Path $TermsFile)) {
    Write-Error "Terms file not found: $TermsFile"
    exit 2
}

$resolvedTerms = (Resolve-Path $TermsFile).Path
if ($resolvedTerms.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Error 'The terms file must not live inside the repository.'
    exit 2
}

$terms = Get-Content $TermsFile -Encoding utf8 | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') }
if (-not $terms) {
    Write-Error 'The terms file is empty.'
    exit 2
}

Push-Location $repo
try {
    $files = git ls-files --cached --others --exclude-standard
} finally {
    Pop-Location
}

$hits = 0
foreach ($relative in $files) {
    foreach ($term in $terms) {
        if ($relative.IndexOf($term, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            Write-Host "path: $relative"
            $hits++
        }
    }

    $path = Join-Path $repo $relative
    if (-not (Test-Path $path -PathType Leaf)) { continue }
    $found = Select-String -Path $path -Pattern $terms -SimpleMatch -List:$false -ErrorAction SilentlyContinue
    foreach ($m in $found) {
        # Report location only, never the matched text.
        Write-Host "content: ${relative}:$($m.LineNumber)"
        $hits++
    }
}

Push-Location $repo
try {
    $grepArgs = @('-I', '-i', '-F', '-l') + ($terms | ForEach-Object { '-e', $_ })
    foreach ($commit in $Commits) {
        # Committed content can differ from the working tree.
        foreach ($file in (git grep @grepArgs $commit)) {
            Write-Host "commit $($commit.Substring(0, 7)): content in $($file.Substring($commit.Length + 1))"
            $hits++
        }
        foreach ($file in (git ls-tree -r --name-only $commit)) {
            foreach ($term in $terms) {
                if ($file.IndexOf($term, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    Write-Host "commit $($commit.Substring(0, 7)): path $file"
                    $hits++
                }
            }
        }
    }

    # Commit messages are part of the public record too.
    $tips = @($Commits)
    $null = git rev-parse --verify --quiet HEAD
    if ($LASTEXITCODE -eq 0) { $tips += 'HEAD' }
    if ($tips) {
        $log = git log --format='%H%n%B' @tips
        foreach ($term in $terms) {
            if ($log -match [regex]::Escape($term)) {
                Write-Host 'commit history: forbidden term found in a commit message'
                $hits++
                break
            }
        }
    }
} finally {
    Pop-Location
}

if ($hits -gt 0) {
    Write-Host "FAILED: $hits occurrence(s) found."
    exit 1
}

Write-Host "OK: $($files.Count) files checked, no forbidden terms."
