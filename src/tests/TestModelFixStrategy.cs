using System;
using System.Collections.Generic;
using ZhuaQianDesktopApp.Agent.Coding;

// Validates ModelFixStrategy.ParsePatches extracts file edits from a model reply
// (with ```json fences) without any real model.
static class TestModelFixStrategy
{
    public static int RunAll()
    {
        int failures = 0;
        failures += TestParseValid();
        failures += TestParseGarbage();
        Console.WriteLine("[TestModelFixStrategy] failures=" + failures);
        return failures;
    }

    static void Assert(bool cond, string msg, ref int fails)
    {
        if (!cond) { fails++; Console.WriteLine("  FAIL: " + msg); }
    }

    static int TestParseValid()
    {
        int fails = 0;
        string reply = "Sure, here are the edits:\n```json\n[{\"file\":\"Program.cs\",\"oldText\":\"int x =\",\"newText\":\"int x = 0\"}]\n```";
        var patches = ModelFixStrategy.ParsePatches(reply);
        Assert(patches.Count == 1, "one patch parsed, got " + patches.Count, ref fails);
        if (patches.Count == 1)
        {
            Assert(patches[0].file == "Program.cs", "file mapped", ref fails);
            Assert(patches[0].newText == "int x = 0", "newText mapped", ref fails);
        }
        return fails;
    }

    static int TestParseGarbage()
    {
        int fails = 0;
        var patches = ModelFixStrategy.ParsePatches("no json here at all");
        Assert(patches.Count == 0, "garbage -> no patches", ref fails);
        return fails;
    }
}
