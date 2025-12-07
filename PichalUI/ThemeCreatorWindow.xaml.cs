using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PichalUI
{
    public partial class ThemeCreatorWindow : Window
    {
        // Guarda as 5 cores do tema atual
        private Color[] themeColors = new Color[6];
        private int currentSlot = 0; // Qual cor estamos a editar agora (0 a 4)
        private bool isLoading = false; // Para evitar loops infinitos ao atualizar sliders

        // Referência à janela principal para aplicar em tempo real
        private GameLauncherWindow _mainWindow;

        public ThemeCreatorWindow(GameLauncherWindow main)
        {
            InitializeComponent();
            _mainWindow = main;

            // Inicializa com cores padrão ou tenta ler do tema atual
            // (Aqui estou a pôr cores padrão para começar)
            themeColors[0] = (Color)ColorConverter.ConvertFromString("#FF4758");   // Accent
            themeColors[1] = Colors.Gray;                                          // Text Sec
            themeColors[2] = (Color)ColorConverter.ConvertFromString("#1A0A0D");   // BG Top
            themeColors[3] = (Color)ColorConverter.ConvertFromString("#A81826");   // BG Bot
            themeColors[4] = (Color)ColorConverter.ConvertFromString("#D9101010"); // Panel
            themeColors[5] = Colors.White;

            // Atualiza a UI para o Slot 0
            UpdateUIFromColor(themeColors[0]);
            UpdatePreviewButtons();
            GenerateCodeString();
        }

        // Quando clica num dos 5 botões da esquerda
        private void Slot_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag.ToString(), out int slot))
            {
                currentSlot = slot;
                UpdateUIFromColor(themeColors[currentSlot]);
            }
        }

        // Quando mexe nos sliders
        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isLoading || ChkLivePreview == null) return;

            try
            {
                byte a = (byte)SliderA.Value;
                byte r = (byte)SliderR.Value;
                byte g = (byte)SliderG.Value;
                byte b = (byte)SliderB.Value;

                Color newColor = Color.FromArgb(a, r, g, b);

                // 1. Atualiza a memória local
                themeColors[currentSlot] = newColor;

                // 2. Atualiza a caixinha pequena de preview
                PreviewBox.Background = new SolidColorBrush(newColor);

                // 3. Atualiza os botões da esquerda
                UpdatePreviewButtons();

                // 4. Gera o código de texto
                GenerateCodeString();

                // --- NOVO: LIVE PREVIEW ---
                // Se a checkbox estiver marcada, atualiza o Launcher IMEDIATAMENTE
                if (ChkLivePreview.IsChecked == true && _mainWindow != null)
                {
                    _mainWindow.UpdateSingleThemeColor(currentSlot, newColor);
                }
            }
            catch { }
        }

        // Atualiza os sliders com base na cor guardada
        void UpdateUIFromColor(Color c)
        {
            isLoading = true;
            SliderA.Value = c.A;
            SliderR.Value = c.R;
            SliderG.Value = c.G;
            SliderB.Value = c.B;
            PreviewBox.Background = new SolidColorBrush(c);
            isLoading = false;
        }

        // Pinta os botões da esquerda com as cores atuais
        void UpdatePreviewButtons()
        {
            SetBtnBg(BtnSlot1, themeColors[0]);
            SetBtnBg(BtnSlot2, themeColors[1]);
            SetBtnBg(BtnSlot3, themeColors[2]);
            SetBtnBg(BtnSlot4, themeColors[3]);
            SetBtnBg(BtnSlot5, themeColors[4]);
            SetBtnBg(BtnSlot6, themeColors[5]);
        }

        void SetBtnBg(Button btn, Color c)
        {
            // Cria um gradiente pequeno para se notar a cor
            btn.Background = new SolidColorBrush(c);
            // Borda branca se for o selecionado
            btn.BorderBrush = (int.Parse(btn.Tag.ToString()) == currentSlot) ? Brushes.White : Brushes.Transparent;
            btn.BorderThickness = new Thickness((int.Parse(btn.Tag.ToString()) == currentSlot) ? 2 : 0);
        }

        // Gera a String Mágica
        void GenerateCodeString()
        {
            string code = "";
            for (int i = 0; i < 6; i++)
            {
                code += ColorToHex(themeColors[i]);
                if (i < 5) code += ",";
            }
            TxtResultCode.Text = code;
        }

        string ColorToHex(Color c)
        {
            return c.A.ToString("X2") + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(TxtResultCode.Text);
            MessageBox.Show("Código copiado! Podes guardar num bloco de notas ou enviar ao teu amigo.");
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            // Chama o método da janela principal para aplicar
            _mainWindow.ThemeEditor(TxtResultCode.Text);
        }
    }
}