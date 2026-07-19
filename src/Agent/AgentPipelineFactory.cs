using ZhuaQianDesktopApp.Core;
using ZhuaQianDesktopApp.Documents;
using ZhuaQianDesktopApp.Tools;

namespace ZhuaQianDesktopApp.Agent
{
    public sealed class AgentPipelineFactory
    {
        readonly string auditLogPath;
        readonly string configDir;
        readonly OutputsHub outputsHub;
        readonly OfficeExporter officeExporter;
        readonly WebSearchClient webSearchClient;

        public AgentPipelineFactory(string auditLogPath, string configDir, OutputsHub outputsHub, OfficeExporter officeExporter, WebSearchClient webSearchClient)
        {
            this.auditLogPath = auditLogPath ?? "";
            this.configDir = configDir ?? "";
            this.outputsHub = outputsHub;
            this.officeExporter = officeExporter ?? new OfficeExporter();
            this.webSearchClient = webSearchClient ?? new WebSearchClient();
        }

        public AgentPipeline Create(PermissionGate permissionGate, string pluginDir, bool allowAdvancedPlugins, string projectRootOverride = null)
        {
            var pipeline = new AgentPipeline(permissionGate, new AuditLog(auditLogPath), outputsHub);
            RegisterStandardExecutors(pipeline, pluginDir, allowAdvancedPlugins, projectRootOverride);
            return pipeline;
        }

        public void RegisterStandardExecutors(AgentPipeline pipeline, string pluginDir, bool allowAdvancedPlugins, string projectRootOverride = null)
        {
            pipeline.Register(new ExportFileExecutor(officeExporter));
            pipeline.Register(new OfficeTemplateExecutor(officeExporter));
            pipeline.Register(new OrganizeFolderExecutor(configDir));
            pipeline.Register(new PluginRunExecutor(pluginDir, allowAdvancedPlugins, 30000, 20000, System.IO.Path.Combine(configDir, "plugin-output")));
            pipeline.Register(new ProcessManageExecutor());
            pipeline.Register(new ComputerControlExecutor());
            pipeline.Register(new RollbackExecutor(configDir));
            pipeline.Register(new WebSearchExecutor(webSearchClient));
            pipeline.Register(new BrowserFetchExecutor(new BrowserRenderClient(webSearchClient)));
            // Epic H: browser interaction + desktop screen perception. These let the
            // agent *complete work* in a real browser (click/fill/submit) and *see*
            // the desktop before acting (screenshot -> verify), both through the
            // same permission pipeline (permNetworkUpload / permScreenshot).
            pipeline.Register(new BrowserControlExecutor(new BrowserAgentClient(webSearchClient), System.IO.Path.Combine(configDir, "browser-shots")));
            pipeline.Register(new ScreenCaptureExecutor(new DesktopScreenCapture(), System.IO.Path.Combine(configDir, "screen-shots")));
            pipeline.Register(new RemoteHostExecutor(System.IO.Path.Combine(configDir, "remote-output")));
            // Epic D: coding-agent closed loop building blocks.
            // configDir is the app config root; the project root for patches/git
            // is the parent of configDir (fall back to current directory).
            string projectRoot = projectRootOverride;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                System.IO.DirectoryInfo parent = configDir != null ? System.IO.Directory.GetParent(configDir) : null;
                projectRoot = parent != null ? parent.FullName : null;
            }
            if (string.IsNullOrWhiteSpace(projectRoot)) projectRoot = System.IO.Directory.GetCurrentDirectory();
            pipeline.Register(new PatchExecutor(projectRoot));
            pipeline.Register(new GitWorkflowExecutor(projectRoot));
            pipeline.Register(new DiagnoseFixExecutor(pipeline, projectRoot));
        }
    }
}
