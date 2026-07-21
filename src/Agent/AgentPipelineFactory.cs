using System;
using System.IO;
using ZhuaQianDesktopApp.Core;
using ZhuaQianDesktopApp.Documents;
using ZhuaQianDesktopApp.Plugins;
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

        public AgentPipeline Create(PermissionGate permissionGate, string pluginDir, bool allowAdvancedPlugins, string projectRootOverride = null, Func<PluginManifest, bool> capabilityConfirm = null, bool allowUntrustedPlugins = false)
        {
            var pipeline = new AgentPipeline(permissionGate, new AuditLog(auditLogPath), outputsHub);
            RegisterStandardExecutors(pipeline, pluginDir, allowAdvancedPlugins, projectRootOverride, capabilityConfirm, allowUntrustedPlugins);
            return pipeline;
        }

        public void RegisterStandardExecutors(AgentPipeline pipeline, string pluginDir, bool allowAdvancedPlugins, string projectRootOverride = null, Func<PluginManifest, bool> capabilityConfirm = null, bool allowUntrustedPlugins = false)
        {
            pipeline.Register(new ExportFileExecutor(officeExporter));
            pipeline.Register(new WriteFileExecutor());
            pipeline.Register(new OfficeTemplateExecutor(officeExporter));
            pipeline.Register(new OrganizeFolderExecutor(configDir));
            var pluginTrust = new PluginTrustStore(System.IO.Path.Combine(configDir, "trusted-publishers.json"));
            // Opt-in to run untrusted (no-manifest) plugins: explicit flag OR a marker
            // file in the config dir. Absent both -> trust enforcement stays on.
            bool allowUntrusted = allowUntrustedPlugins
                || System.IO.File.Exists(System.IO.Path.Combine(configDir, "allow-untrusted-plugins.txt"));
            pipeline.Register(new PluginRunExecutor(pluginDir, allowAdvancedPlugins, 30000, 20000, System.IO.Path.Combine(configDir, "plugin-output"))
            {
                TrustStore = pluginTrust,
                CapabilityConfirm = capabilityConfirm,
                AllowUntrustedPlugins = allowUntrusted
            });
            pipeline.Register(new ProcessManageExecutor());
            pipeline.Register(new ComputerControlExecutor());
            pipeline.Register(new RollbackExecutor(configDir));
            pipeline.Register(new WebSearchExecutor(webSearchClient));
            pipeline.Register(new BrowserFetchExecutor(new BrowserRenderClient(webSearchClient)));
            // Epic H: browser interaction + desktop screen perception. These let the
            // agent *complete work* in a real browser (click/fill/submit) and *see*
            // the desktop before acting (screenshot -> verify), both through the
            // same permission pipeline (permNetworkUpload / permScreenshot).
            pipeline.Register(new BrowserControlExecutor(new BrowserAgentClient(webSearchClient), System.IO.Path.Combine(configDir, "browser-shots"), System.IO.Path.Combine(configDir, "browser-sessions")));
            pipeline.Register(new ScreenCaptureExecutor(new DesktopScreenCapture(), System.IO.Path.Combine(configDir, "screen-shots")));
            pipeline.Register(new RemoteHostExecutor(System.IO.Path.Combine(configDir, "remote-output")));
            // Epic D (publish): open-source repo upload to GitHub / Gitee / GitLab.
            pipeline.Register(new GitHostPublisherExecutor(configDir));
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
