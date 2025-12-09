using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PichalUI
{
    public partial class ThemeCreatorWindow : Window
    {
        // VARIÁVEIS GLOBAIS
        private Color[] themeColors = new Color[15]; // AGORA SÃO 8 CORES
        private int currentSlot = 0;
        private int gradientType = 0; // 0=Linear, 1=Radial (CORRIGIDO)
        private bool isLoading = false;

        private GameLauncherWindow _mainWindow;

        public ThemeCreatorWindow(GameLauncherWindow main)
        {
            InitializeComponent();
            _mainWindow = main;

            // Preencher defaults (incluindo os novos slots)
            themeColors[0] = (Color)ColorConverter.ConvertFromString("#FF4758"); // Accent
            themeColors[1] = Colors.Gray;
            themeColors[2] = (Color)ColorConverter.ConvertFromString("#1A0A0D");
            themeColors[3] = (Color)ColorConverter.ConvertFromString("#A81826");
            themeColors[4] = (Color)ColorConverter.ConvertFromString("#D9101010");
            themeColors[5] = Colors.White;
            themeColors[6] = Colors.White; // Hero Title
            themeColors[7] = Colors.LightGray; // Chat Text
            themeColors[8] = Colors.LightGray;
            themeColors[9] = Colors.White; // Info Pri
            themeColors[10] = Colors.Gray; // Info Sec
            themeColors[11] = Colors.White; // Friend Name
            themeColors[12] = Colors.Gray;  // Friend Status
            themeColors[13] = Colors.White; // Button Normal
            themeColors[14] = Colors.Black;

            UpdateUIFromColor(themeColors[0]);
            UpdatePreviewButtons();
            GenerateCodeString();
        }

        private void Slot_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag.ToString(), out int slot))
            {
                currentSlot = slot;
                UpdateUIFromColor(themeColors[currentSlot]);
                UpdatePreviewButtons(); // Atualiza bordas dos botões
            }
        }

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

                themeColors[currentSlot] = newColor;
                if (PreviewBox != null) PreviewBox.Background = new SolidColorBrush(newColor);

                UpdatePreviewButtons();
                GenerateCodeString();

                if (ChkLivePreview.IsChecked == true && _mainWindow != null)
                {
                    _mainWindow.UpdateSingleThemeColor(currentSlot, newColor, gradientType);
                }
            }
            catch { }
        }

        void UpdateUIFromColor(Color c)
        {
            isLoading = true;
            if (SliderA != null) SliderA.Value = c.A;
            if (SliderR != null) SliderR.Value = c.R;
            if (SliderG != null) SliderG.Value = c.G;
            if (SliderB != null) SliderB.Value = c.B;
            if (PreviewBox != null) PreviewBox.Background = new SolidColorBrush(c);
            isLoading = false;
        }

        void UpdatePreviewButtons()
        {
            // Tens de mapear todos os botões novos aqui
            SetBtnBg(BtnSlot1, themeColors[0]); // Accent
            SetBtnBg(BtnSlot2, themeColors[1]); // Sec
            SetBtnBg(BtnSlot3, themeColors[2]); // BG1
            SetBtnBg(BtnSlot4, themeColors[3]); // BG2
            SetBtnBg(BtnSlot5, themeColors[4]); // Panel
            SetBtnBg(BtnSlot6, themeColors[5]); // Pri
            SetBtnBg(BtnSlot7, themeColors[6]); // Hero
            SetBtnBg(BtnSlot8, themeColors[7]); // Chat
            SetBtnBg(BtnSlot9, themeColors[8]); // Tert

            // Novos
            SetBtnBg(BtnSlot10, themeColors[9]);
            SetBtnBg(BtnSlot11, themeColors[10]);
            SetBtnBg(BtnSlot12, themeColors[11]);
            SetBtnBg(BtnSlot13, themeColors[12]);
            SetBtnBg(BtnSlot14, themeColors[13]);
            SetBtnBg(BtnSlot15, themeColors[14]);
        }

        void SetBtnBg(Button btn, Color c)
        {
            if (btn == null) return;
            btn.Background = new SolidColorBrush(c);
            // Borda branca se selecionado
            btn.BorderBrush = (int.Parse(btn.Tag.ToString()) == currentSlot) ? Brushes.White : Brushes.Transparent;
            btn.BorderThickness = new Thickness((int.Parse(btn.Tag.ToString()) == currentSlot) ? 2 : 0);
        }

        private void GradientType_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (CmbGradientType.SelectedItem is ComboBoxItem item)
            {
                gradientType = int.Parse(item.Tag.ToString());
                GenerateCodeString();

                if (ChkLivePreview?.IsChecked == true && _mainWindow != null)
                {
                    // Força update do fundo com o novo tipo de gradiente
                    _mainWindow.UpdateSingleThemeColor(-1, Colors.Transparent, gradientType);
                }
            }
        }

        void GenerateCodeString()
        {
            string code = "";
            for (int i = 0; i < 15; i++) // Loop até 15
            {
                code += ColorToHex(themeColors[i]) + ",";
            }
            code += gradientType.ToString();
            if (TxtResultCode != null) TxtResultCode.Text = code;
        }

        string ColorToHex(Color c)
        {
            return c.A.ToString("X2") + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (TxtResultCode != null) Clipboard.SetText(TxtResultCode.Text);
            MessageBox.Show("Código copiado!");
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (TxtResultCode != null) _mainWindow.ThemeEditor(TxtResultCode.Text);
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            if (TxtResultCode == null) return;

            string code = TxtResultCode.Text.Trim();
            string[] parts = code.Split(',');

            // Validação básica (tem de ter pelo menos 5 cores)
            if (parts.Length < 5)
            {
                MessageBox.Show("Código inválido ou incompleto.");
                return;
            }

            try
            {
                isLoading = true; // Pausa os eventos para não crashar enquanto carregamos

                // 1. Ler as cores do código para a memória local
                for (int i = 0; i < parts.Length; i++)
                {
                    string hex = parts[i].Trim();

                    // Se for o último e for curto, é o tipo de gradiente
                    if (i == parts.Length - 1 && hex.Length < 3)
                    {
                        if (int.TryParse(hex, out int type))
                        {
                            gradientType = type;
                            // Atualiza a ComboBox visualmente
                            if (CmbGradientType != null) CmbGradientType.SelectedIndex = type;
                        }
                        continue;
                    }

                    // Se for cor (8 digitos) e couber no array
                    if (hex.Length == 8 && i < themeColors.Length)
                    {
                        byte a = Convert.ToByte(hex.Substring(0, 2), 16);
                        byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                        byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                        byte b = Convert.ToByte(hex.Substring(6, 2), 16);
                        themeColors[i] = Color.FromArgb(a, r, g, b);
                    }
                }

                // 2. Atualizar os Botões da Esquerda (Preview)
                UpdatePreviewButtons();

                // 3. Atualizar os Sliders (com base na cor do slot que está selecionado agora)
                UpdateUIFromColor(themeColors[currentSlot]);

                // 4. Aplicar Preview na Janela Principal
                if (_mainWindow != null)
                {
                    // Envia o código completo para o motor principal processar tudo de uma vez
                    _mainWindow.ThemeEditor(code);
                }

                MessageBox.Show("Tema carregado com sucesso!");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao ler código: " + ex.Message);
            }
            finally
            {
                isLoading = false; // Volta a permitir edições
            }
        }
    }
}