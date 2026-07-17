Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem

[System.Windows.Forms.Application]::EnableVisualStyles()
$ErrorActionPreference = "Stop"

$AppName = "ZhuaQian Desktop"
$DefaultModel = "gemini-2.5-flash-latest"
$ConfigDir = Join-Path $env:APPDATA "ZhuaQianDesktop"
$ConfigPath = Join-Path $ConfigDir "config.json"
$ImageExts = @(".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp")
$TextExts = @(".txt", ".md", ".markdown", ".csv", ".json", ".jsonl", ".xml", ".html", ".htm", ".log", ".ini", ".cfg", ".yaml", ".yml", ".py", ".js", ".ts", ".css", ".sql")
$DocExts = @(".docx", ".xlsx", ".xlsm", ".pptx", ".pdf", ".doc", ".xls", ".ppt") + $TextExts
$MaxExtractedChars = 24000
$MaxInlineBytes = 20MB
$MaxDocBytes = 50MB

function Load-Config {
    if (!(Test-Path -LiteralPath $ConfigDir)) {
        New-Item -ItemType Directory -Path $ConfigDir | Out-Null
    }
    if (Test-Path -LiteralPath $ConfigPath) {
        try {
            return Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
        } catch {}
    }
    return [pscustomobject]@{ apiKey = ""; model = $DefaultModel }
}

function Save-Config($cfg) {
    if (!(Test-Path -LiteralPath $ConfigDir)) {
        New-Item -ItemType Directory -Path $ConfigDir | Out-Null
    }
    $cfg | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ConfigPath -Encoding UTF8
}

function Format-Size([long]$bytes) {
    if ($bytes -lt 1KB) { return "$bytes B" }
    if ($bytes -lt 1MB) { return ("{0:N1} KB" -f ($bytes / 1KB)) }
    return ("{0:N1} MB" -f ($bytes / 1MB))
}

function Clean-Path([string]$path) {
    $p = ($path -replace '^&\s+', '').Trim().Trim('"').Trim("'")
    if ($p.ToLower().StartsWith("file:///")) {
        $p = [System.Uri]::UnescapeDataString($p.Substring(8)).Replace("/", "\")
    }
    return $p
}

function Get-MimeType([string]$path) {
    $ext = [System.IO.Path]::GetExtension($path).ToLowerInvariant()
    switch ($ext) {
        ".png"  { return "image/png" }
        ".jpg"  { return "image/jpeg" }
        ".jpeg" { return "image/jpeg" }
        ".gif"  { return "image/gif" }
        ".webp" { return "image/webp" }
        ".bmp"  { return "image/bmp" }
        ".pdf"  { return "application/pdf" }
        default { return "application/octet-stream" }
    }
}

function Trim-Extracted([string]$text) {
    if ($null -eq $text) { return "" }
    if ($text.Length -le $MaxExtractedChars) { return $text }
    return $text.Substring(0, $MaxExtractedChars) + "`r`n`r`n[content truncated: $($text.Length - $MaxExtractedChars) chars omitted]"
}

function Decode-Html([string]$s) {
    return [System.Net.WebUtility]::HtmlDecode($s)
}

function Extract-XmlText([string]$xml) {
    $matches = [regex]::Matches($xml, '<[^>/]*:?t[^>]*>(.*?)</[^>]*:?t>', 'Singleline')
    $items = New-Object System.Collections.Generic.List[string]
    foreach ($m in $matches) {
        $value = Decode-Html(($m.Groups[1].Value -replace '<[^>]+>', ''))
        if ($value.Trim()) { $items.Add($value.Trim()) }
    }
    return ($items -join "`r`n")
}

function Read-ZipEntryText($zip, [string]$name) {
    $entry = $zip.GetEntry($name)
    if ($null -eq $entry) { return "" }
    $stream = $entry.Open()
    $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
    try {
        return $reader.ReadToEnd()
    } finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Extract-Docx([string]$path) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($path)
    try {
        $parts = New-Object System.Collections.Generic.List[string]
        $entries = $zip.Entries | Where-Object { $_.FullName -like "word/*.xml" }
        foreach ($entry in $entries) {
            $stream = $entry.Open()
            $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
            try {
                $text = Extract-XmlText $reader.ReadToEnd()
                if ($text.Trim()) { $parts.Add($text) }
            } finally {
                $reader.Dispose()
                $stream.Dispose()
            }
        }
        return ($parts -join "`r`n`r`n")
    } finally {
        $zip.Dispose()
    }
}

function Extract-Pptx([string]$path) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($path)
    try {
        $parts = New-Object System.Collections.Generic.List[string]
        $slides = $zip.Entries | Where-Object { $_.FullName -like "ppt/slides/slide*.xml" } | Sort-Object FullName
        $i = 1
        foreach ($entry in $slides) {
            $stream = $entry.Open()
            $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
            try {
                $text = Extract-XmlText $reader.ReadToEnd()
                if ($text.Trim()) {
                    $parts.Add("[Slide $i]`r`n$text")
                    $i++
                }
            } finally {
                $reader.Dispose()
                $stream.Dispose()
            }
        }
        return ($parts -join "`r`n`r`n")
    } finally {
        $zip.Dispose()
    }
}

