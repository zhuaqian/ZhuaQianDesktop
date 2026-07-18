# Open Source Readiness Review

Updated: 2026-07-16

## Conclusion

ZhuaQian Desktop is ready to be described as a free open-source v0.1 preview, not as a mature commercial product.

Best label:

```text
ZhuaQian Desktop v0.1 Preview
Windows local-first AI workbench prototype
```

## Current Verification

| Check | Result |
|---|---|
| Root build | passed |
| `src/scripts/run-tests.ps1` | `168` passed / `0` failed |
| `work/zq-desktop/scripts/run-tests.ps1` | `168` passed / `0` failed |
| `work/zq-desktop/scripts/smoke-test.ps1` | passed; no production empty catches |
| Release package snapshot | regenerated under `outputs/ZhuaQianDesktop-open-source/` |

## Strengths

- Clear product direction: Windows local-first AI workbench with visible local side effects.
- Multi-provider model support with local/free-first settings.
- Real local outputs: TXT, Markdown, DOCX, PPTX, XLSX.
- Permission-aware execution path: PermissionGate, ApprovalCard, AuditLog, OutputsHub, AgentPipeline, and executors.
- Natural-language local actions now map to controlled execution for selected actions.
- Basic computer-control commands and a read-only Activity Monitor exist, but they should be described as preview-grade diagnostics and kept within `docs/OPEN_SOURCE_MONITORING_BOUNDARY.md`.
- Honest docs that label the project as prototype-grade.
- MIT license, contribution guide, security policy, issue templates, PR template, and CI workflow are present.

## Remaining Release Risks

1. `.git` metadata is unusable in this workspace. Public launch needs a clean repository initialization or repaired clone.
2. `src/` and `work/zq-desktop/` still coexist. Contributors must be told to edit `src/`.
3. `ZhuaQianDesktop.cs` remains too large in both trees.
4. Security must not be oversold. The app has visible controls and audit trails, but it is not hardened enterprise automation.
5. Tests are real but custom; the repo still lacks a standard test framework project.
6. Public wording around monitoring must avoid anti-cheat, hidden supervisor, or automated enforcement claims.

## Readiness Score

| Dimension | Score | Judgment |
|---|---:|---|
| Product direction | 8/10 | clear niche and differentiation |
| Prototype usability | 7/10 | broad and working, still preview-grade |
| Engineering structure | 6/10 | modules exist, main form still too large |
| Tests | 6/10 | useful custom suite, not yet standard |
| Security posture | 6/10 | good prototype controls, not hardened |
| Open-source hygiene | 7/10 | key files exist, Git history missing |
| Docs clarity | 7/10 | improved, but older docs remain archived |

Overall: 7/10 for a preview release.

## Must Do Before Public Launch

- Recreate or repair Git history.
- Publish from `src/` as the contribution tree.
- Keep `work/zq-desktop/` either synchronized, renamed as legacy, or omitted from the public release.
- Re-run the verification matrix in a clean checkout.
- Make release notes say "preview" clearly.
- Rename and document the monitoring feature according to `docs/OPEN_SOURCE_MONITORING_BOUNDARY.md`.
