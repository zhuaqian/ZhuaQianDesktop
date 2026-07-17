# 多项目 .sln 迁移方案

更新时间：2026-07-11
依据：`docs/ARCHITECTURE_CHARTER.md` §4

---

## 1. 目标结构

```
ZhuaQian.sln
├─ src/
│  ├─ ZhuaQian.Core/              # ConfigStore, AuditLog, PermissionGate, OutputsHub
│  │  ├─ ZhuaQian.Core.csproj
│  │  └─ *.cs
│  ├─ ZhuaQian.Providers/         # IProviderClient, *Client, ProviderManager
│  │  ├─ ZhuaQian.Providers.csproj
│  │  └─ *.cs
│  ├─ ZhuaQian.Documents/         # OfficeExporter, Redactor
│  │  ├─ ZhuaQian.Documents.csproj
│  │  └─ *.cs
│  ├─ ZhuaQian.Knowledge/         # Chunker, VectorIndex
│  │  ├─ ZhuaQian.Knowledge.csproj
│  │  └─ *.cs
│  ├─ ZhuaQian.Tools/             # FolderOrganizer, PluginRunner, ProcessSnapshotCollector
│  │  ├─ ZhuaQian.Tools.csproj
│  │  └─ *.cs
│  ├─ ZhuaQian.Agent/             # IAgentCommand, AgentPipeline, *Executor
│  │  ├─ ZhuaQian.Agent.csproj
│  │  └─ *.cs
│  ├─ ZhuaQian.App/               # WinForms UI, MainForm
│  │  ├─ ZhuaQian.App.csproj
│  │  └─ *.cs
│  └─ ZhuaQian.Tests/             # TestRunner, SelfTest, xUnit tests
│     ├─ ZhuaQian.Tests.csproj
│     └─ *.cs
├─ docs/
├─ scripts/
└─ README.md
```

---

## 2. 文件映射表

| 当前路径 | 目标项目 | 说明 |
|----------|----------|------|
| `ZhuaQianDesktop.cs` | `ZhuaQian.App` | 主窗体+Program |
| `MainForm.cs` | `ZhuaQian.App` | 主窗体 partial |
| `Program.cs` | `ZhuaQian.App` | 入口点 |
| `TaskInfo.cs` | `ZhuaQian.App` | 任务数据类 |
| `ui/*.cs` | `ZhuaQian.App` | 所有 UI 文件 |
| `Core/*.cs` | `ZhuaQian.Core` | 基础设施 |
| `Tools/*.cs` | `ZhuaQian.Tools` | 工具层 |
| `providers/*.cs` | `ZhuaQian.Providers` | 模型提供者 |
| `Documents/*.cs` | `ZhuaQian.Documents` | 文档处理 |
| `Knowledge/*.cs` | `ZhuaQian.Knowledge` | 知识库 |
| `Agent/*.cs` | `ZhuaQian.Agent` | 管道+执行器 |
| `Models/*.cs` | `ZhuaQian.App` | 模型类 |
| `tests/*.cs` | `ZhuaQian.Tests` | 测试 |

---

## 3. 项目引用（防止反向依赖）

```
ZhuaQian.App
  ├─ ZhuaQian.Core
  ├─ ZhuaQian.Providers
  ├─ ZhuaQian.Documents
  ├─ ZhuaQian.Knowledge
  ├─ ZhuaQian.Tools
  └─ ZhuaQian.Agent

ZhuaQian.Agent
  └─ ZhuaQian.Core

ZhuaQian.Tools
  └─ ZhuaQian.Core

ZhuaQian.Providers
  └─ ZhuaQian.Core

ZhuaQian.Documents
  └─ ZhuaQian.Core

ZhuaQian.Knowledge
  └─ ZhuaQian.Core

ZhuaQian.Tests
  ├─ ZhuaQian.Core
  ├─ ZhuaQian.Providers
  ├─ ZhuaQian.Documents
  ├─ ZhuaQian.Knowledge
  ├─ ZhuaQian.Tools
  └─ ZhuaQian.Agent

ZhuaQian.Core
  └─ (无项目引用)
```

