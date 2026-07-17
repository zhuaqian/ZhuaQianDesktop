param(
    [string]$Output = "ZhuaQianDesktop.exe"
)

$csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
$src = @(
    "ZhuaQianDesktop.cs"
    "ui\MainForm.Share.cs"
    "ui\MainForm.LiveSession.cs"
    "ui\MainForm.Monitoring.cs"
    "ui\MainForm.PlanExecution.cs"
    "ui\MainForm.PlanReview.cs"
    "ui\MainForm.PromptWorkbench.cs"
    "Models\TaskTimelineItem.cs"
    "Core\ConfigStore.cs"
    "Core\AuditLog.cs"
    "Core\PermissionGate.cs"
    "Core\PromptLibrary.cs"
    "Core\ShareCrypto.cs"
    "Core\PackageBuilder.cs"
    "Core\OutputsHub.cs"
    "Core\LanShareServer.cs"
    "Documents\OfficeExporter.cs"
    "Documents\Redactor.cs"
    "Knowledge\Chunker.cs"
    "Knowledge\VectorIndex.cs"
    "Agent\IAgentCommand.cs"
    "Agent\AgentPlan.cs"
    "Agent\AgentPlanCommandMapper.cs"
    "Agent\ICommandExecutor.cs"
    "Agent\IAsyncCommandExecutor.cs"
    "Agent\CommandResult.cs"
    "Agent\AgentPipeline.cs"
    "Agent\AgentPipelineFactory.cs"
    "Agent\ExportFileExecutor.cs"
    "Agent\OrganizeFolderExecutor.cs"
    "Agent\PluginRunExecutor.cs"
    "Agent\ProcessManageExecutor.cs"
    "Agent\ComputerControlExecutor.cs"
    "Agent\RollbackExecutor.cs"
    "Agent\WebSearchExecutor.cs"
    "ui\SettingsDialog.cs"
    "ui\PlanReviewDialog.cs"
    "ui\PromptWorkbenchDialog.cs"
    "Tools\FolderOrganizer.cs"
    "Tools\ApprovalCard.cs"
    "Tools\ProcessSnapshotCollector.cs"
    "Tools\SystemDiagnostics.cs"
    "Tools\PluginRunner.cs"
    "Tools\CommandParser.cs"
    "Tools\WebSearchClient.cs"
    "Tools\SmartCommand.cs"
    "Tools\ExecutionTimeline.cs"
    "Tools\SandboxProgressPanel.cs"
    "Tools\UndoRedoManager.cs"
    "providers\ModelRegistry.cs"
    "providers\IProviderClient.cs"
    "providers\GeminiClient.cs"
    "providers\OpenRouterClient.cs"
    "providers\LocalClient.cs"
    "providers\OpenAIClient.cs"
    "providers\TencentWorkBuddyClient.cs"
    "providers\AlibabaQianwenClient.cs"
    "providers\ZhipuAIGLMClient.cs"
    "providers\ProviderManager.cs"
    "providers\ShareClient.cs"
    "providers\StreamingBridge.cs"
)

$refs = @(
    "System.Windows.Forms.dll"
    "System.Drawing.dll"
    "System.Web.Extensions.dll"
    "System.Security.dll"
    "System.IO.Compression.dll"
    "System.IO.Compression.FileSystem.dll"
)

$argsList = @(
    "/target:winexe"
    "/out:$Output"
    "/nologo"
    "/reference:$($refs -join ';')"
    $src
)

Write-Host "Compiling $Output ..."
Push-Location $PSScriptRoot
try {
    & $csc $argsList

    if ($LASTEXITCODE -eq 0) {
        Write-Host "Build OK: $Output" -ForegroundColor Green
    } else {
        Write-Host "Build FAILED" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
