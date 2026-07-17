# Contributing to ZhuaQian Desktop

Thanks for helping make ZhuaQian Desktop better.

## Quick Start

```powershell
git clone <repo>
cd src
.\build.ps1
.\scripts\run-tests.ps1
```

Requirements: Windows, .NET Framework 4.x, and `csc.exe` from the .NET Framework installation.

## Development Baseline

For public open-source contributions, target `src/` first. It is the modular tree and has the cleaner contributor path.

`work/zq-desktop/` remains the runtime verification baseline until the remaining deltas are retired. Touch it only when fixing runtime-only behavior or explicitly syncing the baseline.

Do not manually edit `outputs/` unless preparing a release package.

## Before Submitting a PR

Read `docs/ARCHITECTURE_CHARTER.md`; it defines the non-negotiable architecture rules.

## PR Checklist

- [ ] New side-effect operations implement an executor/pipeline path.
- [ ] Permission checks, audit records, and output records are not bypassed.
- [ ] UI code does not directly call provider clients or mutate Core state.
- [ ] New executors or Core modules include tests.
- [ ] No new empty `catch {}` blocks or swallowed failure states.
- [ ] User-facing docs were updated if behavior changed.

## Running Tests

```powershell
# Modular tree tests
cd src
.\scripts\run-tests.ps1

# Runtime baseline tests
cd ..\work\zq-desktop
.\scripts\run-tests.ps1

# Extra runtime checks
.\scripts\smoke-test.ps1
```

## Commit Style

Use a short title, then an optional body:

```text
OrganizeFolderExecutor: wire into AgentPipeline

- Implements the organize-folder executor path
- Passes through PermissionGate -> ApprovalCard -> Execute
- Writes audit and output records from the pipeline
```

## License

MIT. See LICENSE.
