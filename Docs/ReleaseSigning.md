# Release signing and artifact inventory

`build/release-signing.json` records the reviewed release identity: **CN=ZTMZClub**, thumbprint **3BD6CE6D7D7065A78FC4C8864DF7CDF4F8B33FB3**, matching published rc28. Thumbprints identify certificates; the `/sha1` signtool selector does not change the SHA256 file/timestamp digest algorithms.

The public certificate investigation found rc24 used this `3BD6...` identity, rc26/rc27 used the Local `2419...` identity, and rc28 returned to `3BD6...`. The signing scripts were identical between rc27 and rc28. The precise override that selected the other certificate remains unknown. This document intentionally contains no raw session history or secret-bearing evidence.

## Release flow

1. `onekey.bat` checks the configured PFX against reviewed metadata before building or publishing, then retains its build, publish and signed-package sequence.
2. `package_signed.bat` delegates to `release_signing.ps1 -Action Package`. The existing release payload and pinned resource validators must both succeed before any signing. Validator exceptions and nonzero exit codes stop packaging.
3. PFX inspection imports keys with `EphemeralKeySet`, never into a certificate store. Exactly one private-key signer must match the pinned thumbprint and be within its validity period. Mismatch diagnostics show expected and actual public subjects/thumbprints. There is no fallback certificate generation or expected-thumbprint environment override.
4. Packaging signs only the published `ZTMZ.Launcher.exe`, `ZTMZ.PacenoteTool.WpfGUI.exe`, the x64/x86 `ztmz_dashboard_overlay.dll`, and the x86 `ztmz_dashboard_injector.exe`. Third-party DLLs are not re-signed. These changes do not add optional overlays to startup requirements.
5. Inno compiles the same `setup_normal_signed.iss` and resources into a unique temporary output directory. Its `sign_local.bat` callback repeats the guard and uses the pinned `/sha1` selection. Signing retains SHA256 and the existing DigiCert RFC3161 timestamp endpoint. No installer is launched by packaging.
6. After compilation/signing, the final installer and owned payload signatures must match the reviewed identity and contain a timestamp. Native WinVerifyTrust must return success or exactly `0x800B0109` (untrusted root). PowerShell can report `UnknownError` for this self-signed certificate; arbitrary `UnknownError`, digest errors, expiration errors and explicit distrust are not accepted. No trust-store modifications are made. Verification uses cached trust information, not a fresh online revocation audit.
7. The final `<installer>.exe.inventory.json` is written beside the installer in `Output`. A new installer is selected only from this compilation's unique directory, never by finding the newest pre-existing EXE. Failures preserve staging for diagnosis and return a nonzero status.

## Inventory contents and limits

The JSON contains the installer's SHA256, byte size, signer public identity, Authenticode status, native WinVerifyTrust result, and timestamp presence/authority certificate. `generatedAtUtc` is the inventory creation time; timestamp certificate validity dates are **not** the time the artifact was signed. The inventory records timestamp evidence without inventing an extracted RFC3161 signing time.

It also inventories every EXE/DLL under the published `native-dashboard` directory and the two owned root EXEs, including hashes and signature information for any third-party binaries encountered. Only the explicit owned list must have the reviewed signature. Payload hashes are captured after signing and checked again after compilation to reject changes during packaging. The records describe published source files, not a claim that an installer extraction was compared; `verify_installer_payload.ps1` remains the separate extracted-payload verification tool. Installer resource inclusion rules and startup manifest generation are unchanged.

An inventory is not an antivirus scan, a SmartScreen/reputation approval, or proof of public CA trust. The JSON states that scans were not performed. Keep it with the installer when recording a release. The sidecar itself is not signed.

## Configuration and deliberate rotation

The PFX defaults to `%USERPROFILE%\ztmz_selfsigncert.pfx`; `ZTMZ_SIGNING_PFX_PATH` can select a file but cannot override the reviewed identity. The existing password setting is read directly inside PowerShell, first from the process and then from the user's `ZTMZ_SIGNING_PFX_PASSWORD`. No setting is written. Do not put passwords in scripts, terminal commands, Inno commands, transcripts or reports.

Signtool's existing PFX `/p` interface still requires a password on the short-lived signtool process command line; this is a documented residual limitation, not a claim of secret-free process arguments. The wrapper avoids the old batch/stdout password relay and does not print the signtool command or output. Switching key providers to remove that limitation is a separate reviewed change.

For a deliberate rotation, review the new certificate's public identity and update the committed `subject`, `thumbprint` and baseline description in `build/release-signing.json`, alongside release documentation. Configure the corresponding PFX separately. Never work around a mismatch by disabling the guard or adding an environment bypass.

## Isolated checks

Run `powershell -NoProfile -ExecutionPolicy Bypass -File build/test_release_signing.ps1`. Tests use ephemeral synthetic certificates, temporary files, mocked signing/compiler operations and read-only WinVerifyTrust checks. They do not use real signing credentials, install certificates, build products, publish, or mutate real release artifacts.

Optionally pass `-ReadOnlyInstaller <existing-rc28-installer.exe>` to validate the real self-signed trust result and check its SHA256 remains unchanged. The test also copies that installer into its isolated temporary directory, flips a byte within a PE code section (outside headers/checksum and the certificate table), and requires the real WinVerifyTrust result `0x80096010` (`TRUST_E_BAD_DIGEST`) plus rejection by the release guard. The original is never modified; its hash is checked again even if the tamper test fails. Neither installer is executed. This reads only the public signature, not the PFX/password. Real signing and a complete oneKey run still require separate authorized release validation.
