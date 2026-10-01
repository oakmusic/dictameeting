using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("DictaMeeting")]
[assembly: AssemblyProduct("DictaMeeting")]
[assembly: AssemblyDescription("DictaMeeting — Transcriptor Local de Reuniones")]
[assembly: AssemblyCompany("Aritz Villodas")]
[assembly: AssemblyCopyright("© 2026 Aritz Villodas. Todos los derechos reservados.")]
[assembly: AssemblyVersion("1.5.2.0")]
[assembly: AssemblyFileVersion("1.5.2.0")]

namespace DictaMeeting.Launcher
{
    static class Program
    {
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
                    MessageBox.Show(
                        "No se encontró el ejecutable principal en la carpeta 'data':\n\n" +
                        targetExe + "\n\n" +
                        "Asegúrese de que el directorio 'data' no ha sido movido ni eliminado.",
                        "DictaMeeting — Error de inicio",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
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
                MessageBox.Show(
                    "Error al iniciar DictaMeeting:\n\n" + ex.Message,
                    "DictaMeeting — Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
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
