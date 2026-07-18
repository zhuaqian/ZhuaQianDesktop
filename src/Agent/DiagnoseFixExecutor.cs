using System;
using System.Collections.Generic;

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

            var loop = new CodingLoop(root);

            string buildCmd = GetString(command, "buildCommand");
            string testCmd = GetString(command, "testCommand");
            if (!string.IsNullOrWhiteSpace(buildCmd)) loop.BuildCommand = buildCmd;
            if (!string.IsNullOrWhiteSpace(testCmd)) loop.TestCommand = testCmd;

            int maxIt;
            if (int.TryParse(GetString(command, "maxIterations"), out maxIt) && maxIt > 0) loop.MaxIterations = maxIt;

            string patchText = GetString(command, "patch");
            if (!string.IsNullOrWhiteSpace(patchText))
            {
                var patch = new UnifiedPatch
                {
                    FilePath = GetString(command, "patchFile"),
                    Kind = ChangeKind.Modify,
                    PatchText = patchText,
                    Rationale = "patch supplied via command parameter"
                };
                var plan = new List<List<UnifiedPatch>> { new List<UnifiedPatch> { patch } };
                loop.Decider = new ScriptedCodingFixDecider(plan);
            }
            else
            {
                loop.Decider = new InteractiveCodingFixDecider(ctx => new FixDecision { Stop = true, Rationale = "no model decider configured; diagnose-only" });
            }

            var report = loop.Run();
            return CommandResult.Ok(null, false, null, "report", 0, report.ToMarkdown());
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
