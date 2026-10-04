---
name: plotree-store-release
description: Prepare a versioned Plotree Microsoft Store upload and optional direct installer, verify release artifacts, and hand them to the user. Use for Plotree release preparation; publishing, uploading, tagging, and merging require their own authorization.
---

# Plotree Store release preparation

Run from the existing repository root. Deliver verified local artifacts, their SHA256 hashes and source revision, and a clear handoff for the user to upload in Partner Center. Preserve existing working changes/index and other active sessions; create a work branch first if requested. Do not move the repository or clean unrelated files/processes.

## Existing skills and responsibilities

This repository skill owns Plotree's version metadata, current project configuration, bilingual user documentation, artifact comparison, direct-installer helper, and handoff. Generic WinUI/MSIX/certificate guidance belongs to the existing **winui-packaging** skill (catalog name may be **winui:winui-packaging**); build diagnosis belongs to **winui-dev-workflow**. Resolve these by name in the active skill catalog. They may come from a user installation or plugin cache, so do not hardcode a home directory or plugin version and do not duplicate their full instructions here.

The optional user-local **winui-ui-testing** skill covers GUI testing only when requested. Release preparation does not authorize installing/running an app or changing certificate trust. If the generic skills are unavailable, the SDK-native commands and verification helper below support a standalone release workflow. No installation of another skill/tool is required merely to prepare artifacts. Do not edit/delete user-local skills to resolve overlap.

For prose review, use available Antigravity/Yomiyasu when requested and supported. Inspect the actual resulting diff even if the tool was instructed to review without editing; retain only accurate authorized changes. Keep development instructions here and in contributor docs, outside end-user READMEs.

## Establish the release input

- Obtain the user's explicit four-part package version. Do not reuse a previous release's version; ask if a three-part request is ambiguous. Update `src/Plotree/Package.appxmanifest` Identity Version and `src/Plotree/Plotree.csproj` Version/AssemblyVersion/FileVersion consistently with the repository's convention (app Version uses three parts, package/assembly/file versions four).
- Read current `Package.appxmanifest`, `.csproj`, release notes, `.gitignore`, `scripts/New-DirectInstaller.ps1`, and any existing local publish profiles relevant to packaging. Preserve Store Identity Name/Publisher/PublisherDisplayName and compare them with a prior package; changing Store identity is a separate task.
- The current bundle targets **x64 and ARM64**, includes **en-US and ja-JP** resources in each architecture package, and is self-contained. Verify effective settings and actual output rather than assuming a publish profile exists on another contributor's machine. Both `.NET SelfContained` and `WindowsAppSDKSelfContained` matter; preserve existing trimming/ReadyToRun/AOT choices.
- Update JA/EN README and release notes for actual user features, installation/use and limitations. Check in-app instructions against localized defaults/UI names and file-format compatibility. Exclude build/test/work logs and implementation explanations from READMEs. Do not claim GUI behavior was tested when only code or unit tests were checked.
- On a shared PC, coordinate a sustained build/test window with active performance measurements before starting. Do not stop another session's jobs or alter its repository. Report when all CPU-heavy jobs have ended.

## Prerequisites and final tests

Use Windows, the .NET SDK required by the current target framework, restored project dependencies, and Windows SDK MSIX tools (`signtool.exe`, `makepri.exe` when inspecting packaged strings). Locate SDK tools from the installed Windows Kits; verify help/versions before using unfamiliar flags. The generic WinApp skills require CLI 0.7+; an older/missing CLI does not block the verified SDK-native route. Do not install or upgrade tooling without authorization.

Run the full applicable suite after final code/resource/version edits:

```powershell
dotnet test .\tests\Plotree.Tests\Plotree.Tests.csproj -p:Platform=x64 --logger trx --results-directory .\artifacts\release-tests
```

Use `--no-restore` only if the current configuration is already restored. Stop on test/build failures, resolve the cause within scope, and do not silently omit failing tests. Documentation/skill-only changes after a verified build do not require another binary build. Record the build's source hashes/revision; a later commit may additionally contain only documentation.

## Reuse signing material securely

Read the manifest Publisher and discover existing matching, unexpired signing certificates in `Cert:\CurrentUser\My` using public metadata (`Subject`, `Thumbprint`, `NotAfter`, `HasPrivateKey`). Compare a prior public `.cer` and signed bundle. Select the existing approved signer; if several candidates remain, resolve the ambiguity before signing. Do not create credentials/certificates or export private keys.

Prefer the existing certificate store key. A configured certificate thumbprint allows overriding `PackageCertificateKeyFile` to empty and avoids reading a PFX/password. If a password-protected PFX is genuinely needed, locate the project's existing UserSecretsId and approved credential mechanism without displaying its contents. A missing ID is not permission to invent one or inspect arbitrary secret stores. Never place a password in a command line, transcript, build log, source, PR, or generated reference. Do not dump all MSBuild properties or environment variables.

