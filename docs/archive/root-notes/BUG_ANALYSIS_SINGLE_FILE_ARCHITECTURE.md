# BUG ANALYSIS: SINGLE FILE ARCHITECTURE

## Primary BUG IDENTIFIED

The project suffers from a **monolithic architecture** where all functionality is contained in a single file:

**File:** `work/zq-desktop/ZhuaQianDesktop.cs`
**Size:** ~207,505 bytes (200KB+)

This file simultaneously handles:
- UI building (500+ lines)
- Configuration management
- Task storage
- Provider calls
- File extraction
- Office exports (3000+ lines of XML generation)
- Knowledge base indexing
- Plugin execution
- Permission checking
- Audit logging
- Command palette
- Outputs management
- Multi-language support

## IMPACT

1. **Cannot write unit tests** - No separation of concerns
2. **High maintenance overhead** - One change risks breaking everything
3. **Poor collaboration** - Team members cannot work independently
4. **No code reuse** - Multiple responsibilities in one location
5. **Risk-prone UI changes** - Touching UI affects backend logic

## RECOMMENDED FIX

立即按照 `CURRENT_GAPS_ASSESSMENT.md:44-107` 的建议拆分架构：

```
src/
├─ Program.cs
├─ MainForm.cs
├─ Ui/
│  ├─ CommandPalette.cs
│  ├─ OutputsPanel.cs
│  ├─ SettingsDialog.cs
│  └─ PermissionDialog.cs
├─ Core/
│  ├─ ConfigStore.cs
│  ├─ ChatTaskStore.cs
│  ├─ AuditLog.cs
│  └─ PermissionGate.cs
├─ Providers/
│  ├─ GeminiProvider.cs
│  ├─ OpenRouterProvider.cs
│  └─ LocalProvider.cs
├─ Documents/
│  ├─ DocumentExtractor.cs
│  ├─ OfficeExporter.cs
│  └─ Redactor.cs
├─ Knowledge/
│  ├─ KnowledgeIndex.cs
│  ├─ Chunker.cs
│  └─ KnowledgeSearch.cs
└─ Tools/
   ├─ PluginRunner.cs
   ├─ FolderOrganizer.cs
   └─ ResourceMonitor.cs
```

## IMMEDIATE ACTION REQUIRED

1. **Extract Core Classes** from `ZhuaQianDesktop.cs` into separate files
2. **Update build script** (`src/build.ps1`) to include new files
3. **Refactor imports** and dependencies
4. **Test compilation** to ensure no breaking changes

## PRIORITY

This is a **P0 critical defect** that must be addressed immediately for:
- Testability
- Maintainability
- Open-source collaboration readiness