function Extract-Xlsx([string]$path) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($path)
    try {
        $shared = @()
        $sharedXml = Read-ZipEntryText $zip "xl/sharedStrings.xml"
        if ($sharedXml) { $shared = (Extract-XmlText $sharedXml) -split "\r?\n" }

        $parts = New-Object System.Collections.Generic.List[string]
        $sheets = $zip.Entries | Where-Object { $_.FullName -like "xl/worksheets/sheet*.xml" } | Sort-Object FullName
        $sheetNo = 1
        foreach ($entry in $sheets) {
            $stream = $entry.Open()
            $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
            try {
                $xml = $reader.ReadToEnd()
                $rows = New-Object System.Collections.Generic.List[string]
                $rowMatches = [regex]::Matches($xml, '<row[^>]*>(.*?)</row>', 'Singleline')
                foreach ($rm in $rowMatches) {
                    $cells = New-Object System.Collections.Generic.List[string]
                    $cellMatches = [regex]::Matches($rm.Groups[1].Value, '<c([^>]*)>(.*?)</c>', 'Singleline')
                    foreach ($cm in $cellMatches) {
                        $attrs = $cm.Groups[1].Value
                        $body = $cm.Groups[2].Value
                        $v = ""
                        $vm = [regex]::Match($body, '<v>(.*?)</v>', 'Singleline')
                        if ($vm.Success) { $v = Decode-Html($vm.Groups[1].Value) }
                        if ($attrs -match 't="s"' -and $v -match '^\d+$') {
                            $idx = [int]$v
                            if ($idx -ge 0 -and $idx -lt $shared.Count) { $v = $shared[$idx] }
                        }
                        if ($body -match '<is>') { $v = Extract-XmlText $body }
                        $cells.Add($v)
                    }
                    if (($cells -join "").Trim()) { $rows.Add(($cells -join "`t")) }
                    if ($rows.Count -ge 120) {
                        $rows.Add("[sheet truncated]")
                        break
                    }
                }
                if ($rows.Count -gt 0) {
                    $parts.Add("[Sheet $sheetNo]`r`n" + ($rows -join "`r`n"))
                }
                $sheetNo++
            } finally {
                $reader.Dispose()
                $stream.Dispose()
            }
        }
        return ($parts -join "`r`n`r`n")
    } finally {
        $zip.Dispose()
    }
}

