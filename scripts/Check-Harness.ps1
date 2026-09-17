[CmdletBinding()]
param([switch]$CheckPrivateImport)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
$requiredFiles = @(
    'AGENTS.md', 'MEMORY.md', 'README.md',
    'docs/SPECIFICATION.md', 'docs/ARCHITECTURE.md',
    'docs/ACCEPTANCE.md', 'docs/PLAN.md', '.gitignore', '.editorconfig'
)
$failures = [System.Collections.Generic.List[string]]::new()

foreach ($relativePath in $requiredFiles) {
    $fullPath = Join-Path $projectRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        $failures.Add("Missing required file: $relativePath")
        continue
    }
    $content = Get-Content -LiteralPath $fullPath -Raw
    if ([string]::IsNullOrWhiteSpace($content)) {
        $failures.Add("Empty required file: $relativePath")
    }
    if ($relativePath.EndsWith('.md')) {
        foreach ($match in [regex]::Matches($content, '\[[^\]]+\]\(([^)]+)\)')) {
            $target = $match.Groups[1].Value
            if ($target -match '^(https?://|mailto:|#)') { continue }
            $targetPath = ($target -split '#', 2)[0]
            $resolvedPath = Join-Path (Split-Path -Parent $fullPath) $targetPath
            if (-not (Test-Path -LiteralPath $resolvedPath)) {
                $failures.Add("Broken local link in ${relativePath}: $target")
            }
        }
    }
}

$ignorePath = Join-Path $projectRoot '.gitignore'
if (Test-Path -LiteralPath $ignorePath) {
    $ignoreRules = @(Get-Content -LiteralPath $ignorePath)
    foreach ($rule in @('/transactions.csv', '/private/', '/snapshots/', '/backups/', '*.db', '*.db-*', '*.sqlite', '*.sqlite-*')) {
        if ($ignoreRules -notcontains $rule) { $failures.Add("Missing ignore rule: $rule") }
    }
}

if ($CheckPrivateImport) {
    $sourcePath = Join-Path $projectRoot 'transactions.csv'
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        $failures.Add('Optional import check requested, but transactions.csv is absent.')
    } else {
        $rows = @(Import-Csv -LiteralPath $sourcePath)
        if ($rows.Count -eq 0) {
            $failures.Add('CSV contains no data rows.')
        } else {
            $headers = @($rows[0].PSObject.Properties.Name)
            $needed = @('ID', 'Date', 'Description', 'Currency', 'Amount', 'Type', 'Tags', 'Account', 'Status', 'Memo', 'IOU')
            foreach ($header in $needed) {
                if ($headers -notcontains $header) { $failures.Add("Missing CSV column: $header") }
            }
            if (($headers -contains 'Type') -and ($headers -contains 'Currency')) {
                $unsupported = @($rows | Where-Object { $_.Type -notin @('Expense', 'Income', 'Transfer') -or $_.Currency -ne 'CHF' })
                if ($unsupported.Count -gt 0) {
                    $failures.Add("CSV has $($unsupported.Count) rows with unsupported type/currency.")
                }
            }
            Write-Output "Private CSV structure inspected: $($rows.Count) rows. Values were not printed."
        }
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Output "FAIL: $failure" }
    exit 1
}
Write-Output 'Harness checks passed. Application behavior is not tested by this script.'
exit 0
