using System;
using System.Windows;

namespace PichalUI
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // abrir manualmente para podermos try/catch e logar
            try
            {
                var w = new GameLauncherWindow();
                w.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao criar janela: " + ex.Message + "\\n" + ex.StackTrace);
                Environment.Exit(1);
            }
        }
    }
}
