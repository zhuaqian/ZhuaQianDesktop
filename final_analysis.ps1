# Use a simple approach: Read the file line by line and extract ShowSettings method
$path = 'C:\Users\本机\Documents\Codex\2026-07-10\c-users-workbuddy-2026-07-10\src\ZhuaQianDesktop.cs'

# Read all lines
$lines = [System.IO.File]::ReadAllLines($path)
$methodFound = $false
$methodLines = @()
$inMethod = $false

foreach ($i in 0..($lines.Count - 1)) {
    $line = $lines[$i]
    
    # Start of method
    if ($line -match '^    void ShowSettings\(\)') {
        $methodFound = $true
        $inMethod = $true
    }
    
    if ($inMethod) {
        $methodLines += "$($i + 1): $line"
        
        # Check for method end (brace matching)
        $braceCount = ($line -match '{' | Measure-Object -Sum | Select-Object -ExpandProperty Sum) - ($line -match '}' | Measure-Object -Sum | Select-Object -ExpandProperty Sum)
        if ($i -gt 20 -and $braceCount -eq 0) {
            break
        }
    }
}

if ($methodFound) {
    Write-Host -ForegroundColor Green "=== FOUND SHOWSETTINGS METHOD ==="
    Write-Host -ForegroundColor Yellow "The method appears to be around line: $($methodLines[0].Split(':')[0])"
    
    # Display just the method signature and a few key lines
    Write-Host -ForegroundColor White \"Method signature:\"
    $methodLines[0] | Write-Host -ForegroundColor Cyan
    
    # Look for specific parts of the method
    $signature = ''
    $body = ''
    $close = ''
    
    foreach ($line in $methodLines) {
        if ($line -match '^(.*)void ShowSettings\(\)') {
            $signature = $line
        }
        if ($line -match '^        using \(var dlg = new SettingsDialog') {
            $body += "$line\n"
        }
        if ($line -match '^                if \(dlg\.ShowDialog\(this\) == DialogResult\.OK\)' -or $line -match '^                var sel = dlg\.SelectedModel') {
            $body += "$line\n"
        }
        if ($line -match '^        }') {
            $close = $line
        }
    }
    
    Write-Host -ForegroundColor White "\nMethod signature and important parts:"
    Write-Host -ForegroundColor Cyan "$signature"
    Write-Host -ForegroundColor Yellow "$body"
    Write-Host -ForegroundColor Cyan "$close"
    
    Write-Host -ForegroundColor Green \"\n=== ANALYSIS ===\"
    Write-Host \"1. The Settings button at lines 355 and 515 both call 'ShowSettings()'\" 
    Write-Host \"2. This method immediately opens and closes the SettingsDialog\" 
    Write-Host \"3. After OK, it performs model switching without showing the UI properly\" 
    Write-Host \"4. Other settings (language, API keys) are processed but UI is never seen by user\" 
} else {
    Write-Host -ForegroundColor Red \"ShowSettings method NOT found in file!\" }