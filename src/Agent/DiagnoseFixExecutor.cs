using System;
using System.Collections.Generic;
using ZhuaQianDesktopApp.Agent.Coding;
using ZhuaQianDesktopApp.Core;

namespace ZhuaQianDesktopApp.Agent
{
    // Command entry for the coding-agent closed loop (CommandType=DiagnoseFix).
    // Runs CodingLoop inside the agent pipeline so build/test/patch/git side
    // effects are audited as one command. The "decide the fix" step uses an
    // injected ICodingFixDecider; when none is provided it diagnoses only and
    // stops (safe default). A deterministic patch can be supplied via the
    // `patch` parameter to drive a real fix through the loop for demos/tests.
    public sealed class DiagnoseFixExecutor : ICommandExecutor
    {
        public string CommandType { get { return "DiagnoseFix"; } }

        public CommandResult Execute(IAgentCommand command)
        {
            string root = GetString(command, "root");
            if (string.IsNullOrWhiteSpace(root)) root = command.Target;
            if (string.IsNullOrWhiteSpace(root))
                return CommandResult.Failed("DiagnoseFix requires a root directory (Target or root param)");

            var gate = new PermissionGate();
            gate.AutoMode = true;
            gate.Set("permFileWrite", PermissionLevel.Allow);
            gate.Set("permCommandRun", PermissionLevel.Allow);

            string preApplied = "";
            string patchText = GetString(command, "patch");
            if (!string.IsNullOrWhiteSpace(patchText))
            {
                var patch = new UnifiedPatch
                {
                    FilePath = GetString(command, "patchFile"),
                    Kind = ChangeKind.Modify,
                    PatchText = patchText,
                    Rationale = "manual patch supplied from Diagnose & Fix dialog"
                };
                preApplied = PatchApplier.ApplyToWorkspace(root, patch);
            }

            var options = new BuildFixLoopOptions();
            options.RootDirectory = root;
            string buildCmd = GetString(command, "buildCommand");
            string testCmd = GetString(command, "testCommand");
            if (!string.IsNullOrWhiteSpace(buildCmd)) options.BuildCommand = buildCmd;
            if (!string.IsNullOrWhiteSpace(testCmd)) options.TestCommand = testCmd;

            int maxIt;
            if (int.TryParse(GetString(command, "maxIterations"), out maxIt) && maxIt > 0) options.MaxIterations = maxIt;

            var session = new CodingLoopSession(root, gate, new GuardedCommandRunRecorder(root, gate, null), new RuleBasedFixStrategy());
            var report = session.Run(command.DisplaySummary, options);
            string markdown = report.ToMarkdown();
            if (!string.IsNullOrWhiteSpace(preApplied))
                markdown = "# Pre-applied Patch\n\n" + preApplied + "\n\n" + markdown;
            return CommandResult.Ok(null, false, null, "report", 0, markdown);
        }

        static string GetString(IAgentCommand command, string key)
        {
            if (command.Parameters == null) return "";
            object v;
            if (command.Parameters.TryGetValue(key, out v) && v != null) return v.ToString();
            return "";
        }
    }
}
