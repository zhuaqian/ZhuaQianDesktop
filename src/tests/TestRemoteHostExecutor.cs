using System;
using System.Collections.Generic;
using System.IO;
using ZhuaQianDesktopApp.Agent;

namespace ZhuaQianDesktopApp.Tests
{
    public static class TestRemoteHostExecutor
    {
        public static int RunAll()
        {
            int failures = 0;
            string tmp = null;
            try
            {
                tmp = Path.Combine(Path.GetTempPath(), "zqremote_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);
                failures += TestInvalidHostRejected(tmp);
                failures += TestMissingCommandRejected(tmp);
                failures += TestUnknownActionRejected(tmp);
            }
            catch (Exception ex)
            {
                Console.WriteLine("TestRemoteHostExecutor CRASH: " + ex.Message);
                failures++;
            }
            finally
            {
                try { if (tmp != null && Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }
            return failures;
        }

        static int TestInvalidHostRejected(string tmp)
        {
            var exec = new RemoteHostExecutor(tmp);
            var args = new Dictionary<string, object>();
            args["action"] = "run";
            args["host"] = "example.com;rm -rf /";
            args["command"] = "pwd";
            var result = exec.Execute(new AgentCommand("RemoteHost", "permNetworkUpload", "task", "bad", "bad host", args));
            int f = 0;
            Assert(ref f, result.Status == CommandStatus.Failed, "invalid host rejected");
            Assert(ref f, (result.ErrorMessage ?? "").Contains("invalid host"), "error mentions invalid host");
            return f;
        }

        static int TestMissingCommandRejected(string tmp)
        {
            var exec = new RemoteHostExecutor(tmp);
            var args = new Dictionary<string, object>();
            args["action"] = "run";
            args["host"] = "user@example.com";
            var result = exec.Execute(new AgentCommand("RemoteHost", "permNetworkUpload", "task", "host", "missing command", args));
            int f = 0;
            Assert(ref f, result.Status == CommandStatus.Failed, "missing command rejected");
            Assert(ref f, (result.ErrorMessage ?? "").Contains("command"), "error mentions command");
            return f;
        }

        static int TestUnknownActionRejected(string tmp)
        {
            var exec = new RemoteHostExecutor(tmp);
            var args = new Dictionary<string, object>();
            args["action"] = "format-disk";
            var result = exec.Execute(new AgentCommand("RemoteHost", "permNetworkUpload", "task", "host", "unknown action", args));
            int f = 0;
            Assert(ref f, result.Status == CommandStatus.Failed, "unknown action rejected");
            Assert(ref f, (result.ErrorMessage ?? "").Contains("unknown remote action"), "error mentions unknown action");
            return f;
        }

        static void Assert(ref int failures, bool condition, string message)
        {
            if (condition)
                Console.WriteLine("  PASS: " + message);
            else
            {
                Console.WriteLine("  FAIL: " + message);
                failures++;
            }
        }
    }
}
