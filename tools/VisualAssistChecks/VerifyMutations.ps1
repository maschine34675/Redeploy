param([Parameter(Mandatory=$true)][string]$ReleasedDll)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$source = Join-Path $repo 'Redeploy.Core\Compat\VisualAssist'
$mutants = @(
    @{ Name='old-singleton-release'; From='if (ReferenceEquals(_contract.Instance.GetValue(null), arc))'; To='if (true)'; Failure='old Dispose cleared the new singleton' },
    @{ Name='lost-first-death-resources'; From='foreach (var resources in captured) resources.Release();'; To='foreach (var resources in captured) _ = resources;'; Failure='captured initial resources leaked' },
    @{ Name='unpublished-body'; From='if ((!published && BodyReplacementContext.IsCreating) || !ExactCurrent(body)'; To='if (!ExactCurrent(body)'; Failure='unpublished candidate acquired preview' }
)
Push-Location $repo
try {
    foreach ($mutant in $mutants) {
        $folder = Join-Path $PSScriptRoot ('obj\mutants\' + $mutant.Name)
        New-Item -ItemType Directory -Force -Path $folder | Out-Null
        Get-ChildItem -LiteralPath $source -Filter '*.cs' | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $folder -Force }
        $target = Join-Path $folder 'VisualAssistCompat.cs'
        $text = Get-Content -LiteralPath $target -Raw
        if (-not $text.Contains($mutant.From)) { throw ('Mutation anchor missing: ' + $mutant.Name) }
        Set-Content -LiteralPath $target -Value ($text.Replace($mutant.From, $mutant.To)) -NoNewline
        $output = (& dotnet run --project tools/VisualAssistChecks -c Release "-p:VisualAssistSource=$folder" -- $ReleasedDll 2>&1 | Out-String)
        if ($LASTEXITCODE -eq 0 -or -not $output.Contains($mutant.Failure)) { throw ('Mutation did not fail at expected behavior: ' + $mutant.Name + [Environment]::NewLine + $output) }
        Write-Output ('REJECTED ' + $mutant.Name + ': ' + $mutant.Failure)
    }
    dotnet run --project tools/VisualAssistChecks -c Release -- $ReleasedDll
    if ($LASTEXITCODE -ne 0) { throw 'Unmodified production adapter failed after mutations' }
    Write-Output 'VISUAL_ASSIST_MUTATIONS_REJECTED'
}
finally { Pop-Location }
