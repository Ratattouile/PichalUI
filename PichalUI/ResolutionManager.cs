using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;

namespace PichalUI
{
    public static class ResolutionManager
    {
        [DllImport("user32.dll")] public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);
        [DllImport("user32.dll")] public static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);

        public const int ENUM_CURRENT_SETTINGS = -1;
        public const int CDS_UPDATEREGISTRY = 0x01;
        public const int DISP_CHANGE_SUCCESSFUL = 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion; public short dmDriverVersion; public short dmSize; public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX; public int dmPositionY;
            public int dmDisplayOrientation; public int dmDisplayFixedOutput;
            public short dmColor; public short dmDuplex; public short dmYResolution; public short dmTTOption; public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels; public int dmBitsPerPel; public int dmPelsWidth; public int dmPelsHeight;
            public int dmDisplayFlags; public int dmDisplayFrequency;
            public int dmICMMethod; public int dmICMIntent; public int dmMediaType; public int dmDitherType;
            public int dmReserved1; public int dmReserved2; public int dmPanningWidth; public int dmPanningHeight;
        }

        private static DEVMODE? _originalResolution = null;

        public static void SaveCurrentResolution()
        {
            if (_originalResolution != null) return;
            DEVMODE dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm)) _originalResolution = dm;
        }

        // --- NOVO: Algoritmo de Escala Inteligente ---
        public static void ApplyScaling(float scaleFactor = 0.85f) // 0.85 é ~900p em ecrãs 1080p
        {
            if (_originalResolution == null) SaveCurrentResolution();
            var native = _originalResolution.Value;

            // Calcula a resolução alvo (Ex: 1920 * 0.85 = 1632)
            int targetW = (int)(native.dmPelsWidth * scaleFactor);
            int targetH = (int)(native.dmPelsHeight * scaleFactor);

            // Procura a resolução suportada MAIS PRÓXIMA da alvo
            // Isto evita erros se o monitor não suportar exatamente 1632x918
            DEVMODE bestMatch = new DEVMODE();
            int bestDiff = int.MaxValue;
            bool found = false;

            DEVMODE dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
            int modeNum = 0;

            while (EnumDisplaySettings(null, modeNum++, ref dm))
            {
                // Filtra apenas resoluções com a mesma taxa de atualização (Hz) e profundidade de cor
                if (dm.dmDisplayFrequency == native.dmDisplayFrequency && dm.dmBitsPerPel == native.dmBitsPerPel)
                {
                    // Calcula a diferença de pixels totais
                    int diff = Math.Abs((dm.dmPelsWidth * dm.dmPelsHeight) - (targetW * targetH));
                    
                    // Queremos uma resolução menor que a nativa, mas próxima do alvo
                    if (diff < bestDiff && dm.dmPelsWidth < native.dmPelsWidth)
                    {
                        bestDiff = diff;
                        bestMatch = dm;
                        found = true;
                    }
                }
            }

            if (found)
            {
                ChangeDisplaySettings(ref bestMatch, CDS_UPDATEREGISTRY);
            }
        }

        public static void RestoreResolution()
        {
            if (_originalResolution.HasValue)
            {
                DEVMODE dm = _originalResolution.Value;
                ChangeDisplaySettings(ref dm, CDS_UPDATEREGISTRY);
            }
        }
    }
}