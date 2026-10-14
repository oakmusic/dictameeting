using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DictaMeeting.Launcher
{
    static class Program
    {
        [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

        private const uint MB_OK = 0x00000000;
        private const uint MB_ICONERROR = 0x00000010;

        // Umbral para detectar terminación inmediata (posible bloqueo por EDR/WatchGuard)
        private const int ImmediateExitThresholdMs = 2000;
        private const int MaxLogSizeBytes = 256 * 1024; // 256 KB

        private static void ShowErrorMessage(string message, string title)
        {
            MessageBoxW(IntPtr.Zero, message, title, MB_OK | MB_ICONERROR);
        }

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string targetExe = Path.Combine(baseDir, "data", "DictaMeeting.exe");

                if (!File.Exists(targetExe))
                {
                    LogDiagnostic("[Launcher] ERROR: No se encontró el binario 'data\\DictaMeeting.exe'. Ruta buscada: " + targetExe);
                    ShowErrorMessage(
                        "No se encontró el ejecutable principal en la carpeta 'data':\n\n" +
                        targetExe + "\n\n" +
                        "Asegúrese de que el directorio 'data' no ha sido movido ni eliminado.",
                        "DictaMeeting — Error de inicio");
                    return;
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = targetExe,
                    WorkingDirectory = Path.Combine(baseDir, "data"),
                    UseShellExecute = true
                };

                if (args != null && args.Length > 0)
                {
                    psi.Arguments = FormatArguments(args);
                }

                Process? child = null;
                Stopwatch sw = Stopwatch.StartNew();

                try
                {
                    // Usamos UseShellExecute=false solo para obtener el objeto Process y monitorear
                    // el PID. Si falla (p.ej. por bloqueo de AppLocker), también lo capturamos.
                    // NOTA: UseShellExecute=true es necesario para que WPF pueda iniciar correctamente
                    // con sus propias asociaciones de shell. Lo mantenemos tal cual.
                    child = Process.Start(psi);
                }
                catch (Win32Exception win32Ex)
                {
                    sw.Stop();
                    // NativeErrorCode da el código de error Win32 exacto (más útil que el mensaje)
                    LogDiagnostic(string.Format(
                        "[Launcher] ERROR Win32 en Process.Start: NativeErrorCode={0} (0x{0:X}), Message={1}",
                        win32Ex.NativeErrorCode, win32Ex.Message));
                    LogDiagnostic("[Launcher] Posibles causas del error " + win32Ex.NativeErrorCode + ": " + DescribeWin32Error(win32Ex.NativeErrorCode));
                    LogTargetExeMetadata(targetExe);
                    ShowErrorMessage(
                        "Error al iniciar DictaMeeting (código Win32: " + win32Ex.NativeErrorCode + "):\n\n" +
                        win32Ex.Message + "\n\n" +
                        "Consulte %LOCALAPPDATA%\\DictaMeeting\\logs\\boot.log para más detalles.",
                        "DictaMeeting — Error de inicio");
                    return;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    LogDiagnostic("[Launcher] EXCEPCIÓN en Process.Start: " + ex.GetType().FullName + " - " + ex.Message + "\n" + ex.StackTrace);
                    LogTargetExeMetadata(targetExe);
                    ShowErrorMessage(
                        "Error al iniciar DictaMeeting:\n\n" + ex.Message,
                        "DictaMeeting — Error");
                    return;
                }

                if (child == null)
                {
                    sw.Stop();
                    // UseShellExecute=true puede devolver null en algunos escenarios (activación de URI)
                    // pero para un .exe local esto no debería ocurrir. Si ocurre, lo registramos.
                    LogDiagnostic("[Launcher] ADVERTENCIA: Process.Start devolvió null (proceso no monitoreado).");
                    return;
                }

                // Esperar brevemente para detectar terminación inmediata (señal de bloqueo por seguridad)
                // No bloqueamos el hilo principal más de ImmediateExitThresholdMs para no demorar el launcher
                bool exitedImmediately = child.WaitForExit(ImmediateExitThresholdMs);
                sw.Stop();

                if (exitedImmediately)
                {
                    int exitCode = -1;
                    try { exitCode = child.ExitCode; } catch { /* proceso puede no ser accesible */ }

                    LogDiagnostic(string.Format(
                        "[Launcher] ALERTA: El proceso terminó en {0}ms (< {1}ms). ExitCode={2} (0x{2:X}). " +
                        "Posible bloqueo por EDR/WatchGuard, AppLocker, o error de inicialización del runtime.",
                        sw.ElapsedMilliseconds, ImmediateExitThresholdMs, exitCode));
                    LogDiagnostic("[Launcher] ExitCode " + exitCode + ": " + DescribeExitCode(exitCode));
                    LogDiagnostic("[Launcher] ACCIÓN RECOMENDADA: Revisar el Visor de Eventos de Windows " +
                        "(eventvwr.msc > Registros de Windows > Aplicación y Sistema) " +
                        "y los logs de WatchGuard Endpoint Security en el equipo afectado.");
                    LogTargetExeMetadata(targetExe);
                }
                else
                {
                    LogDiagnostic(string.Format(
                        "[Launcher] Proceso principal iniciado (PID: {0}).",
                        child.Id));
                }
            }
            catch (Exception ex)
            {
                LogDiagnostic("[Launcher] EXCEPCIÓN no controlada: " + ex.GetType().FullName + " - " + ex.Message + "\n" + ex.StackTrace);
                ShowErrorMessage(
                    "Error al iniciar DictaMeeting:\n\n" + ex.Message,
                    "DictaMeeting — Error");
            }
        }

        /// <summary>
        /// Registra metadatos del ejecutable destino: tamaño, fecha de modificación y estado de firma.
        /// No lanza excepciones (el diagnóstico nunca debe bloquear el arranque).
        /// </summary>
        private static void LogTargetExeMetadata(string exePath)
        {
            try
            {
                var fi = new FileInfo(exePath);
                LogDiagnostic(string.Format(
                    "[Launcher] Metadatos destino: Tamaño={0} bytes, UltimaEscritura={1:yyyy-MM-dd HH:mm:ss}",
                    fi.Length, fi.LastWriteTime));

                // Verificar firma Authenticode via WinTrust (sin dependencias externas)
                // Usamos Get-AuthenticodeSignature equivalente en .NET via System.Security.Cryptography.X509Certificates
                // ya que WinVerifyTrust requiere P/Invoke complejo. Registramos si es posible.
                try
                {
                    var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(exePath);
                    LogDiagnostic("[Launcher] Firma Authenticode: Presente. Emisor=" + cert.Issuer);
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    LogDiagnostic("[Launcher] Firma Authenticode: NO PRESENTE (binario sin firmar). " +
                        "Este es el principal factor de riesgo para detecciones heurísticas de WatchGuard/EDR.");
                }
                catch
                {
                    // Si no podemos verificar, simplemente no registramos
                }
            }
            catch
            {
                // El diagnóstico nunca debe bloquear el arranque
            }
        }

        /// <summary>
        /// Devuelve una descripción legible del código de error Win32 más relevante para el contexto de lanzamiento.
        /// </summary>
        private static string DescribeWin32Error(int code)
        {
            return code switch
            {
                2 => "Archivo no encontrado (ERROR_FILE_NOT_FOUND). El ejecutable no existe en la ruta indicada.",
                3 => "Ruta no encontrada (ERROR_PATH_NOT_FOUND). El directorio no existe.",
                5 => "Acceso denegado (ERROR_ACCESS_DENIED). Puede indicar bloqueo por AppLocker, Software Restriction Policies o antivirus.",
                87 => "Parámetro inválido (ERROR_INVALID_PARAMETER).",
                193 => "No es un ejecutable Win32 válido (ERROR_BAD_EXE_FORMAT). Puede indicar corrupción o arquitectura incorrecta.",
                216 => "Aplicación de 32 bits no puede ejecutarse en modo 64 bits (ERROR_EXE_MACHINE_TYPE_MISMATCH).",
                225 => "Operación no completada porque el archivo contiene un virus u otro software potencialmente no deseado (ERROR_VIRUS_INFECTED). Indica bloqueo explícito del antivirus.",
                740 => "Se requiere elevación (ERROR_ELEVATION_REQUIRED).",
                1223 => "Operación cancelada por el usuario (ERROR_CANCELLED). SmartScreen u otro componente puede haberla detenido.",
                _ => "Código desconocido. Consultar https://docs.microsoft.com/en-us/windows/win32/debug/system-error-codes"
            };
        }

        /// <summary>
        /// Interpreta los códigos de salida más frecuentes del runtime .NET y de Windows.
        /// </summary>
        private static string DescribeExitCode(int code)
        {
            return code switch
            {
                0 => "Salida limpia (éxito).",
                -1 => "No se pudo obtener el código de salida.",
                1 => "Error genérico de la aplicación.",
                -1073741515 => "0xC0000135: DLL requerida no encontrada. Falta una dependencia nativa (whisper.dll, sherpa-onnx-c-api.dll, etc.).",
                -1073741502 => "0xC0000142: Error de inicialización de DLL. Una DLL nativa falló durante su inicialización.",
                -1073741701 => "0xC000007B: Imagen de aplicación incorrecta. Mezcla de arquitecturas x64/x86 o DLL corrupta.",
                -1073741819 => "0xC0000005: Infracción de acceso (Access Violation). Crash nativo en el runtime o en una DLL.",
                -1073740777 => "0xC0000FE7: Política de bloqueo. AppLocker o Software Restriction Policy ha bloqueado la ejecución.",
                -532462766 => "0xE0434352: Excepción no controlada en el CLR .NET.",
                _ => string.Format("Código 0x{0:X} ({0}). Ver Visor de Eventos > Aplicación para detalles.", code)
            };
        }

        private static void LogDiagnostic(string message)
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string logDir = Path.Combine(localAppData, "DictaMeeting", "logs");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
                string logFile = Path.Combine(logDir, "boot.log");

                RotateLog(logDir, logFile);

                string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}{2}", DateTime.Now, message, Environment.NewLine);
                File.AppendAllText(logFile, line);
            }
            catch
            {
                // El diagnóstico nunca debe bloquear la ejecución de la app
            }
        }

        private static void RotateLog(string logDir, string logFile)
        {
            try
            {
                if (!File.Exists(logFile))
                {
                    return;
                }

                var fi = new FileInfo(logFile);
                if (fi.Length < MaxLogSizeBytes)
                {
                    return;
                }

                string log1 = Path.Combine(logDir, "boot.1.log");
                string log2 = Path.Combine(logDir, "boot.2.log");
                string legacyOld = Path.Combine(logDir, "boot.old.log");

                try { if (File.Exists(legacyOld)) File.Delete(legacyOld); } catch { }
                try { if (File.Exists(log2)) File.Delete(log2); } catch { }
                try { if (File.Exists(log1)) File.Move(log1, log2, overwrite: true); } catch { }
                try { File.Move(logFile, log1, overwrite: true); } catch { }
            }
            catch
            {
                // La rotación nunca debe propagar excepciones
            }
        }

        private static string FormatArguments(string[] args)
        {
            string[] formatted = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a.Contains(" ") || a.Contains("\""))
                {
                    formatted[i] = "\"" + a.Replace("\"", "\\\"") + "\"";
                }
                else
                {
                    formatted[i] = a;
                }
            }
            return string.Join(" ", formatted);
        }
    }
}
