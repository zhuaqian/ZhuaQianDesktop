using System;

namespace ZhuaQianDesktopApp.Core
{
    // A pluggable permission rule. Evaluate returns a decision or null to abstain.
    // This is the seam the strategy layer (roadmap 2.4) and the future sandbox-tier
    // (2.2) / trust-TTL (1.3) rules register against, so policy becomes data-driven
    // instead of hardcoded inside PermissionGate.Check.
    public interface IPermissionRule
    {
        // null = no opinion (abstain); Allow/Deny overrides the built-in decision.
        PermissionDecision? Evaluate(string action, string target);
    }

    // Wraps a delegate so callers can register inline rules without a new class.
    public sealed class DelegatePermissionRule : IPermissionRule
    {
        readonly Func<string, string, PermissionDecision?> fn;
        public DelegatePermissionRule(Func<string, string, PermissionDecision?> fn) { this.fn = fn; }
        public PermissionDecision? Evaluate(string action, string target)
        {
            return fn == null ? (PermissionDecision?)null : fn(action, target);
        }
    }
}
