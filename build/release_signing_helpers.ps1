# Windows PowerShell 5.1 compatible. Loading this file performs no signing or PFX reads.
function Get-ReleaseSigningPolicy {
    param([string]$MetadataPath = (Join-Path $PSScriptRoot 'release-signing.json'))
    $policy = Get-Content -LiteralPath $MetadataPath -Raw | ConvertFrom-Json
    if ($policy.schemaVersion -ne 1 -or $policy.thumbprint -notmatch '^[0-9a-fA-F]{40}$' -or
        [string]::IsNullOrWhiteSpace($policy.subject)) {
        throw 'Invalid reviewed release signing metadata; expected schemaVersion 1, subject and a 40-digit thumbprint.'
    }
    $policy.thumbprint = $policy.thumbprint.ToUpperInvariant()
    return $policy
}

function Assert-ReleaseSigningIdentity {
    param($Policy, $Certificate)
    $actual = if ($null -eq $Certificate) { '<no signer certificate>' } else {
        "subject=$($Certificate.Subject); thumbprint=$($Certificate.Thumbprint)"
    }
    if ($null -eq $Certificate -or $Certificate.Thumbprint -ne $Policy.thumbprint) {
        throw "Release signing identity mismatch. Expected subject=$($Policy.subject); thumbprint=$($Policy.thumbprint). Actual $actual. Select the reviewed PFX, or deliberately review/edit build/release-signing.json for certificate rotation."
    }
}

