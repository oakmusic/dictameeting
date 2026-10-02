# =====================================================================
# DictaMeeting — Script de Compilación y Distribución para Windows
# Genera la distribución autónoma (self-contained win-x64),
# el archivo portable (.zip) y el instalador (.exe con Inno Setup).
# =====================================================================

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.5.4",
    [switch]$SkipTests = $false,
    [string]$CertificatePath = $env:CODE_SIGN_CERTIFICATE,
    [string]$CertificatePassword = $env:CODE_SIGN_PASSWORD,
    [string]$TimestampServer = $env:CODE_SIGN_TIMESTAMP_URL
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = (Get-Item $ScriptDir).Parent.FullName
$DistDir = Join-Path $RootDir "dist"
$PublishDir = Join-Path $DistDir "publish-$Runtime"
$DataDir = Join-Path $PublishDir "data"
$MeetingsDir = Join-Path $PublishDir "Meetings"
$LauncherSrc = Join-Path $RootDir "src\DictaMeeting.Launcher\Launcher.cs"
$LauncherManifest = Join-Path $RootDir "src\DictaMeeting.Launcher\app.manifest"
$AppIcon = Join-Path $RootDir "src\DictaMeeting.App\Resources\app.ico"
$InstallerDir = Join-Path $DistDir "installer"
$ZipFile = Join-Path $DistDir "DictaMeeting-v$Version-$Runtime-portable.zip"
$ProjectFile = Join-Path $RootDir "src\DictaMeeting.App\DictaMeeting.App.csproj"
$SolutionFile = Join-Path $RootDir "DictaMeeting.sln"
$InnoScript = Join-Path $RootDir "installer\DictaMeeting-Setup.iss"

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "  DictaMeeting - Construccion de Distribucion (v$Version)" -ForegroundColor White
Write-Host "  Runtime: $Runtime | Configuracion: $Configuration" -ForegroundColor DarkGray
Write-Host "=================================================================" -ForegroundColor Cyan

# Función para firma digital Authenticode
function Invoke-CodeSigning {
    param(
        [string[]]$Targets,
        [string]$CertPath,
        [string]$CertPass,
        [string]$Timestamp
    )

    if (-not $CertPath -or -not (Test-Path $CertPath)) {
        Write-Host "  [INFO] Firma digital no configurada (CODE_SIGN_CERTIFICATE vacio o no encontrado)." -ForegroundColor DarkGray
        Write-Host "         Los binarios se distribuiran sin firma Authenticode." -ForegroundColor DarkGray
        Write-Host "         Para firmar digitalmente en produccion/CI, defina `$env:CODE_SIGN_CERTIFICATE y `$env:CODE_SIGN_PASSWORD." -ForegroundColor DarkGray
        return
    }

    if (-not $Timestamp) {
        $Timestamp = "http://timestamp.digicert.com"
    }

    # Buscar signtool.exe en el sistema
    $SignTool = $null
    $SdkPaths = @(
        "signtool.exe",
        "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe",
        "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\signtool.exe",
        "C:\Program Files\Microsoft Visual Studio\*\*\bin\Hostx64\x64\signtool.exe"
    )
    foreach ($p in $SdkPaths) {
        if (Get-Command $p -ErrorAction SilentlyContinue) {
            $SignTool = $p
            break
        }
        $found = Get-ChildItem -Path (Split-Path $p -Parent) -Filter (Split-Path $p -Leaf) -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            $SignTool = $found.FullName
            break
        }
    }

    if (-not $SignTool) {
        Write-Warning "signtool.exe no se encontro en el sistema. Los binarios no pudieron firmarse."
        return
    }

    Write-Host "  [Firma Digital] Firmando con $SignTool..." -ForegroundColor Cyan
    foreach ($target in $Targets) {
        if (Test-Path $target) {
            Write-Host "    -> Firmando: $(Split-Path $target -Leaf)" -ForegroundColor Gray
            if ($CertPass) {
                & $SignTool sign /f "$CertPath" /p "$CertPass" /fd SHA256 /tr "$Timestamp" /td SHA256 /d "DictaMeeting" "$target"
            } else {
                & $SignTool sign /f "$CertPath" /fd SHA256 /tr "$Timestamp" /td SHA256 /d "DictaMeeting" "$target"
            }
        }
    }
    Write-Host "  [OK] Binarios firmados digitalmente con Authenticode (SHA-256)." -ForegroundColor Green
}

# 1. Ejecutar tests unitarios para verificar integridad
if (-not $SkipTests) {
    Write-Host ""
    Write-Host "[1/5] Ejecutando suite completa de pruebas automatizadas..." -ForegroundColor Yellow
    & dotnet test $SolutionFile -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Las pruebas unitarias fallaron. Cancelando empaquetado."
    }
    Write-Host "[OK] 100% de las pruebas pasadas con exito." -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "[1/5] Omitiendo pruebas (--SkipTests especificado)." -ForegroundColor DarkGray
}

