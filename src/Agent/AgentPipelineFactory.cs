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

        public AgentPipeline Create(PermissionGate permissionGate, string pluginDir, bool allowAdvancedPlugins)
        {
            var pipeline = new AgentPipeline(permissionGate, new AuditLog(auditLogPath), outputsHub);
            RegisterStandardExecutors(pipeline, pluginDir, allowAdvancedPlugins);
            return pipeline;
        }

        public void RegisterStandardExecutors(AgentPipeline pipeline, string pluginDir, bool allowAdvancedPlugins)
        {
            pipeline.Register(new ExportFileExecutor(officeExporter));
            pipeline.Register(new OrganizeFolderExecutor(configDir));
            pipeline.Register(new PluginRunExecutor(pluginDir, allowAdvancedPlugins, 30000, 20000, System.IO.Path.Combine(configDir, "plugin-output")));
            pipeline.Register(new ProcessManageExecutor());
            pipeline.Register(new ComputerControlExecutor());
            pipeline.Register(new RollbackExecutor(configDir));
            pipeline.Register(new WebSearchExecutor(webSearchClient));
        }
    }
}