Certificate trust is separate from packaging. Never install a trusted root, modify security settings, or run the direct installer automatically to make signature policy verification pass.

## SDK-native StoreUpload packaging

Set `$releaseVersion` from the resolved user request, `$releasePackages` to the versioned directory below, and `$signingThumbprint` to the discovered existing signer. These are task variables, not persistent configuration:

```powershell
$releasePackages = Join-Path (Get-Location) "src\Plotree\AppPackages\$releaseVersion\"
$publishArguments = @(
  'publish', '.\src\Plotree\Plotree.csproj', '--configuration', 'Release',
  '-p:Platform=x64', '-p:GenerateAppxPackageOnBuild=true', '-p:AppxBundle=Always',
  '-p:AppxBundlePlatforms=x64|ARM64', '-p:UapAppxPackageBuildMode=StoreUpload',
  "-p:AppxPackageDir=$releasePackages",
  "-p:PackageCertificateThumbprint=$signingThumbprint",
  '-p:PackageCertificateKeyFile=', '-p:PackageCertificatePassword='
)
dotnet @publishArguments
```

Inspect exit status and output paths. The project also sets a versioned AppxPackageTestDir from manifest identity. Verify the actual versioned folder rather than assuming all outputs followed a command-line directory override.

If `mspdbcmf.exe` is missing, symbol generation can fail even though package files were produced. First check existing tools. Store analytics symbols are optional; when omitted, disclose the limitation and add `-p:AppxSymbolPackageEnabled=false` to the publish arguments. Warnings about the missing converter may still occur. Do not label a failed publish as successful merely because files exist; the adjusted publish must exit successfully.

StoreUpload can produce an unsigned outer bundle even when architecture payloads were signed. Inspect the final bundle. If needed, use the discovered SDK tool and existing key:

```powershell
& $signtool sign /fd SHA256 /s My /sha1 $signingThumbprint $releaseBundle
& $signtool verify /pa /v $releaseBundle
```

Use the established signing/timestamp policy; do not introduce a new service. Capture any verification failure accurately. A self-signed bundle can have valid cryptography while Windows policy reports an untrusted root. Check the exact error, compare history, and verify cryptography/content without changing trust; unrelated digest/signature errors are blockers.

After signing, replace the single bundle entry in the generated `.msixupload` ZIP with the final signed bundle. Preserve any symbols entries; close/dispose the archive before verification. Compare embedded and standalone SHA256 hashes. SDK upload containers are ZIP files; never assume a container still contains the final bundle after external signing.

For a requested direct installer, copy the matching existing **public** `.cer` into the versioned package directory, then use the established helper:

```powershell
.\scripts\New-DirectInstaller.ps1 -PackageDirectory $releasePackages
```

The helper replaces its versioned staging/ZIP paths. Before running it, verify resolved targets are inside the intended artifacts directory, inspect any existing same-version output and preserve concurrent/user-owned work. Never delete old releases broadly. The ZIP must contain the two installer scripts, README, final signed bundle and matching public certificate.

## Verify and hand off

Run the read-only helper [scripts/Test-ReleaseArtifacts.ps1](scripts/Test-ReleaseArtifacts.ps1):

```powershell
.\.agents\skills\plotree-store-release\scripts\Test-ReleaseArtifacts.ps1 `
  -Version $releaseVersion -VerifyDirectInstaller
```

It checks manifest identity/version, x64/ARM64 payloads and both languages, self-contained files, CMS signature, all payload/bundle block-map hashes, the upload's embedded bundle, and optional direct ZIP/certificate. CMS checking alone is not a substitute for Windows SIP/policy verification or Store certification; report those checks separately. Inspect packaged PRI resources with `makepri dump` when localized copy changed; verify both architectures actually contain the final wording.

Record artifact paths, lengths, SHA256, source revision and any documentation-only difference from build inputs. Keep logs, TRX and receipts in ignored/private output, not in the PR. Review staged paths explicitly for source/docs/tests only; do not include generated packages, certificates, credentials or private story files.

Commit/push/create a Draft PR only when authorized. Use the repository PR template and record tests, packaging, manual-only checks and known unresolved observations. Verify the remote PR is a draft and its head SHA matches the pushed commit. Preparation is not authorization to upload to Partner Center, create a GitHub release/tag, publish, or merge.

Handoff the precise `.msixupload` (and direct ZIP if requested), hashes, Draft PR URL, and remaining manual checks. The user uploads to Microsoft Store. Do not guarantee Store acceptance or claim ARM64 runtime/GUI/packaged installation testing that was not performed.

Primary references: [SDK packaging](https://learn.microsoft.com/en-us/windows/uwp/packaging/auto-build-package-uwp-apps), [upload container and optional symbols](https://learn.microsoft.com/en-us/windows/msix/package/packaging-uwp-apps), [Partner Center package upload](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/upload-app-packages).