# 2. Limpiar directorios de distribucion previos
Write-Host ""
Write-Host "[2/5] Preparando directorios de destino..." -ForegroundColor Yellow
Get-Process -Name "DictaMeeting" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 200
if (Test-Path $DistDir) {
    Remove-Item -Path $DistDir -Recurse -Force
}
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
New-Item -ItemType Directory -Path $MeetingsDir -Force | Out-Null
New-Item -ItemType Directory -Path $InstallerDir -Force | Out-Null
Write-Host "[OK] Directorio '$DistDir' preparado." -ForegroundColor Green

# 3. Publicar aplicacion autonoma (Self-Contained win-x64) en subdirectorio 'data'
Write-Host ""
Write-Host "[3/5] Publicando binarios autonomos en subdirectorio 'data'..." -ForegroundColor Yellow
& dotnet publish $ProjectFile -c $Configuration -r $Runtime --self-contained true -p:PublishReadyToRun=true -p:PublishSingleFile=false -o $DataDir --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Error "Fallo durante la publicacion de DictaMeeting."
}

# Limpieza de artefactos de desarrollo e innecesarios para el usuario final:
# createdump.exe suele disparar falsos positivos en EDRs (técnicas de volcado de memoria LSASS).
$DumpExe = Join-Path $DataDir "createdump.exe"
if (Test-Path $DumpExe) {
    Remove-Item $DumpExe -Force -ErrorAction SilentlyContinue
}
# Eliminar archivos .pdb y .lib en la distribución final
Get-ChildItem -Path $DataDir -Filter "*.pdb" | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $DataDir -Filter "*.lib" | Remove-Item -Force -ErrorAction SilentlyContinue

# Copiar avisos legales y licencias dentro de data/
$LicenseFile = Join-Path $RootDir "LICENSE"
if (Test-Path $LicenseFile) {
    Copy-Item $LicenseFile -Destination $DataDir
}
$NoticesFile = Join-Path $RootDir "THIRD-PARTY-NOTICES.md"
if (Test-Path $NoticesFile) {
    Copy-Item $NoticesFile -Destination $DataDir
}
$NoticesEnFile = Join-Path $RootDir "THIRD-PARTY-NOTICES.en.md"
if (Test-Path $NoticesEnFile) {
    Copy-Item $NoticesEnFile -Destination $DataDir
}

# Asegurar que las DLLs nativas de Whisper x64 esten en el directorio data/
$NativeWinX64 = Join-Path $DataDir "runtimes\win-x64"
if (Test-Path $NativeWinX64) {
    Copy-Item "$NativeWinX64\*.dll" -Destination $DataDir -Force
}

# Asegurar modelos integrados (Silero VAD y Punctuation) dentro de data/models/
$RootModelsDir = Join-Path $RootDir "models"
$TargetDataModels = Join-Path $DataDir "models"
if (Test-Path $RootModelsDir) {
    if (-not (Test-Path $TargetDataModels)) {
        New-Item -ItemType Directory -Path $TargetDataModels -Force | Out-Null
    }
    Copy-Item (Join-Path $RootModelsDir "*") -Destination $TargetDataModels -Recurse -Force
}

