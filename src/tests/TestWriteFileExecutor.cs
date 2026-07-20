using System;
using System.Collections.Generic;
using System.IO;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Core;

// Validates WriteFileExecutor writes text and base64 binary content correctly,
// without any real model or pipeline.
static class TestWriteFileExecutor
{
    public static int RunAll()
    {
        int failures = 0;
        failures += TestWritesText();
        failures += TestWritesBase64();
        Console.WriteLine("[TestWriteFileExecutor] failures=" + failures);
        return failures;
    }

    static int TestWritesText()
    {
        int fails = 0;
        string dir = Path.Combine(Path.GetTempPath(), "zq_test_write_" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "note.md");
        try
        {
            var exec = new WriteFileExecutor();
            var cmd = new AgentCommand("WriteFile", "permFileWrite", "t", path, "save",
                new Dictionary<string, object> { { "content", "# Hello" } });
            var res = exec.Execute(cmd);
            Assert(res.Status == CommandStatus.Success, "text write succeeds");
            Assert(File.Exists(path), "file created");
            Assert(File.ReadAllText(path) == "# Hello", "content matches");
        }
        catch (Exception ex) { Assert(false, "no exception: " + ex.Message); }
        finally { try { Directory.Delete(dir, true); } catch { } }
        return fails;
    }

    static int TestWritesBase64()
    {
        int fails = 0;
        string dir = Path.Combine(Path.GetTempPath(), "zq_test_write2_" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "bin.dat");
        byte[] data = new byte[] { 1, 2, 3, 4 };
        try
        {
            var exec = new WriteFileExecutor();
            var cmd = new AgentCommand("WriteFile", "permFileWrite", "t", path, "save",
                new Dictionary<string, object> { { "base64", Convert.ToBase64String(data) } });
            var res = exec.Execute(cmd);
            Assert(res.Status == CommandStatus.Success, "binary write succeeds");
            Assert(File.ReadAllBytes(path).Length == 4, "binary bytes written");
        }
        catch (Exception ex) { Assert(false, "no exception: " + ex.Message); }
        finally { try { Directory.Delete(dir, true); } catch { } }
        return fails;
    }
}
