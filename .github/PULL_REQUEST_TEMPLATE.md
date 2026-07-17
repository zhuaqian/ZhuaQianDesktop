## Summary

- 

## Verification

- [ ] `src/build.ps1`
- [ ] `src/scripts/run-tests.ps1`
- [ ] If touching the runtime baseline: `work/zq-desktop/build.ps1`
- [ ] If touching the runtime baseline: `work/zq-desktop/scripts/run-tests.ps1`

## Architecture Checklist

- [ ] I read `docs/ARCHITECTURE_CHARTER.md`.
- [ ] New side-effect operations go through the command/executor pipeline.
- [ ] Permission checks, audit records, and output records are not bypassed.
- [ ] No new empty `catch {}` blocks were added in production code.
- [ ] User-facing docs were updated when behavior changed.