# Compilar lanzador principal en la raiz (DictaMeeting.exe) con icono y manifiesto incrustados
Write-Host "Compilando lanzador raiz 'DictaMeeting.exe' con icono y manifiesto Win32..." -ForegroundColor Yellow
$CscPaths = @(
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$CscExe = $null
foreach ($c in $CscPaths) {
    if (Test-Path $c) { $CscExe = $c; break }
}
if (-not $CscExe) {
    Write-Error "No se encontro el compilador de C# de Windows (csc.exe)."
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
    Write-Error "Fallo la compilacion del lanzador raiz '$ExePath'."
}

# Crear archivo informativo en la carpeta Meetings
$ReadmeContent = @"
=====================================================================
DICTAMEETING — CARPETA DE REUNIONES GUARDADAS
=====================================================================

En este directorio se almacenan automáticamente todas las sesiones
de reunión procesadas por DictaMeeting.

Para cada reunión se genera una subcarpeta con:
  - *_meeting.json    : Datos estructurados de la reunión y segmentos
  - *_transcript.md   : Transcripción completa formateada en Markdown
  - *_transcript.txt  : Transcripción en texto plano
  - *_acta.md         : Acta ejecutiva generada con Inteligencia Artificial
  - *_audio.mp3       : Grabación de audio de la sesión (si se activó)

=====================================================================
"@
Set-Content -Path (Join-Path $MeetingsDir "LEEME.txt") -Value $ReadmeContent -Encoding UTF8

# Firma digital de binarios si se dispone de certificado
$BinariesToSign = @($ExePath, (Join-Path $DataDir "DictaMeeting.exe"))
$AppDlls = Get-ChildItem -Path $DataDir -Filter "DictaMeeting*.dll" | ForEach-Object { $_.FullName }
if ($AppDlls) {
    $BinariesToSign += $AppDlls
}
Invoke-CodeSigning -Targets $BinariesToSign -CertPath $CertificatePath -CertPass $CertificatePassword -Timestamp $TimestampServer

Write-Host "[OK] Estructura limpia completada en: $PublishDir" -ForegroundColor Green
Write-Host "     - $ExePath (Lanzador con icono y manifiesto Win32)" -ForegroundColor Gray
Write-Host "     - $MeetingsDir (Carpeta de reuniones)" -ForegroundColor Gray
Write-Host "     - $DataDir (Binarios protegidos, sin createdump ni artefactos de depuracion)" -ForegroundColor Gray

# Sincronizar directorio de pruebas local publish/ con los binarios limpios
Get-Process -Name "DictaMeeting" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300
$RootPublish = Join-Path $RootDir "publish"
if (Test-Path $RootPublish) {
    Remove-Item -Path $RootPublish -Recurse -Force
}
New-Item -ItemType Directory -Path $RootPublish -Force | Out-Null
Copy-Item "$PublishDir\*" -Destination $RootPublish -Recurse -Force
Write-Host "[OK] Directorio local '$RootPublish' actualizado con la nueva estructura." -ForegroundColor Green

# 4. Generar paquete portable ZIP
Write-Host ""
Write-Host "[4/5] Empaquetando distribucion portable (.zip)..." -ForegroundColor Yellow
if (Test-Path $ZipFile) {
    Remove-Item $ZipFile -Force
}
# Empaquetado portable rápido (alternativa optimizada a Compress-Archive):
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($PublishDir, $ZipFile, [System.IO.Compression.CompressionLevel]::Fastest, $false)
$ZipItem = Get-Item $ZipFile
$ZipSizeMb = [math]::Round($ZipItem.Length / 1MB, 2)
Write-Host "[OK] Paquete portable generado: $ZipFile ($ZipSizeMb MB)" -ForegroundColor Green

# 5. Compilar instalador con Inno Setup si esta disponible
Write-Host ""
Write-Host "[5/5] Buscando compilador de Inno Setup (ISCC)..." -ForegroundColor Yellow
$IsccPaths = @(
    "iscc.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 7\iscc.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\iscc.exe",
    "${env:ProgramFiles}\Inno Setup 7\iscc.exe",
    "${env:ProgramFiles}\Inno Setup 6\iscc.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\iscc.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\iscc.exe"
)

$IsccExe = $null
foreach ($p in $IsccPaths) {
    if (Get-Command $p -ErrorAction SilentlyContinue) {
        $IsccExe = $p
        break
    }
    if (Test-Path $p) {
        $IsccExe = $p
        break
    }
}

if ($IsccExe) {
    Write-Host "Compilador Inno Setup detectado en: $IsccExe" -ForegroundColor Cyan
    Write-Host "Compilando instalador oficial de Windows..." -ForegroundColor Yellow
    & $IsccExe $InnoScript
    if ($LASTEXITCODE -eq 0) {
        $SetupExe = Join-Path $InstallerDir "DictaMeeting-v$Version-Setup.exe"
        if (Test-Path $SetupExe) {
            # Firmar el instalador si hay certificado
            Invoke-CodeSigning -Targets @($SetupExe) -CertPath $CertificatePath -CertPass $CertificatePassword -Timestamp $TimestampServer
            $SetupItem = Get-Item $SetupExe
            $SetupSizeMb = [math]::Round($SetupItem.Length / 1MB, 2)
            Write-Host "[OK] Instalador Windows generado: $SetupExe ($SetupSizeMb MB)" -ForegroundColor Green
        }
    } else {
        Write-Warning "Inno Setup finalizo con advertencias o error (codigo $LASTEXITCODE)."
    }
} else {
    Write-Host "Inno Setup 6 no esta instalado en las rutas predeterminadas." -ForegroundColor Cyan
    Write-Host "El script del instalador esta listo en: $InnoScript" -ForegroundColor DarkGray
    Write-Host "Para generar 'DictaMeeting-v$Version-Setup.exe', instale Inno Setup 6 y ejecute: iscc '$InnoScript'" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "  DISTRIBUCION COMPLETADA CON EXITO" -ForegroundColor Green
Write-Host "  Ejecutable directo: $ExePath" -ForegroundColor White
Write-Host "  Paquete Portable:   $ZipFile" -ForegroundColor White
Write-Host "=================================================================" -ForegroundColor Cyan
