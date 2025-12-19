using System;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace PichalUI
{
    public static class NativeUtils
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        // --- APIS ANTIGAS ---
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        // --- APIS NOVAS PARA DPI E ESTILOS ---
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // Constantes para Click-Through
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        public static RECT GetGameRect(IntPtr hwnd)
        {
            // Método 1: Tenta obter a área de cliente (sem bordas)
            if (GetClientRect(hwnd, out var rect))
            {
                var ul = new POINT { X = rect.Left, Y = rect.Top };
                var lr = new POINT { X = rect.Right, Y = rect.Bottom };

                ClientToScreen(hwnd, ref ul);
                ClientToScreen(hwnd, ref lr);

                var finalRect = new RECT { Left = ul.X, Top = ul.Y, Right = lr.X, Bottom = lr.Y };

                // Validação Simples: Se a largura for < 10, algo correu mal, tenta retornar valores padrão
                if (finalRect.Width > 10 && finalRect.Height > 10)
                    return finalRect;
            }

            return new RECT(); // Retorna vazio se falhar
        }

        public static void SetClickThrough(IntPtr hwnd)
        {
            try
            {
                int initialStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, initialStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
            }
            catch { }
        }
    }
}