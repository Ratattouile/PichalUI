using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using System.Net;
using System.Net.Sockets;
using DualSenseAPI;
using DualSenseAPI.State;
using System.Windows.Media.Animation;
using System.Xml;

namespace PichalUI
{
    // --- MODELOS DE DADOS ---
    public class GameEntry
    {
        public string Title { get; set; } = "";
        public string Source { get; set; } = "";
        public string SteamAppId { get; set; } = "";
        public string ExePath { get; set; } = "";
        public string WorkingDirectory { get; set; } = "";
        public ImageSource? Cover { get; set; }
        public ImageSource? Icon { get; set; }
        public float Scale = 1.0f;

        public string Description { get; set; } = "";
        public int ProgressPercent { get; set; } = 0;
        public List<string> Achievements { get; set; } = new List<string>();
        public double PlaytimeHours { get; set; } = 0.0;
        public DateTime? LastPlayed { get; set; }
        public string StorePlatform { get; set; } = "";
        public string StoreId { get; set; } = "";
    }

    public class FriendGameInfo
    {
        public string Name { get; set; } = "";
        public string AppId { get; set; } = "";
        public string CoverUrl => $"https://cdn.cloudflare.steamstatic.com/steam/apps/{AppId}/library_600x900.jpg";
        public string Playtime2Weeks { get; set; } = ""; // Ex: "4.5 hrs"
    }

    public class PlayerSummary
    {
        public string SteamId { get; set; } = "";
        public string PersonaName { get; set; } = "Unknown";
        public string AvatarFull { get; set; } = "";
        public string ProfileUrl { get; set; } = "";
        public int PersonaState { get; set; } = 0;
        public string GameExtraInfo { get; set; } = "";

        public string GameId { get; set; } = "";

        public string StatusText => !string.IsNullOrEmpty(GameExtraInfo) ? $"A jogar: {GameExtraInfo}" : (PersonaState > 0 ? "Online" : "Offline");

        public SolidColorBrush StatusColor
        {
            get
            {
                if (!string.IsNullOrEmpty(GameExtraInfo)) return Brushes.LightGreen;
                if (PersonaState > 0) return Brushes.SkyBlue;
                return Brushes.Gray;
            }
        }

        public string GameCoverUrl
        {
            get
            {
                if (!string.IsNullOrEmpty(GameId) && GameId != "0")
                {
                    // URL oficial da Steam para capas de biblioteca (600x900)
                    return $"https://cdn.cloudflare.steamstatic.com/steam/apps/{GameId}/library_600x900.jpg";
                }
                return ""; // Retorna vazio se não estiver a jogar
            }
        }

        // Propriedade para controlar se mostramos a imagem do jogo ou não
        public Visibility GameCoverVisibility => !string.IsNullOrEmpty(GameId) && GameId != "0" ? Visibility.Visible : Visibility.Collapsed;

        private string _mutualFriendsText = "A calcular...";
        public string MutualFriendsText
        {
            get => _mutualFriendsText;
            set { _mutualFriendsText = value; OnPropertyChanged("MutualFriendsText"); }
        }

        // Lista de jogos recentes (Observable para a UI atualizar sozinha)
        public System.Collections.ObjectModel.ObservableCollection<FriendGameInfo> RecentGames { get; set; }
            = new System.Collections.ObjectModel.ObservableCollection<FriendGameInfo>();

        // Boilerplate para a UI saber que os dados mudaram
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }

    // --- COMANDOS ---
    public class RelayCommand<T> : ICommand
    {
        readonly Action<T> _act;
        public RelayCommand(Action<T> a) { _act = a; }
        public bool CanExecute(object? parameter) => true;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public void Execute(object? parameter)
        {
            if (parameter is T t) _act(t);
            else if (parameter == null && default(T) == null) _act(default(T)!);
        }
    }

    // --- INPUT HANDLERS ---
    public interface IInputHandler
    {
        void OnLeft();
        void OnRight();
        void OnUp();
        void OnDown();
        void OnAccept();
        void OnCancel();
    }

    public class GamesInputHandler : IInputHandler
    {
        readonly GameLauncherWindow w;
        public GamesInputHandler(GameLauncherWindow window) { w = window; }
        public void OnLeft() => w.SelectPrevious();
        public void OnRight() => w.SelectNext();
        public void OnUp() { }
        public void OnDown() { w.SwitchToInfo(); }
        public void OnAccept() { w.LaunchSelected(); }
        public void OnCancel() { }
    }

    public class FriendsInputHandler : IInputHandler
    {
        readonly GameLauncherWindow w;
        public FriendsInputHandler(GameLauncherWindow window) { w = window; }

        void EnsureList()
        {
            if (w.FriendListBox == null) return;
            if (w.FriendListBox.Items.Count > 0 && w.FriendListBox.SelectedIndex < 0) w.FriendListBox.SelectedIndex = 0;
        }

        public void OnLeft() { }
        public void OnRight() { }
        public void OnUp()
        {
            if (w.FriendListBox == null) return;
            EnsureList();
            w.FriendListBox.SelectedIndex = Math.Max(0, w.FriendListBox.SelectedIndex - 1);
            w.FriendListBox.ScrollIntoView(w.FriendListBox.SelectedItem);
        }
        public void OnDown()
        {
            if (w.FriendListBox == null) return;
            EnsureList();
            w.FriendListBox.SelectedIndex = Math.Min(Math.Max(0, w.FriendListBox.Items.Count - 1), w.FriendListBox.SelectedIndex + 1);
            w.FriendListBox.ScrollIntoView(w.FriendListBox.SelectedItem);
        }
        public void OnAccept()
        {
            if (w.FriendListBox?.SelectedItem is PlayerSummary ps && !string.IsNullOrEmpty(ps.ProfileUrl))
                w.OpenFriendProfileCommand.Execute(ps.ProfileUrl);
        }
        public void OnCancel() { }
    }

    public class InfoInputHandler : IInputHandler
    {
        readonly GameLauncherWindow w;

        // 0 = Botões Topo (Jogar)
        // 1 = Achievements (Esq) - Vertical
        // 2 = News (Meio) - Texto
        // 3 = Screenshots (Dir) - Horizontal
        private int currentZone = 0;

        public InfoInputHandler(GameLauncherWindow window) { w = window; }

        public void EnterAtStart()
        {
            currentZone = 0;
            FocusZone();
        }

        void FocusZone()
        {
            w.Dispatcher.InvokeAsync(() =>
            {
                // Garante que as seleções existem para feedback visual
                if (w.Info_AchievementsList.Items.Count > 0 && w.Info_AchievementsList.SelectedIndex < 0)
                    w.Info_AchievementsList.SelectedIndex = 0;

                if (w.Info_Screenshots.Items.Count > 0 && w.Info_Screenshots.SelectedIndex < 0)
                    w.Info_Screenshots.SelectedIndex = 0;

                switch (currentZone)
                {
                    case 0: w.StartBtn.Focus(); break;
                    case 1: w.Info_AchievementsList.Focus(); break;
                    case 2: w.Info_Feed.Focus(); break;
                    case 3: w.Info_Screenshots.Focus(); break;
                }
            }, DispatcherPriority.Input);
        }

        public void OnUp()
        {
            if (currentZone == 0)
            {
                // Sair para o Carrossel
                w.SwitchToGamesFromInfo();
            }
            else if (currentZone == 1) // Achievements
            {
                // Se estiver no topo da lista, sobe para o botão Jogar
                if (w.Info_AchievementsList.SelectedIndex <= 0)
                {
                    currentZone = 0;
                    FocusZone();
                }
                else
                {
                    MoveListFocus(w.Info_AchievementsList, -1);
                }
            }
            else
            {
                // News e Screenshots sobem sempre para o botão Jogar
                currentZone = 0;
                FocusZone();
            }
        }

        public void OnDown()
        {
            if (currentZone == 0)
            {
                currentZone = 1; // Desce para Achievements por defeito
                FocusZone();
            }
            else if (currentZone == 1) // Achievements
            {
                MoveListFocus(w.Info_AchievementsList, 1);
            }
            // News e Screenshots não fazem nada no Down (ou podes fazer scroll no texto)
        }

        public void OnLeft()
        {
            if (currentZone == 0) return;

            if (currentZone == 1)
            {
                // Já estamos na esquerda, não faz nada
                return;
            }
            else if (currentZone == 2) // News
            {
                currentZone = 1; // Vai para Achievements
                FocusZone();
            }
            else if (currentZone == 3) // Screenshots (Horizontal)
            {
                // Lógica Inteligente:
                // Se estiver no primeiro item, SALTA para News.
                // Senão, navega para o item anterior.
                if (w.Info_Screenshots.SelectedIndex <= 0)
                {
                    currentZone = 2;
                    FocusZone();
                }
                else
                {
                    MoveListFocus(w.Info_Screenshots, -1);
                }
            }
        }

        public void OnRight()
        {
            if (currentZone == 0) return;

            if (currentZone == 1) // Achievements (Vertical)
            {
                // SALTO FORÇADO: Direita sai sempre da lista vertical
                currentZone = 2;
                FocusZone();
            }
            else if (currentZone == 2) // News
            {
                currentZone = 3;
                FocusZone();
            }
            else if (currentZone == 3) // Screenshots (Horizontal)
            {
                // Navega nos itens da lista
                MoveListFocus(w.Info_Screenshots, 1);
            }
        }

        public void OnAccept()
        {
            if (currentZone == 0) w.LaunchSelected();
            // Adiciona aqui lógica para abrir Screenshots/Achievements em grande se quiseres
        }

        public void OnCancel()
        {
            w.SwitchToGamesFromInfo();
        }

        void MoveListFocus(ListBox lb, int direction)
        {
            if (lb.Items.Count == 0) return;
            int next = Math.Clamp(lb.SelectedIndex + direction, 0, lb.Items.Count - 1);
            lb.SelectedIndex = next;
            lb.ScrollIntoView(lb.SelectedItem);
        }
    }
    public class SettingsInputHandler : IInputHandler
    {
        readonly GameLauncherWindow w;