function Extract-TextDocument([string]$path) {
    $ext = [System.IO.Path]::GetExtension($path).ToLowerInvariant()
    switch ($ext) {
        ".docx" { return Extract-Docx $path }
        ".pptx" { return Extract-Pptx $path }
        ".xlsx" { return Extract-Xlsx $path }
        ".xlsm" { return Extract-Xlsx $path }
        ".doc"  { return "Old .doc is not supported yet. Save as .docx and upload again." }
        ".xls"  { return "Old .xls is not supported yet. Save as .xlsx and upload again." }
        ".ppt"  { return "Old .ppt is not supported yet. Save as .pptx and upload again." }
        default {
            try {
                return Get-Content -LiteralPath $path -Raw -Encoding UTF8
            } catch {
                return Get-Content -LiteralPath $path -Raw -Encoding Default
            }
        }
    }
}

function New-TextPart([string]$text) {
    return @{ text = $text }
}

function New-InlinePart([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    return @{
        inlineData = @{
            mimeType = Get-MimeType $path
            data = [Convert]::ToBase64String($bytes)
        }
    }
}

$script:Config = Load-Config
$script:Messages = New-Object System.Collections.ArrayList
$script:PendingParts = New-Object System.Collections.ArrayList
$script:PendingLabels = New-Object System.Collections.ArrayList
$script:SystemPrompt = "You are ZhuaQian Desktop, a practical free Windows AI work assistant. You can analyze images, PDFs, Word, PowerPoint, Excel, Markdown, TXT, CSV, JSON, summarize documents, extract todos, write copy, and help with code. Be concise and actionable."

function Add-Attachment([string]$rawPath) {
    $path = Clean-Path $rawPath
    if (!(Test-Path -LiteralPath $path)) { throw "File does not exist: $rawPath" }
    $item = Get-Item -LiteralPath $path
    if ($item.PSIsContainer) { throw "Please choose a file, not a folder." }
    $ext = $item.Extension.ToLowerInvariant()

    if ($ImageExts -contains $ext -or $ext -eq ".pdf") {
        if ($item.Length -gt $MaxInlineBytes) {
            throw "File too large: $(Format-Size $item.Length). Limit: $(Format-Size $MaxInlineBytes)."
        }
        [void]$script:PendingParts.Add((New-InlinePart $item.FullName))
        [void]$script:PendingLabels.Add("$($item.Name) ($(Format-Size $item.Length))")
        return
    }

    if ($DocExts -contains $ext) {
        if ($item.Length -gt $MaxDocBytes) {
            throw "Document too large: $(Format-Size $item.Length). Limit: $(Format-Size $MaxDocBytes)."
        }
        $text = Trim-Extracted (Extract-TextDocument $item.FullName)
        $docBlock = "[Loaded document]`r`nname: $($item.Name)`r`npath: $($item.FullName)`r`ntype: $ext`r`nsize: $(Format-Size $item.Length)`r`n`r`n$text"
        [void]$script:PendingParts.Add((New-TextPart $docBlock))
        [void]$script:PendingLabels.Add("$($item.Name) ($(Format-Size $item.Length))")
        return
    }

    throw "Unsupported file type: $ext"
}

$Form = New-Object System.Windows.Forms.Form
$Form.Text = "$AppName v0.1"
$Form.StartPosition = "CenterScreen"
$Form.Size = New-Object System.Drawing.Size(980, 700)
$Form.MinimumSize = New-Object System.Drawing.Size(760, 520)
$Form.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 10)

$TopPanel = New-Object System.Windows.Forms.Panel
$TopPanel.Dock = "Top"
$TopPanel.Height = 46
$TopPanel.BackColor = [System.Drawing.Color]::FromArgb(245, 247, 250)
$Form.Controls.Add($TopPanel)

$Title = New-Object System.Windows.Forms.Label
$Title.Text = "ZhuaQian Desktop"
$Title.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 14, [System.Drawing.FontStyle]::Bold)
$Title.Location = New-Object System.Drawing.Point(14, 10)
$Title.AutoSize = $true
$TopPanel.Controls.Add($Title)

$ModelLabel = New-Object System.Windows.Forms.Label
$ModelLabel.Text = "Model: $($script:Config.model)"
$ModelLabel.AutoSize = $true
$ModelLabel.Location = New-Object System.Drawing.Point(190, 15)
$TopPanel.Controls.Add($ModelLabel)

