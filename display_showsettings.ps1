#Find and display ShowSettings method using direct file reading
$path = 'C:\Users\本机\Documents\Codex\2026-07-10\c-users-workbuddy-2026-07-10\src\ZhuaQianDesktop.cs'

# Read the entire file
$content = Get-Content -Path $path -Encoding Default -Raw

# Split into lines
$lines = $content -split "`r`n"

# Find ShowSettings method
$methodStart = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^    void ShowSettings\(\)') {
        $methodStart = $i
        break
    }
}

if ($methodStart -ne -1) {
    # Extract method lines
    $methodLines = @()
    $braceCount = 0
    $i = $methodStart
    
    while ($i -lt $lines.Count) {
        $line = $lines[$i]
        $methodLines += \"Line $(($i + 1)): $line\"
        
        # Count braces
        if ($line -match '{') { $braceCount++ }
        if ($line -match '}') { $braceCount-- }
        
        if ($braceCount -eq 0 -and $i -gt $methodStart) {
            break
        }
        
        $i++
    }
    
    # Display the method
    $output = '=== SHOWSETTINGS METHOD ===`n'
    foreach ($line in $methodLines) {
        $output += "$line`n"
    }
    $output += '=== END ==='
    
    Write-Host $output
    
    # Also display the surrounding context
    Write-Host -ForegroundColor Yellow '=== BUTTON CLICK HANDLERS ==='
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match 'sideSettingsButton\.Click') {
            Write-Host "Line $(($i + 1)): $lines[$i]"
        }
    }
}

if ($methodStart -eq -1) {
    Write-Host -ForegroundColor Red \"ShowSettings method not found in file\"
}