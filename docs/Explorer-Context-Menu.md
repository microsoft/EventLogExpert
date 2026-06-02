# Explorer context menu — install + smoke recipe

EventLogExpert's MSIX package registers three Windows Explorer right-click context menu entries:

| Right-click target | Verb | Surface | Implementation |
|---|---|---|---|
| `.evtx` file (single or multi-select) | **Open with EventLogExpert** | Top-level Win11 modern menu (via `desktop4:fileExplorerContextMenus` + `IExplorerCommand`) AND `Open With` submenu (via FTA `uap3:Verb`) | Native C++/WinRT shell extension DLL + MAUI activation pipeline |
| Folder icon | **Open with EventLogExpert** | Top-level Win11 modern menu (via `desktop5:ItemType Type="Directory"`) | Same native DLL filters `Directory` items + spawns the main exe with the folder path |
| Empty space inside an open folder | **Open with EventLogExpert** | Top-level Win11 modern menu (via `desktop5:ItemType Type="Directory\Background"`) | Same native DLL, same handler |

Single-instance activation is handled by `Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey`, wired in a handwritten WinUI `Main` (`src/EventLogExpert/Platforms/Windows/Program.cs`) so secondary launches route activation args to the running primary instance via the `ActivationDispatcher` channel. Multi-select fans into a single combined view.

## Architecture

The shell extension is a **native C++/WinRT DLL** (`src/EventLogExpert.ExplorerExtensionNative/`), packaged at the MSIX root as `EventLogExpert.ExplorerExtension.dll` and loaded by `dllhost.exe` via the manifest's `<com:SurrogateServer>` registration. A native DLL is required: Explorer activates context-menu COM handlers via `CLSCTX_INPROC_HANDLER` (in-process surrogate), not `CLSCTX_LOCAL_SERVER` (standalone exe).

