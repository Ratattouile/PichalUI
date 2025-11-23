using LibreHardwareMonitor.Hardware;
using System;
using System.Linq;

namespace PichalUI
{
    public class HardwareMonitor
    {
        private Computer computer;

        public HardwareMonitor()
        {
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true, // Para ler RAM real se quiseres
                // IsMotherboardEnabled = true, // Necessário para alguns sensores específicos
            };
            
            try
            {
                computer.Open();
            }
            catch (Exception)
            {
                // Se falhar (falta de permissão admin), não crasha a app
            }
        }

        public (string cpuName, string gpuName, string ramInfo) GetInfo()
        {
            string cpu = "Processador n/d";
            string gpu = "Gráfica n/d";
            string ram = "";

            try
            {
                foreach (var hw in computer.Hardware)
                {
                    // hw.Update(); // Só precisas disto se fores ler Temperaturas/Load em tempo real

                    if (hw.HardwareType == HardwareType.Cpu)
                    {
                        cpu = hw.Name;
                    }
                    else if (hw.HardwareType == HardwareType.GpuNvidia || 
                             hw.HardwareType == HardwareType.GpuAmd || 
                             hw.HardwareType == HardwareType.GpuIntel)
                    {
                        gpu = hw.Name;
                    }
                    else if (hw.HardwareType == HardwareType.Memory)
                    {
                        // Exemplo de como ler RAM Total (soma dos pentes)
                        // Nota: Para tamanho total simples, a classe nativa do Windows é melhor,
                        // mas aqui ficas com o nome do controlador de memória.
                        ram = "RAM Detetada"; 
                    }
                }
            }
            catch { }

            return (cpu, gpu, ram);
        }

        public void Close()
        {
            computer.Close();
        }
    }
}