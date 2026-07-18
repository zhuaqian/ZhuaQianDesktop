# Browser Render Integration (Epic: Web Research Upgrade)

**Goal:** read pages that need JavaScript rendering, anti-scraping defenses, or a
login session — which the existing static `WebSearchClient.FetchPage` (raw
`WebClient.DownloadString`) cannot see.

**Engine:** Playwright for .NET (`Microsoft.Playwright`) — headless Chromium.
This is the **first external NuGet dependency** in the project, and it changes the
build model (the raw-csc `build.ps1` / `run-tests.ps1` no longer compile with only
framework DLLs).

---

## What was added

| File | Role |
|------|------|
| `Tools/BrowserRenderClient.cs` | Async wrapper over headless Chromium. Produces a `WebPageFetchResult` (with a new `Html` field). Reuses `WebSearchClient.ValidatePublicHttpUrl` so invalid URLs never launch a browser. |
| `Agent/BrowserFetchExecutor.cs` | `IAsyncCommandExecutor`, `CommandType = "BrowserFetch"`, permission `permNetworkUpload`. Parses `waitForSelector`, `timeoutMs`, `returnHtml`, `headless`, `useStorageState`, `saveStorageState`, etc. |
| `Tools/WebResearchFetcher.cs` | `FetchOne` / `FetchMany`: static `FetchPage` first, fall back to browser render only when static is empty/short/failed. Drops straight into `WebPageReportBuilder`. |
| `src/packages.config` | Pins `Microsoft.Playwright` 1.48.0 (adjust to match the installed browser build). |
| `build.ps1` / `run-tests.ps1` | Resolve `Microsoft.Playwright.dll` + transitive DLLs from the restored `packages/` folder and `/reference` them; `build.ps1` also copies the runtime DLLs + native driver next to the EXE. |
| `ZhuaQianDesktop.cs` + `ui/MainForm.LocalActionRouting.cs` | The two web-research entry points now call `WebResearchFetcher.FetchOne(...)` instead of `webSearchClient.FetchPage(...)`, so research auto-upgrades to browser rendering when needed. |

---

## Install steps (one-time, on the dev/run machine)

1. **Restore NuGet packages** so `Microsoft.Playwright.dll` + transitive DLLs land in
   `src/packages/`:

   ```powershell
   nuget restore src/packages.config
   # or, if you open the solution in Visual Studio, Build does this automatically.
   ```

2. **Build** (the script references + copies the Playwright DLLs automatically):

   ```powershell
   .\src\build.ps1
   ```

3. **Browser binaries** are downloaded on first browser fetch via
   `Playwright.InstallAsync()` (called once per process by `BrowserRenderClient`).
   If you prefer to pre-install, run from the output directory:

   ```powershell
   # from the folder containing ZhuaQianDesktop.exe after build:
   pwsh -Command "Microsoft.Playwright.Playwright.InstallAsync() | Out-Null"
   # or install the standalone tool:  dotnet tool install --global Microsoft.Playwright.CLI
   #                                   playwright install chromium
   ```

   The download goes to `~/.cache/ms-playwright` (user profile). No per-project copy needed.

---

## Usage

- **As an agent command** (`BrowserFetch`):
  - `Target` = URL, or `Parameters["url"]`.
  - Options: `timeoutMs`, `waitForSelector`, `waitForTimeoutMs`, `returnHtml` (bool),
    `headless` (bool), `userAgent`, `viewport` (`WxH`), `useStorageState`
    (path to a saved login session JSON), `saveStorageState` (path to persist the
    session after this navigation).
- **Login state:** fetch a site once with `saveStorageState=login.json` (after you
  have authenticated in a visible browser, or after a manual cookie export), then
  reuse it with `useStorageState=login.json` for subsequent fetches. The storage
  state file holds cookies + `localStorage` for that origin.
- **In web research:** the existing "深度分析 / URL 分析" flows now transparently
  render JS-heavy pages. No prompt change required.

---

## Anti-scraping posture

- Real desktop `User-Agent`, viewport, and `zh-CN` locale are set.
- `--disable-blink-features=AutomationControlled` reduces the `navigator.webdriver`
  tell. This is a baseline, not a full stealth suite — sites with aggressive bot
  detection (CAPTCHA, fingerprinting) may still block; route those through a saved
  login session or a proxy as needed.

---

## Build-integration caveats (raw csc + NuGet)

The raw `csc` build cannot use msbuild's automatic package-copy. `build.ps1`:

- globs `src/packages/` for `Microsoft.Playwright.dll`, `System.Text.Json.dll`,
  `Microsoft.Bcl.AsyncInterfaces.dll`, `System.Runtime.CompilerServices.Unsafe.dll`,
  and `System.Threading.Tasks.Extensions.dll`, and references them;
- if the package is missing it fails with a clear "run nuget restore" message;
- after a successful compile it copies those DLLs **and** the Playwright native
  driver (under `packages/Microsoft.Playwright.*/runtimes/win-*`) next to the EXE.

If the runtime still reports a missing assembly, add that DLL to the `$pwRefs`
resolution list in `build.ps1` (and `run-tests.ps1`) and re-run. The `csproj`
`HintPath` points at `lib\netstandard2.0`; if the installed package only ships
`lib\net48`, update the `HintPath` to match.

---

## Verification status

- Static checks (`check-architecture.ps1`, CS0101 scan) pass.
- Unit tests (`TestBrowserRenderClient`) cover the validation gate and the
  static-first fallback **without launching a browser**, so CI stays fast and
  browser-binary-free.
- The actual Chromium render path is verified only when a browser is installed;
  run `.\src\build.ps1` + a real `BrowserFetch` on a JS-heavy URL to confirm end to end.