**强制约束**：
- `Core` 不引用任何其他项目
- `App` 是唯一引用全部模块的项目
- `Agent` 只引用 `Core`
- 其他模块只引用 `Core`
- `Tests` 引用除 `App` 外的全部模块

---

## 4. 迁移脚本

保存为 `scripts/migrate-to-sln.ps1`，在 `work/zq-desktop/` 中运行：

```powershell
param(
    [string]$ProjectRoot = (Split-Path -Parent $MyInvocation.MyCommand.Path | Split-Path -Parent)
)

$ErrorActionPreference = "Stop"

function New-Csproj {
    param([string]$Name, [string]$OutputType, [string[]]$References)
    $guid1 = [Guid]::NewGuid().ToString("D").ToUpper()
    $guid2 = [Guid]::NewGuid().ToString("D").ToUpper()
    $refs = ""
    foreach ($r in $References) {
        $refs += @"
    <Reference Include="$r">
      <HintPath>..\..\lib\$r</HintPath>
    </Reference>
"@ + "`r`n"
    }
    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="`$(MSBuildExtensionsPath)\`$(MSBuildToolsVersion)\Microsoft.Common.props" />
  <PropertyGroup>
    <Configuration Condition=" '`$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '`$(Platform)' == '' ">AnyCPU</Platform>
    <ProjectGuid>{$guid1}</ProjectGuid>
    <OutputType>$OutputType</OutputType>
    <RootNamespace>ZhuaQianDesktopApp.$Name</RootNamespace>
    <AssemblyName>ZhuaQian.$Name</AssemblyName>
    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
  </PropertyGroup>
  <ItemGroup>$refs
  </ItemGroup>
  <ItemGroup>
    <Compile Include="*.cs" />
  </ItemGroup>
  <Import Project="`$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
"@
    return $xml
}

# Backup current tree
$backup = Join-Path $ProjectRoot "..\work\zq-desktop-backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
Write-Host "Backing up to $backup ..."
Copy-Item -LiteralPath $ProjectRoot -Destination $backup -Recurse

$srcDir = Join-Path $ProjectRoot "src"

# Create target directory structure
$modules = @{
    "ZhuaQian.Core"       = @("Core")
    "ZhuaQian.Providers"  = @("providers")
    "ZhuaQian.Documents"  = @("Documents")
    "ZhuaQian.Knowledge"  = @("Knowledge")
    "ZhuaQian.Tools"      = @("Tools", "Agent")
    "ZhuaQian.App"        = @(".", "ui", "Models")
    "ZhuaQian.Tests"      = @("tests")
}

foreach ($m in $modules.Keys) {
    $target = Join-Path $srcDir $m
    New-Item -ItemType Directory -Force -Path $target | Out-Null
}

# Move files
foreach ($m in $modules.Keys) {
    $target = Join-Path $srcDir $m
    foreach ($srcSub in $modules[$m]) {
        $sourcePath = Join-Path $ProjectRoot $srcSub
        if (Test-Path $sourcePath) {
            $items = Get-ChildItem -LiteralPath $sourcePath -Filter "*.cs"
            foreach ($item in $items) {
                $dest = Join-Path $target $item.Name
                Move-Item -LiteralPath $item.FullName -Destination $dest -Force
                Write-Host "  Moved $($item.FullName) → $dest"
            }
        }
    }
}

# Create .csproj files
# Core - no references
$coreCsproj = New-Csproj -Name "Core" -OutputType "Library" -References @()
[System.IO.File]::WriteAllText((Join-Path $srcDir "ZhuaQian.Core\ZhuaQian.Core.csproj"), $coreCsproj, [System.Text.Encoding]::UTF8)

# Providers - references Core
$provCsproj = New-Csproj -Name "Providers" -OutputType "Library" -References @("ZhuaQian.Core.dll")
[System.IO.File]::WriteAllText((Join-Path $srcDir "ZhuaQian.Providers\ZhuaQian.Providers.csproj"), $provCsproj, [System.Text.Encoding]::UTF8)