$BtnSettings = New-Object System.Windows.Forms.Button
$BtnSettings.Text = "Settings"
$BtnSettings.Width = 92
$BtnSettings.Height = 28
$BtnSettings.Anchor = "Top,Right"
$BtnSettings.Location = New-Object System.Drawing.Point(760, 9)
$TopPanel.Controls.Add($BtnSettings)

$BtnClear = New-Object System.Windows.Forms.Button
$BtnClear.Text = "Clear"
$BtnClear.Width = 72
$BtnClear.Height = 28
$BtnClear.Anchor = "Top,Right"
$BtnClear.Location = New-Object System.Drawing.Point(860, 9)
$TopPanel.Controls.Add($BtnClear)

$Chat = New-Object System.Windows.Forms.RichTextBox
$Chat.Dock = "Fill"
$Chat.ReadOnly = $true
$Chat.BorderStyle = "None"
$Chat.BackColor = [System.Drawing.Color]::White
$Chat.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 10)
$Form.Controls.Add($Chat)

$BottomPanel = New-Object System.Windows.Forms.Panel
$BottomPanel.Dock = "Bottom"
$BottomPanel.Height = 128
$BottomPanel.BackColor = [System.Drawing.Color]::FromArgb(248, 249, 251)
$Form.Controls.Add($BottomPanel)

$AttachLabel = New-Object System.Windows.Forms.Label
$AttachLabel.Text = "No file loaded"
$AttachLabel.AutoSize = $false
$AttachLabel.Height = 24
$AttachLabel.Left = 12
$AttachLabel.Top = 8
$AttachLabel.Width = 660
$BottomPanel.Controls.Add($AttachLabel)

$BtnAttach = New-Object System.Windows.Forms.Button
$BtnAttach.Text = "Upload"
$BtnAttach.Width = 112
$BtnAttach.Height = 34
$BtnAttach.Anchor = "Top,Right"
$BtnAttach.Location = New-Object System.Drawing.Point(680, 38)
$BottomPanel.Controls.Add($BtnAttach)

$BtnSend = New-Object System.Windows.Forms.Button
$BtnSend.Text = "SEND"
$BtnSend.Width = 112
$BtnSend.Height = 40
$BtnSend.Anchor = "Top,Right"
$BtnSend.Location = New-Object System.Drawing.Point(680, 78)
$BtnSend.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 10, [System.Drawing.FontStyle]::Bold)
$BottomPanel.Controls.Add($BtnSend)

$Input = New-Object System.Windows.Forms.TextBox
$Input.Multiline = $true
$Input.ScrollBars = "Vertical"
$Input.AcceptsReturn = $true
$Input.Anchor = "Left,Right,Top,Bottom"
$Input.Location = New-Object System.Drawing.Point(12, 40)
$Input.Size = New-Object System.Drawing.Size(820, 76)
$BottomPanel.Controls.Add($Input)

function Set-BottomLayout {
    $w = $BottomPanel.ClientSize.Width
    if ($w -lt 520) { $w = 520 }
    $buttonW = 112
    $gap = 12
    $rightX = $w - $buttonW - $gap
    $inputW = $rightX - 24
    if ($inputW -lt 320) { $inputW = 320 }

    $AttachLabel.SetBounds(12, 8, $w - 24, 24)
    $Input.SetBounds(12, 40, $inputW, 76)
    $BtnAttach.SetBounds($rightX, 40, $buttonW, 34)
    $BtnSend.SetBounds($rightX, 78, $buttonW, 40)
}

$BottomPanel.Add_Resize({ Set-BottomLayout })
$BottomPanel.BringToFront()
$TopPanel.BringToFront()

function Append-Chat([string]$speaker, [string]$text, [System.Drawing.Color]$color) {
    $Chat.SelectionStart = $Chat.TextLength
    $Chat.SelectionColor = $color
    $Chat.AppendText("$speaker`r`n")
    $Chat.SelectionColor = [System.Drawing.Color]::FromArgb(30, 30, 30)
    $Chat.AppendText("$text`r`n`r`n")
    $Chat.ScrollToCaret()
}

