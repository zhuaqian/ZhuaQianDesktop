using System.Collections.Generic;
using System.IO;
using ZhuaQianDesktopApp.Tools;

namespace ZhuaQianDesktopApp.Agent
{
    public sealed class OrganizeFolderExecutor : ICommandExecutor
    {
        readonly string configDir;

        public OrganizeFolderExecutor(string configDir)
        {
            this.configDir = configDir;
        }

        public string CommandType { get { return "OrganizeFolder"; } }

        public CommandResult Execute(IAgentCommand command)
        {
            object rootDirObj;
            if (!command.Parameters.TryGetValue("rootDir", out rootDirObj) || rootDirObj == null)
                return CommandResult.Failed("missing parameter: rootDir");

            string rootDir = rootDirObj.ToString();
            var organizer = new FolderOrganizer(configDir);
            var plan = organizer.BuildPlan(rootDir);
            var result = organizer.Execute(rootDir, plan);

            if (result.Errors > 0 && result.Moved == 0)
                return CommandResult.Failed("organize failed for all files, see manifest: " + result.ManifestPath);

            int size = 0;
            try
            {
                var info = new FileInfo(result.ManifestPath);
                if (info.Exists && info.Length <= int.MaxValue) size = (int)info.Length;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("OrganizeFolderExecutor manifest size: " + ex.Message);
            }

            return CommandResult.Ok(result.ManifestPath, canRollback: true, manifestPath: result.ManifestPath, outputType: "rollback", sizeBytes: size);
        }
    }
}
