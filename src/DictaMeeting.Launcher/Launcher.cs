using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DictaMeeting.Launcher
{
    static class Program
    {
        [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

        private const uint MB_OK = 0x00000000;
        private const uint MB_ICONERROR = 0x00000010;

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

                LogDiagnostic("[Launcher] Iniciando subproceso principal: " + targetExe);
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                LogDiagnostic("[Launcher] EXCEPCIÓN al invocar subproceso: " + ex.GetType().FullName + " - " + ex.Message + "\n" + ex.StackTrace);
                ShowErrorMessage(
                    "Error al iniciar DictaMeeting:\n\n" + ex.Message,
                    "DictaMeeting — Error");
            }
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
                string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}{2}", DateTime.Now, message, Environment.NewLine);
                File.AppendAllText(logFile, line);
            }
            catch
            {
                // El diagnóstico nunca debe bloquear la ejecución de la app
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
