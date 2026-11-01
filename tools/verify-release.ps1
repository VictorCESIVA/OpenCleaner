<#
.SYNOPSIS
Checks the Authenticode signature and SHA-256 of an OpenCleaner release asset.

.DESCRIPTION
Accepts the published Setup.exe, the portable .zip, or an application .exe.
Prints the SHA-256 and signature status of each executable, then exits:
  0 = every executable has a valid signature, or the asset is unsigned and
      -RequireValidSignature was not requested;
  2 = -RequireValidSignature was requested and at least one signature is not valid.

Use -ChecksumsPath to also write a SHA256SUMS.txt file containing the archive
hash (for .zip inputs) and the hash of every executable in the asset.

Windows only: Get-AuthenticodeSignature and Expand-Archive are used.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools/verify-release.ps1 -Path .\OpenCleaner-win-x64-Setup.exe

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools/verify-release.ps1 -Path .\OpenCleaner-win-x64-Portable.zip -RequireValidSignature -ChecksumsPath .\SHA256SUMS.txt
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [switch]$RequireValidSignature,

    [string]$ChecksumsPath
)

$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $Path).Path
if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
    throw "Fichier introuvable : $resolved"
}

function Get-FileSha256([string]$file) {
    (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
}

$tempDirectory = $null
$exitCode = 0
$checksumLines = [System.Collections.Generic.List[string]]::new()
try {
    $extension = [IO.Path]::GetExtension($resolved).ToLowerInvariant()
    if ($extension -eq '.zip') {
        $archiveHash = Get-FileSha256 $resolved
        Write-Host ("Archive : {0}" -f $resolved)
        Write-Host ("SHA-256 : {0}" -f $archiveHash)
        $checksumLines.Add(("{0} *{1}" -f $archiveHash, [IO.Path]::GetFileName($resolved)))
        $tempDirectory = Join-Path ([IO.Path]::GetTempPath()) ("OpenCleaner-verify-" + [Guid]::NewGuid().ToString('N'))
        Expand-Archive -LiteralPath $resolved -DestinationPath $tempDirectory
        $executables = @(Get-ChildItem -LiteralPath $tempDirectory -Recurse -File -Filter '*.exe')
    }
    elseif ($extension -eq '.exe') {
        $executables = @(Get-Item -LiteralPath $resolved)
    }
    else {
        throw "Format attendu : .exe ou .zip (reçu : $extension)"
    }

    if ($executables.Count -eq 0) {
        throw "Aucun exécutable trouvé dans l'archive."
    }

    $invalid = $false
    foreach ($executable in $executables) {
        $signature = Get-AuthenticodeSignature -LiteralPath $executable.FullName
        $subject = if ($null -ne $signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { '(aucun)' }
        $hash = Get-FileSha256 $executable.FullName
        $relativeName = $executable.Name
        if ($tempDirectory -and $executable.FullName.StartsWith($tempDirectory, [StringComparison]::OrdinalIgnoreCase)) {
            $relativeName = $executable.FullName.Substring($tempDirectory.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
        }
        Write-Host ""
        Write-Host ("Exécutable : {0}" -f $executable.Name)
        Write-Host ("SHA-256    : {0}" -f $hash)
        Write-Host ("Signature  : {0}" -f $signature.Status)
        Write-Host ("Éditeur    : {0}" -f $subject)
        $checksumLines.Add(("{0} *{1}" -f $hash, $relativeName))
        if ($signature.Status -ne 'Valid') { $invalid = $true }
    }

    if ($ChecksumsPath) {
        $absoluteChecksumsPath = if ([IO.Path]::IsPathRooted($ChecksumsPath)) {
            $ChecksumsPath
        }
        else {
            Join-Path (Get-Location).Path $ChecksumsPath
        }
        [IO.File]::WriteAllLines($absoluteChecksumsPath, $checksumLines, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host ""
        Write-Host ("Sommes SHA-256 écrites : {0}" -f $absoluteChecksumsPath)
    }

    if ($invalid) {
        if ($RequireValidSignature) {
            [Console]::Error.WriteLine("Signature invalide ou absente : publication refusée.")
            $exitCode = 2
        }
        else {
            Write-Warning "Signature absente ou invalide : ce binaire peut déclencher SmartScreen."
        }
    }
}
finally {
    if ($tempDirectory -and (Test-Path -LiteralPath $tempDirectory)) {
        Remove-Item -LiteralPath $tempDirectory -Recurse -Force
    }
}

exit $exitCode
