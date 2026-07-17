# Use a simpler approach to find the ShowSettings method
$path = 'C:\Users\本机\Documents\Codex\2026-07-10\c-users-workbuddy-2026-07-10\src\ZhuaQianDesktop.cs'

# Read the entire file
$content = Get-Content -Path $path -Encoding Default -Raw

# Find the ShowSettings method using a proper string search
$methodStart = $content.IndexOf('    void ShowSettings()')
if ($methodStart -eq -1) {
    Write-Host -ForegroundColor Red "Method 'void ShowSettings()' not found in file"
    exit
}

Write-Host -ForegroundColor Green "Method found at position: $methodStart"

# Extract a reasonable section after the method start
$afterStart = $content.Substring($methodStart, 2000)

# Find the end of the first line containing 'void ShowSettings()'
$firstLineEnd = $afterStart.IndexOf("\r\n")
$afterStart = $afterStart.Substring($firstLineEnd + 2)

# Look for the complete method (finding matching braces)
$methodText = ""
$braceCount = 0
$foundMethod = $false
$charCount = 0

for ($i = 0; $i -lt $afterStart.Length; $i++) {
    $char = $afterStart[$i]
    $methodText += $char
    $charCount++
    
    if ($char -eq '{') { $braceCount++ }
    if ($char -eq '}') { 
        $braceCount--
        if ($braceCount -eq 0 -and $charCount -gt 100) {
            $foundMethod = $true
            break
        }
    }
}

if ($foundMethod) {
    Write-Host -ForegroundColor Green "=== EXTRACTED SHOWSETTINGS METHOD ==="
    Write-Host $methodText
    Write-Host -ForegroundColor Green "=== END OF METHOD ==="
} else {
    Write-Host -ForegroundColor Yellow \"Could not extract complete method, showing partial content...\"
    Write-Host $afterStart.Substring(0, [Math]::Min(500, $afterStart.Length))