function Refresh-AttachLabel {
    if ($script:PendingLabels.Count -eq 0) {
        $AttachLabel.Text = "No file loaded. Supports images, PDF, Word, PPT, Excel, Markdown, TXT, CSV, JSON."
    } else {
        $AttachLabel.Text = "Loaded: " + (($script:PendingLabels | Select-Object -First 3) -join "; ")
    }
}

function Show-Settings {
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = "Settings"
    $dlg.StartPosition = "CenterParent"
    $dlg.Size = New-Object System.Drawing.Size(520, 230)
    $dlg.FormBorderStyle = "FixedDialog"
    $dlg.MaximizeBox = $false
    $dlg.MinimizeBox = $false
    $dlg.Font = $Form.Font

    $lblKey = New-Object System.Windows.Forms.Label
    $lblKey.Text = "Gemini API Key"
    $lblKey.Location = New-Object System.Drawing.Point(16, 22)
    $lblKey.AutoSize = $true
    $dlg.Controls.Add($lblKey)

    $txtKey = New-Object System.Windows.Forms.TextBox
    $txtKey.Location = New-Object System.Drawing.Point(130, 18)
    $txtKey.Width = 350
    $txtKey.UseSystemPasswordChar = $true
    $txtKey.Text = $script:Config.apiKey
    $dlg.Controls.Add($txtKey)

    $lblModel = New-Object System.Windows.Forms.Label
    $lblModel.Text = "Model"
    $lblModel.Location = New-Object System.Drawing.Point(16, 62)
    $lblModel.AutoSize = $true
    $dlg.Controls.Add($lblModel)

    $txtModel = New-Object System.Windows.Forms.TextBox
    $txtModel.Location = New-Object System.Drawing.Point(130, 58)
    $txtModel.Width = 350
    $txtModel.Text = $script:Config.model
    $dlg.Controls.Add($txtModel)

    $hint = New-Object System.Windows.Forms.Label
    $hint.Text = "Saved locally: $ConfigPath"
    $hint.Location = New-Object System.Drawing.Point(16, 105)
    $hint.Size = New-Object System.Drawing.Size(465, 36)
    $dlg.Controls.Add($hint)

    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = "Save"
    $ok.Location = New-Object System.Drawing.Point(300, 150)
    $ok.Width = 80
    $dlg.Controls.Add($ok)

    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = "Cancel"
    $cancel.Location = New-Object System.Drawing.Point(400, 150)
    $cancel.Width = 80
    $dlg.Controls.Add($cancel)

    $ok.Add_Click({
        $script:Config.apiKey = $txtKey.Text.Trim()
        if ($txtModel.Text.Trim()) { $script:Config.model = $txtModel.Text.Trim() } else { $script:Config.model = $DefaultModel }
        Save-Config $script:Config
        $ModelLabel.Text = "Model: $($script:Config.model)"
        $dlg.DialogResult = [System.Windows.Forms.DialogResult]::OK
        $dlg.Close()
    })
    $cancel.Add_Click({ $dlg.Close() })
    [void]$dlg.ShowDialog($Form)
}

