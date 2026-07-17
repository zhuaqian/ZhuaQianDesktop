# Free Open Source Release Plan

Updated: 2026-07-16

## Release Goal

Publish an honest, runnable preview:

```text
ZhuaQian Desktop v0.1 Preview
Free open-source Windows local-first AI workbench
```

## Must Be True At Release Time

- Clean Git repository exists.
- `src/` is the documented contribution source.
- `outputs/` is generated, not hand-edited.
- Verification matrix is green from a clean checkout.
- Release notes clearly say this is a preview, not a hardened enterprise product.
- No secrets, local config, API keys, or user files are included.

## Current Release Package Shape

```text
ZhuaQianDesktop-open-source/
|-- README.md
|-- LICENSE
|-- SECURITY.md
|-- CONTRIBUTING.md
|-- .github/
|-- assets/
|-- docs/
|-- src/
|-- dist/
```

`dist/` may contain:

- `ZhuaQianDesktop.exe`
- `ZhuaQianDesktop.exe.sha256`

## Release Notes Draft

```markdown
# ZhuaQian Desktop v0.1 Preview

Free, open-source, local-first AI workbench for Windows.

## Highlights

- Multi-provider chat with local/free-first model selection
- Real local exports: TXT, Markdown, Word, PowerPoint, Excel
- Natural-language file generation that creates local files through the app
- Permission-aware local actions with Power mode, ApprovalCard, AuditLog, OutputsHub, and AgentPipeline
- Folder organization with rollback manifest
- Trusted plugin runner
- Process management through approval and audit
- Screenshot OCR, clipboard monitor, batch reports, and local knowledge search

## Verification

- Root build: passed
- src tests: 148 passed / 0 failed
- work mirror tests: 148 passed / 0 failed
- smoke test: passed; no production empty catches

## Known Limitations

- Main WinForms files are still large
- Source-tree convergence is not complete
- No installer yet
- No standard xUnit/NUnit test project yet
- Provider calls require user-supplied API keys or local models
- Security controls are prototype-grade
```

## Launch Order

1. Create clean Git repo.
2. Copy `README.md`, `LICENSE`, `SECURITY.md`, `CONTRIBUTING.md`, `.github/`, `assets/`, `docs/`, `src/`, and generated `dist/`.
3. Decide whether to omit `work/` or publish it as a clearly marked legacy mirror.
4. Run verification in the clean repo.
5. Create tag `v0.1-preview`.
6. Upload zip, exe, and SHA256.
7. Open with a short preview announcement and invite issues around build, providers, permissions, plugins, and docs.

## First Week Rule

Do not add broad new features in the first week. Fix build, install, provider setup, permission prompts, docs confusion, and crash reports first.
