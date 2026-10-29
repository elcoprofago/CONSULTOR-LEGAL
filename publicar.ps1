# Publica una release de CONSULTOR-LEGAL en GitHub: compila el .exe
# autocontenido de un solo archivo, crea la etiqueta v<Version> y la release
# con CONSULTOR-LEGAL.exe adjunto (es lo que baja el botón "Buscar
# actualización" de Configuración).
#
#   .\publicar.ps1 -Notas "Qué cambió"            # publica
#   .\publicar.ps1 -Notas "..." -SoloProbar       # todo menos etiqueta/push/release
#
# Se niega a publicar si: hay cambios sin commitear, no se está en master, la
# etiqueta ya existe (local o en GitHub), o la versión no es mayor que la
# última publicada. La versión sale de <Version> en CONSULTOR-LEGAL.csproj y se
# verifica contra la que quedó grabada en el .exe.

param(
    [Parameter(Mandatory = $true)][string]$Notas,
    [switch]$SoloProbar
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Fallar($msg) { Write-Host "ERROR: $msg" -ForegroundColor Red; exit 1 }
function Paso($msg) { Write-Host "-- $msg" -ForegroundColor Cyan }

# ---------- Versión ----------
[xml]$csproj = Get-Content (Join-Path $PSScriptRoot 'CONSULTOR-LEGAL.csproj') -Raw -Encoding UTF8
$version = ($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
if (-not $version -or $version -notmatch '^\d+\.\d+\.\d+$') { Fallar "No se encontró <Version>x.y.z</Version> en CONSULTOR-LEGAL.csproj." }
$etiqueta = "v$version"
Paso "Versión $version (etiqueta $etiqueta)"

# ---------- Estado de git ----------
$cambios = git status --porcelain
if ($LASTEXITCODE -ne 0) { Fallar "git status falló." }
if ($cambios) { Fallar "Hay cambios sin commitear:`n$($cambios -join "`n")" }

$rama = git rev-parse --abbrev-ref HEAD
if ($rama -ne 'master') { Fallar "Hay que publicar desde master (rama actual: $rama)." }

if (git tag --list $etiqueta) { Fallar "La etiqueta $etiqueta ya existe localmente." }
$remotas = git ls-remote --tags origin
if ($LASTEXITCODE -ne 0) { Fallar "No se pudo consultar las etiquetas de origin." }
if ($remotas -match "refs/tags/$([regex]::Escape($etiqueta))$") { Fallar "La etiqueta $etiqueta ya existe en GitHub." }

$publicadas = @($remotas | ForEach-Object { if ($_ -match 'refs/tags/v(\d+\.\d+\.\d+)$') { [version]$Matches[1] } })
if ($publicadas.Count -gt 0) {
    $ultima = ($publicadas | Sort-Object | Select-Object -Last 1)
    if ([version]$version -le $ultima) { Fallar "La versión $version no es mayor que la última publicada ($ultima). Subí <Version> en CONSULTOR-LEGAL.csproj." }
}

# ---------- Compilación ----------
$staging = Join-Path $PSScriptRoot "bin\Release\release\$etiqueta"
if (Test-Path $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }

Paso "Publicando en $staging"
dotnet publish (Join-Path $PSScriptRoot 'CONSULTOR-LEGAL.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:PublishDir="$staging\" -nologo -v q
if ($LASTEXITCODE -ne 0) { Fallar "dotnet publish falló (código $LASTEXITCODE)." }

# ---------- Verificación del resultado ----------
$exe = Join-Path $staging 'CONSULTOR-LEGAL.exe'
if (-not (Test-Path $exe)) { Fallar "No se generó CONSULTOR-LEGAL.exe." }
$fileVersion = (Get-Item $exe).VersionInfo.FileVersion
if ($fileVersion -ne "$version.0") { Fallar "El .exe quedó con versión $fileVersion, se esperaba $version.0." }
if (Test-Path (Join-Path $staging 'CONSULTOR-LEGAL.dll')) { Fallar "La publicación no quedó en un solo archivo (hay CONSULTOR-LEGAL.dll)." }
Paso "Exe verificado: $fileVersion ($([math]::Round((Get-Item $exe).Length / 1MB, 1)) MB)"

if ($SoloProbar) {
    Paso "SoloProbar: no se crea etiqueta, ni push, ni release."
    exit 0
}

# ---------- Etiqueta, push y release ----------
Paso "Etiqueta y push"
git tag -a $etiqueta -m "CONSULTOR-LEGAL $version"
if ($LASTEXITCODE -ne 0) { Fallar "No se pudo crear la etiqueta." }
git push origin master $etiqueta
if ($LASTEXITCODE -ne 0) { Fallar "git push falló (la etiqueta $etiqueta quedó solo local: 'git tag -d $etiqueta' para reintentar)." }

Paso "Release en GitHub"
# Las notas van por archivo: PowerShell 5.1 no escapa las comillas internas
# al pasar un argumento a un programa nativo.
$archivoNotas = Join-Path $env:TEMP "CONSULTOR-LEGAL-notas-$etiqueta.md"
[IO.File]::WriteAllText($archivoNotas, $Notas, (New-Object Text.UTF8Encoding $false))
gh release create $etiqueta $exe --title "CONSULTOR-LEGAL $version" --notes-file $archivoNotas
$codigoGh = $LASTEXITCODE
Remove-Item $archivoNotas -ErrorAction SilentlyContinue
if ($codigoGh -ne 0) { Fallar "gh release create falló (la etiqueta ya está en GitHub; reintentar solo este paso)." }

$assets = gh release view $etiqueta --json assets --jq '.assets[].name'
if ($LASTEXITCODE -ne 0 -or $assets -notcontains 'CONSULTOR-LEGAL.exe') { Fallar "La release se creó pero no se verificó el exe adjunto." }
Paso "Publicada $etiqueta con CONSULTOR-LEGAL.exe"
