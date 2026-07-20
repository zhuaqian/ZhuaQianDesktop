using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ZhuaQianDesktopApp.Agent.Coding
{
    // Model-driven fallback for the self-healing loop. RuleBasedFixStrategy only
    // applies guaranteed-safe fixes (CS1002/CS0246/CS0103). For anything else, this
    // strategy asks the LLM to produce a JSON array of precise file edits and
    // returns them as PatchOp "edit" operations that FixLoopRunner applies through
    // the gated pipeline (CodePatcher -> permFileWrite).
    //
    // Implements FixLoopRunner.IFixStrategy so it drops straight into the loop as a
    // fallback. If no model is configured (LlmBridge unavailable) it returns no
    // patches -- the loop then honestly reports "cannot fix".
    public sealed class ModelFixStrategy : FixLoopRunner.IFixStrategy
    {
        public List<FixLoopRunner.PatchOp> Propose(string root, AgentPlanStepResult failedStep, List<FixLoopRunner.BuildError> errors)
        {
            var ops = new List<FixLoopRunner.PatchOp>();
            if (errors == null || errors.Count == 0) return ops;
            if (!LlmBridge.IsAvailable) return ops;

            string errorBlob = BuildErrorBlob(errors);
            const string system =
                "You are a senior software engineer. Given build errors, produce a JSON array of precise edits. " +
                "Each edit is an object: {\"file\": \"relative/path\", \"oldText\": \"exact existing snippet\", " +
                "\"newText\": \"replacement\"}. Only include edits you are confident about. Respond with a single " +
                "JSON array and nothing else.";
            string user = "Project root: " + (root ?? "?") + "\nErrors:\n" + errorBlob;

            string reply;
            try
            {
                // Run off the calling (possibly UI) context to avoid deadlocks.
                reply = Task.Run(() => LlmBridge.AskAsync(LlmBridge.Conversation(system, user)))
                            .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ModelFixStrategy LLM call failed: " + ex.Message);
                return ops;
            }

            var parsed = ParsePatches(reply);
            foreach (var p in parsed)
            {
                if (string.IsNullOrEmpty(p.file) || p.oldText == null || p.newText == null) continue;
                ops.Add(new FixLoopRunner.PatchOp
                {
                    Op = "edit",
                    Target = p.file,
                    OldText = p.oldText,
                    NewText = p.newText
                });
            }
            return ops;
        }

        static string BuildErrorBlob(List<FixLoopRunner.BuildError> errors)
        {
            var sb = new StringBuilder();
            foreach (var e in errors)
                sb.AppendLine((e.File ?? "?") + (e.Line.HasValue ? "(" + e.Line.Value + ")" : "") + ": " + (e.Message ?? ""));
            return sb.ToString();
        }

        // Visible for unit testing: parse the model's JSON array into patch tuples,
        // tolerating surrounding prose / ```json fences.
        public static List<ModelPatch> ParsePatches(string reply)
        {
            var list = new List<ModelPatch>();
            if (string.IsNullOrEmpty(reply)) return list;
            string json = ExtractJsonArray(reply);
            if (string.IsNullOrEmpty(json)) return list;
            try
            {
                var ser = new JavaScriptSerializer();
                var arr = ser.Deserialize<List<ModelPatch>>(json);
                if (arr != null) list.AddRange(arr);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ModelFixStrategy parse failed: " + ex.Message);
            }
            return list;
        }

        static string ExtractJsonArray(string reply)
        {
            int start = reply.IndexOf('[');
            int end = reply.LastIndexOf(']');
            if (start >= 0 && end > start) return reply.Substring(start, end - start + 1);
            return "";
        }

        public sealed class ModelPatch
        {
            public string file;
            public string oldText;
            public string newText;
        }
    }
}
