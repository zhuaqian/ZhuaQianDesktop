# Security Policy

ZhuaQian Desktop is a local-first Windows AI assistant prototype. It can read
files, export files, call cloud providers, run local plugins, inspect processes,
and organize folders, so security reports are welcome.

## Supported Versions

Only the latest source snapshot is supported before the first stable release.

## Reporting a Vulnerability

Please do not open a public issue for exploitable security vulnerabilities.

Until a dedicated security contact is published, report privately to the
repository maintainer account. Include:

- A short description of the issue
- Steps to reproduce
- Impact and affected feature
- Whether local files, API keys, provider uploads, plugins, or process actions are involved
- Suggested fix, if available

## Current Security Boundaries

- API keys are intended to be stored with Windows DPAPI for the current user.
- Risky local actions should pass through permission checks and user confirmation.
- Plugin execution is intended to be restricted to a trusted plugin folder.
- Cloud provider calls may upload text or attachments selected by the user.
- Local monitoring features must remain visible, user-triggered, local-first,
  and non-enforcing unless a future release documents an explicit opt-in model.

This project is not yet a hardened enterprise endpoint-security product.
