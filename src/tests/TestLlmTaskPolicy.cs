using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZhuaQianDesktopApp.Agent;

// Validates the LLM-backed task policy's prompt/reply parsing WITHOUT any real
// model: the chat function is faked to return fixed JSON (or garbage), so the
// parse/fallback logic can run in CI on the raw-csc test build.
static class TestLlmTaskPolicy
{
    public static int RunAll()
    {
        int failures = 0;
        failures += TestParseClick();
        failures += TestParseDone();
        failures += TestFallbackOnGarbage();
        failures += TestFallbackOnMissingAction();
        failures += TestStripFences();
        Console.WriteLine("[TestLlmTaskPolicy] failures=" + failures);
        return failures;
    }

    static int TestParseClick()
    {
        int fails = 0;
        string reply = "{\"done\":false,\"reasoning\":\"click login\",\"action\":{\"commandType\":\"click\",\"target\":\"#login\",\"parameters\":{\"selector\":\"#login\"}}}";
        var policy = new LlmTaskPolicy((p, t) => Task.FromResult(reply));
        var decision = policy.DecideAsync(new Observation(), new List<StepRecord>(), CancellationToken.None).GetAwaiter().GetResult();
        Assert(!decision.Done, "click decision not done");
        Assert(decision.NextAction != null && decision.NextAction.CommandType == "click", "commandType is click");
        Assert(decision.NextAction.Parameters.ContainsKey("selector"), "selector param carried");
        return fails;
    }

    static int TestParseDone()
    {
        int fails = 0;
        string reply = "{\"done\":true,\"reasoning\":\"goal reached\"}";
        var policy = new LlmTaskPolicy((p, t) => Task.FromResult(reply));
        var decision = policy.DecideAsync(new Observation(), new List<StepRecord>(), CancellationToken.None).GetAwaiter().GetResult();
        Assert(decision.Done, "done flag parsed");
        Assert(decision.Reasoning == "goal reached", "reasoning parsed");
        return fails;
    }

    static int TestFallbackOnGarbage()
    {
        int fails = 0;
        var policy = new LlmTaskPolicy((p, t) => Task.FromResult("I'm not sure what to do here, no json"));
        var decision = policy.DecideAsync(new Observation(), new List<StepRecord>(), CancellationToken.None).GetAwaiter().GetResult();
        Assert(!decision.Done, "fallback not done");
        Assert(decision.NextAction != null && decision.NextAction.CommandType == "wait", "fallback is wait action");
        return fails;
    }

    static int TestFallbackOnMissingAction()
    {
        int fails = 0;
        string reply = "{\"done\":false,\"reasoning\":\"hmm\"}"; // no action object
        var policy = new LlmTaskPolicy((p, t) => Task.FromResult(reply));
        var decision = policy.DecideAsync(new Observation(), new List<StepRecord>(), CancellationToken.None).GetAwaiter().GetResult();
        Assert(decision.NextAction != null && decision.NextAction.CommandType == "wait", "missing action -> wait fallback");
        return fails;
    }

    static int TestStripFences()
    {
        int fails = 0;
        string reply = "Here is the decision:\n```json\n{\"done\":false,\"reasoning\":\"fill\",\"action\":{\"commandType\":\"fill\",\"target\":\"#q\",\"parameters\":{\"selector\":\"#q\",\"value\":\"hi\"}}}\n```";
        var policy = new LlmTaskPolicy((p, t) => Task.FromResult(reply));
        var decision = policy.DecideAsync(new Observation(), new List<StepRecord>(), CancellationToken.None).GetAwaiter().GetResult();
        Assert(decision.NextAction != null && decision.NextAction.CommandType == "fill", "fenced json stripped and parsed");
        Assert(decision.NextAction.Parameters.ContainsKey("value"), "value param carried");
        return fails;
    }

    static void Assert(bool cond, string msg) { if (!cond) { Console.WriteLine("  FAIL: " + msg); } }
}