The `IExplorerCommand` implementation:
- `GetTitle` → `"Open with EventLogExpert"`
- `GetIcon` → main exe path (Explorer picks the embedded default icon resource)
- `GetState` → `ECS_ENABLED` only when the selection contains at least one `.evtx` file or directory; otherwise `ECS_HIDDEN`. Required because the manifest registers `Type="*"` (every file); without filtering the verb would appear on every right-click in Explorer. The fast path uses `IShellItem::GetAttributes(SFGAO_FOLDER)` and path extension only — no filesystem syscalls during menu construction. When `okToBeSlow` is FALSE on the background-menu surface, the verb is enabled without a folder probe (deferring the `.evtx`-presence check to `Invoke`).
- `Invoke` → `CreateProcessW` of the sibling `eventlogexpert.exe` with each selected `.evtx` file / directory passed as a quoted argument. Folders are pre-filtered for at least one `.evtx` child to avoid a flash of empty app window.
- `IObjectWithSite::SetSite` → stores the shell-supplied site so the `Directory\Background` surface (right-click inside an open folder's empty space) can resolve the current folder via `QueryService(SID_SFolderView, IFolderView)` → `IPersistFolder2::GetCurFolder` → `SHGetPathFromIDListEx` (32K wstring buffer + `GPFIDL_DEFAULT`, long-path-safe — the legacy `SHGetPathFromIDListW` caps at `MAX_PATH` regardless of process long-path awareness). Required because the shell passes `items == null` for that surface.

The MAUI receiving side:
- `Program.Main` runs before `Application.Start`, calls `AppInstance.FindOrRegisterForKey("eventlogexpert-main")`. Secondary instances redirect their activation args to the primary and exit.
- `ActivationBootstrap` (thread-safe static buffer) seeds cold-launch args BEFORE MAUI DI is built, so they're not lost when `MainPage` is constructed.
- `ActivationDispatcher` (channel + `Interlocked` idempotent start) drains the buffer + listens for redirected `AppInstance.Activated` events. UI work is marshaled via `MainThread.InvokeOnMainThreadAsync`.
- `ActivationArgsExtractor` discriminates per `ExtendedActivationKind`: `File` → `IFileActivatedEventArgs.Files`, `Launch` → `ILaunchActivatedEventArgs.Arguments` parsed via `CommandLineToArgvW`, `CommandLineLaunch` → `ICommandLineActivatedEventArgs.Operation.Arguments`. Tokens are classified via `ActivationTokenClassifier` (in `EventLogExpert.WindowsPlatform`) — only `.evtx`-extension files and existing directories are accepted, so the launching exe path (`argv[0]` from the shell extension's `CreateProcess` spawn) is silently dropped instead of being treated as a log to open.

## Build prerequisites

The native shell extension requires the **MSVC C++ workload** + the **Windows 10/11 SDK 10.0.26100+** to build. Specifically:

- Visual Studio 2026 (or 2022) with the **Desktop development with C++** workload
- Windows SDK 10.0.26100.0 or newer (the C++/WinRT projection headers shipped with this SDK)
- `nuget.exe` on PATH. Install via `winget install Microsoft.NuGet` (dev), the `NuGetToolInstaller@1` pipeline task (ADO), or any equivalent. The `BuildExplorerExtensionNative` MSBuild target fails fast with an actionable error if not found — there is no auto-download (removed to eliminate supply-chain risk from the moving `latest` URL).

`dotnet build` of the .NET solution alone does NOT exercise the C++ build — the C++ project is intentionally not in `EventLogExpert.slnx` because the .NET SDK MSBuild doesn't include `$(VCTargetsPath)`. The native build is triggered only during MSIX packaging (`/p:WindowsPackageType=MSIX`) by the `BuildExplorerExtensionNative` MSBuild target in `src/EventLogExpert/EventLogExpert.csproj`, which locates VS MSBuild via `vswhere` and invokes the vcxproj.

### One-time NuGet restore

After cloning, restore the C++ project's packages once:

```powershell
cd src/EventLogExpert.ExplorerExtensionNative
nuget restore packages.config -PackagesDirectory ..\packages
```

This populates `src/packages/Microsoft.Windows.CppWinRT.*` and `src/packages/Microsoft.Windows.ImplementationLibrary.*` (WIL). Both are gitignored.

## Local dev install (signed MSIX)

Loose-file deploy (`Add-AppxPackage -Register AppxManifest.xml`) leaves the package with `SignatureKind=None`, which silently drops the COM server + context menu extension registrations. **Use the signed-MSIX install path** for any local testing of the context menu surface.

A one-shot installation script is at `eng/install-signed-dev-msix.ps1` — run from an ELEVATED PowerShell. To replicate manually:

```powershell
# 1. Generate a self-signed dev cert matching the manifest's Publisher
$publisher = "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"
$cert = New-SelfSignedCertificate -Type Custom -Subject $publisher `
  -KeyUsage DigitalSignature -FriendlyName "EventLogExpert Dev Cert" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3","2.5.29.19={text}")

# 2. Export + trust in LocalMachine\Root (requires elevation; needed for Add-AppxPackage of dev MSIX)
$pwd = ConvertTo-SecureString "elx-dev-cert" -Force -AsPlainText
$pfx = "$env:TEMP\eventlogexpert-dev.pfx"
Export-PfxCertificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath $pfx -Password $pwd | Out-Null
$rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
$rootStore.Open("ReadWrite"); $rootStore.Add($cert); $rootStore.Close()

# 3. Build + sign MSIX
dotnet publish src/EventLogExpert -c Debug `
  /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false /p:WindowsPackageType=MSIX
& "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe" `
  sign /fd SHA256 /a /f $pfx /p "elx-dev-cert" `
  "src/EventLogExpert/bin/Debug/net10.0-windows10.0.19041.0/win-x64/AppPackages/EventLogExpert_0.9.0.0_Debug_Test/EventLogExpert_0.9.0.0_x64_Debug.msix"

# 4. Install
Add-AppxPackage -Path "src/EventLogExpert/bin/Debug/.../EventLogExpert_0.9.0.0_x64_Debug.msix"
```

## Smoke recipe

Run after any change touching the manifest, the activation pipeline, the native shell extension DLL, or `MauiMenuActionService.OpenFolderAsync`. Automated tests cover manifest structural invariants (`ManifestSchemaTests`), folder enumeration (`EvtxFolderEnumeratorTests`), CLI parsing (`CommandLineToArgvWHelperTests`), and token classification (`ActivationTokenClassifierTests`) but cannot verify Explorer integration end-to-end.

**Prerequisites:**
- Windows 11 (the only target — Win10 dropped per scope decision since OOS in Oct 2025).
- The signed MSIX installed per above; cert trusted in `LocalMachine\Root`.
- A folder containing 2+ `.evtx` files plus a folder containing no `.evtx` files.

**Scenarios:**

1. **FTA single-file double-click (regression gate)** — Double-click a `.evtx` in Explorer. EventLogExpert launches and loads the file. **Required to pass** — the activation refactor changes how all FTA opens flow.
2. **Single-file right-click** — Right-click one `.evtx` → "Open with EventLogExpert" at top level → app opens with the file.
3. **Multi-file right-click** — Select 3–5 `.evtx` files → "Open with EventLogExpert" → **one** app instance opens with all files combined.
4. **Secondary activation redirect** — With app already running, right-click another `.evtx` → existing window receives the file (no new window).
5. **Folder icon right-click** — Folder with `.evtx` files → "Open with EventLogExpert" → all top-level `.evtx` open in one app instance.
6. **Folder background right-click** — Open a folder containing `.evtx` files, right-click in the empty space inside it → "Open with EventLogExpert" → all top-level `.evtx` open in one app instance. This surface relies on `IObjectWithSite` recovering the folder from the shell view; if it fails, the verb is silently absent.
7. **Empty folder right-click** — Folder with no `.evtx` → verb still appears (filtering happens lazily) → click → silent no-op (native DLL pre-filters via `Directory.EnumerateFiles(*.evtx).Any()` before spawning).

## Diagnostics

- **Folder verb missing after upgrade install** → restart Explorer: `Stop-Process -Id (Get-Process explorer).Id -Force; Start-Process explorer`. Required to refresh the COM extension catalog.
- **Verb shows but click does nothing** → check **Event Viewer → Windows Logs → Application** for COM activation failures matching the verb's CLSID (`F1B2C3D4-E5F6-4789-AB12-CD34EF567890`). Verify the package is `SignatureKind=Developer` or `Store`, not `None` (loose-file register doesn't activate the COM extensions).
- **Verb missing entirely** → verify the manifest registration is intact:
  ```powershell
  (Get-AppxPackageManifest -Package "EventLogExpert_0.9.0.0_x64__8wekyb3d8bbwe").Package.Applications.Application.Extensions.Extension | Select-Object Category
  # Should list: windows.appExecutionAlias, windows.fileTypeAssociation, windows.fileExplorerContextMenus, windows.comServer
  ```
  And the COM class:
  ```powershell
  reg query "HKCR\PackagedCom\Package\EventLogExpert_0.9.0.0_x64__8wekyb3d8bbwe\Class\{F1B2C3D4-E5F6-4789-AB12-CD34EF567890}"
  # Should show DllPath = EventLogExpert.ExplorerExtension.dll, Threading = 0
  ```
- **"Failed to open Log" dialog on launch** → the activation pipeline tried to open a non-`.evtx` token as a log. Should not happen (filtered in `ActivationTokenClassifier` + regression-tested), but if seen, check `%LOCALAPPDATA%\Packages\EventLogExpert_8wekyb3d8bbwe\LocalState\debug.log` for which path triggered `HandleOpenLog`.

## CLSID immutability

The verb's CLSID is `F1B2C3D4-E5F6-4789-AB12-CD34EF567890`. It is **immutable across releases** — changing it orphans Explorer's COM catalog from prior installations. The CLSID appears in three places that MUST stay in sync:

1. `src/EventLogExpert.ExplorerExtensionNative/dllmain.cpp` — `CLSID_UUID` macro + `kCanonical` constant
2. `src/EventLogExpert/Platforms/Windows/Package.appxmanifest` — `<com:Class Id=...>` + both `<desktop4:Verb Clsid=...>` / `<desktop5:Verb Clsid=...>`
3. `tests/Unit/EventLogExpert.Windows.Tests/ManifestSchemaTests.cs` — `OpenEvtxCommandClsid` constant; the tests fail on mismatch.