# Documents - references Core
$docCsproj = New-Csproj -Name "Documents" -OutputType "Library" -References @("ZhuaQian.Core.dll")
[System.IO.File]::WriteAllText((Join-Path $srcDir "ZhuaQian.Documents\ZhuaQian.Documents.csproj"), $docCsproj, [System.Text.Encoding]::UTF8)

# Knowledge - references Core
$knowCsproj = New-Csproj -Name "Knowledge" -OutputType "Library" -References @("ZhuaQian.Core.dll")
[System.IO.File]::WriteAllText((Join-Path $srcDir "ZhuaQian.Knowledge\ZhuaQian.Knowledge.csproj"), $knowCsproj, [System.Text.Encoding]::UTF8)

# Tools - references Core
$toolsCsproj = New-Csproj -Name "Tools" -OutputType "Library" -References @("ZhuaQian.Core.dll")
[System.IO.File]::WriteAllText((Join-Path $srcDir "ZhuaQian.Tools\ZhuaQian.Tools.csproj"), $toolsCsproj, [System.Text.Encoding]::UTF8)

# Agent - references Core
$agentCsproj = New-Csproj -Name "Agent" -OutputType "Library" -References @("ZhuaQian.Core.dll")
[System.IO.File]::WriteAllText((Join-Path $srcDir "ZhuaQian.Agent\ZhuaQian.Agent.csproj"), $agentCsproj, [System.Text.Encoding]::UTF8)

# App - references all others (WinForms exe)
$appCsproj = New-Csproj -Name "App" -OutputType "WinExe" -References @(
    "ZhuaQian.Core.dll",
    "ZhuaQian.Providers.dll",
    "ZhuaQian.Documents.dll",
    "ZhuaQian.Knowledge.dll",
    "ZhuaQian.Tools.dll",
    "ZhuaQian.Agent.dll",
    "System.Windows.Forms.dll",
    "System.Drawing.dll"
)
[System.IO.File]::WriteAllText((Join-Path $srcDir "ZhuaQian.App\ZhuaQian.App.csproj"), $appCsproj, [System.Text.Encoding]::UTF8)

# Tests - references all except App
$testCsproj = New-Csproj -Name "Tests" -OutputType "Exe" -References @(
    "ZhuaQian.Core.dll",
    "ZhuaQian.Providers.dll",
    "ZhuaQian.Documents.dll",
    "ZhuaQian.Knowledge.dll",
    "ZhuaQian.Tools.dll",
    "ZhuaQian.Agent.dll"
)
[System.IO.File]::WriteAllText((Join-Path $srcDir "ZhuaQian.Tests\ZhuaQian.Tests.csproj"), $testCsproj, [System.Text.Encoding]::UTF8)

Write-Host ""
Write-Host "Migration script generated. Run 'dotnet restore && dotnet build' to verify."
Write-Host "Note: This script produces .csproj files for .NET Framework 4.8."
Write-Host "To upgrade to .NET 8, update TargetFrameworkVersion to net8.0-windows after migration."
```

---

## 5. 迁移步骤

### 阶段 1：准备
1. 确认 `work/zq-desktop/` 所有构建和测试通过
2. 备份整个目录
3. 运行 `scripts/migrate-to-sln.ps1`

### 阶段 2：验证
4. `dotnet restore`（需安装 .NET Framework 4.8 SDK或 .NET 8 SDK）
5. `dotnet build src/ZhuaQian.sln`
6. 运行测试

### 阶段 3：收敛
7. 将 `src/` 和 `outputs/` 的重复代码合并进新结构
8. 删除旧的 `work/` 和 `outputs/` 目录
9. 更新 `build.ps1` 指向新 `.csproj`

### 阶段 4（可选）：升级到 .NET 8
10. 修改 `TargetFrameworkVersion` → `net8.0-windows`
11. 验证 `dotnet build` 通过
12. 迁移测试到 xUnit

---

## 6. 风险与回退

- 如果 `dotnet build` 失败：检查 `.csproj` 中缺失的引用
- 如果测试不通过：从备份恢复，分析引用关系
- 如果 `System.Security.dll` 等系统引用缺失：在 `.csproj` 中添加 `<Reference>` 显式声明
