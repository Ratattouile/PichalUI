using System;
using System.Diagnostics;
using System.IO;

namespace PichalUI
{
    public static class MagpieIntegrator
    {
        private static Process? _magpieProcess;

        public static bool IsAvailable()
        {
            return File.Exists(GetMagpiePath());
        }

        private static string GetMagpiePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "Magpie", "Magpie.exe");
        }

        public static void Start()
        {
            try
            {
                if (!IsAvailable()) return;

                Stop();

                var psi = new ProcessStartInfo
                {
                    FileName = GetMagpiePath(),
                    WorkingDirectory = Path.GetDirectoryName(GetMagpiePath()),
                    UseShellExecute = true,
                    Arguments = "/tray"
                };

                _magpieProcess = Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro Magpie: {ex.Message}");
            }
        }

        public static void Stop()
        {
            try
            {
                if (_magpieProcess != null && !_magpieProcess.HasExited)
                {
                    _magpieProcess.Kill();
                    _magpieProcess = null;
                }

                foreach (var p in Process.GetProcessesByName("Magpie"))
                {
                    p.Kill();
                }
            }
            catch
            {

            }
        }
    }
}