function Send-Message {
    if (!$script:Config.apiKey) {
        Show-Settings
        if (!$script:Config.apiKey) { return }
    }

    $text = $Input.Text.Trim()
    if (!$text -and $script:PendingParts.Count -eq 0) { return }

    $maybePath = Clean-Path $text
    if ($text -and (Test-Path -LiteralPath $maybePath) -and $script:PendingParts.Count -eq 0) {
        try {
            Add-Attachment $maybePath
            $text = "Please analyze this file."
        } catch {
            [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "Load failed") | Out-Null
            return
        }
    }

    $parts = New-Object System.Collections.ArrayList
    if ($text) { [void]$parts.Add((New-TextPart $text)) }
    foreach ($p in $script:PendingParts) { [void]$parts.Add($p) }

    $displayText = if ($text) { $text } else { "Please analyze the uploaded file." }
    if ($script:PendingLabels.Count -gt 0) {
        $displayText += "`r`n[Files] " + ($script:PendingLabels -join "; ")
    }
    Append-Chat "You" $displayText ([System.Drawing.Color]::FromArgb(30, 90, 180))

    [void]$script:Messages.Add(@{ role = "user"; parts = @($parts) })
    $script:PendingParts.Clear()
    $script:PendingLabels.Clear()
    Refresh-AttachLabel
    $Input.Clear()

    $BtnSend.Enabled = $false
    $BtnAttach.Enabled = $false
    $Form.Cursor = [System.Windows.Forms.Cursors]::WaitCursor
    try {
        $payload = @{
            systemInstruction = @{ parts = @(@{ text = $script:SystemPrompt }) }
            contents = @($script:Messages)
            generationConfig = @{
                temperature = 0.4
                topP = 0.95
                maxOutputTokens = 4096
            }
        }
        $json = $payload | ConvertTo-Json -Depth 30 -Compress
        $url = "https://generativelanguage.googleapis.com/v1beta/models/$($script:Config.model):generateContent?key=$($script:Config.apiKey)"
        $resp = Invoke-RestMethod -Method Post -Uri $url -ContentType "application/json; charset=utf-8" -Body $json -TimeoutSec 180
        $replyParts = @()
        foreach ($part in $resp.candidates[0].content.parts) {
            if ($part.text) { $replyParts += $part.text }
        }
        $reply = ($replyParts -join "`r`n")
        if (!$reply) { $reply = "No text response received." }
        Append-Chat "ZhuaQian" $reply ([System.Drawing.Color]::FromArgb(0, 130, 80))
        [void]$script:Messages.Add(@{ role = "model"; parts = @(@{ text = $reply }) })
    } catch {
        Append-Chat "Error" $_.Exception.Message ([System.Drawing.Color]::FromArgb(190, 40, 40))
    } finally {
        $BtnSend.Enabled = $true
        $BtnAttach.Enabled = $true
        $Form.Cursor = [System.Windows.Forms.Cursors]::Default
    }
}

$BtnAttach.Add_Click({
    $ofd = New-Object System.Windows.Forms.OpenFileDialog
    $ofd.Title = "Choose files"
    $ofd.Multiselect = $true
    $ofd.Filter = "Supported files|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.pdf;*.docx;*.xlsx;*.xlsm;*.pptx;*.txt;*.md;*.csv;*.json|All files|*.*"
    if ($ofd.ShowDialog($Form) -eq [System.Windows.Forms.DialogResult]::OK) {
        foreach ($file in $ofd.FileNames) {
            try {
                Add-Attachment $file
            } catch {
                [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "Load failed") | Out-Null
            }
        }
        Refresh-AttachLabel
    }
})

$BtnSend.Add_Click({ Send-Message })
$BtnSettings.Add_Click({ Show-Settings })
$BtnClear.Add_Click({
    $script:Messages.Clear()
    $script:PendingParts.Clear()
    $script:PendingLabels.Clear()
    $Chat.Clear()
    Refresh-AttachLabel
})

$Input.Add_KeyDown({
    if ($_.KeyCode -eq [System.Windows.Forms.Keys]::Enter -and !$_.Shift) {
        $_.SuppressKeyPress = $true
        Send-Message
    }
})

Refresh-AttachLabel
Set-BottomLayout
Append-Chat "ZhuaQian" "Desktop app is ready. Click Settings to add your Gemini API key, upload files, then ask a question. Press Enter to send, Shift+Enter for a new line." ([System.Drawing.Color]::FromArgb(0, 130, 80))

if (!$script:Config.apiKey) {
    $Form.Add_Shown({ Show-Settings })
}

[void][System.Windows.Forms.Application]::Run($Form)
