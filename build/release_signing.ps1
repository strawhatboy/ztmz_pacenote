param(
    [Parameter(Mandatory = $true)][ValidateSet('Check', 'Sign', 'Package', 'Inventory')][string]$Action,
    [string]$Path,
    [string]$DestinationRoot = (Join-Path $PSScriptRoot 'target/publish/ZTMZ.PacenoteTool.WpfGUI/release_win-x64')
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release_signing_helpers.ps1')
try {
    switch ($Action) {
        'Check' {
            $context = Get-ReleaseSigningContext
            $context.Password.Dispose()
        }
        'Sign' {
            if ([string]::IsNullOrWhiteSpace($Path)) { throw 'Signing target was not provided.' }
            $context = Get-ReleaseSigningContext
            try { Invoke-ReleaseSignFile $Path $context } finally { $context.Password.Dispose() }
        }
        'Package' {
            # setup_normal.iss has an established fixed publish source. Never sign
            # and inventory a different tree from the one Inno will actually read.
            if ($PSBoundParameters.ContainsKey('DestinationRoot')) {
                throw 'Package uses the fixed release publish directory from setup_normal.iss; DestinationRoot is supported only for Inventory.'
            }
            Invoke-ReleasePackage $DestinationRoot (Join-Path (Split-Path -Parent $PSScriptRoot) 'Output')
        }
        'Inventory' {
            if ([string]::IsNullOrWhiteSpace($Path)) { throw 'Installer path was not provided.' }
            Write-ReleaseArtifactInventory $Path $DestinationRoot (Get-ReleaseSigningPolicy)
        }
    }
} catch {
    # Print only the deliberately sanitized message, not invocation/argument details.
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
