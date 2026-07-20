# Shared C# compiler resolver for the raw-compiler build/test scripts.
#
# The legacy in-box .NET Framework compiler (%WINDIR%\Microsoft.NET\Framework*\
# v4.0.30319\csc.exe) only supports up to C# 5, so it rejects the C# 6/7 syntax
# this project uses (expression-bodied members, local functions). We therefore
# prefer the modern Roslyn compiler shipped with Visual Studio / Build Tools,
# which supports /langversion:7.3. When Roslyn is used we must also add /lib:
# entries pointing at the .NET Framework reference/runtime assemblies so that
# bare-name references (e.g. System.Windows.Forms.dll) still resolve.
#
# Dot-source this file, then call Resolve-CSharpCompiler. It returns a hashtable:
#   @{ Path = <compiler exe>; LibDirs = @(<dir>,...); Roslyn = $true|$false }
# or $null if no compiler is found.

function Get-FrameworkLibDirs {
    $dirs = New-Object System.Collections.Generic.List[string]
    # Prefer the .NET Framework reference assemblies (canonical for compilation).
    $refRoot = Join-Path ${env:ProgramFiles(x86)} "Reference Assemblies\Microsoft\Framework\.NETFramework"
    if (Test-Path $refRoot) {
        $refAsm = Get-ChildItem -Path $refRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^v4\.' } |
            Sort-Object Name -Descending | Select-Object -First 1
        if ($refAsm) { $dirs.Add($refAsm.FullName) }
    }
    # Add the runtime framework dir too (covers assemblies not in the ref set,
    # e.g. System.Web.Extensions.dll on some SKUs).
    foreach ($fd in @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319"
    )) {
        if (Test-Path $fd) { $dirs.Add($fd); break }
    }
    return $dirs.ToArray()
}

function Resolve-CSharpCompiler {
    $libDirs = Get-FrameworkLibDirs

    # 1. Locate the latest Visual Studio / Build Tools via vswhere, then Roslyn.
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $vsPath = & $vswhere -latest -prerelease -products * -property installationPath 2>$null |
            Select-Object -First 1
        if ($vsPath) {
            foreach ($rel in @(
                "MSBuild\Current\Bin\Roslyn\csc.exe",
                "MSBuild\15.0\Bin\Roslyn\csc.exe"
            )) {
                $p = Join-Path $vsPath $rel
                if (Test-Path $p) { return @{ Path = $p; LibDirs = $libDirs; Roslyn = $true } }
            }
        }
    }

    # 2. Fall back to any Roslyn compiler under the VS install roots.
    foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if (-not $root) { continue }
        $vsRoot = Join-Path $root "Microsoft Visual Studio"
        if (-not (Test-Path $vsRoot)) { continue }
        $hit = Get-ChildItem -Path $vsRoot -Recurse -Filter "csc.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "[\\/]Roslyn[\\/]" } |
            Select-Object -First 1
        if ($hit) { return @{ Path = $hit.FullName; LibDirs = $libDirs; Roslyn = $true } }
    }

    # 3. Legacy in-box compiler (C# 5 only) as a last resort.
    $legacy = @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($legacy) { return @{ Path = $legacy; LibDirs = $libDirs; Roslyn = $false } }

    return $null
}
