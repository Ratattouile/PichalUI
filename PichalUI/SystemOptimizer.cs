using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace PichalUI
{
    public static class SystemOptimizer
    {
        // --- API NATIVA WINDOWS ---
        [DllImport("psapi.dll")] static extern int EmptyWorkingSet(IntPtr hwProc);
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")] public static extern uint TimeBeginPeriod(uint uPeriod);
        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")] public static extern uint TimeEndPeriod(uint uPeriod);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", SetLastError = true)] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // --- CONFIGURAÇÃO ---
        private static readonly string[] ServicesToPause = {
            "WSearch", "Themes", "Spooler", "DiagTrack", "PcaSvc", "SysMain", "MapsBroker", "lfsvc", "Fax", "WerSvc"
        };

        private static List<string> _pausedServices = new List<string>();
        private static bool _explorerKilled = false;

        // ---------------------------------------------------------
        // 1. OTIMIZAR
        // ---------------------------------------------------------
        public static void Optimize()
        {
            try
            {
                TimeBeginPeriod(1);

                // Tenta os planos de energia...
                RunPowerCfg("/s e9a42b02-d5df-448d-aa00-03f14749eb61");
                RunPowerCfg("/s 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

                using (Process p = Process.GetCurrentProcess())
                {
                    // 1. Prioridade Mínima
                    p.PriorityClass = ProcessPriorityClass.Idle;

                    // 2. AFINIDADE DE CPU (NOVO):
                    // Força o Launcher a rodar APENAS no último núcleo do processador.
                    // Ex: Se tens 8 cores, o Launcher só pode usar o Core 8.
                    // O jogo fica com os Cores 1-7 livres.
                    long affinityMask = (long)p.ProcessorAffinity;
                    // Pega no último bit disponível (último core)
                    long lastCore = 1L << (Environment.ProcessorCount - 1);
                    p.ProcessorAffinity = (IntPtr)lastCore;
                }

                PauseServices();
                KillExplorer();
                FlushLauncherMemory();
            }
            catch (Exception ex)
            {
                // Dica: Se isto falhar, é porque não tens Admin ou o CPU não permite.
                System.Diagnostics.Debug.WriteLine("Erro Optimize: " + ex.Message);
            }
        }

        // ---------------------------------------------------------
        // 2. BOOST INTELIGENTE (Procura o jogo ativo)
        // ---------------------------------------------------------
        public static async Task MonitorAndBoostActiveGame()
        {
            await Task.Run(async () =>
            {
                int attempts = 0;
                int myPid = Process.GetCurrentProcess().Id;
                uint gamePid = 0;

                // Tenta durante 30 segundos encontrar uma janela que NÃO seja o nosso launcher
                while (attempts < 15)
                {
                    await Task.Delay(2000);

                    IntPtr hwnd = GetForegroundWindow();
                    if (hwnd == IntPtr.Zero) continue;

                    GetWindowThreadProcessId(hwnd, out uint pid);

                    // Se a janela ativa não for o Launcher nem o Explorer (que já matámos), é o jogo!
                    if (pid != 0 && pid != myPid)
                    {
                        try
                        {
                            var proc = Process.GetProcessById((int)pid);

                            // Ignora processos de sistema
                            if (proc.ProcessName == "Taskmgr" || proc.ProcessName == "cmd") continue;

                            // APLICA O BOOST
                            proc.PriorityClass = ProcessPriorityClass.High;
                            gamePid = pid;
                            break; // Encontrou e otimizou!
                        }
                        catch { }
                    }
                    attempts++;
                }
            });
        }

        // ---------------------------------------------------------
        // 3. RESTAURAR
        // ---------------------------------------------------------
        public static void Restore()
        {
            try
            {
                TimeEndPeriod(1);
                RestoreExplorer();
                ResumeServices();
                RunPowerCfg("/s 381b4222-f694-41f0-9685-ff5bb260df2e");

                using (Process p = Process.GetCurrentProcess())
                {
                    p.PriorityClass = ProcessPriorityClass.Normal;

                    // Restaura para usar TODOS os núcleos
                    p.ProcessorAffinity = (IntPtr)((1L << Environment.ProcessorCount) - 1);
                }

                FlushLauncherMemory();
            }
            catch { }
        }

        // --- MÉTODOS INTERNOS (Mantidos iguais aos anteriores, mas limpos) ---
        private static void KillExplorer()
        {
            try { foreach (var p in Process.GetProcessesByName("explorer")) p.Kill(); _explorerKilled = true; } catch { _explorerKilled = false; }
        }

        private static void RestoreExplorer()
        {
            if (Process.GetProcessesByName("explorer").Length == 0)
                try { Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")); } catch { }
            _explorerKilled = false;
        }

        private static void PauseServices()
        {
            _pausedServices.Clear();
            foreach (var name in ServicesToPause)
            {
                try
                {
                    using (var sc = new ServiceController(name))
                    {
                        if (sc.Status == ServiceControllerStatus.Running && sc.CanStop) { sc.Stop(); _pausedServices.Add(name); }
                    }
                }
                catch { }
            }
        }

        private static void ResumeServices()
        {
            foreach (var name in _pausedServices)
            {
                try { using (var sc = new ServiceController(name)) if (sc.Status == ServiceControllerStatus.Stopped) sc.Start(); } catch { }
            }
            _pausedServices.Clear();
        }

        private static void FlushLauncherMemory()
        {
            try
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                if (Environment.OSVersion.Platform == PlatformID.Win32NT) EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }
            catch { }
        }

        private static void RunPowerCfg(string args)
        {
            try
            {
                var p = new ProcessStartInfo("powercfg", args) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                Process.Start(p)?.WaitForExit(500);
            }
            catch { }
        }

        public static void ForceHighPerformanceGPU(string gameExePath)
        {
            if (string.IsNullOrEmpty(gameExePath)) return;

            try
            {
                // Registo do Windows 10/11 para Preferência Gráfica
                // 0 = Let Windows Decide, 1 = Power Saving, 2 = High Performance
                string keyName = @"Software\Microsoft\DirectX\UserGpuPreferences";
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyName, true) ?? Registry.CurrentUser.CreateSubKey(keyName))
                {
                    if (key != null)
                    {
                        // Valor: "GpuPreference=2;"
                        key.SetValue(gameExePath, "GpuPreference=2;");
                    }
                }
            }
            catch { }
        }
    }
}