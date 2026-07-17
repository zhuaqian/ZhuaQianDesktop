# ZhuaQian Desktop plugin starter: upper.ps1
# Reads text from STDIN, outputs UPPERCASE.
$input_text = [Console]::In.ReadToEnd()
if ($input_text) {
    $input_text.ToUpperInvariant()
} else {
    Write-Output "(no input)"
}
