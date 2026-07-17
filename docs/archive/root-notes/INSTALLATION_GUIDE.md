# ZhuaQian Desktop - Installation Guide & Version Management

## Current State Assessment

**ZhuaQian Desktop v0.1** currently offers:
1. **Single Executable**: `outputs/ZhuaQianDesktop.exe` (~200KB)
2. **Open-Source Package**: `outputs/ZhuaQianDesktop-open-source.zip` (~650KB)
3. **No Installer**: Direct double-click execution only
4. **No Update System**: Manual version management

**Current Structure**:
```
outputs/
├── ZhuaQianDesktop.exe              # End-user distribution
├── ZhuaQianDesktop-open-source.zip  # Full source + build script
└── docs/                            # Analysis documents
```

## Recommended Implementation Path

### Phase 1: Version Management (Short-term, 1-2 weeks)

#### 1.1 Add Code-Level Version Tracking

**Current Gap**: UI shows "v0.1" but no code-level version

**Solution**: Add semantic versioning in MainForm.cs

```csharp
// Line 174 in ZhuaQianDesktop.cs
const string Version = "0.1.0";  // Add this constant
Text = "ZhuaQian Desktop v" + Version;  // Update UI display
```

**Requirements**:
- Major.Minor.Patch versioning
- Inclusion in package.json/Build.ps1
- GitHub Actions version bump automation

#### 1.2 Configuration Backup/Export System

**Current Gap**: No backup or migration of user settings

**Solution**: Add export/import commands in Tools menu

```csharp
// Add to Tools panel in MainForm.cs
// Commands: Export Config, Import Config, Backup Config
// Store in: %APPDATA%\\ZhuaQianDesktop\\ZhuaQianDesktop-config-backup.json
```

**Features**:
- Export all settings (API keys, permissions, UI preferences)
- Import from backup
- Automated daily backup
- Password protection for export

#### 1.3 Basic Update Detection

**Current Gap**: No automatic update checking

**Solution**: Add version check at startup

```csharp
// In MainForm constructor
void CheckForUpdates()
{
    string latestVersion = await CheckGitHubVersionAsync();
    if (IsNewerVersion(currentVersion, latestVersion))
        ShowUpdateNotification(latestVersion);
}
```

**Implementation Options**:
1. **Simple Approach**: Baseline version check, manual download link
2. **Advanced Approach**: Background download, automatic replacement

### Phase 2: Installer Development (Medium-term, Month 1)

#### 2.1 Choose Installer Technology

**Options**:
- **WiX (Windows Installer)**: Industry standard, complex setup
- **Inno Setup**: Popular, easy to use, good documentation
- **NSIS**: Lightweight, script-based
- **Electron/WinRT**: Modern, but heavy dependencies

**Recommendation**: **Inno Setup** - mature, mature, well-documented

#### 2.2 Basic Installer Features

**Inno Script (basic)**:
```inisub
[Files]
Source: "ZhuaQianDesktop.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userdocs}\ZhuaQian Desktop"; Filename: "{app}\ZhuaQianDesktop.exe"
Name: "{commondesktop}\ZhuaQian Desktop"; Filename: "{app}\ZhuaQianDesktop.exe"

[Run]
Filename: "{app}\ZhuaQianDesktop.exe"; Description: "Start ZhuaQian Desktop"; Flags: postinstall, shellexecute

[Code]
procedure InitializeWizard;
begin
    // Custom initialization if needed
end;

procedure CurStepChanged(CurrentStep);
begin
    if CurrentStep = ssDone then
    begin
        // Optional: start application after installation
        // ShellExecute(0, nil, '"{app}\\ZhuaQianDesktop.exe"', nil, nil, SW_SHOWNORMAL);
    end;
end;
```

**Required Features**:
- Silent install mode (`/VERYSILENT`)
- Desktop shortcut creation
- Start Menu folder integration
- Configuration migration from exe data folder
- Uninstaller with clean config removal

### Phase 3: Smart Update System (Month 1-2)

#### 3.1 Update Architecture

**Components**:
```
updater/
├── ZhuaQianUpdater.exe            # Update checker & installer
├── update-config.json             # Update settings
└── changelog.html                 # Release notes
```

**Update Flow**:
1. **Check at Startup**: Compare current version with GitHub Releases
2. **Background Download**: Notify user, download silently
3. **Atomic Replacement**: Extract to temp dir, replace on next restart
4. **Rollback**: Keep previous version if update fails

#### 3.2 Update Implementation Strategy

**Version Check**:
```csharp
async Task CheckForUpdatesAsync()
{
    var releases = await GetGitHubReleasesAsync();
    var latest = releases.OrderByDescending(r => r.Version).First();
    
    if (currentVersion < latest.Version)
    {
        ShowUpdateDialog(latest);
    }
}
```