        // Estado da Navegação
        private bool inContentArea = false; // false = Esquerda (Menu), true = Direita (Conteúdo)
        private int categoryIndex = 0;      // Qual aba estamos (0, 1, 2)
        private int contentIndex = 0;       // Qual item da direita estamos

        public SettingsInputHandler(GameLauncherWindow window)
        {
            w = window;
        }

        // Chamado quando entras nas Settings
        public void Reset()
        {
            inContentArea = false;
            categoryIndex = 0;
            contentIndex = 0;
            UpdateFocus();
        }

        public void OnUp()
        {
            if (w.WifiSelectorModal.Visibility == Visibility.Visible)
            {
                MoveListFocus(w.WifiListBox, -1);
                return;
            }

            if (!inContentArea) // Menu Esquerdo
            {
                if (categoryIndex > 0)
                {
                    categoryIndex--;
                    UpdateTabSelection();
                }
            }
            else // Conteúdo Direito
            {
                if (contentIndex > 0)
                {
                    contentIndex--;
                    UpdateFocus();
                }
            }
        }

        public void OnDown()
        {
            if (w.WifiSelectorModal.Visibility == Visibility.Visible)
            {
                MoveListFocus(w.WifiListBox, 1);
                return;
            }

            if (!inContentArea) // Menu Esquerdo
            {
                // Temos 3 categorias fixas
                if (categoryIndex < 2)
                {
                    categoryIndex++;
                    UpdateTabSelection();
                }
            }
            else // Conteúdo Direito
            {
                int maxItems = GetCurrentContentCount() - 1;
                if (contentIndex < maxItems)
                {
                    contentIndex++;
                    UpdateFocus();
                }
            }
        }

        public void OnRight()
        {
            if (w.WifiSelectorModal.Visibility == Visibility.Visible) return;

            // Se estamos na esquerda, vamos para a direita
            if (!inContentArea)
            {
                inContentArea = true;
                contentIndex = 0; // Começa sempre no topo ao entrar
                UpdateFocus();
            }
            else
            {
                // Se já estamos na direita, e for um Slider, aumenta valor
                if (GetFocusedElement() is Slider slider)
                {
                    slider.Value += slider.TickFrequency;
                }
            }
        }

        public void OnLeft()
        {
            if (w.WifiSelectorModal.Visibility == Visibility.Visible) return;

            if (inContentArea)
            {
                // Se for Slider, diminui valor...
                if (GetFocusedElement() is Slider slider && slider.Value > slider.Minimum)
                {
                    slider.Value -= slider.TickFrequency;
                    return; // Não sai do slider se estiver a diminuir
                }

                // ...senão, volta para o menu da esquerda
                inContentArea = false;
                UpdateFocus(); // Foca a categoria atual
            }
        }

        public void OnAccept()
        {
            if (w.WifiSelectorModal.Visibility == Visibility.Visible)
            {
                w.ConnectWifi_Click(null, null);
                return;
            }

            var element = GetFocusedElement();

            // Aciona Botões, Checkboxes e RadioButtons
            if (element is System.Windows.Controls.Primitives.ButtonBase btn)
            {
                btn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }

            // Se for Slider, não faz nada (ou podia alternar modo de edição)
        }

        public void OnCancel()
        {
            if (w.WifiSelectorModal.Visibility == Visibility.Visible)
            {
                w.CloseWifiModal_Click(null, null);
            }
            else if (inContentArea)
            {
                // Se estiver na direita, volta para a esquerda
                inContentArea = false;
                UpdateFocus();
            }
            else
            {
                // Se estiver na esquerda, sai das settings
                w.currentInputHandler = null; // Volta ao anterior ou Home
                w.SwapViewRight(); // Ou outra lógica de sair
            }
        }

        // --- HELPERS DE LÓGICA ---

        // Muda a aba visualmente e atualiza o conteúdo
        void UpdateTabSelection()
        {
            if (categoryIndex == 0) w.BtnTabGeneral.IsChecked = true;
            else if (categoryIndex == 1) w.BtnTabSystem.IsChecked = true;
            else if (categoryIndex == 2) w.BtnTabPersonalization.IsChecked = true;

            // Força atualização visual imediata
            w.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        }

        // Aplica o foco real no elemento certo
        void UpdateFocus()
        {
            if (!inContentArea)
            {
                // Foca o botão da categoria certa
                if (categoryIndex == 0) w.BtnTabGeneral.Focus();
                else if (categoryIndex == 1) w.BtnTabSystem.Focus();
                else if (categoryIndex == 2) w.BtnTabPersonalization.Focus();
            }
            else
            {
                // Foca o elemento dentro do painel ativo
                var panel = GetCurrentPanel();
                if (panel != null)
                {
                    var controls = GetFocusableControls(panel);
                    if (controls.Count > contentIndex)
                    {
                        controls[contentIndex].Focus();
                    }
                }
            }
        }

        // Obtém o painel visível
        Panel? GetCurrentPanel()
        {
            if (w.TabGeneral.Visibility == Visibility.Visible) return w.TabGeneral;
            if (w.TabSystem.Visibility == Visibility.Visible) return w.TabSystem;
            if (w.TabPersonalization.Visibility == Visibility.Visible) return w.TabPersonalization;
            return null;
        }

        // Encontra todos os botões/sliders/checkboxes dentro do painel
        List<Control> GetFocusableControls(Panel parent)
        {
            var list = new List<Control>();
            foreach (var child in GetLogicalChildren(parent))
            {
                if (child is Control c && c.Focusable && c.Visibility == Visibility.Visible && c.IsEnabled)
                {
                    // Ignora o botão "invisível" do Wi-Fi se não quiseres que ele conte, 
                    // mas no nosso caso queremos focar o BtnWifiReal
                    list.Add(c);
                }
            }
            return list;
        }

        // Helper recursivo para achar controlos dentro de Grids aninhadas
        IEnumerable<DependencyObject> GetLogicalChildren(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Control c && c.Focusable)
                    yield return c;

