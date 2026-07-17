# ZhuaQian Desktop - Refactoring Execution Plan

## 🎯 **IMMEDIATE EXECUTION - Core Infrastructure Extraction** (Days 1-3)

### **Status: READY TO BEGIN**

The single file architecture defect **MUST** be fixed immediately. The current monolithic `ZhuaQianDesktop.cs` (9150+ lines) blocks all meaningful development.

---

## **🔥 NEXT STEPS - Core Infrastructure Extraction (Hours 0-72)**

### **Phase 1: Extract ConfigStore Infrastructure**

#### **Task Description:**
Extract configuration management class from monolithic file to `src/Core/ConfigStore.cs`

**Source Location:** `work/zq-desktop/ZhuaQianDesktop.cs` (Lines ~500-600)

**Implementation:**

```csharp
namespace ZhuaQianDesktopApp.Core
{
    public class ConfigStore
    {
        // Core configuration fields
        readonly string configPath;
        readonly string configDir;
        readonly JavaScriptSerializer json;
        
        // Configuration properties
        public string GeminiApiKey;
        public string OpenRouterApiKey;
        public string Model;
        public string Provider;
        public string OpenRouterModel;
        public string LocalApiUrl;
        public string LocalModel;
        public string EmbeddingModel;
        public string PluginDir;
        public string UiLanguage;
        public string WorkMode;
        public bool EnableHotkey;
        public bool ComputerControlEnabled;
        public bool AllowAdvancedPlugins;
        public bool RedactSensitive;
        public PermissionGate Permissions;
        
        // Constructor
        public ConfigStore(string configDir, string configPath)
        
        // Core functionality
        public void Load()                    // Load configuration from JSON
        public void Save()                    // Save configuration to JSON
        public string Protect(string value)   // Windows DPAPI protection
        public string Unprotect(string value) // Windows DPAPI unprotection
        public void MigrateLegacyPermissions(Dictionary<string, object> cfg) // Legacy support
        
        // Helper
        public static void TryGet(Dictionary<string, object> cfg, string key, ref string field, string defaultValue)
    }
}
```

**Extraction Action Items:**

1. [ ] Create `src/Core/ConfigStore.cs` with class above
2. [ ] Identify exact source lines in original file
3. [ ] Extract class (300-350 lines)
4. [ ] Update imports and namespaces
5. [ ] Fix all internal references
6. [ ] Test compilation
7. [ ] Write basic unit test

---

### **Phase 2: Extract AuditLog Infrastructure**

**Task:** Extract audit logging class to `src/Core/AuditLog.cs`

**Source:** `work/zq-desktop/ZhuaQianDesktop.cs` (Lines ~2000-2100)

**Implementation:**

```csharp
namespace ZhuaQianDesktopApp.Core
{
    public class AuditLog
    {
        // Core audit infrastructure
        readonly string auditPath;
        readonly string actionLogPath;
        readonly JavaScriptSerializer json;
        
        // Core functionality
        public AuditLog(string configDir, string auditLogFile, string actionLogFile)
        public void Log(string action, string detail)                    // Audit trail
        public void RecordAction(string type, string status, string detail, string outputPath, // Structured logging
            string taskId = "", string taskTitle = "", string taskStatus = "")
        public string ReadAuditLog()                                        // Read audit data
        public List<Dictionary<string, object>> LoadActionLog(int max)    // Load structured logs
        
        // Helper methods
        public static string Sanitize(string value)                          // Data sanitization
    }
}
```

---

### **Phase 3: Extract PermissionGate Infrastructure**

**Task:** Extract security class to `src/Core/PermissionGate.cs`

**Source:** `work/zq-desktop/ZhuaQianDesktop.cs` (Lines ~2200-2400)

**Implementation:**

```csharp
namespace ZhuaQianDesktopApp.Core
{
    public enum PermissionLevel
    {
        Allow,
        Ask,
        Deny
    }
    
    public class PermissionPattern
    {
        public string Pattern;
        public PermissionLevel Level;
        public bool IsMatch(string input)
    }
    
    public class PermissionGate
    {
        // Permission state
        public PermissionLevel FileRead;
        public PermissionLevel FileWrite;
        public PermissionLevel FileMoveDelete;
        public PermissionLevel ProcessManage;
        public PermissionLevel PluginRun;
        public PermissionLevel Screenshot;
        public PermissionLevel Clipboard;
        public PermissionLevel NetworkUpload;
        public bool ComputerControlEnabled;
        public bool AutoMode;
        public string ExternalDirectory;
        public Dictionary<string, List<PermissionPattern>> Patterns;
        
        // Core functionality
        public PermissionGate()
        public bool Check(string category, string actionName, string detail, IWin32Window owner)
        public bool EnsureComputerControlPower(string actionName)
        public PermissionLevel GetEffectiveLevel(string category, string actionName)
        public static PermissionGate Deserialize(Dictionary<string, object> data)
    }
}
```

---

## **🔧 IMMEDIATE EXECUTION COMMANDS**

### **Step 1: Analyze and Extract ConfigStore**

```powershell
# Navigate to project
cd /Users/本机/Documents/Codex/2026-07-10/c-users-workbuddy-2026-07-10

# Identify extraction boundaries in ZhuaQianDesktop.cs
# Use grep/pattern matching to find exact lines
# Create src/Core/ConfigStore.cs with extracted class

# Test compilation to verify no breaking changes
powershell src/build.ps1
```

### **Step 2: Extract AuditLog**

