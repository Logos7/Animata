# Uruchamia testy z filtrem i zamienia porażki w adnotacje GitHub (jedna na test, z komunikatem) plus koniec logu.
param(
    [Parameter(Mandatory)] [string] $Filter,
    [ValidateSet('error', 'warning')] [string] $Level = 'error',
    [switch] $ReportPass
)
$log = "test-$Level.log"
dotnet test Animata.slnx -c Release --no-build --filter $Filter --logger "console;verbosity=normal" 2>&1 | Tee-Object -FilePath $log
$code = $LASTEXITCODE
if ($code -eq 0) {
    if ($ReportPass) { Write-Output "::notice::Wszystkie testy z filtrem '$Filter' przechodzą — można zdjąć znacznik znanej porażki." }
    exit 0
}
$lines = Get-Content $log
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s*Failed (\S+)') {
        $block = @($lines[$i].Trim())
        for ($j = $i + 1; $j -lt [Math]::Min($i + 25, $lines.Count); $j++) {
            if ($lines[$j] -match '^\s*(Failed |Passed )') { break }
            if ($lines[$j].Trim()) { $block += $lines[$j].Trim() }
        }
        Write-Output ("::${Level}::" + ($block -join ' | '))
    }
}
$tail = (Get-Content $log -Tail 40 | ForEach-Object { $_.Trim() } | Where-Object { $_ }) -join ' | '
Write-Output "::${Level}::tail: $tail"
exit 1
