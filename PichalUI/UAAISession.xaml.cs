using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using PichalUI.Graphics;

namespace PichalUI
{
    public partial class UAAISession : Window
    {
        private CaptureEngine _engine;
        private SilkGameWindow _gameWindow;

        public UAAISession() { InitializeComponent(); }

        public async Task Start(Process gameProcess)
        {
            this.Hide();

            // Garante que temos o ID da janela do jogo
            IntPtr gameHwnd = gameProcess.MainWindowHandle;
            if (gameHwnd == IntPtr.Zero)
            {
                gameProcess.Refresh();
                gameHwnd = gameProcess.MainWindowHandle;
            }

            if (gameHwnd == IntPtr.Zero)
            {
                MessageBox.Show("Abre o jogo primeiro!");
                this.Show();
                return;
            }

            try
            {
                _engine = new CaptureEngine();

                // --- IMPORTANTE: Define o alvo ---
                _engine.GameHandle = gameHwnd;
                _engine.TargetProcessId = gameProcess.Id;

                _engine.Initialize();

                _gameWindow = new SilkGameWindow(_engine);
                _gameWindow.Run();

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro: {ex.Message}");
                this.Show();
            }
        }
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}