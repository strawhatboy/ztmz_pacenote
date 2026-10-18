param([string]$ReadOnlyInstaller)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release_signing_helpers.ps1')
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('ztmz-signing-tests-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $scratch
$script:checks = 0
function Check([string]$Name, [scriptblock]$Body) {
    & $Body
    $script:checks++
    Write-Host "PASS: $Name"
}
function Equal($Actual, $Expected) {
    if ($Actual -cne $Expected) { throw "Expected '$Expected', got '$Actual'." }
}
function Fails([scriptblock]$Body, [string]$Pattern) {
    try { & $Body } catch {
        if ($_.Exception.Message -notlike $Pattern) { throw }
        return
    }
    throw "Expected failure matching: $Pattern"
}
function Write-Fixture([string]$Path, [string]$Text = 'synthetic unsigned test bytes') {
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force
    [IO.File]::WriteAllText($Path, $Text)
}
$keys = @()
$certs = @()
try {
    $policy = Get-ReleaseSigningPolicy
    Check 'Reviewed rc28 identity is pinned' { Equal $policy.thumbprint '3BD6CE6D7D7065A78FC4C8864DF7CDF4F8B33FB3' }
    $badMetadata = Join-Path $scratch 'bad.json'
    Write-Fixture $badMetadata '{"schemaVersion":1,"subject":"CN=bad","thumbprint":"oops"}'
    Check 'Malformed policy fails closed' { Fails { Get-ReleaseSigningPolicy $badMetadata } '*Invalid reviewed*' }
    Check 'Wrong signer reports both public identities' {
        Fails { Assert-ReleaseSigningIdentity $policy ([pscustomobject]@{ Subject = 'CN=Other'; Thumbprint = ('A' * 40) }) } '*Expected subject=CN=ZTMZClub; thumbprint=3BD6*Actual subject=CN=Other; thumbprint=AAAA*'
    }
    Check 'Unsigned input rejected' { Fails { Assert-ReleaseSigningIdentity $policy $null } '*no signer certificate*' }

    # CNG ephemeral keys + CertificateRequest: no certificate store, no real signing.
    foreach ($name in @('FixtureOne', 'FixtureTwo')) {
        $key = [Security.Cryptography.RSACng]::new(2048)
        $keys += $key
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
            "CN=$name", $key, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $certs += $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddDays(1))
    }
    $fixturePolicy = [pscustomobject]@{ subject = $certs[0].Subject; thumbprint = $certs[0].Thumbprint }
    $fixturePassword = ConvertTo-SecureString 'synthetic-fixture-only' -AsPlainText -Force
    $pfx = Join-Path $scratch 'fixture.pfx'
    [IO.File]::WriteAllBytes($pfx, $certs[0].Export([Security.Cryptography.X509Certificates.X509ContentType]::Pkcs12, 'synthetic-fixture-only'))
    Check 'Ephemeral PFX matching signer accepted' { Equal (Read-ReleaseSigningCertificate $pfx $fixturePassword $fixturePolicy).Thumbprint $certs[0].Thumbprint }
    Check 'PFX mismatch rejected before any signing' { Fails { Read-ReleaseSigningCertificate $pfx $fixturePassword $policy } '*identity mismatch*' }
    $wrongPassword = ConvertTo-SecureString 'incorrect-synthetic-value' -AsPlainText -Force
    Check 'Wrong PFX password returns sanitized failure' { Fails { Read-ReleaseSigningCertificate $pfx $wrongPassword $fixturePolicy } 'Cannot read signing PFX*' }
    $wrongPassword.Dispose()
    $multi = New-Object Security.Cryptography.X509Certificates.X509Certificate2Collection
    $null = $multi.Add($certs[0]); $null = $multi.Add($certs[1])
    $multiPfx = Join-Path $scratch 'ambiguous.pfx'
    [IO.File]::WriteAllBytes($multiPfx, $multi.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pkcs12, 'synthetic-fixture-only'))
    Check 'Multiple private-key signers rejected' { Fails { Read-ReleaseSigningCertificate $multiPfx $fixturePassword $fixturePolicy } '*exactly one private-key signer*' }
    $publicPfx = Join-Path $scratch 'public.cer'
    [IO.File]::WriteAllBytes($publicPfx, $certs[0].Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    Check 'Public certificate without private key rejected' { Fails { Read-ReleaseSigningCertificate $publicPfx $fixturePassword $fixturePolicy } '*no private-key signer*' }

    if ($ReadOnlyInstaller) {
        Check 'Existing release signature accepted read-only, artifact unmodified' {
            $before = (Get-FileHash -LiteralPath $ReadOnlyInstaller -Algorithm SHA256).Hash
            $signature = Get-ReleaseSignature $ReadOnlyInstaller
            Assert-ReleaseFileSignature $ReadOnlyInstaller $policy $signature
            Equal (Get-FileHash -LiteralPath $ReadOnlyInstaller -Algorithm SHA256).Hash $before
            Write-Host "Read-only signature: $($signature.Status); WinVerifyTrust=$($signature.WinVerifyTrustResult); signer=$($signature.SignerCertificate.Thumbprint)"
        }
        Check 'Real PE-section tampering rejects bad digest despite the self-signed root' {
            $originalHash = (Get-FileHash -LiteralPath $ReadOnlyInstaller -Algorithm SHA256).Hash
            $copy = Join-Path $scratch 'tampered-installer.exe'
            try {
                Copy-Item -LiteralPath $ReadOnlyInstaller -Destination $copy
                Equal (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash $originalHash
                $intactSignature = Get-ReleaseSignature $copy
                Assert-ReleaseFileSignature $copy $policy $intactSignature

                # Locate actual code-section bytes using the PE headers. Do not edit
                # the checksum, security-directory entry, certificate table or overlay.
                $stream = [IO.File]::Open($copy, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
                $reader = New-Object IO.BinaryReader($stream)
                try {
                    if ($reader.ReadUInt16() -ne 0x5A4D) { throw 'Test copy has no DOS header.' }
                    $stream.Position = 0x3c
                    $pe = $reader.ReadInt32()
                    $stream.Position = $pe
                    if ($reader.ReadUInt32() -ne 0x4550) { throw 'Test copy has no PE header.' }
                    $stream.Position = $pe + 6
                    $sectionCount = $reader.ReadUInt16()
                    $stream.Position = $pe + 20
                    $optionalSize = $reader.ReadUInt16()
                    $optional = $pe + 24
                    $stream.Position = $optional
                    $magic = $reader.ReadUInt16()
                    $directoryOffset = switch ($magic) {
                        0x10b { 96 }
                        0x20b { 112 }
                        default { throw 'Unsupported PE optional header in test copy.' }
                    }
                    $stream.Position = $optional + 60
                    $headersSize = $reader.ReadUInt32()
                    $stream.Position = $optional + $directoryOffset + (4 * 8)
                    $certificateOffset = $reader.ReadUInt32()
                    $certificateSize = $reader.ReadUInt32()
                    if ($certificateOffset -eq 0 -or $certificateSize -eq 0 -or
                        ([long]$certificateOffset + $certificateSize) -gt $stream.Length) {
                        throw 'Test copy has no in-bounds certificate table.'
                    }
                    $sectionTable = $optional + $optionalSize
                    $tamperOffset = $null
                    for ($section = 0; $section -lt $sectionCount; $section++) {
                        $stream.Position = $sectionTable + ($section * 40)
                        $sectionName = [Text.Encoding]::ASCII.GetString($reader.ReadBytes(8)).TrimEnd([char]0)
                        $virtualSize = $reader.ReadUInt32()
                        $null = $reader.ReadUInt32() # VirtualAddress
                        $rawSize = $reader.ReadUInt32()
                        $rawStart = $reader.ReadUInt32()
                        $stream.Position = $sectionTable + ($section * 40) + 36
                        $flags = $reader.ReadUInt32()
                        $dataSize = [Math]::Min($virtualSize, $rawSize)
                        if (($flags -band 0x20) -ne 0 -and $dataSize -gt 32 -and
                            $rawStart -ge $headersSize -and $rawStart -ge ($sectionTable + $sectionCount * 40) -and
                            ([long]$rawStart + $rawSize) -le $certificateOffset) {
                            $tamperOffset = [long]$rawStart + [long][Math]::Floor($dataSize / 2)
                            break
                        }
                    }
                    if ($null -eq $tamperOffset) { throw 'No safe code-section byte found for tamper test.' }
                    $stream.Position = $tamperOffset
                    $originalByte = $reader.ReadByte()
                    $stream.Position = $tamperOffset
                    $stream.WriteByte([byte]($originalByte -bxor 1))
                } finally { $reader.Dispose(); $stream.Dispose() }

                if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -eq $originalHash) {
                    throw 'Tamper test did not change the copy.'
                }
                $tamperedSignature = Get-ReleaseSignature $copy
                # Exact TRUST_E_BAD_DIGEST proves this is not merely a trust-root failure.
                Equal $tamperedSignature.WinVerifyTrustResult '0x80096010'
                Assert-ReleaseSigningIdentity $policy $tamperedSignature.SignerCertificate
                Fails { Assert-ReleaseFileSignature $copy $policy $tamperedSignature } '*Unusable release signature*WinVerifyTrust=0x80096010*'
                Write-Host "Tampered copy: section=$sectionName; offset=$tamperOffset; status=$($tamperedSignature.Status); WinVerifyTrust=$($tamperedSignature.WinVerifyTrustResult)"
            } finally {
                Equal (Get-FileHash -LiteralPath $ReadOnlyInstaller -Algorithm SHA256).Hash $originalHash
            }
        }
    }
    $unsigned = Join-Path $scratch 'unsigned.exe'
    Write-Fixture $unsigned
    Check 'Real WinVerifyTrust rejects unsigned synthetic bytes' {
        $signature = Get-ReleaseSignature $unsigned
        if ($signature.WinVerifyTrustResult -in @('0x00000000', '0x800B0109')) { throw 'Unsigned fixture was accepted.' }
        Fails { Assert-ReleaseFileSignature $unsigned $fixturePolicy $signature } '*no signer certificate*'
    }
    $exitScript = Join-Path $scratch 'exit.ps1'
    Write-Fixture $exitScript 'exit 23'
    Check 'Validator explicit nonzero exit propagates' { Fails { Invoke-ReleaseValidationScript $exitScript @() } '*exit code 23*' }
    Write-Fixture $exitScript 'exit 0'
    Check 'Validator zero exit accepted' { Invoke-ReleaseValidationScript $exitScript @() }

    $script:signatureStatus = 'UnknownError'
    $script:trustResult = '0x800B0109'
    $script:noTimestamp = $false
    function Get-ReleaseSignature([string]$Path) {
        return [pscustomobject]@{
            Status = $script:signatureStatus
            SignerCertificate = $certs[0]
            TimeStamperCertificate = $(if (-not $script:noTimestamp) { $certs[1] })
            WinVerifyTrustResult = $script:trustResult
        }
    }
    Check 'UnknownError accepted only for exact untrusted-root HRESULT' { Assert-ReleaseFileSignature $unsigned $fixturePolicy }
    foreach ($failure in @('0x80096010', '0x800B0111', '0x800B0101', '0x80004005')) {
        $script:trustResult = $failure
        Check "Native trust failure $failure rejected" { Fails { Assert-ReleaseFileSignature $unsigned $fixturePolicy } '*Unusable release signature*' }
    }
    $script:trustResult = '0x00000000'
    $script:noTimestamp = $true
    Check 'Missing timestamp rejected even with valid signer' { Fails { Assert-ReleaseFileSignature $unsigned $fixturePolicy } '*no timestamp*' }
    $script:noTimestamp = $false
    $script:trustResult = '0x800B0109'

    # Run the real signing wrapper against a fake tool and synthetic PFX only.
    $fakeTool = Join-Path $scratch 'fake-signtool.ps1'
    $fakeSource = @'
$selector = [Array]::IndexOf($args, '/sha1')
if ($selector -lt 0 -or $args[$selector + 1] -ne 'FIXTURE_THUMBPRINT' -or $args -contains '/a') { throw 'Invalid certificate selection arguments' }
if ($args -notcontains '/tr' -or $args -notcontains '/td' -or $args -notcontains '/fd') { throw 'Missing timestamp or digest arguments' }
$global:LASTEXITCODE = 0
'@
    Write-Fixture $fakeTool ($fakeSource.Replace('FIXTURE_THUMBPRINT', $fixturePolicy.thumbprint))
    $script:toolAccessed = $false
    function Get-ReleaseSignTool { $script:toolAccessed = $true; return $fakeTool }
    $fixtureContext = [pscustomobject]@{ PfxPath = $pfx; Password = $fixturePassword; Policy = $fixturePolicy }
    Check 'Real signing wrapper pins sha1 and keeps timestamp/digest arguments' {
        Invoke-ReleaseSignFile $unsigned $fixtureContext
        Equal $script:toolAccessed $true
    }
    $script:toolAccessed = $false
    Check 'Real signing wrapper rejects changed PFX identity before tool lookup' {
        $mismatchContext = [pscustomobject]@{ PfxPath = $pfx; Password = $fixturePassword; Policy = $policy }
        Fails { Invoke-ReleaseSignFile $unsigned $mismatchContext } '*identity mismatch*'
        Equal $script:toolAccessed $false
    }
    Write-Fixture $fakeTool "Write-Output 'simulated diagnostic that must be suppressed'; `$global:LASTEXITCODE = 9"
    Check 'Signtool nonzero failure is sanitized and propagated' {
        Fails { Invoke-ReleaseSignFile $unsigned $fixtureContext } 'Signtool failed with exit code 9. Diagnostic output suppressed to protect credentials.'
    }

    $payloadRoot = Join-Path $scratch 'payload with spaces'
    foreach ($relative in Get-ReleaseOwnedBinaryPaths) { Write-Fixture (Join-Path $payloadRoot $relative) }
    $thirdParty = Join-Path $payloadRoot 'native-dashboard/win-x64/vendor.dll'
    Write-Fixture $thirdParty 'vendor bytes preserved'
    $installer = Join-Path $scratch 'installer.exe'
    Write-Fixture $installer 'final installer bytes'
    Check 'Inventory hashes exact final bytes and covers every native binary' {
        $inventoryPath = Write-ReleaseArtifactInventory $installer $payloadRoot $fixturePolicy
        Equal $inventoryPath ($installer + '.inventory.json')
        $inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
        Equal $inventory.installer.sha256 (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
        Equal $inventory.installer.sizeBytes (Get-Item -LiteralPath $installer).Length
        Equal $inventory.payload.Count 6
        Equal $inventory.installer.signature.timestamp.present $true
        Equal $inventory.installer.signature.winVerifyTrustResult '0x800B0109'
        Equal $inventory.installer.signature.signer.thumbprint $fixturePolicy.thumbprint
        if ($inventory.securityScans -notlike 'Not performed*') { throw 'Missing scan disclaimer.' }
    }
    $payloadBefore = @(Get-ReleasePayloadInventory $payloadRoot $fixturePolicy)
    [IO.File]::AppendAllText($thirdParty, 'drift')
    Check 'Changed payload during compilation rejected' { Fails { Write-ReleaseArtifactInventory $installer $payloadRoot $fixturePolicy $payloadBefore } '*payload changed during*' }
    $script:trustResult = '0x80096010'
    $invalidInstaller = Join-Path $scratch 'invalid-installer.exe'
    Write-Fixture $invalidInstaller
    Check 'Invalid final signature cannot produce inventory' {
        Fails { Write-ReleaseArtifactInventory $invalidInstaller $payloadRoot $fixturePolicy } '*Unusable release signature*'
        Equal (Test-Path -LiteralPath ($invalidInstaller + '.inventory.json')) $false
    }
    $script:trustResult = '0x800B0109'

    # Exercise the real packaging orchestration, replacing only external operations.
    # None of these mocks loads the user's password/PFX or invokes a real signing tool.
    $script:events = New-Object 'System.Collections.Generic.List[string]'
    $script:validationFails = $false
    $script:identityFails = $false
    $script:compilerFails = $false
    $script:omitInstaller = $false
    function Invoke-ReleasePayloadValidation([string]$DestinationRoot) {
        $script:events.Add('validate')
        if ($script:validationFails) { throw 'fixture validation failure' }
    }
    function Get-ReleaseSigningContext {
        $script:events.Add('identity')
        if ($script:identityFails) { throw 'fixture identity mismatch' }
        return [pscustomobject]@{ Policy = $fixturePolicy; Password = (ConvertTo-SecureString 'fixture' -AsPlainText -Force) }
    }
    function Invoke-ReleaseSignFile([string]$Path, $Context) {
        $script:events.Add('sign:' + [IO.Path]::GetFileName($Path))
        [IO.File]::AppendAllText($Path, '|mock signature')
    }
    function Invoke-ReleaseCompiler([string]$OutputDirectory) {
        $script:events.Add('compile')
        if ($script:compilerFails) { throw 'fixture compiler failure' }
        if (-not $script:omitInstaller) { Write-Fixture (Join-Path $OutputDirectory 'fixture-rc.exe') 'new installer bytes' }
    }
    $output = Join-Path $scratch 'output'
    $vendorHash = (Get-FileHash -LiteralPath $thirdParty -Algorithm SHA256).Hash
    Check 'Package validates, signs exact owned list, compiles, then inventories; vendor unchanged' {
        Invoke-ReleasePackage $payloadRoot $output
        Equal ($script:events -join ',') 'validate,identity,sign:ZTMZ.Launcher.exe,sign:ZTMZ.PacenoteTool.WpfGUI.exe,sign:ztmz_dashboard_overlay.dll,sign:ztmz_dashboard_overlay.dll,sign:ztmz_dashboard_injector.exe,compile'
        Equal (Get-FileHash -LiteralPath $thirdParty -Algorithm SHA256).Hash $vendorHash
        $final = Join-Path $output 'fixture-rc.exe'
        $inventory = Get-Content -LiteralPath ($final + '.inventory.json') -Raw | ConvertFrom-Json
        Equal $inventory.installer.sha256 (Get-FileHash -LiteralPath $final -Algorithm SHA256).Hash
        Equal @(Get-ChildItem -LiteralPath $output -Directory -Force).Count 0
    }
    $script:events.Clear()
    $script:validationFails = $true
    Check 'Payload failure stops before identity, signing and compilation' {
        Fails { Invoke-ReleasePackage $payloadRoot $output } '*fixture validation failure*'
        Equal ($script:events -join ',') 'validate'
    }
    $script:validationFails = $false
    $script:identityFails = $true
    $script:events.Clear()
    Check 'Identity mismatch stops before signing and compilation' {
        Fails { Invoke-ReleasePackage $payloadRoot $output } '*fixture identity mismatch*'
        Equal ($script:events -join ',') 'validate,identity'
    }
    $script:identityFails = $false
    $script:compilerFails = $true
    $final = Join-Path $output 'fixture-rc.exe'
    $finalHash = (Get-FileHash -LiteralPath $final -Algorithm SHA256).Hash
    Check 'Compiler failure does not replace previous installer' {
        Fails { Invoke-ReleasePackage $payloadRoot $output } '*fixture compiler failure*'
        Equal (Get-FileHash -LiteralPath $final -Algorithm SHA256).Hash $finalHash
    }
    $script:compilerFails = $false
    $script:omitInstaller = $true
    Check 'Successful compiler without output cannot select stale installer' {
        Fails { Invoke-ReleasePackage $payloadRoot $output } '*exactly one freshly compiled installer*'
    }
    Write-Host "$script:checks release signing/inventory checks passed. No real signing, credentials, stores or builds used."
} finally {
    if ($fixturePassword) { $fixturePassword.Dispose() }
    foreach ($cert in $certs) { $cert.Dispose() }
    foreach ($key in $keys) { $key.Dispose() }
    # Only delete the uniquely named test directory, after resolving its containment.
    $resolved = [IO.Path]::GetFullPath($scratch)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notlike 'ztmz-signing-tests-*') { throw 'Unsafe test cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
