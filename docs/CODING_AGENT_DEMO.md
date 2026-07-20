# Coding-Agent End-to-End Demo Runbook

Proves the **ZhuaQianDesktop autonomous coding agent** (DiagnoseFix closed
loop) runs on a *foreign* repository — a plain `dotnet` console project, not
ZhuaQianDesktop's own `build.ps1` convention. This exercises the
repo-agnostic path that was generalized in commit `45a3971`
(`ProjectAnalyzer` command detection + `GuardedCommandRunRecorder`
`allowedPrograms`).

The demo targets `outputs/coding-agent-demo/DemoApp`, which ships with a
deliberate, safe-to-auto-fix compile error (CS1002: missing `;`).

---

## 0. Prerequisites

- A built `ZhuaQianDesktop.exe` (run `.\src\build.ps1` from the repo root).
  The sandbox cannot compile C#, so this step runs on your machine.
- The **.NET 8 SDK** (`dotnet`) on PATH — `dotnet --version` should print 8.x.
  The demo repo is a `net8.0` console project; `ProjectAnalyzer` will detect
  `dotnet build` / `dotnet test` from its `.csproj`.
- (Optional) `git` on PATH, if you want to perform the final **Commit** step
  from the Diagnose & Fix report dialog.

The demo folder is already a standalone git repo (initialized when it was
created) with the buggy source committed, so you can go straight to step 2.

---

## 1. Build ZhuaQianDesktop

```powershell
cd <repo-root>
.\src\build.ps1
```

This produces `src\ZhuaQianDesktop.exe` (or `outputs\ZhuaQianDesktop.exe`
depending on the build output config). Confirm it launches.

---

## 2. Launch and open DiagnoseFix

Run `ZhuaQianDesktop.exe`, then either:

- **Button (easiest):** click **Diagnose & Fix Project** on the tool panel
  (or the menu item *Diagnose and fix project*). A folder picker opens.
- **Chat:** type a message that contains a diagnose/fix keyword and the path,
  e.g. `fix build C:\...\outputs\coding-agent-demo\DemoApp`.

Pick / point at:

```
<repo-root>\outputs\coding-agent-demo\DemoApp
```

---

## 3. Approve and watch the loop

On launch, DiagnoseFix opens an approval card:

> Run build/test and modify code files — `permFileWrite`
> Root: `<...>\DemoApp`

Approve it. The closed loop then runs **through the single audited pipeline**
(`Command → PermissionGate → Executor → AuditLog`):

1. `ProjectAnalyzer.Analyze(DemoApp)` → detects `dotnet build` / `dotnet test`,
   whitelists `dotnet` for `GuardedCommandRunRecorder`.
2. `FixLoopRunner` runs `dotnet build` → **fails** with
   `Program.cs(11,...): error CS1002: ; expected`.
3. `ErrorParser` extracts the error code + line; `RuleBasedFixStrategy`
   proposes a safe CS1002 fix (append `;`).
4. The patch is applied as a file-write **through the pipeline** — you get a
   second approval card for the actual edit (high-risk surface, by design).
5. `dotnet build` re-runs → **passes**.
6. A **Diagnose & Fix report** opens (build output, patches, diff, review
   notes). Status: `passed`.

---

## 4. Commit the fix (optional)

From the report dialog you can:

- **Commit** — runs `GitWorkflow` (commit) through the pipeline. Because the
  demo folder is its own git repo, this creates a second commit containing the
  fixed `Program.cs`.
- **Export patch** — writes a unified-diff patch file.
- **Apply patch + rerun** — round-trips a manual unified diff back through the
  loop (useful for errors the safe strategy does *not* auto-fix).

---

## 5. What this proves

| Claim | Evidence in this demo |
|-------|------------------------|
| Repo-agnostic | Target is a `dotnet` repo, not ZhuaQianDesktop; no `build.ps1` involved. |
| Auto-detects build/test | `ProjectAnalyzer` returns `dotnet build` / `dotnet test` from `.csproj`. |
| Permission-gated | Every build, edit, and commit goes through `PermissionGate` approval cards. |
| Safe auto-fix | Only CS1002 (deterministic) is patched; other errors are reported, not guessed. |
| Audited | All side effects land in `AuditLog` + `OutputsHub`. |

---

## 6. Variations to try

- **Manual round-trip:** introduce a *non*-auto-fixable error (e.g. rename a
  variable so it no longer compiles for a semantic reason), re-run, then paste
  a unified diff into the report dialog's *Apply patch* box to drive a real
  fix through the loop.
- **Another language:** point DiagnoseFix at any `npm` / `cargo` / `go` repo;
  `ProjectAnalyzer` detects the right tool and the same loop runs.
- **No `dotnet`?** Swap the demo for a repo using one of the detected tools
  (`npm test`, `cargo build`, `go build`, `mvn`, `gradle`, `make`).

---

## Troubleshooting

- **Build is denied / not run:** ensure `dotnet` is on PATH and the demo folder
  contains `DemoApp.csproj` at its root. `ProjectAnalyzer` only whitelists the
  detected executable.
- **Agent reports "cannot fix":** the error is not CS1002/CS0246/CS0103, or it
  is not a well-known missing-`using`. Use the manual patch round-trip.
- **Commit does nothing:** the demo folder must be a git repo. Re-init with
  `git init` inside `DemoApp` if needed.