                // Continua a descer na árvore (se for Grid, StackPanel, etc)
                foreach (var grandChild in GetLogicalChildren(child))
                    yield return grandChild;
            }
        }

        int GetCurrentContentCount()
        {
            var p = GetCurrentPanel();
            return p != null ? GetFocusableControls(p).Count : 0;
        }

        Control? GetFocusedElement() => Keyboard.FocusedElement as Control;

        void MoveListFocus(ListBox lb, int dir)
        {
            if (lb.Items.Count == 0) return;
            int next = Math.Clamp(lb.SelectedIndex + dir, 0, lb.Items.Count - 1);
            lb.SelectedIndex = next;
            lb.ScrollIntoView(lb.SelectedItem);
        }
    }

    public static class WifiScanner
    {
        // Executa comandos de terminal invisíveis
        public static List<WifiNetwork> ScanNetworks()
        {
            var list = new List<WifiNetwork>();
            try
            {
                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = "wlan show networks mode=bssid",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                proc.Start();
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                // Parse simples do texto que o CMD devolve
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                string currentSSID = "";

                foreach (var line in lines)
                {
                    var l = line.Trim();
                    if (l.StartsWith("SSID") && l.Contains(":"))
                    {
                        currentSSID = l.Split(new[] { ':' }, 2)[1].Trim();
                    }
                    else if (l.StartsWith("Signal") && !string.IsNullOrEmpty(currentSSID))
                    {
                        var signal = l.Split(':')[1].Trim();
                        // Evita duplicados
                        if (!list.Any(n => n.SSID == currentSSID))
                        {
                            list.Add(new WifiNetwork { SSID = currentSSID, SignalStrength = signal });
                        }
                        currentSSID = "";
                    }
                }
            }
            catch { }
            return list;
        }

        public static void Connect(string ssid, string password = "")
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"wlan connect name=\"{ssid}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();
        }
    }

    public class WifiNetwork
    {
        public string SSID { get; set; } = "";
        public string SignalStrength { get; set; } = "";
    }


    // --- JANELA PRINCIPAL ---
    public partial class GameLauncherWindow : Window
    {
        // Inputs
        public IInputHandler currentInputHandler;
        GamesInputHandler gamesInputHandler;
        FriendsInputHandler friendsInputHandler;
        InfoInputHandler infoInputHandler;
        SettingsInputHandler settingsInputHandler;

        private PlayStationController? _controller;

        private CancellationTokenSource? _scrollCts;

        private bool isLoading = false;

        // Dados
        List<GameEntry> games = new List<GameEntry>();
        List<GameEntry> storeGames = new List<GameEntry>();
        public int selectedIndex = -1;

        // Flags de estado
        bool isPopulating = false;
        bool isShowingStoreView = false;

        // Timers e Ferramentas
        DispatcherTimer xinputTimer;
        readonly string appCache;
        static readonly HttpClient http = new HttpClient();

        // DualSense / Input States
        XInputNative.XINPUT_STATE xInputPrevState;
        DateTime lastNav = DateTime.MinValue;
        private DualSenseInputState? _prevDsState;
        private DateTime _lastNav = DateTime.MinValue;

        // Steam Config
        string? connectedSteamId = null;
        string? steamApiKey = null;
        bool steamConnected => !string.IsNullOrEmpty(connectedSteamId);
        readonly string configDir;
        readonly string apiKeyFilePath;
        readonly string loginFilePath;

        // Navegação de Vistas
        public int currentIndex = 0;
        public bool isFriendMenu = false;

        // --- FULLSCREEN VARS ---
        bool isFullScreen = false;
        WindowState prevState;
        WindowStyle prevStyle;
        Rect prevBounds;

        // Comando para abrir perfil
        public ICommand OpenFriendProfileCommand => new RelayCommand<string>(url =>
        {
            if (!string.IsNullOrEmpty(url))
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
            }
        });


        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);
        const int VK_VOLUME_MUTE = 0xAD;
        const int VK_VOLUME_DOWN = 0xAE;
        const int VK_VOLUME_UP = 0xAF;

        HardwareMonitor hwMonitor;

        public GameLauncherWindow()
        {
            InitializeComponent();

            ToggleFullscreen();

            // 1. Configurar diretorias
            configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PichalnovenseUI");
            apiKeyFilePath = Path.Combine(configDir, "steam_key.dat");
            loginFilePath = Path.Combine(configDir, "steam_login.dat");
            appCache = Path.Combine(configDir, "covercache");
            Directory.CreateDirectory(appCache);

            // 2. Inicializar Handlers
            gamesInputHandler = new GamesInputHandler(this);
            friendsInputHandler = new FriendsInputHandler(this);
            infoInputHandler = new InfoInputHandler(this);
            settingsInputHandler = new SettingsInputHandler(this);
            currentInputHandler = gamesInputHandler;

            // 3. Ligar Eventos UI
            BtnConnectSteam.Click += (s, e) => _ = Task.Run(() => ConnectSteamFlowAsync());
            StartBtn.Click += (s, e) => LaunchSelected();

            // 4. Timer de Input (XInput)
            xinputTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            xinputTimer.Tick += XinputTimer_Tick;
            xinputTimer.Start();

            this.KeyDown += GameLauncherWindow_KeyDown;
            this.PreviewKeyDown += GameLauncherWindow_PreviewKeyDown;

            // 5. Carregar dados Steam guardados
            try
            {
                LoadSteamApiKey();
                var savedId = LoadConnectedSteamId();
                if (!string.IsNullOrEmpty(savedId))
                {
                    connectedSteamId = savedId;
                    StatusLabel.Text = $"Steam: connected (cached) {savedId}";

                    // Background load
                    _ = Task.Run(async () =>
                    {
                        var profileSummary = await GetProfileSummary();
                        await FetchSteamDataForAllGamesAsync();
                        await FetchAndShowFriendsAsync();

                        await Dispatcher.InvokeAsync(() =>
                        {
                            PopulateGamesPanel();
                            if (games.Count > 0) SelectIndex(0);

                            if (!string.IsNullOrEmpty(profileSummary.PersonaName))
                                ProfileName.Text = profileSummary.PersonaName;

                            if (!string.IsNullOrEmpty(profileSummary.AvatarFull))
                            {
                                var bitmapImage = new BitmapImage();
                                bitmapImage.BeginInit();
                                bitmapImage.UriSource = new Uri(profileSummary.AvatarFull);
                                bitmapImage.EndInit();
                                ProfileImage.Source = bitmapImage;
                            }
                        });
                    });
                }
                else
                {
                    StatusLabel.Text = "Steam not connected.";
                }
            }
            catch { }

            // 6. Scan inicial
            Task.Run(() => RescanGamesAsync());

            // 7. Teste vibração (feedback tátil inicial)
            VibrateFor(0, 20000, 20000, 500);

            DispatcherTimer clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            clockTimer.Tick += (s, e) =>
            {
                ClockTime.Text = DateTime.Now.ToString("HH:mm");
                ClockDate.Text = DateTime.Now.ToString("ddd, dd MMM");
            };
            clockTimer.Start();
        }

        // --- NAVEGAÇÃO ENTRE VISTAS ---

        public void SwitchToInfo()
        {
            currentInputHandler = infoInputHandler;
            Dispatcher.InvokeAsync(() => infoInputHandler.EnterAtStart());
        }

        public void SwitchToGamesFromInfo()
        {
            currentInputHandler = gamesInputHandler;
            Dispatcher.InvokeAsync(() =>
            {
                GamesListBox?.Focus();
                Keyboard.Focus(GamesListBox);
            });
        }

        public void SwapViewRight()
        {
            currentIndex = (currentIndex + 1) % 3; // 0=Home, 1=Friends, 2=Settings (simulado)
            UpdateViewVisibility();
        }

        void UpdateViewVisibility()
        {
            Carousel.Visibility = Visibility.Collapsed;
            InfoArea.Visibility = Visibility.Collapsed;
            FriendMenu.Visibility = Visibility.Collapsed;
            SettingsMenu.Visibility = Visibility.Collapsed;

            // Cores do menu de topo
            BannerText.Foreground = Brushes.Gray;
            FriendsMenu.Foreground = Brushes.Gray;
            SettingMenu.Foreground = Brushes.Gray;

            if (currentIndex == 0) // Home
            {
                Carousel.Visibility = Visibility.Visible;
                InfoArea.Visibility = Visibility.Visible;
                BannerText.Foreground = Brushes.White;

                isFriendMenu = false;
                currentInputHandler = gamesInputHandler;
                GamesListBox.Focus();
            }
            else if (currentIndex == 1) // Friends
            {
                FriendMenu.Visibility = Visibility.Visible;
                FriendsMenu.Foreground = Brushes.White;

                isFriendMenu = true;
                currentInputHandler = friendsInputHandler;
                FriendListBox.Focus();
                if (FriendListBox.SelectedIndex < 0 && FriendListBox.HasItems)
                {
                    FriendListBox.SelectedIndex = 0; // Seleciona o primeiro automaticamente
                }
            }
            else if (currentIndex == 2) // Settings
            {
                SettingsMenu.Visibility = Visibility.Visible;
                SettingMenu.Foreground = Brushes.White;
                currentInputHandler = settingsInputHandler;
                isFriendMenu = false;

                settingsInputHandler.Reset();

                LoadMonitorInfo();
                LoadSystemInfo();

                VolumeSlider.Focus();
            }
        }

        // --- LÓGICA PRINCIPAL DE SELEÇÃO ---

        public void SelectIndex(int idx)
        {
            var currentList = isShowingStoreView ? storeGames : games;
            if (currentList.Count == 0) return;

            // Bloqueia loop
            if (idx < 0 || idx >= currentList.Count) return;

            selectedIndex = idx;

            GamesListBox.SelectedIndex = selectedIndex;

            var item = GamesListBox.SelectedItem;
            if (item != null)
            {
                // Usamos Dispatcher para garantir que o zoom visual (XAML) já começou
                // antes de calcularmos o centro
                Dispatcher.InvokeAsync(() =>
                {
                    ScrollToCenterOfView(GamesListBox, item);
                }, DispatcherPriority.Input);
            }

            var g = currentList[selectedIndex];

            Dispatcher.Invoke(() =>
            {
                if (g.Source == "System" && g.Title == "Loja")
                {
                    BannerImage.Source = MakePlaceholderBitmap(1000, 400);
                    HeroTitle.Text = "Loja Steam";
                    StatusLabel.Text = "Loja - Jogos não instalados";
                    Info_OwnedOn.Text = "Clica ENTER para instalar jogos";
                    Info_Playtime.Text = "—";
                    HeroCover.Source = null;
                    Info_CompletionPercent.Text = "";
                    Info_LastPlayed.Text = "";
                    Info_AchievementsList.Items.Clear();
                    Info_Screenshots.ItemsSource = null;
                }
                else
                {
                    BannerImage.Source = g.Cover ?? g.Icon ?? MakePlaceholderBitmap(1000, 400);
                    HeroCover.Source = g.Cover ?? g.Icon ?? MakePlaceholderBitmap(800, 450);
                    HeroTitle.Text = g.Title ?? "Unknown";
                    StatusLabel.Text = $"Selected: {g.Title}";
                    Info_OwnedOn.Text = $"Source: {g.Source}";
                    Info_Playtime.Text = $"{g.PlaytimeHours:0.0} hrs";
                    Info_CompletionPercent.Text = $"{g.ProgressPercent}%";
                    Info_LastPlayed.Text = g.LastPlayed.HasValue ? g.LastPlayed.Value.ToString("g") : "-";

                    Info_AchievementsList.Items.Clear();
                    if (g.Achievements.Count > 0)
                        foreach (var a in g.Achievements) Info_AchievementsList.Items.Add(new TextBlock { Text = a, Foreground = Brushes.LightGray });
                    else
                        Info_AchievementsList.Items.Add(new TextBlock { Text = "No achievements", Foreground = Brushes.Gray });

                    int appid = 0;
                    int.TryParse(g.SteamAppId, out appid);
                    var shots = TryGetLocalSteamScreenshots(connectedSteamId ?? "", appid);
                    Info_Screenshots.ItemsSource = shots;
                }
            });

            if (!isShowingStoreView && steamConnected && !string.IsNullOrEmpty(g.SteamAppId) && g.Source != "System")
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (int.TryParse(g.SteamAppId, out int appid))
                        {
                            var play = await GetPlaytimeHoursForAppAsync(connectedSteamId!, appid);
                            if (play.HasValue) g.PlaytimeHours = play.Value;

                            var news = await GetNewsForAppAsync(appid, 1);
                            await Dispatcher.InvokeAsync(() =>
                            {
                                Info_Playtime.Text = $"{g.PlaytimeHours:0.0} hrs";
                                if (news.Count > 0) Info_Feed.Text = news[0].title + "\n\n" + news[0].contents;
                                else Info_Feed.Text = "No news.";
                            });
                        }
                    }
                    catch { }
                });
            }
        }

        public void SelectNext()
        {
            var currentList = isShowingStoreView ? storeGames : games;
            if (selectedIndex < currentList.Count - 1) SelectIndex(selectedIndex + 1);
        }

        public void SelectPrevious()
        {
            if (selectedIndex > 0) SelectIndex(selectedIndex - 1);
        }

        public void LaunchSelected()
        {
            var currentList = isShowingStoreView ? storeGames : games;
            if (selectedIndex < 0 || selectedIndex >= currentList.Count) return;

            var g = currentList[selectedIndex];

            if (!isShowingStoreView && g.Source == "System" && g.Title == "Loja")
            {
                _ = Task.Run(async () => await LoadStoreNotInstalledAsync());
                return;
            }

            if (isShowingStoreView)
            {
                if (int.TryParse(g.SteamAppId, out int appid)) StartSteamInstall(appid);
                return;
            }

            try
            {
                if (g.Source == "Steam" && !string.IsNullOrEmpty(g.SteamAppId))
                {
                    Process.Start(new ProcessStartInfo($"steam://run/{g.SteamAppId}") { UseShellExecute = true });
                }
                else if (!string.IsNullOrEmpty(g.ExePath) && File.Exists(g.ExePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = g.ExePath,
                        WorkingDirectory = g.WorkingDirectory ?? Path.GetDirectoryName(g.ExePath),
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show("Executável não encontrado.", "Erro");
                }
            }
            catch (Exception ex) { MessageBox.Show("Erro ao lançar: " + ex.Message); }
        }

        void PopulateGamesPanel()
        {
            if (isPopulating) return;
            isPopulating = true;

            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (!games.Any(g => g.Title == "Loja" && g.Source == "System"))
                    {
                        games.Add(new GameEntry { Title = "Loja", Source = "System", Cover = null });
                    }

                    GamesListBox.ItemsSource = null;
                    GamesListBox.ItemsSource = games;

                    if (games.Count > 0) SelectIndex(0);
                }
                finally { isPopulating = false; }
            });
        }

        public void ToggleStoreView(bool show)
        {
            isShowingStoreView = show;
            Dispatcher.Invoke(() =>
            {
                if (show)
                {
                    StatusLabel.Text = "MODO LOJA (ESC para voltar)";
                    GamesListBox.ItemsSource = null;
                    GamesListBox.ItemsSource = storeGames;
                    if (storeGames.Count > 0) SelectIndex(0);
                }
                else
                {
                    StatusLabel.Text = "Biblioteca";
                    GamesListBox.ItemsSource = null;
                    GamesListBox.ItemsSource = games;
                    if (games.Count > 0) SelectIndex(0);
                }
            });
        }

        // --- LÓGICA STEAM & SISTEMA ---

        async Task RescanGamesAsync()
        {
            if (isLoading) return; // Evita spam de F5
            SetLoading(true, "A procurar jogos...");
            Dispatcher.Invoke(() => StatusLabel.Text = "A procurar jogos...");
            try
            {
                var found = new List<GameEntry>();
                found.AddRange(ScanSteamLibraries());
                found.AddRange(ScanStartMenuShortcuts());

                found = found.GroupBy(x => (x.SteamAppId + x.ExePath)).Select(g => g.First()).ToList();

                foreach (var e in found)
                {
                    if (!string.IsNullOrEmpty(e.SteamAppId))
                    {
                        var cachePath = Path.Combine(appCache, $"{e.SteamAppId}.jpg");
                        if (File.Exists(cachePath))
                        {
                            try { e.Cover = LoadBitmapImageFromFile(cachePath); } catch { }
                        }
                    }
                }

                games = found;
                PopulateGamesPanel();

                if (games.Any(g => !string.IsNullOrEmpty(g.SteamAppId) && g.Cover == null))
                {
                    SetLoading(true, "A transferir capas...");
                }

                _ = Task.Run(async () =>
                {
                    foreach (var g in games)
                    {
                        if (!string.IsNullOrEmpty(g.SteamAppId) && g.Cover == null)
                        {
                            var img = await TryDownloadSteamCoverAsync(g.SteamAppId);
                            if (img != null)
                            {
                                g.Cover = img;
                                var cachePath = Path.Combine(appCache, $"{g.SteamAppId}.jpg");
                                try { SaveBitmapImageToFile(img, cachePath); } catch { }
                            }
                        }
                    }
                    await Dispatcher.InvokeAsync(() => GamesListBox.Items.Refresh());
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => StatusLabel.Text = "Erro scan: " + ex.Message);
            }
            finally
            {
                SetLoading(false);
            }
        }

        List<GameEntry> ScanSteamLibraries()
        {
            var results = new List<GameEntry>();
            try
            {
                var steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (string.IsNullOrEmpty(steamPath)) return results;

                var paths = new List<string> { Path.Combine(steamPath, "steamapps") };
                var libFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(libFile))
                {
                    var matches = Regex.Matches(File.ReadAllText(libFile), "\"(path|1|2|3)\"\\s*\"([^\"]+)\"");
                    foreach (Match m in matches)
                    {
                        var p = m.Groups[2].Value.Replace("\\\\", "\\");
                        if (Directory.Exists(p)) paths.Add(Path.Combine(p, "steamapps"));
                    }
                }

                foreach (var p in paths.Distinct())
                {
                    if (!Directory.Exists(p)) continue;
                    foreach (var acf in Directory.GetFiles(p, "appmanifest_*.acf"))
                    {
                        try
                        {
                            var txt = File.ReadAllText(acf);
                            var name = Regex.Match(txt, "\"name\"\\s*\"([^\"]+)\"").Groups[1].Value;
                            var id = Regex.Match(txt, "\"appid\"\\s*\"(\\d+)\"").Groups[1].Value;
                            if (!string.IsNullOrEmpty(name))
                            {
                                results.Add(new GameEntry { Title = name, SteamAppId = id, Source = "Steam" });
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return results;
        }

        List<GameEntry> ScanStartMenuShortcuts()
        {
            var list = new List<GameEntry>();
            try
            {
                var paths = new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) };
                foreach (var p in paths)
                {
                    if (string.IsNullOrEmpty(p)) continue;
                    foreach (var lnk in Directory.GetFiles(p, "*.lnk", SearchOption.AllDirectories))
                    {
                        try
                        {
                            // Aqui poderias adicionar a resolução COM se necessário
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return list;
        }

        async Task LoadStoreNotInstalledAsync()
        {
            if (string.IsNullOrEmpty(steamApiKey) || string.IsNullOrEmpty(connectedSteamId))
            {
                await Dispatcher.InvokeAsync(() => MessageBox.Show("Precisas de login Steam e API Key."));
                return;
            }

            await Dispatcher.InvokeAsync(() => StatusLabel.Text = "A carregar Loja...");

            var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={steamApiKey}&steamid={connectedSteamId}&include_appinfo=1&include_played_free_games=1";
            using var res = await http.GetAsync(url);
            if (!res.IsSuccessStatusCode) return;

            using var st = await res.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(st);

            storeGames.Clear();

            if (doc.RootElement.TryGetProperty("response", out var resp) && resp.TryGetProperty("games", out var gamesArr))
            {
                foreach (var g in gamesArr.EnumerateArray())
                {
                    int appid = g.GetProperty("appid").GetInt32();
                    string name = g.TryGetProperty("name", out var nm) ? nm.GetString() ?? "Unknown" : $"App {appid}";

                    if (!IsSteamGameInstalled(appid))
                    {
                        var entry = new GameEntry
                        {
                            Title = name,
                            SteamAppId = appid.ToString(),
                            Source = "Steam Store",
                            Cover = null
                        };

                        try
                        {
                            var uri = new Uri($"https://cdn.cloudflare.steamstatic.com/steam/apps/{appid}/header.jpg");
                            var bi = new BitmapImage(uri);
                            bi.Freeze();
                            entry.Cover = bi;
                        }
                        catch { entry.Cover = MakePlaceholderBitmap(300, 450); }

                        storeGames.Add(entry);
                    }
                }
            }
            await Dispatcher.InvokeAsync(() => ToggleStoreView(true));
        }

        async Task FetchSteamDataForAllGamesAsync()
        {
            if (string.IsNullOrEmpty(connectedSteamId)) return;

            var steamGames = games.Where(g => !string.IsNullOrEmpty(g.SteamAppId)).ToList();
            foreach (var g in steamGames)
            {
                if (int.TryParse(g.SteamAppId, out int appid))
                {
                    try
                    {
                        // 1. Playtime
                        var play = await GetPlaytimeHoursForAppAsync(connectedSteamId, appid);
                        if (play.HasValue) g.PlaytimeHours = play.Value;

                        // 2. Achievements (Desbloqueados)
                        var ach = await GetPlayerAchievementsAsync(connectedSteamId, appid);
                        if (ach != null) g.Achievements = ach;

                        int totalAchievements = await GetTotalAchievementsCount(appid);
                        if (totalAchievements > 0 && g.Achievements.Count > 0)
                        {
                            // Regra de 3 simples
                            g.ProgressPercent = (int)((double)g.Achievements.Count / totalAchievements * 100);
                        }
                        else if (totalAchievements > 0 && g.Achievements.Count == 0)
                        {
                            g.ProgressPercent = 0;
                        }
                        else if (totalAchievements == 0)
                        {
                            // Se não tiver achievements, define como -1 ou deixa 0
                            g.ProgressPercent = 0;
                        }
                    }
                    catch { }
                }
            }
            await Task.CompletedTask;
        }

        async Task LoadFriendExtraDetails(PlayerSummary friend)
        {
            if (friend == null || string.IsNullOrEmpty(steamApiKey) || string.IsNullOrEmpty(connectedSteamId)) return;

            // Evita recarregar se já tivermos dados
            if (friend.RecentGames.Count > 0) return;

            try
            {
                // 1. OBTER JOGOS RECENTES (Últimas 2 semanas)
                var urlGames = $"https://api.steampowered.com/IPlayerService/GetRecentlyPlayedGames/v1/?key={steamApiKey}&steamid={friend.SteamId}&count=3";
                var jsonGames = await SteamApiGetJson(urlGames);

                await Dispatcher.InvokeAsync(() => friend.RecentGames.Clear());

                if (jsonGames.HasValue && jsonGames.Value.TryGetProperty("response", out var r) && r.TryGetProperty("games", out var gamesArr))
                {
                    foreach (var g in gamesArr.EnumerateArray())
                    {
                        var info = new FriendGameInfo
                        {
                            Name = g.GetProperty("name").GetString() ?? "Unknown",
                            AppId = g.GetProperty("appid").GetInt32().ToString(),
                            Playtime2Weeks = $"{(g.GetProperty("playtime_2weeks").GetInt32() / 60.0):0.1} hrs"
                        };
                        await Dispatcher.InvokeAsync(() => friend.RecentGames.Add(info));
                    }
                }

                // 2. OBTER AMIGOS EM COMUM
                // Lógica: Pedimos a lista do amigo e comparamos com a nossa (que já temos na FriendListBox)
                var urlFriends = $"https://api.steampowered.com/ISteamUser/GetFriendList/v1/?key={steamApiKey}&steamid={friend.SteamId}&relationship=friend";
                var jsonFriends = await SteamApiGetJson(urlFriends);

                int mutualCount = 0;
                if (jsonFriends.HasValue && jsonFriends.Value.TryGetProperty("friendslist", out var fl) && fl.TryGetProperty("friends", out var fArr))
                {
                    // Obtém os IDs dos amigos DELE
                    var hisFriendIds = new HashSet<string>();
                    foreach (var item in fArr.EnumerateArray()) hisFriendIds.Add(item.GetProperty("steamid").GetString()!);

                    // Compara com os NOSSOS (que estão na FriendListBox)
                    if (FriendListBox.ItemsSource is IEnumerable<PlayerSummary> myFriends)
                    {
                        mutualCount = myFriends.Count(myF => hisFriendIds.Contains(myF.SteamId));
                    }
                }

                // Atualiza o texto na UI
                await Dispatcher.InvokeAsync(() => friend.MutualFriendsText = $"{mutualCount} Amigos em Comum");
            }
            catch
            {
                // Perfil Privado geralmente causa erro ou retorna vazio
                await Dispatcher.InvokeAsync(() => friend.MutualFriendsText = "Perfil Privado");
            }
        }

        private void FriendListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FriendListBox.SelectedItem is PlayerSummary selectedFriend)
            {
                // Dispara o carregamento em background (fire and forget)
                _ = Task.Run(() => LoadFriendExtraDetails(selectedFriend));
            }
        }

        async Task FetchAndShowFriendsAsync()
        {
            if (string.IsNullOrEmpty(connectedSteamId) || string.IsNullOrEmpty(steamApiKey)) return;
            try
            {
                var url = $"https://api.steampowered.com/ISteamUser/GetFriendList/v1/?key={steamApiKey}&steamid={connectedSteamId}&relationship=friend";
                var root = await SteamApiGetJson(url);
                var ids = new List<string>();

                if (root.HasValue && root.Value.TryGetProperty("friendslist", out var fl) && fl.TryGetProperty("friends", out var fArr))
                {
                    foreach (var f in fArr.EnumerateArray()) ids.Add(f.GetProperty("steamid").GetString()!);
                }

                if (ids.Count > 0)
                {
                    var summaries = new List<PlayerSummary>();
                    var batch = string.Join(",", ids.Take(100));
                    var url2 = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={steamApiKey}&steamids={batch}";
                    var root2 = await SteamApiGetJson(url2);

                    if (root2.HasValue && root2.Value.TryGetProperty("response", out var resp) && resp.TryGetProperty("players", out var players))
                    {
                        foreach (var p in players.EnumerateArray())
                        {
                            summaries.Add(new PlayerSummary
                            {
                                SteamId = p.TryGetProperty("steamid", out var sid) ? sid.GetString() ?? "" : "",
                                PersonaName = p.TryGetProperty("personaname", out var pn) ? pn.GetString() ?? "Unknown" : "Unknown",
                                AvatarFull = p.TryGetProperty("avatarfull", out var af) ? af.GetString() ?? "" : "",
                                ProfileUrl = p.TryGetProperty("profileurl", out var pu) ? pu.GetString() ?? "" : "",
                                PersonaState = p.TryGetProperty("personastate", out var ps) ? ps.GetInt32() : 0,
                                GameExtraInfo = p.TryGetProperty("gameextrainfo", out var ge) ? ge.GetString() ?? "" : "",
                                GameId = p.TryGetProperty("gameid", out var gid) ? gid.GetString() ?? "0" : "0"
                            });
                        }
                    }
                    await Dispatcher.InvokeAsync(() => FriendListBox.ItemsSource = summaries);
                }
            }
            catch { }
        }

        async Task<double?> GetPlaytimeHoursForAppAsync(string steamId64, int appid)
        {
            if (string.IsNullOrEmpty(steamApiKey)) return null;
            var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={steamApiKey}&steamid={steamId64}&include_appinfo=1&include_played_free_games=1";
            var root = await SteamApiGetJson(url);
            if (root.HasValue && root.Value.TryGetProperty("response", out var resp) && resp.TryGetProperty("games", out var games))
            {
                foreach (var g in games.EnumerateArray())
                {
                    if (g.TryGetProperty("appid", out var aid) && aid.GetInt32() == appid)
                        return (g.TryGetProperty("playtime_forever", out var pm) ? pm.GetInt32() : 0) / 60.0;
                }
            }
            return null;
        }

        async Task<List<string>> GetPlayerAchievementsAsync(string steamId64, int appid)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(steamApiKey)) return list;
            var url = $"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/?appid={appid}&key={steamApiKey}&steamid={steamId64}";
            var root = await SteamApiGetJson(url);
            if (root.HasValue && root.Value.TryGetProperty("playerstats", out var ps) && ps.TryGetProperty("achievements", out var ach))
            {
                foreach (var a in ach.EnumerateArray())
                {
                    var apiname = a.TryGetProperty("apiname", out var ap) ? ap.GetString() : null;
                    var achieved = a.TryGetProperty("achieved", out var ac) ? ac.GetInt32() : 0;
                    if (apiname != null && achieved == 1) list.Add(apiname);
                }
            }
            return list;
        }

        async Task<List<(string title, string contents, string url, DateTime date)>> GetNewsForAppAsync(int appid, int count)
        {
            var list = new List<(string, string, string, DateTime)>();
            try
            {
                var url = $"https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid={appid}&count={count}";
                var root = await SteamApiGetJson(url);
                if (root.HasValue && root.Value.TryGetProperty("appnews", out var an) && an.TryGetProperty("newsitems", out var items))
                {
                    foreach (var it in items.EnumerateArray())
                    {
                        list.Add((
                            it.GetProperty("title").GetString() ?? "",
                            it.GetProperty("contents").GetString() ?? "",
                            it.GetProperty("url").GetString() ?? "",
                            DateTime.Now
                        ));
                    }
                }
            }
            catch { }
            return list;
        }

        async Task ConnectSteamFlowAsync()
        {
            SetLoading(true, "A ligar à Steam...");

            try
            {
                string? key = null;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (steamApiKey == null)
                        key = PromptForText("Steam Web API Key (opcional)", "Enter Steam Web API Key (or leave blank):", "");
                    else key = steamApiKey;
                });

                if (key != null) SaveSteamApiKey(string.IsNullOrWhiteSpace(key) ? null : key.Trim());

                var claimed = await DoOpenIdViaExternalBrowserAsync(120);
                if (string.IsNullOrEmpty(claimed)) return;

                var parts = claimed.TrimEnd('/').Split('/');
                var steamId = parts.LastOrDefault();
                if (string.IsNullOrEmpty(steamId)) return;

                connectedSteamId = steamId;
                SaveConnectedSteamId(connectedSteamId);

                await Dispatcher.InvokeAsync(() => StatusLabel.Text = $"Connected: {connectedSteamId}");

                _ = Task.Run(async () =>
                {
                    await FetchSteamDataForAllGamesAsync();
                    await FetchAndShowFriendsAsync();
                    await Dispatcher.InvokeAsync(() => PopulateGamesPanel());
                });

                // Quando chegares à parte de buscar dados:
                SetLoading(true, "A transferir biblioteca Steam...");

                // A parte pesada:
                await FetchSteamDataForAllGamesAsync();
                await FetchAndShowFriendsAsync();

                await Dispatcher.InvokeAsync(() => PopulateGamesPanel());
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => MessageBox.Show(this, "Erro connect: " + ex.Message));
            }
            finally
            {
                // IMPORTANTE: Desbloqueia UI aconteça o que acontecer
                SetLoading(false);
            }
        }

        async Task<string?> DoOpenIdViaExternalBrowserAsync(int timeoutSeconds = 120)
        {
            var listener = new HttpListener();
            int port = 0;
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();

            var redirectUri = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(redirectUri);
            listener.Start();

            var openid = "https://steamcommunity.com/openid/login";
            var query = new Dictionary<string, string>
            {
                {"openid.ns","http://specs.openid.net/auth/2.0"},
                {"openid.mode","checkid_setup"},
                {"openid.return_to", redirectUri},
                {"openid.realm", redirectUri},
                {"openid.identity","http://specs.openid.net/auth/2.0/identifier_select"},
                {"openid.claimed_id","http://specs.openid.net/auth/2.0/identifier_select"}
            };
            var url = openid + "?" + string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            var ctxTask = listener.GetContextAsync();
            var finished = await Task.WhenAny(ctxTask, Task.Delay(timeoutSeconds * 1000));
            if (finished != ctxTask) { listener.Stop(); return null; }

            var ctx = ctxTask.Result;
            var buffer = Encoding.UTF8.GetBytes("<html><body><h2>Logged in. Close this.</h2></body></html>");
            ctx.Response.ContentLength64 = buffer.Length;
            ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
            ctx.Response.OutputStream.Close();

            var claimed = ctx.Request.QueryString["openid.claimed_id"];
            listener.Stop();
            return claimed;
        }

        async Task<JsonElement?> SteamApiGetJson(string url)
        {
            try
            {
                using var res = await http.GetAsync(url);
                if (!res.IsSuccessStatusCode) return null;
                using var st = await res.Content.ReadAsStreamAsync();
                return (await JsonDocument.ParseAsync(st)).RootElement.Clone();
            }
            catch { return null; }
        }

        async Task<PlayerSummary> GetProfileSummary()
        {
            if (string.IsNullOrEmpty(connectedSteamId) || string.IsNullOrEmpty(steamApiKey)) return new PlayerSummary();
            var url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={steamApiKey}&steamids={connectedSteamId}";
            var root = await SteamApiGetJson(url);
            if (root.HasValue && root.Value.TryGetProperty("response", out var r) && r.TryGetProperty("players", out var p) && p.GetArrayLength() > 0)
            {
                var pl = p[0];
                return new PlayerSummary
                {
                    PersonaName = pl.GetProperty("personaname").GetString()!,
                    AvatarFull = pl.GetProperty("avatarfull").GetString()!
                };
            }
            return new PlayerSummary();
        }

        // --- HELPERS ---

        void LoadSteamApiKey()
        {
            if (File.Exists(apiKeyFilePath))
            {
                try { steamApiKey = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(apiKeyFilePath), null, DataProtectionScope.CurrentUser)); } catch { }
            }
        }

        void SaveSteamApiKey(string? key)
        {
            if (key == null) { File.Delete(apiKeyFilePath); steamApiKey = null; return; }
            steamApiKey = key;
            File.WriteAllBytes(apiKeyFilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
        }

        string? LoadConnectedSteamId()
        {
            if (File.Exists(loginFilePath))
            {
                try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(loginFilePath), null, DataProtectionScope.CurrentUser)); } catch { }
            }
            return null;
        }

        void SaveConnectedSteamId(string? id)
        {
            if (id == null) { File.Delete(loginFilePath); return; }
            File.WriteAllBytes(loginFilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(id), null, DataProtectionScope.CurrentUser));
        }

        string? PromptForText(string title, string prompt, string def)
        {
            // Cria uma janela WPF personalizada e moderna via código
            var w = new Window
            {
                Title = title,
                Width = 450,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)), // Fundo Escuro
                Owner = this,
                WindowStyle = WindowStyle.ToolWindow
            };

            var stack = new StackPanel { Margin = new Thickness(20) };

            var lbl = new TextBlock
            {
                Text = prompt,
                Foreground = Brushes.LightGray,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };

            var txt = new TextBox
            {
                Text = def,
                Height = 35,
                FontSize = 14,
                Padding = new Thickness(5),
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var btnOk = new Button
            {
                Content = "OK",
                Width = 90,
                Height = 30,
                IsDefault = true, // Enter ativa o botão
                Margin = new Thickness(10, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0, 120, 215)), // Azul destaque
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            };

            var btnCancel = new Button
            {
                Content = "Cancelar",
                Width = 90,
                Height = 30,
                IsCancel = true, // ESC ativa o botão
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Foreground = Brushes.White
            };

            btnOk.Click += (s, e) => { w.DialogResult = true; w.Close(); };
            btnCancel.Click += (s, e) => { w.DialogResult = false; w.Close(); };

            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnOk);

            stack.Children.Add(lbl);
            stack.Children.Add(txt);
            stack.Children.Add(btnPanel);

            w.Content = stack;

            // Foca a caixa de texto automaticamente ao abrir
            w.Loaded += (s, e) => txt.Focus();

            var res = w.ShowDialog();
            return res == true ? txt.Text : null;
        }
        List<ImageSource> TryGetLocalSteamScreenshots(string steamId64, int appid)
        {
            var res = new List<ImageSource>();
            try
            {
                var steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (string.IsNullOrEmpty(steamPath)) return res;

                // Caminho base para userdata
                var userDataRoot = Path.Combine(steamPath, "userdata");
                if (!Directory.Exists(userDataRoot)) return res;

                // Procura em TODAS as pastas de utilizador (porque o nome da pasta é SteamID3, não ID64)
                var userDirectories = Directory.GetDirectories(userDataRoot);

                foreach (var userDir in userDirectories)
                {
                    // Caminho específico para screenshots deste jogo
                    var screenshotDir = Path.Combine(userDir, "760", "remote", appid.ToString(), "screenshots");

                    if (Directory.Exists(screenshotDir))
                    {
                        // Encontrou! Carrega as imagens
                        var files = Directory.GetFiles(screenshotDir, "*.jpg")
                                             .OrderByDescending(f => File.GetLastWriteTime(f))
                                             .Take(6); // Carrega até 6 screenshots

                        foreach (var f in files)
                        {
                            try { res.Add(LoadBitmapImageFromFile(f)); } catch { }
                        }

                        // Se encontrámos imagens numa pasta, paramos de procurar noutras
                        if (res.Count > 0) break;
                    }
                }
            }
            catch { }
            return res;
        }

        async Task<int> GetTotalAchievementsCount(int appid)
        {
            if (string.IsNullOrEmpty(steamApiKey)) return 0;
            try
            {
                var url = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={steamApiKey}&appid={appid}";
                var root = await SteamApiGetJson(url);

                if (root.HasValue &&
                    root.Value.TryGetProperty("game", out var game) &&
                    game.TryGetProperty("availableGameStats", out var stats) &&
                    stats.TryGetProperty("achievements", out var achievements))
                {
                    return achievements.GetArrayLength();
                }
            }
            catch { }
            return 0;
        }

        ImageSource LoadBitmapImageFromFile(string path)
        {
            var bmp = new BitmapImage();
            using (var fs = File.OpenRead(path))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream();
                fs.CopyTo(bmp.StreamSource);
                bmp.StreamSource.Position = 0;
                bmp.EndInit();
                bmp.Freeze();
            }
            return bmp;
        }

        void SaveBitmapImageToFile(ImageSource src, string path)
        {
            if (src is BitmapSource bsrc)
            {
                var encoder = new JpegBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bsrc));
                using var fs = File.Open(path, FileMode.Create);
                encoder.Save(fs);
            }
        }

        ImageSource MakePlaceholderBitmap(int w, int h)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(22, 28, 38)), null, new Rect(0, 0, w, h));
            }
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv); bmp.Freeze(); return bmp;
        }

        async Task<ImageSource?> TryDownloadSteamCoverAsync(string appid)
        {
            try
            {
                using var res = await http.GetAsync($"https://cdn.cloudflare.steamstatic.com/steam/apps/{appid}/library_600x900.jpg");
                if (res.IsSuccessStatusCode)
                {
                    using var st = await res.Content.ReadAsStreamAsync();
                    return LoadBitmapImageFromStream(st);
                }
            }
            catch { }
            return null;
        }

        ImageSource LoadBitmapImageFromStream(Stream st)
        {
            var bmp = new BitmapImage();
            var ms = new MemoryStream(); st.CopyTo(ms); ms.Position = 0;
            bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = ms; bmp.EndInit(); bmp.Freeze();
            return bmp;
        }

        void StartSteamInstall(int appid)
        {
            try { Process.Start(new ProcessStartInfo($"steam://install/{appid}") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("Erro steam: " + ex.Message); }
        }

        bool IsSteamGameInstalled(int appid) => GetSteamInstallDir(appid) != null;

        string? GetSteamInstallDir(int appid)
        {
            var libs = new List<string> { @"C:\Program Files (x86)\Steam\steamapps" };
            foreach (var lib in libs) if (File.Exists(Path.Combine(lib, $"appmanifest_{appid}.acf"))) return lib;
            return null;
        }

        // --- DUALSENSE & WINDOW ---

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _controller = new PlayStationController();
            _controller.StateChanged += Controller_StateChanged;
            if (!_controller.Start()) { /* Log silencioso */ }
        }

        private void Window_Closed(object sender, EventArgs e){ _controller?.Stop(); hwMonitor?.Close();}

        private void Controller_StateChanged(DualSenseInputState state)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastNav).TotalMilliseconds < 150) return;

            if (_prevDsState != null)
            {
                FireEdge(state.CrossButton, _prevDsState.CrossButton, () => currentInputHandler?.OnAccept());
                FireEdge(state.CircleButton, _prevDsState.CircleButton, () => currentInputHandler?.OnCancel());
                FireEdge(state.R1Button, _prevDsState.R1Button, () => SwapViewRight());
            }
            _prevDsState = state;
        }

        private void FireEdge(bool now, bool prev, Action act) { if (now && !prev) Dispatcher.Invoke(act); }

        private void GameLauncherWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (isLoading) { e.Handled = true; return; } // BLOQUEIA TUDO SE ESTIVER A CARREGAR
            if (Keyboard.FocusedElement is TextBox) return;
            bool handled = false;
            switch (e.Key)
            {
                case Key.F11: ToggleFullscreen(); handled = true; break;
                case Key.Left: currentInputHandler?.OnLeft(); handled = true; break;
                case Key.Right: currentInputHandler?.OnRight(); handled = true; break;
                case Key.Up: currentInputHandler?.OnUp(); handled = true; break;
                case Key.Down: currentInputHandler?.OnDown(); handled = true; break;
                case Key.Enter: currentInputHandler?.OnAccept(); handled = true; break;
                case Key.Escape:
                    if (isShowingStoreView) ToggleStoreView(false);
                    else currentInputHandler?.OnCancel();
                    handled = true; break;
                case Key.RightShift: SwapViewRight(); handled = true; break;
            }
            if (handled) e.Handled = true;
        }

        private void GameLauncherWindow_KeyDown(object sender, KeyEventArgs e) { }

        void XinputTimer_Tick(object? sender, EventArgs e)
        {
            if (isLoading) return; // BLOQUEIA COMANDO

            if (!XInputNative.GetState(0, out var st)) { xInputPrevState = st; return; }
            var now = DateTime.UtcNow;
            if ((now - lastNav).TotalMilliseconds < 150) return;

            var buttons = XInputNative.ButtonsFromState(st);
            bool action = false;

            if ((buttons & XInputNative.GamepadButtons.DPadLeft) != 0 || st.Gamepad.sThumbLX < -16000) { currentInputHandler?.OnLeft(); action = true; }
            else if ((buttons & XInputNative.GamepadButtons.DPadRight) != 0 || st.Gamepad.sThumbLX > 16000) { currentInputHandler?.OnRight(); action = true; }
            else if ((buttons & XInputNative.GamepadButtons.DPadUp) != 0) { currentInputHandler?.OnUp(); action = true; }
            else if ((buttons & XInputNative.GamepadButtons.DPadDown) != 0) { currentInputHandler?.OnDown(); action = true; }
            else if ((buttons & XInputNative.GamepadButtons.A) != 0) { currentInputHandler?.OnAccept(); action = true; }
            else if ((buttons & XInputNative.GamepadButtons.B) != 0)
            {
                if (isShowingStoreView) ToggleStoreView(false);
                else currentInputHandler?.OnCancel();
                action = true;
            }
            else if ((buttons & XInputNative.GamepadButtons.RightShoulder) != 0) { SwapViewRight(); action = true; }

            if (action) lastNav = now;
            xInputPrevState = st;
        }

        private ScrollViewer? GetScrollViewer(DependencyObject o)
        {
            if (o is ScrollViewer sv) return sv;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
            {
                var child = VisualTreeHelper.GetChild(o, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        // Função para centrar o item com animação
        private void ScrollToCenterOfView(ListBox listBox, object item)
        {
            if (listBox == null || item == null) return;

            var container = listBox.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
            if (container == null)
            {
                listBox.UpdateLayout();
                listBox.ScrollIntoView(item);
                listBox.UpdateLayout();
                container = listBox.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
            }

            if (container != null)
            {
                var scrollViewer = GetScrollViewer(listBox);
                if (scrollViewer != null)
                {
                    // CANCELA A ANIMAÇÃO ANTERIOR SE EXISTIR
                    _scrollCts?.Cancel();
                    _scrollCts = new CancellationTokenSource();
                    var token = _scrollCts.Token;

                    Point relativePoint = container.TransformToAncestor(scrollViewer).Transform(new Point(0, 0));
                    double currentOffset = scrollViewer.HorizontalOffset;
                    double targetOffset = currentOffset + relativePoint.X - (scrollViewer.ViewportWidth / 2) + (container.ActualWidth / 2);

                    if (targetOffset < 0) targetOffset = 0;
                    if (targetOffset > scrollViewer.ScrollableWidth) targetOffset = scrollViewer.ScrollableWidth;

                    // Passa o token para a animação
                    _ = AnimateScroll(scrollViewer, targetOffset, token);
                }
            }
        }

        // Método Atualizado com CancellationToken
        private async Task AnimateScroll(ScrollViewer scroll, double targetOffset, CancellationToken token)
        {
            double current = scroll.HorizontalOffset;
            double diff = targetOffset - current;

            if (Math.Abs(diff) < 1)
            {
                scroll.ScrollToHorizontalOffset(targetOffset);
                return;
            }

            int steps = 15; // Reduzi passos para ser mais reativo (menos "lag" visual)
            for (int i = 1; i <= steps; i++)
            {
                // Se entretanto carregaste noutra tecla, PÁRA esta animação imediatamente
                if (token.IsCancellationRequested) return;

                double t = (double)i / steps;
                double ease = 1 - Math.Pow(1 - t, 3);

                scroll.ScrollToHorizontalOffset(current + (diff * ease));
                await Task.Delay(10); // ~60fps
            }

            if (!token.IsCancellationRequested)
                scroll.ScrollToHorizontalOffset(targetOffset);
        }

        void SetLoading(bool loading, string message = "A carregar...")
        {
            isLoading = loading;
            Dispatcher.Invoke(() =>
            {
                if (LoadingOverlay != null)
                {
                    LoadingOverlay.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
                    if (LoadingText != null) LoadingText.Text = message;
                }
            });
        }

        public void ToggleFullscreen()
        {
            if (!isFullScreen)
            {
                // 1. Guarda o estado atual (para podermos voltar atrás)
                prevState = this.WindowState;
                prevStyle = this.WindowStyle;
                prevBounds = new Rect(this.Left, this.Top, this.Width, this.Height);

                // 2. Aplica o Fullscreen
                this.WindowStyle = WindowStyle.None; // Remove a barra de título
                this.WindowState = WindowState.Maximized; // Ocupa o ecrã todo
                this.Topmost = true; // Fica por cima da barra de tarefas
                isFullScreen = true;
            }
            else
            {
                // 3. Restaura o estado original
                this.Topmost = false;
                this.WindowStyle = prevStyle;
                this.WindowState = prevState;
                this.Left = prevBounds.X;
                this.Top = prevBounds.Y;
                this.Width = prevBounds.Width;
                this.Height = prevBounds.Height;
                isFullScreen = false;
            }


        }

        // --- DEFINIÇÕES DE SISTEMA ---
        private void OpenSystemSettings(string uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch { MessageBox.Show("Não foi possível abrir as definições."); }
        }

        private void BtnSysWifi_Click(object sender, RoutedEventArgs e) => OpenSystemSettings("ms-settings:network-wifi");
        private void BtnSysDisplay_Click(object sender, RoutedEventArgs e) => OpenSystemSettings("ms-settings:display");
        private void BtnSysBluetooth_Click(object sender, RoutedEventArgs e) => OpenSystemSettings("ms-settings:bluetooth");

        public void ChangeSystemVolume(bool up)
        {
            // Simula a tecla de volume do teclado
            keybd_event((byte)(up ? VK_VOLUME_UP : VK_VOLUME_DOWN), 0, 0, IntPtr.Zero);
            // Toca um som de feedback (opcional)
            System.Media.SystemSounds.Beep.Play();
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Apenas executa se o user estiver a mexer (Evita loops)
            if (!VolumeSlider.IsFocused) return;

            // Diferença entre valor antigo e novo
            double diff = e.NewValue - e.OldValue;

            // Se a mudança for significativa, envia teclas
            if (Math.Abs(diff) >= 5) // TickFrequency
            {
                bool up = diff > 0;
                // Envia a tecla de volume
                keybd_event((byte)(up ? VK_VOLUME_UP : VK_VOLUME_DOWN), 0, 0, IntPtr.Zero);

                // Toca som de feedback "Plim"
                System.Media.SystemSounds.Exclamation.Play();
            }
        }

        // 2. WI-FI TOGGLE
        private void ToggleWifi_Checked(object sender, RoutedEventArgs e)
        {
            // Opção de segurança se o nome falhar
            var wifiText = this.FindName("WifiStatusText") as TextBlock;
            if (wifiText != null) wifiText.Text = "Ativado";
        }

        private void ToggleWifi_Unchecked(object sender, RoutedEventArgs e)
        {
            var wifiText = this.FindName("WifiStatusText") as TextBlock;
            if (wifiText != null) wifiText.Text = "Desativado";
        }

        // 3. MONITOR INFO
        void LoadMonitorInfo()
        {
            // Obtém a resolução do ecrã principal
            double w = SystemParameters.PrimaryScreenWidth;
            double h = SystemParameters.PrimaryScreenHeight;
            if (MonitorInfoText != null)
            {
                MonitorInfoText.Text = $"{w} x {h}";
            }
        }

        // Evento disparado ao clicar ou selecionar uma RadioButton
        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            // Garante que só corremos isto se a UI estiver carregada
            if (TabGeneral == null) return;

            TabGeneral.Visibility = Visibility.Collapsed;
            TabSystem.Visibility = Visibility.Collapsed;
            TabPersonalization.Visibility = Visibility.Collapsed;

            if (BtnTabGeneral.IsChecked == true) TabGeneral.Visibility = Visibility.Visible;
            if (BtnTabSystem.IsChecked == true) TabSystem.Visibility = Visibility.Visible;
            if (BtnTabPersonalization.IsChecked == true) TabPersonalization.Visibility = Visibility.Visible;
            if (SettingsMenu.Visibility == Visibility.Visible)
            {
                // Pequeno delay para o visual atualizar
                Dispatcher.BeginInvoke(() => settingsInputHandler?.OnRight(), DispatcherPriority.Input);
            }
        }

        // Para garantir que o clique com o rato também ativa a aba
        private void Category_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb) rb.IsChecked = true;
        }


        // --- LÓGICA DE WI-FI (Simulada mas Funcional na UI) ---

        private void WifiListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (WifiListBox.SelectedItem != null)
            {
                WifiPasswordPanel.Visibility = Visibility.Visible;
                WifiPasswordBox.Focus(); // Foca logo para escreveres
            }
            else
            {
                WifiPasswordPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async void OpenWifiModal_Click(object sender, RoutedEventArgs e)
        {
            WifiSelectorModal.Visibility = Visibility.Visible;
            WifiStatusText.Text = "A procurar redes...";

            // Executa o scan real em background
            var nets = await Task.Run(() => WifiScanner.ScanNetworks());

            WifiListBox.ItemsSource = nets;
            WifiStatusText.Text = $"{nets.Count} redes encontradas.";

            // Foca a lista
            WifiListBox.Focus();
            if (WifiListBox.Items.Count > 0) WifiListBox.SelectedIndex = 0;
        }

        public void ConnectWifi_Click(object sender, RoutedEventArgs e)
        {
            if (WifiListBox.SelectedItem is WifiNetwork net)
            {
                string password = WifiPasswordBox.Password;

                WifiStatusText.Text = $"A conectar a {net.SSID}...";
                CloseWifiModal_Click(null, null); // Fecha logo para não bloquear

                Task.Run(async () =>
                {
                    bool connected = await WifiHelper.ConnectToNetwork(net.SSID, password);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (connected)
                        {
                            WifiStatusText.Text = $"Ligado: {net.SSID} (Sinal Excelente)";
                            MessageBox.Show("Conectado com sucesso!", "Wi-Fi");
                        }
                        else
                        {
                            WifiStatusText.Text = "Falha na conexão.";
                            MessageBox.Show("Não foi possível conectar. Verifica a password.", "Erro Wi-Fi");
                        }
                    });
                });
            }
        }
        public void CloseWifiModal_Click(object sender, RoutedEventArgs e)
        {
            WifiSelectorModal.Visibility = Visibility.Collapsed;
            // Devolve o foco ao botão de abrir wi-fi
            BtnTabSystem.Focus(); // Ou foca o painel direito
        }

        // --- TEMAS / BACKGROUND ---
        void ApplyThemeColors(Color accent, Color textSecondary, Color bgStart, Color bgEnd, Color panelBg)
        {
            // 1. Atualiza o Background (Gradiente)
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            brush.GradientStops.Add(new GradientStop(bgStart, 0.0)); // Offset ajustado para suavidade
            brush.GradientStops.Add(new GradientStop(bgEnd, 0.8));
            MainBackground.Background = brush;

            // Atualiza os Recursos Dinâmicos
            this.Resources.Remove("AccentBrush");
            this.Resources.Remove("PanelBackgroundBrush");

            this.Resources.Add("AccentBrush", new SolidColorBrush(accent));
            this.Resources.Add("PanelBackgroundBrush", new SolidColorBrush(panelBg));

        }

        private void BtnThemeRed_Click(object sender, RoutedEventArgs e)
        {
            // DEFAULT/ORIGINAL (Igual ao XAML inicial)
            var accent = (Color)ColorConverter.ConvertFromString("#FF4758");
            var bgStart = (Color)ColorConverter.ConvertFromString("#1A0A0D"); // Cor escura do topo
            var bgEnd = (Color)ColorConverter.ConvertFromString("#A81826");   // Cor viva do fundo
            var pnl = (Color)ColorConverter.ConvertFromString("#D9101010");

            ApplyThemeColors(accent, Colors.Gray, bgStart, bgEnd, pnl);
        }

        private void BtnThemeBlue_Click(object sender, RoutedEventArgs e)
        {
            // DEEP BLUE (PlayStation Vibes)
            var accent = (Color)ColorConverter.ConvertFromString("#00A8E8");
            var bgStart = (Color)ColorConverter.ConvertFromString("#000814");
            var bgEnd = (Color)ColorConverter.ConvertFromString("#003566");
            var pnl = (Color)ColorConverter.ConvertFromString("#D9051020");

            ApplyThemeColors(accent, Colors.LightBlue, bgStart, bgEnd, pnl);
        }

        private void BtnThemeDark_Click(object sender, RoutedEventArgs e)
        {
            // OLED BLACK
            var accent = Colors.White;
            var bgStart = Colors.Black;
            var bgEnd = (Color)ColorConverter.ConvertFromString("#111111");
            var pnl = (Color)ColorConverter.ConvertFromString("#E6000000");

            ApplyThemeColors(accent, Colors.DarkGray, bgStart, bgEnd, pnl);
        }

        private void BtnThemePichal_Click(object sender, RoutedEventArgs e)
        {
            var accent = (Color)ColorConverter.ConvertFromString("#FFFFC20E");

            // Preto com tom esverdeado (Fundo Topo)
            var bgStart = (Color)ColorConverter.ConvertFromString("#FF004D25");

            // Amarelo/Dourado Escuro (Fundo Base)
            var bgEnd = (Color)ColorConverter.ConvertFromString("#FF020F05");

            // Painel Verde Tropa escuro (Semi-transparente)
            var pnl = (Color)ColorConverter.ConvertFromString("#E60A2610");

            // Se a tua função ApplyThemeColors pede 5 argumentos (como no teu código colado):
            // Usei uma cor de texto secundária amarela/dourada clara.
            var textSecondary = (Color)ColorConverter.ConvertFromString("#FFD4AF37");

            ApplyThemeColors(accent, textSecondary, bgStart, bgEnd, pnl);
        }

        // --- POWER OPTIONS ---

        private void BtnSleep_Click(object sender, RoutedEventArgs e)
        {
            // Suspender o PC
            System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, true, true);
        }

        private void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Tens a certeza que queres reiniciar?", "Reiniciar", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                Process.Start("shutdown", "/r /t 0");
            }
        }

        private void BtnShutdown_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Tens a certeza que queres desligar?", "Desligar", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                Process.Start("shutdown", "/s /t 0");
            }
        }

        // --- SISTEMA & ARMAZENAMENTO ---

        void LoadSystemInfo()
        {
            try
            {
                var drive = new DriveInfo("C");
                if (drive.IsReady)
                {
                    long total = drive.TotalSize;
                    long free = drive.AvailableFreeSpace;
                    long used = total - free;
                    double percent = ((double)used / total) * 100;

                    if (StorageBar != null) StorageBar.Value = percent;
                    if (StorageText != null) StorageText.Text = $"{free / 1024 / 1024 / 1024} GB livres de {total / 1024 / 1024 / 1024} GB";
                }
            }
            catch { }

            long ramBytes = (long)new Microsoft.VisualBasic.Devices.Computer().Info.TotalPhysicalMemory;
            if (RamInfoText != null) RamInfoText.Text = $"{ramBytes / 1024 / 1024 / 1024} GB Total";

            // 3. CPU e GPU (Via LibreHardwareMonitor em Background)
            Task.Run(() =>
            {
                if (hwMonitor == null) hwMonitor = new HardwareMonitor();

                var info = hwMonitor.GetInfo();

                Dispatcher.Invoke(() =>
                {
                    if (CpuInfoText != null)
                        CpuInfoText.Text = info.cpuName.Replace("(R)", "").Replace("(TM)", "").Trim();

                    if (GpuInfoText != null)
                        GpuInfoText.Text = info.gpuName.Replace("NVIDIA", "").Trim(); // Limpeza estética
                });
            });
        }

        // Helper para formatar GB/TB
        string FormatBytes(long bytes)
        {
            string[] suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }
            return String.Format("{0:0.0} {1}", dblSByte, suffix[i]);
        }

        private void BtnConnectSteam_Click(object sender, RoutedEventArgs e)
        {
            // Chama a função de login que já existe
            _ = Task.Run(() => ConnectSteamFlowAsync());
        }



        public static class WifiHelper
        {
            // Tenta conectar e espera para ver se funcionou
            public static async Task<bool> ConnectToNetwork(string ssid, string password)
            {
                try
                {
                    // 1. Apagar perfil antigo para garantir que a password nova entra
                    RunNetsh($"wlan delete profile name=\"{ssid}\"");

                    // 2. Criar XML do Perfil (WPA2-Personal AES - Padrão 99% dos routers)
                    // O truque: hex=false para a password ser texto limpo
                    string profileXml = $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{ssid}</name>
    <SSIDConfig>
        <SSID>
            <name>{ssid}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>auto</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>WPA2PSK</authentication>
                <encryption>AES</encryption>
                <useOneX>false</useOneX>
            </authEncryption>
            <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>{password}</keyMaterial>
            </sharedKey>
        </security>
    </MSM>
</WLANProfile>";

                    string tempFile = Path.GetTempFileName();
                    File.WriteAllText(tempFile, profileXml);

                    // 3. Injetar perfil
                    RunNetsh($"wlan add profile filename=\"{tempFile}\"");

                    // 4. Conectar
                    RunNetsh($"wlan connect name=\"{ssid}\"");

                    File.Delete(tempFile);

                    // 5. Verificar sucesso (Polling durante 5 segundos)
                    for (int i = 0; i < 5; i++)
                    {
                        await Task.Delay(1000);
                        if (IsConnectedTo(ssid)) return true;
                    }
                    return false;
                }
                catch { return false; }
            }

            // Helper para correr comandos invisíveis
            private static void RunNetsh(string args)
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = args,
                        CreateNoWindow = true,
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    }
                };
                p.Start();
                p.WaitForExit();
            }

            // Verifica se estamos ligados à rede certa
            public static bool IsConnectedTo(string ssid)
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo("netsh", "wlan show interfaces")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                // Procura "SSID : NomeDaRede" e "State : connected"
                return output.Contains($"SSID") && output.Contains(ssid) && output.Contains(" connected");
            }
        }


        // --- XINPUT NATIVE CLASS ---
        static class XInputNative
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct XINPUT_STATE { public uint dwPacketNumber; public XINPUT_GAMEPAD Gamepad; }

            [StructLayout(LayoutKind.Sequential)]
            public struct XINPUT_GAMEPAD { public ushort wButtons; public byte bLeftTrigger; public byte bRightTrigger; public short sThumbLX; public short sThumbLY; public short sThumbRX; public short sThumbRY; }

            [Flags]
            public enum GamepadButtons : ushort { DPadUp = 0x0001, DPadDown = 0x0002, DPadLeft = 0x0004, DPadRight = 0x0008, A = 0x1000, B = 0x2000, RightShoulder = 0x0200, LeftShoulder = 0x0100, Start = 0x0010 }

            [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
            public static extern uint XInputGetState14(uint i, out XINPUT_STATE s);

            public static bool GetState(int i, out XINPUT_STATE s)
            {
                s = default;
                try { return XInputGetState14((uint)i, out s) == 0; }
                catch { return false; }
            }

            public static GamepadButtons ButtonsFromState(XINPUT_STATE s) => (GamepadButtons)s.Gamepad.wButtons;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_VIBRATION { public ushort wLeftMotorSpeed; public ushort wRightMotorSpeed; }

        [DllImport("xinput1_4.dll")]
        static extern uint XInputSetState(uint i, ref XINPUT_VIBRATION v);

        static bool VibrateFor(uint i, ushort l, ushort r, int ms)
        {
            var v = new XINPUT_VIBRATION { wLeftMotorSpeed = l, wRightMotorSpeed = r };
            XInputSetState(i, ref v);
            Task.Delay(ms).ContinueWith(_ => { var stop = new XINPUT_VIBRATION(); XInputSetState(i, ref stop); });
            return true;
        }
    }
}