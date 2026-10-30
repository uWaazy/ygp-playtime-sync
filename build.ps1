param([string]$Version = "")

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "YgpPlaytimeSync"
$Out = Join-Path $Root "bin\Release"
$Log = Join-Path $Root "build.log"

function Fail([string]$Message) {
    Write-Host ""; Write-Host "BUILD FAILED" -ForegroundColor Red
    Write-Host $Message -ForegroundColor Red
    Write-Host "Log: $Log"
    exit 1
}

try {
    "YGP Playtime Sync build $(Get-Date -Format s)" | Set-Content $Log
    Write-Host "==============================================="
    Write-Host " YGP Playtime Sync - Release Build"
    Write-Host "==============================================="

    $yamlPath = Join-Path $Project "extension.yaml"
    $currentVersion = ([regex]::Match([IO.File]::ReadAllText($yamlPath), '(?m)^Version:\s*(\S+)')).Groups[1].Value
    if (-not $Version) {
        $answer = Read-Host "Versao atual: $currentVersion. Digite a versao a compilar (ex.: 0.5.2) ou Enter para manter"
        $Version = if ($answer) { $answer } else { $currentVersion }
    }
    $Version = $Version.Trim().TrimStart('v', 'V')
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { Fail "Versao invalida: '$Version'. Use o formato 1.2.3 (ex.: 0.5.2)." }
    Write-Host "Versao   : $Version"

    function Update-VersionInFile([string]$Path, [string]$Pattern, [string]$Replacement) {
        if (-not (Test-Path $Path)) { return }
        $bytes = [IO.File]::ReadAllBytes($Path)
        $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
        $text = [Text.Encoding]::UTF8.GetString($bytes)
        if ($hasBom) { $text = $text.Substring(1) }
        $updated = [regex]::Replace($text, $Pattern, $Replacement)
        if ($updated -ne $text) {
            [IO.File]::WriteAllText($Path, $updated, (New-Object Text.UTF8Encoding($hasBom)))
        }
    }
    Update-VersionInFile $yamlPath '(?m)^(Version:\s*)\S+' ('${1}' + $Version)
    Update-VersionInFile (Join-Path $Root "README.md") '(badge/version-)[0-9][0-9.]*(--)' ('${1}' + $Version + '${2}')
    Update-VersionInFile (Join-Path $Root "README.pt-BR.md") '(badge/vers%C3%A3o-)[0-9][0-9.]*(--)' ('${1}' + $Version + '${2}')
    Update-VersionInFile (Join-Path $Root "README.es.md") '(badge/versi%C3%B3n-)[0-9][0-9.]*(--)' ('${1}' + $Version + '${2}')
    $changelogPath = Join-Path $Root "CHANGELOG.md"
    if ((Test-Path $changelogPath) -and -not ([IO.File]::ReadAllText($changelogPath) -match "(?m)^##\s+$([regex]::Escape($Version))\b")) {
        Write-Host "AVISO: CHANGELOG.md nao tem uma secao '## $Version'." -ForegroundColor Yellow
    }

    $vsRoots = @("${env:ProgramFiles}\Microsoft Visual Studio", "${env:ProgramFiles(x86)}\Microsoft Visual Studio") | Where-Object { Test-Path $_ }
    $vsCsc = @()
    foreach ($vr in $vsRoots) {
        $vsCsc += Get-ChildItem $vr -Filter csc.exe -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\MSBuild\\.*\\Roslyn\\csc\.exe$' } |
            Sort-Object FullName -Descending | ForEach-Object { $_.FullName }
    }
    $cscCandidates = @(
        $env:YGP_CSC,
        (Join-Path $Root "tools\roslyn\csc.exe"),
        "$env:LOCALAPPDATA\Microsoft\MSBuild\Roslyn\csc.exe"
    ) + $vsCsc
    $csc = $cscCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $csc) { Fail "Roslyn csc.exe não encontrado. Instale o Visual Studio Build Tools (workload .NET desktop build tools) ou defina a variável YGP_CSC com o caminho do csc.exe." }

    $sdkCandidates = @(
        $(if ($env:PLAYNITE_DIR) { Join-Path $env:PLAYNITE_DIR "Playnite.SDK.dll" }),
        "$env:LOCALAPPDATA\Playnite\Playnite.SDK.dll",
        "$env:LOCALAPPDATA\Playnite\Playnite\Playnite.SDK.dll",
        "$env:PROGRAMFILES\Playnite\Playnite.SDK.dll",
        "${env:ProgramFiles(x86)}\Playnite\Playnite.SDK.dll"
    )
    $sdk = $sdkCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $sdk -and (Test-Path "$env:LOCALAPPDATA\Playnite")) {
        $sdk = Get-ChildItem "$env:LOCALAPPDATA\Playnite" -Filter Playnite.SDK.dll -File -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $sdk -and $env:PLAYNITE_DIR -and (Test-Path $env:PLAYNITE_DIR)) {
        $sdk = Get-ChildItem $env:PLAYNITE_DIR -Filter Playnite.SDK.dll -File -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $sdk) { Fail "Playnite.SDK.dll não encontrado. Se o Playnite está instalado em outra pasta (versão portátil), defina a variável de ambiente PLAYNITE_DIR com essa pasta." }

    $shipJson = $true
    $jsonPath = Join-Path $Root "packages\Newtonsoft.Json\lib\Newtonsoft.Json.dll"
    if (-not (Test-Path $jsonPath)) {
        Write-Host "Baixando Newtonsoft.Json 13.0.3 do NuGet (apenas na primeira vez)..."
        $tmpZip = Join-Path ([IO.Path]::GetTempPath()) "newtonsoft.json.13.0.3.zip"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/newtonsoft.json/13.0.3/newtonsoft.json.13.0.3.nupkg" -OutFile $tmpZip -UseBasicParsing
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [IO.Compression.ZipFile]::OpenRead($tmpZip)
        try {
            $entry = $zip.Entries | Where-Object { $_.FullName -eq "lib/net45/Newtonsoft.Json.dll" } | Select-Object -First 1
            if (-not $entry) { throw "lib/net45/Newtonsoft.Json.dll não encontrado no pacote NuGet." }
            New-Item (Split-Path $jsonPath) -ItemType Directory -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $jsonPath, $true)
        } finally { $zip.Dispose(); Remove-Item $tmpZip -Force -ErrorAction SilentlyContinue }
    }
    $jsonCandidates = @(
        (Join-Path $Root "packages\Newtonsoft.Json\lib\Newtonsoft.Json.dll"),
        (Join-Path $Root "packages\Newtonsoft.Json.13.0.3\lib\net45\Newtonsoft.Json.dll")
    )
    $json = $jsonCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $json) {
        $playniteJson = Join-Path (Split-Path -Parent $sdk) "Newtonsoft.Json.dll"
        if (Test-Path $playniteJson) { $json = $playniteJson }
    }
    if (-not $json) { Fail "Newtonsoft.Json.dll não encontrado (pasta packages ou pasta do Playnite)." }

    $refRoots = @(
        "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework",
        "$env:ProgramFiles\Reference Assemblies\Microsoft\Framework\.NETFramework"
    ) | Where-Object { Test-Path $_ }
    $refDir = $null
    foreach ($base in $refRoots) {
        if (Test-Path (Join-Path $base "v4.6.2")) { $refDir = Join-Path $base "v4.6.2"; break }
        $dirs = Get-ChildItem $base -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^v4\.[0-9]+(\.[0-9]+)?$' } | Sort-Object Name -Descending
        if ($dirs) { $refDir = $dirs[0].FullName; break }
    }
    if (-not $refDir) {
        $legacy = "C:\Windows\Microsoft.NET\Framework\v4.0.30319"
        if (Test-Path $legacy) { $refDir = $legacy } else { Fail "Nenhuma referência do .NET Framework foi encontrada." }
    }

    Write-Host "Compiler : $csc"
    Write-Host "SDK      : $sdk"
    Write-Host "JSON     : $json"
    Write-Host "Refs     : $refDir"
    Add-Content $Log "Version=$Version`nCompiler=$csc`nSDK=$sdk`nJSON=$json`nRefs=$refDir"

    if (Test-Path $Out) {
        Get-ChildItem $Out -File -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
            try { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue } catch { }
        }
    } else {
        New-Item $Out -ItemType Directory -Force | Out-Null
    }

    $sources = Get-ChildItem $Project -Filter *.cs -File | Sort-Object Name | ForEach-Object { $_.FullName }
    $genDir = Join-Path $Root "bin\obj"
    New-Item $genDir -ItemType Directory -Force | Out-Null
    $assemblyInfo = Join-Path $genDir "AssemblyInfo.Version.cs"
    $assemblyText = "using System.Reflection;`r`n[assembly: AssemblyVersion(`"$Version.0`")]`r`n[assembly: AssemblyFileVersion(`"$Version.0`")]`r`n[assembly: AssemblyInformationalVersion(`"$Version`")]`r`n"
    [IO.File]::WriteAllText($assemblyInfo, $assemblyText, (New-Object Text.UTF8Encoding($false)))
    $sources = @($sources) + $assemblyInfo
    # WPF reference assemblies can be under the framework's WPF subfolder on
    # machines without the 4.6.2 targeting pack. Resolve each DLL explicitly.
    $frameworkSearchDirs = @($refDir, (Join-Path $refDir 'WPF'))
    foreach ($base in $refRoots) {
        Get-ChildItem $base -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^v4\.[0-9]+(\.[0-9]+)?$' } |
            ForEach-Object { $frameworkSearchDirs += $_.FullName; $frameworkSearchDirs += (Join-Path $_.FullName 'WPF') }
    }
    $frameworkSearchDirs += 'C:\Windows\Microsoft.NET\Framework\v4.0.30319'
    $frameworkSearchDirs += 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\WPF'
    $frameworkSearchDirs = @($frameworkSearchDirs | Where-Object { Test-Path $_ } | Select-Object -Unique)

    function Find-FrameworkReference([string]$Name) {
        foreach ($dir in $frameworkSearchDirs) {
            $candidate = Join-Path $dir $Name
            if (Test-Path $candidate) { return $candidate }
        }
        return $null
    }

    $frameworkNames = @('System.dll','System.Core.dll','System.Net.Http.dll','System.Xml.dll','System.Xml.Linq.dll',
        'System.Data.dll','System.Data.DataSetExtensions.dll','System.Drawing.dll',
        'System.Windows.Forms.dll','System.Xaml.dll','PresentationFramework.dll',
        'PresentationCore.dll','WindowsBase.dll')
    $frameworkRefs = @()
    $missingRefs = @()
    foreach ($name in $frameworkNames) {
        $found = Find-FrameworkReference $name
        if ($found) { $frameworkRefs += $found } else { $missingRefs += $name }
    }
    if ($missingRefs.Count -gt 0) {
        Fail "Referências do .NET Framework/WPF ausentes: $($missingRefs -join ', '). Instale o '.NET Framework 4.6.2 targeting pack' no Visual Studio Installer."
    }
    $refs = @($sdk, $json) + $frameworkRefs

    $compilerArgs = @('/nologo','/target:library','/platform:anycpu','/optimize+','/debug:pdbonly','/langversion:7.3',"/out:$Out\YgpPlaytimeSync.dll")
    foreach ($r in $refs) { $compilerArgs += "/reference:$r" }
    $compilerArgs += $sources

    Write-Host "Compiling $($sources.Count) source files..."
    $compilerOutput = & $csc @compilerArgs 2>&1 | ForEach-Object { "$_" }
    $compileExit = $LASTEXITCODE
    $compilerOutput | ForEach-Object { Write-Host $_ }
    if ($compilerOutput) { Add-Content $Log $compilerOutput }
    if ($compileExit -ne 0) { Fail "O compilador retornou código $compileExit." }

    Copy-Item (Join-Path $Project "extension.yaml") $Out -Force
    New-Item (Join-Path $Out "Resources") -ItemType Directory -Force | Out-Null
    Copy-Item (Join-Path $Project "Resources\pluginicon.png") (Join-Path $Out "Resources\pluginicon.png") -Force
    if ($shipJson) { Copy-Item $json $Out -Force }

    # Pasta limpa só com o que o Playnite precisa; é dela que o .pext é gerado
    # (a pasta Release acumula cópias "<guid>_YgpPlaytimeSync.dll" criadas pelo Playnite em modo desenvolvedor).
    $Stage = Join-Path $Root "bin\Stage"
    if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force -ErrorAction SilentlyContinue }
    New-Item $Stage -ItemType Directory -Force | Out-Null
    New-Item (Join-Path $Stage "Resources") -ItemType Directory -Force | Out-Null
    Copy-Item (Join-Path $Out "YgpPlaytimeSync.dll") $Stage -Force
    Copy-Item (Join-Path $Out "extension.yaml") $Stage -Force
    Copy-Item (Join-Path $Out "Resources\pluginicon.png") (Join-Path $Stage "Resources\pluginicon.png") -Force
    if ($shipJson) { Copy-Item $json $Stage -Force }

    # Gera o .pext (um zip com extension.yaml na raiz) em bin\Packages.
    $yaml = Get-Content (Join-Path $Project "extension.yaml") -Raw
    $extId = ([regex]::Match($yaml, '(?m)^Id:\s*(\S+)')).Groups[1].Value
    $extVer = $Version
    $PkgDir = Join-Path $Root "bin\Packages"
    New-Item $PkgDir -ItemType Directory -Force | Out-Null
    $pext = Join-Path $PkgDir ("ygp-playtime-sync-v{0}.pext" -f $extVer)
    if (Test-Path $pext) { Remove-Item $pext -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($Stage, $pext)
    Write-Host "PEXT     : $pext"
    Add-Content $Log "PEXT=$pext"

    Write-Host ""
    Write-Host "BUILD OK" -ForegroundColor Green
    Write-Host "Output: $Out"
    Write-Host "DLL: $(Join-Path $Out 'YgpPlaytimeSync.dll')"
    exit 0
}
catch {
    $_ | Out-String | Add-Content $Log
    Fail $_.Exception.Message
}
