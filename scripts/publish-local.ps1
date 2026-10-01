param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$CertificatePath = $env:CODE_SIGN_CERTIFICATE,
    [string]$CertificatePassword = $env:CODE_SIGN_PASSWORD,
    [string]$TimestampServer = $env:CODE_SIGN_TIMESTAMP_URL
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = (Get-Item $ScriptDir).Parent.FullName
$PublishDir = Join-Path $RootDir "publish"
$DataDir = Join-Path $PublishDir "data"
$MeetingsDir = Join-Path $PublishDir "Meetings"
$LauncherSrc = Join-Path $RootDir "src\DictaMeeting.Launcher\Launcher.cs"
$LauncherManifest = Join-Path $RootDir "src\DictaMeeting.Launcher\app.manifest"
$AppIcon = Join-Path $RootDir "src\DictaMeeting.App\Resources\app.ico"
$ProjectFile = Join-Path $RootDir "src\DictaMeeting.App\DictaMeeting.App.csproj"

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "  DictaMeeting - Compilando entorno de pruebas 'publish/'" -ForegroundColor White
Write-Host "  Runtime: $Runtime | Configuracion: $Configuration" -ForegroundColor DarkGray
Write-Host "=================================================================" -ForegroundColor Cyan

# 1. Detener procesos previos
Get-Process -Name "DictaMeeting" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 200

# 2. Preparar directorios
if (-not (Test-Path $PublishDir)) {
    New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
}
if (-not (Test-Path $DataDir)) {
    New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
}
if (-not (Test-Path $MeetingsDir)) {
    New-Item -ItemType Directory -Path $MeetingsDir -Force | Out-Null
}

# 3. Publicar aplicacion autonoma en publish\data
Write-Host ""
Write-Host "[1/4] Publicando aplicacion autonoma en publish\data..." -ForegroundColor Yellow
& dotnet publish $ProjectFile -c $Configuration -r $Runtime --self-contained true -p:PublishReadyToRun=true -p:PublishSingleFile=false -o $DataDir --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Fallo dotnet publish"
}

# Limpieza de artefactos innecesarios (createdump, pdb, lib)
$DumpExe = Join-Path $DataDir "createdump.exe"
if (Test-Path $DumpExe) {
    Remove-Item $DumpExe -Force -ErrorAction SilentlyContinue
}
Get-ChildItem -Path $DataDir -Filter "*.pdb" | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $DataDir -Filter "*.lib" | Remove-Item -Force -ErrorAction SilentlyContinue

# 4. Copiar avisos legales y licencia
$LicenseFile = Join-Path $RootDir "LICENSE"
if (Test-Path $LicenseFile) {
    Copy-Item $LicenseFile -Destination $DataDir -Force
}
$NoticesFile = Join-Path $RootDir "THIRD-PARTY-NOTICES.md"
if (Test-Path $NoticesFile) {
    Copy-Item $NoticesFile -Destination $DataDir -Force
}
$NoticesEnFile = Join-Path $RootDir "THIRD-PARTY-NOTICES.en.md"
if (Test-Path $NoticesEnFile) {
    Copy-Item $NoticesEnFile -Destination $DataDir -Force
}

# 4.1 Asegurar modelos de Silero VAD en data/models/silero
$ModelsDir = Join-Path $RootDir "models\silero"
$TargetModelsDir = Join-Path $DataDir "models\silero"
if (Test-Path $ModelsDir) {
    if (-not (Test-Path $TargetModelsDir)) {
        New-Item -ItemType Directory -Path $TargetModelsDir -Force | Out-Null
    }
    Copy-Item (Join-Path $ModelsDir "*") -Destination $TargetModelsDir -Recurse -Force
}

# 4.2 Asegurar modelos de Restauracion de Puntuacion en data/models/punctuation
$PunctModelsDir = Join-Path $RootDir "models\punctuation"
$TargetPunctDir = Join-Path $DataDir "models\punctuation"
if (Test-Path $PunctModelsDir) {
    if (-not (Test-Path $TargetPunctDir)) {
        New-Item -ItemType Directory -Path $TargetPunctDir -Force | Out-Null
    }
    Copy-Item (Join-Path $PunctModelsDir "*") -Destination $TargetPunctDir -Recurse -Force
}

# 4.3 Asegurar modelos de Diarización PyAnnote Community-1 en data/models/diarization
$DiarizationModelsDir = Join-Path $RootDir "models\diarization"
$TargetDiarizationDir = Join-Path $DataDir "models\diarization"
if (Test-Path $DiarizationModelsDir) {
    if (-not (Test-Path $TargetDiarizationDir)) {
        New-Item -ItemType Directory -Path $TargetDiarizationDir -Force | Out-Null
    }
    Copy-Item (Join-Path $DiarizationModelsDir "*") -Destination $TargetDiarizationDir -Recurse -Force
}

# 5. Asegurar DLLs nativas
Write-Host "[2/4] Verificando librerias nativas x64..." -ForegroundColor Yellow
$NativeWinX64 = Join-Path $DataDir "runtimes\win-x64"
if (Test-Path $NativeWinX64) {
    $nativeDlls = Get-ChildItem -Path $NativeWinX64 -Recurse -Filter "*.dll"
    foreach ($dll in $nativeDlls) {
        if ($dll.Directory.Name -in @('avx', 'avx2', 'avx512', 'noavx')) {
            continue
        }
        Copy-Item $dll.FullName -Destination $DataDir -Force
    }
}

# 6. Compilar lanzador raiz con icono y manifiesto
Write-Host "[3/4] Compilando lanzador raiz 'DictaMeeting.exe' con manifiesto Win32..." -ForegroundColor Yellow
$CscExe = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $CscExe)) {
    $CscExe = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
$ExePath = Join-Path $PublishDir "DictaMeeting.exe"
$CscArgs = @(
    "/target:winexe",
    "/optimize+",
    "/platform:x64",
    "/win32icon:$AppIcon"
)
if (Test-Path $LauncherManifest) {
    $CscArgs += "/win32manifest:$LauncherManifest"
}
$CscArgs += "/out:$ExePath"
$CscArgs += "$LauncherSrc"

& $CscExe $CscArgs
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $ExePath)) {
    Write-Error "Fallo compilacion del lanzador"
}

# 7. Finalizar
Write-Host "[4/4] Finalizando..." -ForegroundColor Yellow
$ReadmeFile = Join-Path $MeetingsDir "LEEME.txt"
if (-not (Test-Path $ReadmeFile)) {
    Set-Content -Path $ReadmeFile -Value "Carpeta de reuniones de DictaMeeting" -Encoding UTF8
}

Write-Host ""
Write-Host "[OK] Compilacion completada con exito en: $PublishDir" -ForegroundColor Green
Write-Host "     Ejecutable principal: $ExePath" -ForegroundColor White