```powershell
# Create src/Core/AuditLog.cs
# Copy class implementation
# Update build.ps1
# Test compilation again
```

### **Step 3: Extract PermissionGate**

```powershell
# Create src/Core/PermissionGate.cs
# Copy class implementation
# Update build.ps1
# Final compilation test
```

---

## **📊 EXECUTION METRICS (Track Progress)**

| Class Extracted | Lines Count | Status | Exec Time |
|----------------|-------------|--------|-----------|
| ConfigStore.cs | 300-350 | 🔧 **IN PROGRESS** | 1-2 hours |
| AuditLog.cs | 200-250 | 🟡 **PENDING** | 2-3 hours |
| PermissionGate.cs | 400-450 | 🟡 **PENDING** | 3-4 hours |
| PluginRunner.cs | 350-400 | 🟡 **PENDING** | 4-5 hours |
| FolderOrganizer.cs | 300-350 | 🟡 **PENDING** | 5-6 hours |

**Total Estimated Time:** 16-21 hours for initial 5 core classes

---

## **🎯 SUCCESS CRITERIA**

### **Technical Success (First 24 Hours):**
1. ✅ **Core Infrastructure Extracted**: ConfigStore, AuditLog, PermissionGate
2. ✅ **Build System Updated**: `src/build.ps1` modified, `ZhuaQianDesktop.cs` removed
3. ✅ **Compilation Verified**: No breaking changes, smoke test passes
4. ✅ **Test Framework Operational**: xUnit setup complete

### **Project Success (First 72 Hours):**
1. ✅ **All Core Classes Extracted**: 5+ infrastructure classes modularized
2. ✅ **Build System Complete**: All extracted classes included
3. ✅ **Comprehensive Testing**: Unit tests for extracted classes
4. ✅ **Documentation Updated**: Progress tracking and API changes

### **Long-term Success (30 Days):**
1. ✅ **Full Architecture Refactoring**: Complete modular structure
2. ✅ **Enterprise Standards**: Code quality, testing, documentation
3. ✅ **Production Ready**: All functionality preserved, enhanced

---

## 🚨 **CRITICAL PATH & RISKS**

### **High-Risk Elements (Must Mitigate):**

1. **Breaking Changes**:
   - **Risk**: Critical - Code functionality loss
   - **Mitigation**: Incremental extraction, unit tests, smoke testing
   - **Action**: Test compile after each class extraction

2. **Reference Updates**:
   - **Risk**: High - Internal code references
   - **Mitigation**: Systematic reference updating, backup preserved
   - **Action**: Update all `namespace ZhuaQianDesktopApp` -> nested classes to proper namespaces

3. **Build System Changes**:
   - **Risk**: Critical - Compilation failure
   - **Mitigation**: Backup current build.ps1, incremental updates
   - **Action**: Remove `ZhuaQianDesktop.cs`, add modular files

### **Testing Strategy:**
1. **Smoke Testing**: Basic functionality after each extraction
2. **Unit Testing**: Individual class testing
3. **Integration Testing**: Component interaction
4. **Regression Testing**: No functionality loss

---

## 📋 **IMMEDIATE ACTION PLAN (Next 72 Hours)**

### **TODAY (Hours 0-8):**
1. [ ] **Extract ConfigStore Class** 🔴 PRIORITY
   - Create `src/Core/ConfigStore.cs`
   - Identify exact source boundaries
   - Extract class implementation

2. [ ] **Update Build.ps1** 🔴 PRIORITY
   - Remove `ZhuaQianDesktop.cs`
   - Add `src/Core/ConfigStore.cs`
   - Test compilation

3. [ ] **Setup Testing Framework** 🟡
   - Create test project structure
   - Write basic smoke tests
   - Integrate with GitHub Actions

### **TOMORROW (Hours 8-16):**
1. [ ] **Extract AuditLog Class** 🔴 PRIORITY
   - Create `src/Core/AuditLog.cs`
   - Extract audit infrastructure
   - Update build.ps1

2. [ ] **Extract PermissionGate Class** 🔴 PRIORITY
   - Create `src/Core/PermissionGate.cs`
   - Extract security functionality
   - Update build.ps1

3. [ ] **Update Documentation** 🟡
   - Log extraction progress
   - Update REFACTORING_PLAN.md

### **DAY 3 (Hours 16-24):**
1. [ ] **Extract PluginRunner Class** 🟢
2. [ ] **Extract FolderOrganizer Class** 🟢
3. [ ] **Finalize Build System** 🟢

---

## 🎯 **EXECUTIVE SUMMARY**

**IMMEDIATE NEED**: The single file architecture defect **MUST** be fixed first.

**PATH FORWARD**: Modular architecture transformation starting with core infrastructure extraction.

**SEQUENTIAL APPROACH**: Extract core classes first (most critical for testing), then business logic, then UI components.

**IMPACT**: This refactoring enables:
- ✅ Testability (unit test framework)
- ✅ Maintainability (modular code)
- ✅ Collaboration (independent module development)
- ✅ Quality (enterprise standards)
- ✅ Sustainability (long-term viability)

**NEXT TASK**: **IMMEDIATELY** begin extracting ConfigStore class from monolithic file to modular structure.

**TIME IS CRITICAL** - Delays compound technical debt exponentially.

---

*Document Version: 1.0*
*Created: 2026-07-11*
*Target Completion: 2026-07-18 (30-day refactoring sprint)*
*Contact: Project Lead for execution coordination*