**Update Download**:
```powershell
# Embedded in exe or separate updater
.
|
 v
UpdateCheck.ps1 --> Download New Exe --> Atomic Replace --> Restart
```

**Commands**:
- `CheckUpdates()` - Version comparison
- `DownloadUpdate()` - Background download
- `InstallUpdate()` - Atomic replacement
- `RollbackUpdate()` - Restore previous version

### Phase 4: Production Release Preparation (Month 2+)

#### 4.1 Release Package Structure

**Current**:
```
outputs/
├── ZhuaQianDesktop.exe
├── ZhuaQianDesktop-open-source.zip
└── docs/
```

**Enhanced**:
```
outputs/
├── releases/
│   ├── v0.2.0/
│   │   ├── ZhuaQianDesktop.exe
│   │   ├── ZhuaQianDesktop.exe.sha256
│   │   ├── installer.exe
│   │   └── CHANGELOG.md
│   ├── v0.1.0/          # Reference for rollback
│   └── current/         # Latest version symlink
├── installer/           # Build scripts
│   └── ZhuaQianDesktop.iss
├── updater/             # Update components
│   └── ZhuaQianUpdater.exe
└── ZhuaQianDesktop.exe  # Fallback direct exe
```

**Requirements**:
- Sign binaries with Authenticode
- Generate SHA256 checksums
- Create update availability detection
- Web server for update downloads

## Implementation Roadmap

### Immediate Actions (Week 1-2)

1. **Add Version System**
   - Add `const string Version = "0.1.0"` to MainForm.cs
   - Update UI display
   - Add to build script for automation

2. **Add Configuration Backup**
   - Implement export/import
   - Add daily backup
   - Add encryption protection

3. **Add Update Detection**
   - Basic version check
   - Display notification for newer versions

### Medium-term Actions (Month 1)

1. **Create Installer**
   - Install Inno Setup
   - Create basic Inno script
   - Build and test installer

2. **Add Advanced Update System**
   - Add updater component
   - Implement background download
   - Add atomic replacement logic

### Long-term Actions (Month 2+)

1. **Polish Installation Experience**
   - Silent install options
   - Configuration migration
   - Custom setup options

2. **Enterprise Features**
   - Domain deployment
   - Machine-wide installation
   - Group policy integration

## Testing Strategy

### Update System Tests
```powershell
# scripts/test-updater.ps1
function Test-VersionCheck
{
    param($currentVersion, $latestVersion)
    # Verify comparison logic
}

function Test-DownloadUpdate
{
    param($url, $outputPath)
    # Verify download functionality
}

function Test-AtomicReplace
{
    param($oldPath, $newPath)
    # Verify replacement safety
}

function Test-Rollback
{
    param($backupPath, $restorePath)
    # Verify rollback capability
}
```

### Installer Tests
- Silent install verification
- Shortcut validation
- Uninstaller cleanup verification
- Configuration migration tests

## Benefits

### User Experience
1. **One-click installation** with desktop shortcuts
2. **Automatic updates** with progress indicators
3. **Configuration portability** with export/import
4. **Professional appearance** with installer

### Developer Experience
1. **Version tracking** with semantic versioning
2. **Automatic release management** with scripts
3. **Multi-channel distribution** (installer + exe)
4. **Production-ready deployment** with signed binaries

### Production Readiness
1. **Error handling** with rollback support
2. **Validation systems** for file integrity
3. **Support for centralized deployment**
4. **Enterprise-grade features**

## Decision Matrix

| Requirement | Single EXE | Installer + Updates | Recommendation |
|-------------|------------|---------------------|----------------|
| Zero-install capability | ✅ | ✅ | Keep both for flexibility |
| Desktop shortcuts | ❌ | ✅ | Installer required |
| Automatic updates | ❌ | ✅ | Must add for user retention |
| Configuration migration | ⚠️ | ✅ | Installer better for multi-machine |
| Developer workflow | ✅ | ⚠️ | Keep single exe for devs |
| Production distribution | ⚠️ | ✅ | Installer for production |

## Recommended Approach

**Keep Single EXE for Core Distribution**:
- Maintain portability and simplicity
- Keep developer workflow smooth
- Maintain fallback option

**Add Installer for Production**:
- Professional installation experience
- System integration
- Configuration management

**Implement Update System**:
- User retention
- Feature delivery
- Support maintenance

**Key Deliverables**:
1. **Version 0.2.0**: Single exe + installer + backup config + basic updates
2. **Version 0.3.0**: Advanced update system + rollback + enterprise features
3. **Version 1.0**: Full production with all features mature

This approach preserves existing functionality while adding professional installation and update capabilities for market readiness.