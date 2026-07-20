using System;
using ZhuaQianDesktopApp.Core;

// Validates the pluggable permission-rule seam (roadmap 2.4) WITHOUT a real UI.
static class TestPermissionGate
{
    public static int RunAll()
    {
        int failures = 0;
        failures += TestBuiltInPreservedWhenNoRules();
        failures += TestRuleCanDeny();
        failures += TestRuleCanAllowOverrideAsk();
        failures += TestDenyWinsOverAllow();
        Console.WriteLine("[TestPermissionGate] failures=" + failures);
        return failures;
    }

    static int TestBuiltInPreservedWhenNoRules()
    {
        int fails = 0;
        var g = new PermissionGate();
        g.Set("permFileWrite", PermissionLevel.Ask);
        Assert(g.Check("permFileWrite", "x.txt") == PermissionDecision.Ask, "built-in Ask preserved with no extra rules");
        return fails;
    }

    static int TestRuleCanDeny()
    {
        int fails = 0;
        var g = new PermissionGate();
        g.Set("permFileWrite", PermissionLevel.Allow);
        g.AddRule(new DelegatePermissionRule((a, t) => a == "permFileWrite" ? PermissionDecision.Deny : (PermissionDecision?)null));
        Assert(g.Check("permFileWrite", "x.txt") == PermissionDecision.Deny, "extra rule can deny an otherwise-allowed action");
        return fails;
    }

    static int TestRuleCanAllowOverrideAsk()
    {
        int fails = 0;
        var g = new PermissionGate();
        g.Set("permFileWrite", PermissionLevel.Ask);
        g.AddRule(new DelegatePermissionRule((a, t) => a == "permFileWrite" ? PermissionDecision.Allow : (PermissionDecision?)null));
        Assert(g.Check("permFileWrite", "x.txt") == PermissionDecision.Allow, "extra rule can allow an Ask action");
        return fails;
    }

    static int TestDenyWinsOverAllow()
    {
        int fails = 0;
        var g = new PermissionGate();
        g.Set("permFileWrite", PermissionLevel.Ask);
        g.AddRule(new DelegatePermissionRule((a, t) => PermissionDecision.Allow));
        g.AddRule(new DelegatePermissionRule((a, t) => PermissionDecision.Deny));
        Assert(g.Check("permFileWrite", "x.txt") == PermissionDecision.Deny, "deny rule wins over allow rule");
        return fails;
    }

    static void Assert(bool cond, string msg) { if (!cond) Console.WriteLine("  FAIL: " + msg); }
}