function Read-ReleaseSigningCertificate {
    param([string]$PfxPath, [Security.SecureString]$Password, $Policy)
    $certificates = New-Object Security.Cryptography.X509Certificates.X509Certificate2Collection
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
    try {
        try {
            # EphemeralKeySet prevents PFX inspection from installing certificates or keys.
            # .NET Framework's collection Import has no SecureString overload.
            $passwordText = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
            $certificates.Import($PfxPath, $passwordText, [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
        } catch {
            # Do not forward provider exception text or secret-bearing invocation details.
            throw 'Cannot read signing PFX with the configured password using ephemeral keys. Check the PFX/password and .NET support; no certificate will be created.'
        }
        $signers = @($certificates | Where-Object { $_.HasPrivateKey })
        if ($signers.Count -ne 1) {
            $identities = @($signers | ForEach-Object { "subject=$($_.Subject); thumbprint=$($_.Thumbprint)" }) -join '; '
            if (-not $identities) { $identities = '<no private-key signer>' }
            throw "Release PFX must contain exactly one private-key signer. Expected subject=$($Policy.subject); thumbprint=$($Policy.thumbprint). Actual $identities."
        }
        Assert-ReleaseSigningIdentity $Policy $signers[0]
        if ($signers[0].NotBefore -gt (Get-Date) -or $signers[0].NotAfter -le (Get-Date)) {
            throw "Release signing certificate is outside its validity period: subject=$($signers[0].Subject); thumbprint=$($signers[0].Thumbprint)."
        }
        return [pscustomobject]@{ Subject = $signers[0].Subject; Thumbprint = $signers[0].Thumbprint }
    } finally {
        $passwordText = $null
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
        foreach ($certificate in $certificates) { $certificate.Dispose() }
    }
}

function Get-ReleaseSigningContext {
    $policy = Get-ReleaseSigningPolicy
    $pfx = [Environment]::GetEnvironmentVariable('ZTMZ_SIGNING_PFX_PATH', 'Process')
    if ([string]::IsNullOrWhiteSpace($pfx)) { $pfx = Join-Path $env:USERPROFILE 'ztmz_selfsigncert.pfx' }
    if (-not (Test-Path -LiteralPath $pfx -PathType Leaf)) { throw "Signing certificate was not found: $pfx" }
    $passwordText = [Environment]::GetEnvironmentVariable('ZTMZ_SIGNING_PFX_PASSWORD', 'Process')
    if ([string]::IsNullOrEmpty($passwordText)) {
        $passwordText = [Environment]::GetEnvironmentVariable('ZTMZ_SIGNING_PFX_PASSWORD', 'User')
    }
    if ([string]::IsNullOrEmpty($passwordText)) { throw 'ZTMZ_SIGNING_PFX_PASSWORD is not configured for the current user.' }
    try { $password = ConvertTo-SecureString -String $passwordText -AsPlainText -Force }
    finally { $passwordText = $null }
    try {
        $identity = Read-ReleaseSigningCertificate $pfx $password $policy
        Write-Host "Reviewed release signer: subject=$($identity.Subject); thumbprint=$($identity.Thumbprint)"
        return [pscustomobject]@{ Policy = $policy; PfxPath = (Resolve-Path -LiteralPath $pfx).Path; Password = $password }
    } catch { $password.Dispose(); throw }
}

function Get-ReleaseSignTool {
    $root = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $tool = Get-ChildItem -LiteralPath $root -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $tool) { throw 'Windows SDK x64 signtool.exe was not found.' }
    return $tool.FullName
}

function Get-ReleaseSignature {
    param([string]$Path)
    if (-not ('Ztmz.Release.Authenticode' -as [type])) {
        Add-Type -Path (Join-Path $PSScriptRoot 'release_wintrust.cs')
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    return [pscustomobject]@{
        Status = [string]$signature.Status
        SignerCertificate = $signature.SignerCertificate
        TimeStamperCertificate = $signature.TimeStamperCertificate
        WinVerifyTrustResult = [Ztmz.Release.Authenticode]::Verify((Resolve-Path -LiteralPath $Path).Path)
    }
}

function ConvertTo-ReleasePublicCertificate {
    param($Certificate)
    if ($null -eq $Certificate) { return $null }
    return [ordered]@{
        subject = $Certificate.Subject
        issuer = $Certificate.Issuer
        thumbprint = $Certificate.Thumbprint
        notBeforeUtc = $Certificate.NotBefore.ToUniversalTime().ToString('o')
        notAfterUtc = $Certificate.NotAfter.ToUniversalTime().ToString('o')
    }
}

function Assert-ReleaseFileSignature {
    param([string]$Path, $Policy, $Signature = (Get-ReleaseSignature $Path))
    Assert-ReleaseSigningIdentity $Policy $Signature.SignerCertificate
    # PowerShell maps some untrusted self-signed roots to UnknownError. Accept only
    # the exact native CERT_E_UNTRUSTEDROOT result, never UnknownError generally.
    # Digest/signature/timestamp failures and explicit distrust remain fatal.
    if ($Signature.WinVerifyTrustResult -notin @('0x00000000', '0x800B0109')) {
        throw "Unusable release signature on '$Path': $($Signature.Status); WinVerifyTrust=$($Signature.WinVerifyTrustResult)."
    }
    if ($null -eq $Signature.TimeStamperCertificate) { throw "Release signature has no timestamp on '$Path'." }
}

function Invoke-ReleaseSignFile {
    param([string]$Path, $Context)
    $target = (Resolve-Path -LiteralPath $Path).Path
    # Re-read immediately before signing, including every Inno callback. Never use /a.
    $null = Read-ReleaseSigningCertificate $Context.PfxPath $Context.Password $Context.Policy
    $tool = Get-ReleaseSignTool
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Context.Password)
    try {
        $passwordText = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        # Signtool's PFX interface requires /p on its child process command line.
        # Keep the existing RFC3161/SHA256 semantics, never echo the command or its output.
        $signArguments = @('sign', '/f', $Context.PfxPath, '/p', $passwordText,
            '/sha1', $Context.Policy.thumbprint, '/tr', 'http://timestamp.digicert.com', '/td', 'sha256', '/fd', 'sha256', $target)
        try {
            & $tool @signArguments *> $null
            $signExitCode = $LASTEXITCODE
        } catch { throw 'Signtool could not complete signing. Diagnostic output suppressed to protect credentials.' }
        if ($signExitCode -ne 0) { throw "Signtool failed with exit code $signExitCode. Diagnostic output suppressed to protect credentials." }
    } finally {
        $passwordText = $null
        $signArguments = $null
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
    Assert-ReleaseFileSignature $target $Context.Policy
    Write-Host "Signed and checked: $target"
}

function Get-ReleaseOwnedBinaryPaths {
    # Exact relative paths, never a broad DLL glob (third-party binaries retain their signatures).
    return @(
        'ZTMZ.Launcher.exe',
        'ZTMZ.PacenoteTool.WpfGUI.exe',
        'native-dashboard/win-x64/ztmz_dashboard_overlay.dll',
        'native-dashboard/win-x86/ztmz_dashboard_overlay.dll',
        'native-dashboard/win-x86/ztmz_dashboard_injector.exe'
    )
}

function Get-ReleaseInventoryPaths {
    param([string]$DestinationRoot)
    $root = (Resolve-Path -LiteralPath $DestinationRoot).Path.TrimEnd('\', '/')
    $paths = @(Get-ReleaseOwnedBinaryPaths)
    $paths += @(Get-ChildItem -LiteralPath (Join-Path $root 'native-dashboard') -Recurse -File |
        Where-Object { $_.Extension -in @('.exe', '.dll') } |
        ForEach-Object { $_.FullName.Substring($root.Length + 1).Replace('\', '/') })
    return @($paths | Sort-Object -Unique)
}

function Get-ReleaseArtifactRecord {
    param([string]$Path, [string]$RelativePath, $Policy, [switch]$RequireReviewedSignature)
    $file = Get-Item -LiteralPath $Path
    if ($file.PSIsContainer -or $file.Length -eq 0) { throw "Missing or empty release artifact: $Path" }
    $signature = Get-ReleaseSignature $file.FullName
    if ($RequireReviewedSignature) { Assert-ReleaseFileSignature $file.FullName $Policy $signature }
    return [ordered]@{
        path = $RelativePath
        sizeBytes = $file.Length
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        signature = [ordered]@{
            status = [string]$signature.Status
            winVerifyTrustResult = $signature.WinVerifyTrustResult
            signer = ConvertTo-ReleasePublicCertificate $signature.SignerCertificate
            timestamp = [ordered]@{
                present = ($null -ne $signature.TimeStamperCertificate)
                authority = ConvertTo-ReleasePublicCertificate $signature.TimeStamperCertificate
            }
        }
    }
}

function Get-ReleasePayloadInventory {
    param([string]$DestinationRoot, $Policy)
    $owned = @(Get-ReleaseOwnedBinaryPaths)
    foreach ($relative in Get-ReleaseInventoryPaths $DestinationRoot) {
        Get-ReleaseArtifactRecord (Join-Path $DestinationRoot $relative) $relative $Policy -RequireReviewedSignature:($relative -in $owned)
    }
}

function Write-ReleaseArtifactInventory {
    param([string]$InstallerPath, [string]$DestinationRoot, $Policy, [object[]]$PackagedPayload)
    $installer = Get-ReleaseArtifactRecord $InstallerPath ([IO.Path]::GetFileName($InstallerPath)) $Policy -RequireReviewedSignature
    $payload = @(Get-ReleasePayloadInventory $DestinationRoot $Policy)
    if ($null -ne $PackagedPayload) {
        $before = @($PackagedPayload | ForEach-Object { "$($_.path)|$($_.sizeBytes)|$($_.sha256)" })
        $after = @($payload | ForEach-Object { "$($_.path)|$($_.sizeBytes)|$($_.sha256)" })
        if (($before -join "`n") -cne ($after -join "`n")) { throw 'Release payload changed during installer compilation; refusing to write a final inventory.' }
    }
    $inventory = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = [DateTime]::UtcNow.ToString('o')
        expectedSigner = [ordered]@{ subject = $Policy.subject; thumbprint = $Policy.thumbprint }
        installer = $installer
        payloadSource = 'Published files after signing; not an extracted-installer comparison.'
        payload = $payload
        securityScans = 'Not performed by this inventory. No antivirus, SmartScreen or reputation result is implied.'
    }
    $outputPath = $InstallerPath + '.inventory.json'
    [IO.File]::WriteAllText($outputPath, ($inventory | ConvertTo-Json -Depth 12) + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
    return $outputPath
}

function Invoke-ReleasePayloadValidation {
    param([string]$DestinationRoot)
    Invoke-ReleaseValidationScript (Join-Path $PSScriptRoot 'verify_release_payload.ps1') @('-DestinationRoot', $DestinationRoot)
    Invoke-ReleaseValidationScript (Join-Path $PSScriptRoot 'stage_release_resources.ps1') @('-VerifyOnly', '-DestinationRoot', $DestinationRoot)
}

function Invoke-ReleaseValidationScript {
    param([string]$ScriptPath, [string[]]$ScriptArguments)
    # A separate PowerShell process preserves explicit exit N as well as throws.
    # Validators never receive signing credentials in arguments or diagnostic text.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @ScriptArguments
    if ($LASTEXITCODE -ne 0) { throw "Release payload validator '$([IO.Path]::GetFileName($ScriptPath))' failed with exit code $LASTEXITCODE." }
}

function Invoke-ReleaseCompiler {
    param([string]$OutputDirectory)
    $signCommand = '/Smssign=$q' + (Join-Path $PSScriptRoot 'sign_local.bat') + '$q $f'
    & ISCC.exe /V:1 "/O$OutputDirectory" $signCommand (Join-Path $PSScriptRoot 'setup_normal_signed.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }
}

function Invoke-ReleasePackage {
    param([string]$DestinationRoot, [string]$OutputDirectory)
    # Verify the established payload/resources before modifying any file by signing.
    Invoke-ReleasePayloadValidation $DestinationRoot
    $context = Get-ReleaseSigningContext
    try {
        foreach ($relative in Get-ReleaseOwnedBinaryPaths) {
            Invoke-ReleaseSignFile (Join-Path $DestinationRoot $relative) $context
        }
        $payload = @(Get-ReleasePayloadInventory $DestinationRoot $context.Policy)
        # A unique compiler output prevents choosing an old installer by mtime/glob.
        $null = New-Item -ItemType Directory -Path $OutputDirectory -Force
        $staging = Join-Path $OutputDirectory ('.release-' + [guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $staging
        Invoke-ReleaseCompiler $staging
        $installers = @(Get-ChildItem -LiteralPath $staging -Filter '*.exe' -File)
        if ($installers.Count -ne 1) { throw "Expected exactly one freshly compiled installer in $staging." }
        $inventory = Write-ReleaseArtifactInventory $installers[0].FullName $DestinationRoot $context.Policy $payload
        $finalInstaller = Join-Path $OutputDirectory $installers[0].Name
        $finalInventory = $finalInstaller + '.inventory.json'
        # Do not leave a stale inventory attached to a replacement installer on failure.
        if (Test-Path -LiteralPath $finalInventory) { Remove-Item -LiteralPath $finalInventory -Force }
        Move-Item -LiteralPath $installers[0].FullName -Destination $finalInstaller -Force
        Move-Item -LiteralPath $inventory -Destination $finalInventory -Force
        # Avoid Remove-Item's interactive prompt for a non-empty directory.
        if (@(Get-ChildItem -LiteralPath $staging -Force).Count -eq 0) {
            Remove-Item -LiteralPath $staging
        }
        Write-Host "Signed installer: $finalInstaller"
        Write-Host "Final SHA256/signature inventory: $finalInventory"
    } finally { $context.Password.Dispose() }
}
