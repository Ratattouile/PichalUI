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
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using Steamworks;
using SteamKit2;
using System.Reactive;
using System.Windows.Media.Effects;
using System.Configuration;


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
        public string Genre { get; set; } = "";
        public string Description { get; set; } = "";
        public int ProgressPercent { get; set; } = 0;
        public List<AchievementDetail> Achievements { get; set; } = new List<AchievementDetail>();
        public double PlaytimeHours { get; set; } = 0.0;
        public DateTime? LastPlayed { get; set; }
        public string StorePlatform { get; set; } = "";
        public string StoreId { get; set; } = "";

        public bool IsInstalled { get; set; } = true;


        public bool IsStoreItem { get; set; } = false; // Diz se é um jogo da loja ou da biblioteca
        public string PriceDisplay { get; set; } = ""; // Ex: "29.99€" ou "Free"
        public int DiscountPercent { get; set; } = 0;  // Ex: 50 (para -50%)
    }

    public class AchievementDetail
    {
        public string ApiName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public string IconUrl { get; set; } = "";
        public bool Achieved { get; set; } = false;
    }

    public class FriendGameInfo
    {
        public string Name { get; set; } = "";
        public string AppId { get; set; } = "";
        public string CoverUrl => $"https://cdn.cloudflare.steamstatic.com/steam/apps/{AppId}/library_600x900.jpg";
        public string Playtime2Weeks { get; set; } = ""; // Ex: "4.5 hrs"
    }

    public class ChatMessage
    {
        public string SenderName { get; set; } = "";
        public string Message { get; set; } = "";
        public string Time { get; set; } = "";
        public HorizontalAlignment Alignment { get; set; }
        public SolidColorBrush BubbleColor { get; set; } = Brushes.Gray;
        public bool IsMe { get; set; }
    }

    public class GameNewsItem
    {
        public string Title { get; set; } = "";
        public string Date { get; set; } = "";
        public string Snippet { get; set; } = "";
        public string FullContent { get; set; } = "";
    }

    public class PlayerSummary : System.ComponentModel.INotifyPropertyChanged
    {
        public string SteamId { get; set; } = "";
        public string PersonaName { get; set; } = "Unknown";
        public string AvatarFull { get; set; } = "";
        public string ProfileUrl { get; set; } = "";
        public int PersonaState { get; set; } = 0;
        public string GameExtraInfo { get; set; } = "";
        public string GameId { get; set; } = "";
        public bool IsOnline { get; set; } = false;

        public string StatusText => !string.IsNullOrEmpty(GameExtraInfo) ? $"A jogar: {GameExtraInfo}" : (PersonaState > 0 ? "Online" : "Offline");

        public int SteamLevel { get; set; } = 0;
        public string RichPresence { get; set; } = "";

        public SolidColorBrush StatusColor
        {
            get
            {
                if (!string.IsNullOrEmpty(GameExtraInfo)) return Brushes.LightGreen;
                if (PersonaState > 0) return Brushes.SkyBlue;
                return Brushes.Gray;
            }
        }

        // NOVO: URL da Capa do Jogo que está a jogar (ou vazio)
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

        // NOVO: Dados Extra (Amigos em Comum, etc) - Vamos preencher depois
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
        public void OnDown()
        {
            // INICIA A MAGIA
            w.EnterGameDetails();
        }
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

        public void OnLeft()
        {
            // Permite navegar entre os botões "Convidar" e "Ver Perfil"
            var element = Keyboard.FocusedElement as UIElement;
            element?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Left));
        }

        public void OnRight()
        {
            var element = Keyboard.FocusedElement as UIElement;
            element?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Right));
        }
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
            if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.ButtonBase btn)
            {
                btn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                return;
            }
            if (w.FriendListBox?.SelectedItem is PlayerSummary ps && !string.IsNullOrEmpty(ps.ProfileUrl))
                w.OpenFriendProfileCommand.Execute(ps.ProfileUrl);
        }
        public void OnCancel() { }
    }

    public class InfoInputHandler : IInputHandler
    {
        readonly GameLauncherWindow w;
        public InfoInputHandler(GameLauncherWindow window) { w = window; }

        public void EnterAtStart() => w.StartBtn.Focus();

        public void OnUp()
        {
            // Lógica de Saída (Topo absoluto)
            if (w.StartBtn.IsFocused || w.OptionsBtn.IsFocused ||
                IsAtTop(w.GameFriendsList) || IsAtTop(w.OwnersFriendsList) ||
                IsAtTop(w.Info_AchievementsList) || IsAtTop(w.Info_NewsList) || IsAtTop(w.Info_Screenshots))
            {
                w.ExitGameDetails();
            }
            else
            {
                // Tenta subir dentro da lista
                MoveFocus(FocusNavigationDirection.Up);

                // Lógica de Salto Inverso (De baixo para cima entre listas)
                if (IsFocused(w.OwnersFriendsList) && IsAtTop(w.OwnersFriendsList)) TryFocus(w.GameFriendsList);
                else if (IsFocused(w.Info_AchievementsList) && IsAtTop(w.Info_AchievementsList)) TryFocus(w.OwnersFriendsList);
                else if (IsFocused(w.Info_Screenshots) && IsAtTop(w.Info_Screenshots)) TryFocus(w.Info_NewsList);
            }
        }

        public void OnDown()
        {
            // 1. Proteção do Botão Play (Evita o bug de ficar preso)
            if (w.StartBtn.IsFocused || w.OptionsBtn.IsFocused) return;

            // 2. Saltos Verticais Específicos (Pontes entre listas)

            // Coluna do Meio: Amigos Jogar -> Amigos Donos -> Conquistas
            if (IsFocused(w.GameFriendsList))
            {
                // Tenta descer na lista (se for WrapPanel). Se não der, salta para a próxima lista.
                if (!MoveFocus(FocusNavigationDirection.Down)) TryFocus(w.OwnersFriendsList);
                return;
            }
            if (IsFocused(w.OwnersFriendsList))
            {
                if (!MoveFocus(FocusNavigationDirection.Down)) TryFocus(w.Info_AchievementsList);
                return;
            }

            // Coluna da Direita: Notícias -> Galeria
            if (IsFocused(w.Info_NewsList))
            {
                // Verifica se estamos no fim das notícias
                if (w.Info_NewsList.SelectedIndex >= w.Info_NewsList.Items.Count - 1)
                {
                    TryFocus(w.Info_Screenshots);
                    return;
                }
            }

            // Navegação padrão para o resto
            MoveFocus(FocusNavigationDirection.Down);
        }

        public void OnLeft()
        {
            // Tenta andar para a esquerda DENTRO da lista primeiro
            bool moved = MoveFocus(FocusNavigationDirection.Left);

            // Se não conseguiu mover (estamos na borda esquerda), MUDAMOS DE COLUNA
            if (!moved)
            {
                // Direita -> Meio
                if (IsZoneRightActive())
                {
                    if (!FocusZoneMiddle()) w.OptionsBtn.Focus();
                    return;
                }

                // Meio -> Esquerda
                if (IsZoneMiddleActive())
                {
                    w.OptionsBtn.Focus();
                    return;
                }

                // Opções -> Jogar
                if (w.OptionsBtn.IsFocused) w.StartBtn.Focus();
            }
        }

        public void OnRight()
        {
            // Jogar -> Opções
            if (w.StartBtn.IsFocused) { w.OptionsBtn.Focus(); return; }

            // Tenta andar para a direita DENTRO da lista
            bool moved = MoveFocus(FocusNavigationDirection.Right);

            // Se não conseguiu mover (estamos na borda direita), MUDAMOS DE COLUNA
            if (!moved)
            {
                // Opções -> Meio
                if (w.OptionsBtn.IsFocused)
                {
                    if (!FocusZoneMiddle()) FocusZoneRight();
                    return;
                }

                // Meio -> Direita
                if (IsZoneMiddleActive())
                {
                    FocusZoneRight();
                    return;
                }
            }
        }

        public void OnAccept()
        {
            if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.ButtonBase btn)
                btn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            if (w.Info_NewsList.IsKeyboardFocusWithin && w.Info_NewsList.SelectedItem != null)
                w.Info_NewsList_SelectionChanged(w.Info_NewsList, null);
        }

        public void OnCancel()
        {
            if (w.NewsReaderModal.Visibility == Visibility.Visible) { w.CloseNewsModal_Click(null, null); return; }
            if (w.GameOptionsModal.Visibility == Visibility.Visible) { w.CloseGameOptions_Click(null, null); return; }
            w.ExitGameDetails();
        }

        // --- HELPERS ---

        bool IsZoneRightActive() => w.Info_NewsList.IsKeyboardFocusWithin || w.Info_Screenshots.IsKeyboardFocusWithin;
        bool IsZoneMiddleActive() => w.GameFriendsList.IsKeyboardFocusWithin || w.OwnersFriendsList.IsKeyboardFocusWithin || w.Info_AchievementsList.IsKeyboardFocusWithin;
        bool IsFocused(UIElement el) => el.IsKeyboardFocusWithin;

        bool FocusZoneMiddle()
        {
            if (TryFocus(w.GameFriendsList)) return true;
            if (TryFocus(w.OwnersFriendsList)) return true;
            if (TryFocus(w.Info_AchievementsList)) return true;
            return false;
        }

        bool FocusZoneRight()
        {
            if (TryFocus(w.Info_NewsList)) return true;
            if (TryFocus(w.Info_Screenshots)) return true;
            return false;
        }

        bool TryFocus(ListBox list)
        {
            if (list.Visibility == Visibility.Visible && list.Items.Count > 0)
            {
                list.Focus();
                if (list.SelectedIndex < 0)
                {
                    list.SelectedIndex = 0;
                    list.ScrollIntoView(list.SelectedItem);
                }

                var item = list.ItemContainerGenerator.ContainerFromIndex(list.SelectedIndex) as ListBoxItem;
                item?.Focus();
                return true;
            }
            return false;
        }

        // Retorna true se o foco mudou, false se bateu na parede
        bool MoveFocus(FocusNavigationDirection dir)
        {
            var element = Keyboard.FocusedElement as UIElement;
            if (element != null)
            {
                return element.MoveFocus(new TraversalRequest(dir));
            }
            return false;
        }

        bool IsAtTop(ListBox list) => list.IsKeyboardFocusWithin && list.SelectedIndex <= 0;
    }

    public class SettingsInputHandler : IInputHandler
    {
        readonly GameLauncherWindow w;
        bool inContentArea = false;
        int categoryIndex = 0; // Mantemos apenas o índice do menu lateral

        public SettingsInputHandler(GameLauncherWindow window) { w = window; }

        public void Reset()
        {
            inContentArea = false;
            categoryIndex = 0;
            UpdateTabSelection();
            w.BtnTabGeneral.Focus(); // Foca sempre o primeiro botão ao entrar
        }

        public void OnUp()
        {
            if (w.WifiSelectorModal.Visibility == Visibility.Visible)
            {
                MoveListFocus(w.WifiListBox, -1);
                return;
            }

            if (!inContentArea)
            {
                // Navegação no Menu Lateral
                if (categoryIndex > 0)
                {
                    categoryIndex--;
                    UpdateTabSelection();
                    GetCategoryButton(categoryIndex)?.Focus();
                }
            }
            else
            {
                // Navegação no Conteúdo (Nativo)
                MoveFocus(FocusNavigationDirection.Up);
            }
        }

        public void OnDown()
        {
            if (w.WifiSelectorModal.Visibility == Visibility.Visible)
            {
                MoveListFocus(w.WifiListBox, 1);
                return;
            }

            if (!inContentArea)
            {
                // Navegação no Menu Lateral (4 categorias: 0,1,2,3)
                if (categoryIndex < 3)
                {
                    categoryIndex++;
                    UpdateTabSelection();
                    GetCategoryButton(categoryIndex)?.Focus();
                }
            }
            else
            {
                // Navegação no Conteúdo (Nativo)
                MoveFocus(FocusNavigationDirection.Down);
            }
        }

        public void OnRight()
        {
            if (w.WifiSelectorModal.Visibility == Visibility.Visible) return;

            // Do Menu -> Para o Conteúdo
            if (!inContentArea)
            {
                // Tenta focar o primeiro elemento dentro do painel visível
                if (FocusFirstContentElement())
                {
                    inContentArea = true;
                }
            }
            else
            {
                // Dentro do conteúdo (ex: Slider)
                if (Keyboard.FocusedElement is Slider slider) slider.Value += slider.TickFrequency;
                else MoveFocus(FocusNavigationDirection.Right);
            }
        }

        public void OnLeft()
        {
            if (w.WifiSelectorModal.Visibility == Visibility.Visible) return;

            if (inContentArea)
            {
                if (Keyboard.FocusedElement is Slider slider)
                {
                    slider.Value -= slider.TickFrequency;
                    return;
                }

                // Tenta mover para a esquerda dentro do painel
                bool moved = MoveFocus(FocusNavigationDirection.Left);

                // Se não conseguir mover mais, volta para o Menu Lateral
                if (!moved)
                {
                    inContentArea = false;
                    GetCategoryButton(categoryIndex)?.Focus();
                }
            }
        }

        public void OnAccept()
        {
            if (w.WifiSelectorModal.Visibility == Visibility.Visible)
            {
                w.ConnectWifi_Click(null, null);
                return;
            }

            // Clica no botão focado
            if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.ButtonBase btn)
            {
                btn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
            // Checkbox / RadioButton
            else if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.ToggleButton tb)
            {
                tb.IsChecked = !tb.IsChecked;
            }
        }

        public void OnCancel()
        {
            if (w.WifiSelectorModal.Visibility == Visibility.Visible)
            {
                w.CloseWifiModal_Click(null, null);
                return;
            }

            // Se estiver no conteúdo, volta ao menu
            if (inContentArea)
            {
                inContentArea = false;
                GetCategoryButton(categoryIndex)?.Focus();
            }
            else
            {
                // Se estiver no menu, sai das settings
                w.SwapViewRight();
            }
        }

        // --- HELPERS ---

        void UpdateTabSelection()
        {
            w.BtnTabGeneral.IsChecked = categoryIndex == 0;
            w.BtnTabSystem.IsChecked = categoryIndex == 1;
            w.BtnTabPersonalization.IsChecked = categoryIndex == 2;
            w.BtnTabAccount.IsChecked = categoryIndex == 3;
        }

        RadioButton? GetCategoryButton(int index)
        {
            if (index == 0) return w.BtnTabGeneral;
            if (index == 1) return w.BtnTabSystem;
            if (index == 2) return w.BtnTabPersonalization;
            if (index == 3) return w.BtnTabAccount;
            return null;
        }

        bool FocusFirstContentElement()
        {
            // Descobre qual o painel visível e foca o primeiro botão/slider
            UIElement? panel = null;
            if (w.TabGeneral.Visibility == Visibility.Visible) panel = w.TabGeneral;
            else if (w.TabSystem.Visibility == Visibility.Visible) panel = w.TabSystem;
            else if (w.TabPersonalization.Visibility == Visibility.Visible) panel = w.TabPersonalization;
            else if (w.TabAccount.Visibility == Visibility.Visible) panel = w.TabAccount;

            if (panel != null)
            {
                // Tenta mover foco para o primeiro elemento dentro do painel
                return panel.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }
            return false;
        }

        bool MoveFocus(FocusNavigationDirection dir)
        {
            var el = Keyboard.FocusedElement as UIElement;
            if (el != null) return el.MoveFocus(new TraversalRequest(dir));
            return false;
        }

        void MoveListFocus(ListBox lb, int dir)
        {
            if (lb.Items.Count == 0) return;
            int next = Math.Clamp(lb.SelectedIndex + dir, 0, lb.Items.Count - 1);
            lb.SelectedIndex = next;
            lb.ScrollIntoView(lb.SelectedItem);
        }
    }

    public class WelcomeInputHandler : IInputHandler
    {
        readonly GameLauncherWindow w;
        public WelcomeInputHandler(GameLauncherWindow window) { w = window; }

        public void EnterAtStart()
        {
            // Foca o botão principal assim que abre
            w.BtnWelcomePlay.Focus();
        }

        public void OnLeft()
        {
            // Alterna entre os dois botões
            if (w.BtnWelcomeClose.IsFocused) w.BtnWelcomePlay.Focus();
        }

        public void OnRight()
        {
            if (w.BtnWelcomePlay.IsFocused) w.BtnWelcomeClose.Focus();
        }

        public void OnUp() { } // Não faz nada
        public void OnDown() { } // Não faz nada

        public void OnAccept()
        {
            // Clica no que estiver focado
            if (w.BtnWelcomePlay.IsFocused) w.WelcomePlay_Click(null, null);
            else if (w.BtnWelcomeClose.IsFocused) w.WelcomeClose_Click(null, null);
        }

        public void OnCancel()
        {
            // O botão B/Esc fecha o menu
            w.WelcomeClose_Click(null, null);
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

    public class FriendPlayInfo
    {
        public string PersonaName { get; set; } = "";
        public string AvatarFull { get; set; } = "";
        public bool IsPlayingNow { get; set; } = false;
        public string StatusText { get; set; } = ""; // Ex: "A jogar agora"
    }


    public class ChatLogEntry
    {
        public string Sender { get; set; } = ""; // "Eu" ou Nome do Amigo
        public string Message { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    public class UserProfile
    {
        public string Username { get; set; }
        public string ImagePath { get; set; }
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
        WelcomeInputHandler welcomeInputHandler;

        private PlayStationController? _controller;

        private CancellationTokenSource? _scrollCts;

        private bool isLoading = false;

        // Dados
        List<GameEntry> _allGamesMasterList = new List<GameEntry>();
        List<GameEntry> games = new List<GameEntry>();
        List<GameEntry> storeGames = new List<GameEntry>();
        public int selectedIndex = -1;

        // Flags de estado
        bool isPopulating = false;
        bool isShowingStoreView = false;

        // Timers e Ferramentas
        DispatcherTimer xinputTimer;
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

        bool isGameDetailsOpen = false;

        // --- CACHES CRÍTICAS (PARA PERFORMANCE E DADOS) ---
        // Cache de Amigos (Lista completa em memória)
        List<PlayerSummary> _cachedFriendsList = new List<PlayerSummary>();
        // Cache de Donos de Jogos (Para não chamar a API 1000 vezes)

        CancellationTokenSource? _gameLoadCts;
        Dictionary<string, List<PlayerSummary>> _gameOwnersCache = new Dictionary<string, List<PlayerSummary>>();
        // Semaforo para limitar pedidos à Steam (evita bloqueios)
        SemaphoreSlim _steamApiSemaphore = new SemaphoreSlim(5);

        // Variáveis para o Chat
        Steamworks.Friend _currentChatFriend; // O amigo com quem estamos a falar
        ObservableCollection<ChatMessage> _chatMessages = new ObservableCollection<ChatMessage>();
        private static readonly object _chatFileLock = new object();

        // NOTIFICAÇÕES
        private CancellationTokenSource? _notificationCts;

        // --- GESTÃO DE UTILIZADORES ---
        string baseConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PichalnovenseUI");
        string usersDir => Path.Combine(baseConfigDir, "Users");

        // Estes caminhos dependem do utilizador logado
        string CurrentUserDir { get; set; } = "";
        string apiKeyFilePath => Path.Combine(CurrentUserDir, "steam_key.dat");
        string loginFilePath => Path.Combine(CurrentUserDir, "steam_login.dat");
        string chatLogDir => Path.Combine(CurrentUserDir, "chat_logs");
        string themeFilePath => Path.Combine(CurrentUserDir, "current_theme.dat");
        string appCache => Path.Combine(baseConfigDir, "covercache"); // Cache de imagens partilhada

        UserProfile currentUser;
        List<UserProfile> availableUsers = new List<UserProfile>();

        //---- Modo de Natal ----
        DispatcherTimer snowTimer;
        List<System.Windows.Shapes.Ellipse> activeSnowFlakes = new List<System.Windows.Shapes.Ellipse>();
        bool isSnowing = false;
        Random rng = new Random();
        bool golemSpawned = false;

        // ---- Sistema de Recomendação Inteligente ----
        GameEntry? _recommendedGame;


        public GameLauncherWindow()
        {
            InitializeComponent();
            Directory.CreateDirectory(baseConfigDir);
            Directory.CreateDirectory(usersDir);
            Directory.CreateDirectory(appCache);

            this.DataContext = this;
            //ToggleFullscreen();

            gamesInputHandler = new GamesInputHandler(this);
            friendsInputHandler = new FriendsInputHandler(this);
            infoInputHandler = new InfoInputHandler(this);
            settingsInputHandler = new SettingsInputHandler(this);
            welcomeInputHandler = new WelcomeInputHandler(this);
            currentInputHandler = gamesInputHandler;

            BtnConnectSteam.Click += (s, e) => _ = Task.Run(() => ConnectSteamFlowAsync());
            StartBtn.Click += (s, e) => LaunchSelected();

            var xinputTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            xinputTimer.Tick += XinputTimer_Tick;
            xinputTimer.Start();

            this.KeyDown += GameLauncherWindow_KeyDown;
            this.PreviewKeyDown += GameLauncherWindow_PreviewKeyDown;

            CompositionTarget.Rendering += UpdateSteamCallbacks;

            DispatcherTimer clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            clockTimer.Tick += (s, e) => { ClockTime.Text = DateTime.Now.ToString("HH:mm"); ClockDate.Text = DateTime.Now.ToString("ddd, dd MMM"); };
            clockTimer.Start();

            snowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) }; //Cria flocos de neve a cada 80ms
            snowTimer.Tick += SnowTimer_Tick;

            CheckChristmasSeason();
        }

        // --- NAVEGAÇÃO ENTRE VISTAS ---

        public void SwitchToInfo() { currentInputHandler = infoInputHandler; Dispatcher.InvokeAsync(() => infoInputHandler.EnterAtStart()); }
        public void EnterGameDetails()
        {
            if (isGameDetailsOpen) return;
            isGameDetailsOpen = true;
            InfoArea.Visibility = Visibility.Visible;
            var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(300)); Carousel.BeginAnimation(OpacityProperty, fadeOut);
            var slideUp = new DoubleAnimation(200, 0, TimeSpan.FromMilliseconds(400)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(300));
            InfoSlideTransform.BeginAnimation(TranslateTransform.YProperty, slideUp); InfoArea.BeginAnimation(OpacityProperty, fadeIn);
            SwitchToInfo();

            if (selectedIndex >= 0 && selectedIndex < games.Count)
            {
                var g = games[selectedIndex];
                _ = Task.Run(async () =>
                {
                    await LoadFriendsPlayingGame(g.SteamAppId, _gameLoadCts.Token);
                    await LoadFriendsWhoOwnGame(g.SteamAppId, _gameLoadCts.Token);
                    await LoadGameNews(int.Parse(g.SteamAppId));
                    await Dispatcher.InvokeAsync(() =>
                            {
                                Info_AchievementsList.Items.Clear();

                                if (g.Achievements != null && g.Achievements.Count > 0)
                                {
                                    foreach (var ach in g.Achievements)
                                    {
                                        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

                                        if (!string.IsNullOrEmpty(ach.IconUrl))
                                        {
                                            var img = new Image { Width = 40, Height = 40, Margin = new Thickness(0, 0, 10, 0) };
                                            try { img.Source = new BitmapImage(new Uri(ach.IconUrl)); } catch { }
                                            panel.Children.Add(img);
                                        }

                                        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                                        textStack.Children.Add(new TextBlock { Text = ach.DisplayName, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
                                        if (!string.IsNullOrEmpty(ach.Description))
                                            textStack.Children.Add(new TextBlock { Text = ach.Description, FontSize = 10, Foreground = Brushes.Gray });

                                        panel.Children.Add(textStack);

                                        Info_AchievementsList.Items.Add(panel);
                                    }
                                }
                                else
                                {
                                    Info_AchievementsList.Items.Add(new TextBlock { Text = "Sem conquistas", Foreground = Brushes.LightGray });
                                }
                            });
                });
            }
        }

        public void ExitGameDetails()
        {
            if (!isGameDetailsOpen) return;
            isGameDetailsOpen = false;
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(300)); Carousel.BeginAnimation(OpacityProperty, fadeIn);
            var slideDown = new DoubleAnimation(0, 200, TimeSpan.FromMilliseconds(300));
            var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, e) => InfoArea.Visibility = Visibility.Collapsed;
            InfoSlideTransform.BeginAnimation(TranslateTransform.YProperty, slideDown); InfoArea.BeginAnimation(OpacityProperty, fadeOut);
            SwitchToGamesFromInfo();
        }

        public void SwitchToGamesFromInfo() { currentInputHandler = gamesInputHandler; Dispatcher.InvokeAsync(() => { GamesListBox?.Focus(); Keyboard.Focus(GamesListBox); }); }

        public void SwapViewRight()
        {
            currentIndex = (currentIndex + 1) % 3; // 0=Home, 1=Friends, 2=Settings (simulado)
            UpdateViewVisibility();
        }

        public void SwapViewLeft()
        {
            currentIndex = 2 - ((currentIndex + 1) % 3); // 0=Home, 1=Friends, 2=Settings (simulado)
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
            if (currentList.Count == 0 || idx < 0 || idx >= currentList.Count) return;
            selectedIndex = idx; GamesListBox.SelectedIndex = selectedIndex;
            var item = GamesListBox.SelectedItem; if (item != null) Dispatcher.InvokeAsync(() => ScrollToCenterOfView(GamesListBox, item), DispatcherPriority.Input);
            var g = currentList[selectedIndex];

            _gameLoadCts?.Cancel();
            _gameLoadCts = new CancellationTokenSource();
            var token = _gameLoadCts.Token;

            Dispatcher.Invoke(() =>
            {
                // Atualização Visual Imediata
                if (g.Source == "System" && g.Title == "Loja")
                {
                    HeroTitle.Text = "Loja Steam"; Info_OwnedOn.Text = "SISTEMA"; Info_Playtime.Text = "—";
                    Info_AchievementsList.ItemsSource = null; GameFriendsList.ItemsSource = null; OwnersFriendsList.ItemsSource = null;
                }
                else
                {
                    BannerImage.Source = g.Cover ?? g.Icon ?? MakePlaceholderBitmap(1000, 400);
                    HeroTitle.Text = g.Title;
                    Info_OwnedOn.Text = g.Source; Info_Playtime.Text = $"{g.PlaytimeHours:0.0} hrs";
                    Info_CompletionPercent.Text = $"{g.ProgressPercent}%";
                    Info_CompletionBar.Value = g.ProgressPercent;
                    Info_LastPlayed.Text = g.LastPlayed.HasValue ? $"Jogado: {g.LastPlayed:d}" : "Nunca";

                    // Reset das listas para não mostrar dados velhos
                    Info_AchievementsList.ItemsSource = null;
                    Info_AchievementsList.Items.Clear();
                    GameFriendsList.ItemsSource = null;
                    OwnersFriendsList.ItemsSource = null;
                    Info_Screenshots.ItemsSource = null;

                    // Se tivermos achievements locais, mostra já
                    if (g.Achievements.Count > 0)
                        Info_AchievementsList.ItemsSource = g.Achievements;

                    if (int.TryParse(g.SteamAppId, out int appid))
                        Info_Screenshots.ItemsSource = TryGetLocalSteamScreenshots(connectedSteamId ?? "", appid);
                }

                if (g.Cover != null)
                {
                    Task.Run(async () =>
                    {
                        Color domin = await GetDominantColorAsync(g.Cover);

                        await Dispatcher.InvokeAsync(() => AnimateThemeToColor(domin));
                    });
                }
            });

            // Dados Pesados em Background
            if (!isShowingStoreView && steamConnected && !string.IsNullOrEmpty(g.SteamAppId) && g.Source != "System")
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (token.IsCancellationRequested) return;

                        if (int.TryParse(g.SteamAppId, out int appid))
                        {
                            var play = await GetPlaytimeHoursForAppAsync(connectedSteamId!, appid);
                            if (!token.IsCancellationRequested && play.HasValue)
                                await Dispatcher.InvokeAsync(() => Info_Playtime.Text = $"{play.Value:0.0} hrs");

                            // 1. Quem Joga Agora (Rápido - Cache)
                            await LoadFriendsPlayingGame(g.SteamAppId, token);

                            // 2. Quem Tem o Jogo (Lento - API)
                            await LoadFriendsWhoOwnGame(g.SteamAppId, token);

                            // 3. News
                            if (!token.IsCancellationRequested) await LoadGameNews(appid);
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

        void AnimateFadeIn(UIElement element)
        {
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(500));
            element.BeginAnimation(UIElement.OpacityProperty, fadeIn);
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

            if (g.IsStoreItem)
            {
                try
                {
                    // Abre na Steam instalada (Melhor experiência)
                    Process.Start(new ProcessStartInfo($"steam://store/{g.SteamAppId}") { UseShellExecute = true });
                }
                catch
                {
                    // Fallback para Browser
                    Process.Start(new ProcessStartInfo($"https://store.steampowered.com/app/{g.SteamAppId}/") { UseShellExecute = true });
                }
                return;
            }

            if (!g.IsInstalled && !string.IsNullOrEmpty(g.SteamAppId))
            {
                if (MessageBox.Show($"Este jogo não está instalado.\nQueres instalar '{g.Title}'?", "Instalar", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo($"steam://install/{g.SteamAppId}") { UseShellExecute = true });
                    }
                    catch { }
                }
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
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(PopulateGamesPanel);
                return;
            }

            if (isPopulating) return;
            isPopulating = true;

            try
            {
                if (!games.Any(g => g.Title == "Loja" && g.Source == "System"))
                {
                    games.Add(new GameEntry { Title = "Loja", Source = "System", Cover = null });
                }

                GamesListBox.ItemsSource = null;
                GamesListBox.ItemsSource = games;

                GamesListBox.CacheMode = new BitmapCache();

                GamesListBox.UpdateLayout();

                for (int i = 0; i < GamesListBox.Items.Count; i++)
                {
                    var container = GamesListBox.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                    if (container != null)
                    {
                        container.Opacity = 0;
                        container.RenderTransform = new TranslateTransform(0, 250);
                    }
                }

                var startTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                startTimer.Tick += (s, e) =>
                {
                    startTimer.Stop();
                    AnimateGamesEntrance();
                };
                startTimer.Start();
            }
            finally
            {
                isPopulating = false;
            }
        }

        void AnimateGamesEntrance()
        {
            double maxDuration = 0;

            for (int i = 0; i < GamesListBox.Items.Count; i++)
            {
                var container = GamesListBox.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                if (container == null) continue;

                // Preparação
                container.Opacity = 0;
                var transform = new TranslateTransform(0, 250);
                container.RenderTransform = transform;

                // Tempos (Mais rápido entre jogos = mais fluido)
                TimeSpan delay = TimeSpan.FromMilliseconds(i * 40);
                TimeSpan duration = TimeSpan.FromMilliseconds(600);

                var fade = new DoubleAnimation(0, 1, duration) { BeginTime = delay };

                var slide = new DoubleAnimation(250, 0, duration)
                {
                    BeginTime = delay,
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };

                container.BeginAnimation(UIElement.OpacityProperty, fade);
                transform.BeginAnimation(TranslateTransform.YProperty, slide);

                double totalTime = delay.TotalMilliseconds + duration.TotalMilliseconds;
                if (totalTime > maxDuration) maxDuration = totalTime;
            }

            // LIMPEZA E SELEÇÃO
            var cleanupTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(maxDuration + 100) };
            cleanupTimer.Tick += (s, e) =>
            {
                cleanupTimer.Stop();

                GamesListBox.CacheMode = null;

                if (games.Count > 0)
                {
                    SelectIndex(0);
                }
            };
            cleanupTimer.Start();
        }

        public void ToggleStoreView(bool show)
        {
            isShowingStoreView = show;

            if (show)
            {
                // MODO LOJA: Carrega Destaques da Steam
                Dispatcher.Invoke(() =>
                {
                    StatusLabel.Text = "A carregar Loja Steam...";
                    LoadingOverlay.Visibility = Visibility.Visible;
                });

                _ = Task.Run(async () =>
                {
                    // 1. Busca a Loja Real
                    storeGames = await GetSteamStoreFeaturedAsync();

                    await Dispatcher.InvokeAsync(() =>
                    {
                        // 2. Mostra na Lista
                        GamesListBox.ItemsSource = null;
                        GamesListBox.ItemsSource = storeGames;

                        StatusLabel.Text = "MODO LOJA (ESC para Sair)";
                        LoadingOverlay.Visibility = Visibility.Collapsed;

                        if (storeGames.Count > 0) SelectIndex(0);
                    });
                });
            }
            else
            {
                // MODO BIBLIOTECA (Volta aos teus jogos)
                PopulateGamesPanel(); // Usa a tua função normal de popular
                StatusLabel.Text = "Biblioteca";
                InfoArea.Visibility = Visibility.Visible;
            }
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

                if (steamConnected)
                {
                    await Dispatcher.InvokeAsync(() => StatusLabel.Text = "A sincronizar biblioteca Steam...");
                    await MergeUninstalledGamesAsync();
                }

                _allGamesMasterList = games.ToList();
                games = _allGamesMasterList.OrderBy(g => g.Title).Where(g => g.IsInstalled == true).ToList();

                await Dispatcher.InvokeAsync(() => PopulateGamesPanel());

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

        async Task MergeUninstalledGamesAsync()
        {
            if (string.IsNullOrEmpty(steamApiKey) || string.IsNullOrEmpty(connectedSteamId)) return;

            try
            {
                var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={steamApiKey}&steamid={connectedSteamId}&include_appinfo=1&include_played_free_games=1";
                var root = await SteamApiGetJson(url);

                if (root.HasValue && root.Value.TryGetProperty("response", out var resp) && resp.TryGetProperty("games", out var gamesArr))
                {
                    foreach (var g in gamesArr.EnumerateArray())
                    {
                        string appid = g.GetProperty("appid").GetInt32().ToString();
                        string name = g.GetProperty("name").GetString() ?? "App";

                        bool alreadyExists = games.Any(x => x.SteamAppId == appid);

                        if (!alreadyExists)
                        {
                            // Se não existe, adiciona como "Nuvem"
                            var entry = new GameEntry
                            {
                                Title = name,
                                SteamAppId = appid,
                                Source = "Steam Library",
                                IsInstalled = false,
                                Cover = null
                            };

                            try
                            {
                                string coverUrl = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appid}/library_600x900.jpg";
                                _ = TryDownloadStoreCover(entry, coverUrl);
                            }
                            catch { }

                            games.Add(entry);
                        }
                    }
                }
            }
            catch { }
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

        // --- FASE 1: CARGA RÁPIDA (Playtime e Last Played) ---
        async Task FetchBasicStatsAsync()
        {
            if (string.IsNullOrEmpty(connectedSteamId) || string.IsNullOrEmpty(steamApiKey)) return;

            try
            {
                // Pedido ÚNICO que traz todos os jogos de uma vez
                var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={steamApiKey}&steamid={connectedSteamId}&include_appinfo=1&include_played_free_games=1";
                var root = await SteamApiGetJson(url);

                if (root.HasValue && root.Value.TryGetProperty("response", out var resp) && resp.TryGetProperty("games", out var gamesArr))
                {
                    foreach (var gEl in gamesArr.EnumerateArray())
                    {
                        int appid = gEl.GetProperty("appid").GetInt32();

                        // Encontra o jogo na nossa lista local
                        var game = games.FirstOrDefault(x => x.SteamAppId == appid.ToString());
                        if (game != null)
                        {
                            // Preenche apenas o essencial para a Recomendação funcionar
                            if (gEl.TryGetProperty("playtime_forever", out var pt))
                                game.PlaytimeHours = pt.GetInt32() / 60.0;

                            if (gEl.TryGetProperty("rtime_last_played", out var last))
                            {
                                var unixTime = last.GetInt64();
                                if (unixTime > 0)
                                    game.LastPlayed = DateTimeOffset.FromUnixTimeSeconds(unixTime).DateTime;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        async Task FetchSteamDataForAllGamesAsync()
        {
            if (string.IsNullOrEmpty(connectedSteamId)) return;

            try
            {
                await Dispatcher.InvokeAsync(() => StatusLabel.Text = "A atualizar dados da Steam...");

                // 1. Tenta obter TODOS os playtimes de uma vez (Mais rápido)
                Dictionary<int, int> playtimeByApp = new Dictionary<int, int>();
                if (!string.IsNullOrEmpty(steamApiKey))
                {
                    try
                    {
                        var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={steamApiKey}&steamid={connectedSteamId}&include_appinfo=0&include_played_free_games=1";
                        var root = await SteamApiGetJson(url);
                        if (root.HasValue && root.Value.TryGetProperty("response", out var resp) && resp.TryGetProperty("games", out var gamesArr))
                        {
                            foreach (var gEl in gamesArr.EnumerateArray())
                            {
                                if (gEl.TryGetProperty("appid", out var aid) && gEl.TryGetProperty("playtime_forever", out var pt))
                                {
                                    playtimeByApp[aid.GetInt32()] = pt.GetInt32();
                                }
                            }
                        }
                    }
                    catch { }
                }

                // 2. Itera sobre os jogos que já encontrámos no PC
                // Nota: Usamos ToList() para criar uma cópia segura e evitar erros se a lista mudar
                var steamGames = games.Where(g => !string.IsNullOrEmpty(g.SteamAppId)).ToList();

                // Limitador para não bloquear a API (6 pedidos simultâneos)
                var sem = new SemaphoreSlim(6);
                var tasks = new List<Task>();

                foreach (var g in steamGames)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        if (!int.TryParse(g.SteamAppId, out int appid)) return;

                        await sem.WaitAsync();
                        try
                        {
                            // PLAYTIME
                            if (playtimeByApp.TryGetValue(appid, out var minutes))
                            {
                                g.PlaytimeHours = minutes / 60.0;
                            }
                            else
                            {
                                // Fallback: Se não estava na lista geral, tenta pedir específico
                                var play = await GetPlaytimeHoursForAppAsync(connectedSteamId, appid);
                                if (play.HasValue) g.PlaytimeHours = play.Value;
                            }

                            // Reviews
                            string reviewScore = await GetGameReviewSummaryAsync(appid);

                            // Achievements c/ raridade
                            if (!string.IsNullOrEmpty(steamApiKey))
                            {
                                var myUnlockedNames = await GetPlayerAchievementsAsync(connectedSteamId, appid);

                                var schema = await GetGameSchemaAsync(appid);

                                if (myUnlockedNames.Count > 0 && schema.Count > 0)
                                {
                                    var fullList = new List<AchievementDetail>();

                                    foreach (string apiName in myUnlockedNames)
                                    {
                                        if (schema.ContainsKey(apiName))
                                        {
                                            var ach = schema[apiName];
                                            ach.Achieved = true;
                                            fullList.Add(ach);
                                        }
                                        else
                                        {
                                            fullList.Add(new AchievementDetail { DisplayName = apiName, Achieved = true });
                                        }
                                    }
                                    g.Achievements = fullList;
                                }
                                int total = await GetTotalAchievementsCount(appid);
                                if (total > 0) g.ProgressPercent = (int)((double)myUnlockedNames.Count / total * 100);
                            }

                            // C. ATUALIZAR UI EM TEMPO REAL (Se este jogo estiver selecionado)
                            await Dispatcher.InvokeAsync(() =>
                            {
                                if (selectedIndex >= 0 && selectedIndex < games.Count && games[selectedIndex] == g)
                                {
                                    // Só atualiza os textos para não piscar a imagem
                                    Info_Playtime.Text = $"{g.PlaytimeHours:0.0} hrs";
                                    Info_CompletionPercent.Text = $"{g.ProgressPercent}%";
                                    Info_CompletionBar.Value = g.ProgressPercent;

                                    if (!string.IsNullOrEmpty(reviewScore))
                                        Info_OwnedOn.Text = $"STEAM  •  {reviewScore.ToUpper()}";
                                    else
                                        Info_OwnedOn.Text = "STEAM";

                                    // Se achievements chegaram agora, atualiza a lista
                                    if (g.Achievements.Count > 0)
                                    {
                                        Info_AchievementsList.ItemsSource = null;
                                        Info_AchievementsList.ItemsSource = g.Achievements;
                                    }
                                }
                            });

                            var storeDetails = await GetGameGenreAsync(appid);
                            g.Genre = storeDetails;
                        }
                        catch { }
                        finally { sem.Release(); }
                    }));
                }

                await Task.WhenAll(tasks);
                await Dispatcher.InvokeAsync(() => StatusLabel.Text = "Dados Steam atualizados.");
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => StatusLabel.Text = "Erro Steam: " + ex.Message);
            }
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

                int level = await GetSteamLevelAsync(friend.SteamId);
                await Dispatcher.InvokeAsync(() => friend.SteamLevel = level);
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
                    // Batching de 100 para eficiência
                    for (int i = 0; i < ids.Count; i += 100)
                    {
                        var batch = string.Join(",", ids.Skip(i).Take(100));
                        var url2 = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={steamApiKey}&steamids={batch}";
                        var root2 = await SteamApiGetJson(url2);
                        if (root2.HasValue && root2.Value.TryGetProperty("response", out var r) && r.TryGetProperty("players", out var p))
                        {
                            foreach (var pl in p.EnumerateArray())
                            {
                                summaries.Add(new PlayerSummary
                                {
                                    SteamId = pl.TryGetProperty("steamid", out var s) ? s.GetString() ?? "" : "",
                                    PersonaName = pl.TryGetProperty("personaname", out var pn) ? pn.GetString() ?? "U" : "U",
                                    AvatarFull = pl.TryGetProperty("avatarfull", out var af) ? af.GetString() ?? "" : "",
                                    ProfileUrl = pl.TryGetProperty("profileurl", out var pu) ? pu.GetString() ?? "" : "",
                                    GameExtraInfo = pl.TryGetProperty("gameextrainfo", out var ge) ? ge.GetString() ?? "" : "",
                                    GameId = pl.TryGetProperty("gameid", out var gid) ? gid.GetString() ?? "0" : "0"
                                });
                            }
                        }
                    }
                    // AQUI ESTÁ O SEGREDO: Guardar na cache para ser usado na InfoArea
                    _cachedFriendsList = summaries;
                    await Dispatcher.InvokeAsync(() => FriendListBox.ItemsSource = _cachedFriendsList);
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

        async Task<Dictionary<string, AchievementDetail>> GetGameSchemaAsync(int appid)
        {
            var dict = new Dictionary<string, AchievementDetail>();
            if (string.IsNullOrEmpty(steamApiKey)) return dict;

            try
            {
                var url = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={steamApiKey}&appid={appid}&l=portuguese";
                var root = await SteamApiGetJson(url);

                if (root.HasValue &&
                    root.Value.TryGetProperty("game", out var game) &&
                    game.TryGetProperty("availableGameStats", out var stats) &&
                    stats.TryGetProperty("achievements", out var achArray))
                {
                    foreach (var a in achArray.EnumerateArray())
                    {
                        string name = a.GetProperty("name").GetString() ?? "";
                        if (!string.IsNullOrEmpty(name))
                        {
                            dict[name] = new AchievementDetail
                            {
                                ApiName = name,
                                DisplayName = a.TryGetProperty("displayName", out var dn) ? dn.GetString() : name,
                                Description = a.TryGetProperty("description", out var d) ? d.GetString() : "",
                                IconUrl = a.TryGetProperty("icon", out var i) ? i.GetString() : ""
                            };
                        }
                    }
                }
            }
            catch { }
            return dict;
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

        // --- API LOJA ---
        async Task<List<GameEntry>> GetSteamStoreFeaturedAsync()
        {
            var storeList = new List<GameEntry>();
            try
            {
                // API Pública (CC=PT para preços em Euros e região Portugal)
                var url = "https://store.steampowered.com/api/featuredcategories?CC=PT&l=portuguese";
                var root = await SteamApiGetJson(url);

                if (root.HasValue)
                {
                    // Categorias interessantes
                    string[] categories = { "top_sellers", "specials", "new_releases" };

                    foreach (var catName in categories)
                    {
                        if (root.Value.TryGetProperty(catName, out var cat) && cat.TryGetProperty("items", out var items))
                        {
                            foreach (var item in items.EnumerateArray())
                            {
                                string title = item.GetProperty("name").GetString() ?? "";
                                int appid = item.GetProperty("id").GetInt32();
                                string image = item.GetProperty("large_capsule_image").GetString() ?? "";

                                string price = "N/A";
                                int discount = 0;

                                // Ler Preço e Desconto
                                if (item.TryGetProperty("final_price", out var p))
                                {
                                    int val = p.GetInt32();
                                    price = val == 0 ? "Grátis" : $"{(val / 100.0):0.00}€";
                                }
                                if (item.TryGetProperty("discount_percent", out var d))
                                {
                                    discount = d.GetInt32();
                                }

                                // Evita duplicados
                                if (!storeList.Any(x => x.SteamAppId == appid.ToString()))
                                {
                                    var entry = new GameEntry
                                    {
                                        Title = title,
                                        SteamAppId = appid.ToString(),
                                        Source = "Loja Steam", // Fonte diferente
                                        IsStoreItem = true,    // Marca como item de loja
                                        PriceDisplay = price,
                                        DiscountPercent = discount
                                    };

                                    // Inicia download da capa em background
                                    _ = TryDownloadStoreCover(entry, image);

                                    storeList.Add(entry);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return storeList; // Retorna a lista misturada de Top Sellers e Promoções
        }

        async Task TryDownloadStoreCover(GameEntry g, string url)
        {
            try
            {
                using var res = await http.GetAsync(url);
                if (res.IsSuccessStatusCode)
                {
                    using var st = await res.Content.ReadAsStreamAsync();
                    var bmp = LoadBitmapImageFromStream(st);
                    // Atualiza a UI quando a imagem chegar
                    await Dispatcher.InvokeAsync(() => g.Cover = bmp);
                }
            }
            catch { }
        }

        // --- HELPERS ---
        public T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T t) return t; var res = FindChild<T>(child); if (res != null) return res; }
            return null;
        }

        public T? FindParent<T>(DependencyObject child, string name) where T : FrameworkElement
        {
            while (child != null) { if (child is T t && t.Name == name) return t; child = VisualTreeHelper.GetParent(child); }
            return null;
        }

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

        async Task<Dictionary<string, double>> GetGlobalAchievementsPercentagesAsync(int appid)
        {
            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var url = $"https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid={appid}";
                var root = await SteamApiGetJson(url);

                if (root.HasValue && root.Value.TryGetProperty("achievementpercentages", out var ap) && ap.TryGetProperty("achievements", out var list))
                {
                    foreach (var item in list.EnumerateArray())
                    {
                        string name = item.GetProperty("name").GetString() ?? "";
                        double percent = item.GetProperty("percent").GetDouble();
                        if (!string.IsNullOrEmpty(name)) dict[name] = percent;
                    }
                }
            }
            catch { }
            return dict;
        }

        async Task<string> GetGameReviewSummaryAsync(int appid)
        {
            try
            {
                var url = $"https://store.steampowered.com/appreviews/{appid}?json=1&language=all";
                var root = await SteamApiGetJson(url);

                if (root.HasValue && root.Value.TryGetProperty("query_summary", out var summary))
                {
                    string score = summary.GetProperty("review_score_desc").GetString() ?? "";
                    return score;
                }
            }
            catch { }
            return "";
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

        async Task<int> GetSteamLevelAsync(string steamId64)
        {
            if (string.IsNullOrEmpty(steamApiKey)) return 0;
            try
            {
                var url = $"https://api.steampowered.com/IPlayerService/GetSteamLevel/v1/?key={steamApiKey}&steamid={steamId64}";
                var root = await SteamApiGetJson(url);
                if (root.HasValue && root.Value.TryGetProperty("response", out var r) && r.TryGetProperty("player_level", out var lvl))
                {
                    return lvl.GetInt32();
                }
            }
            catch { }
            return 0;
        }

        // --- DUALSENSE & WINDOW ---
        async Task StartupAndLogin()
        {
            await PlayStartupAnimation();

            LoadUsers();
        }


        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ToggleFullscreen();
            _controller = new PlayStationController();
            _controller.StateChanged += Controller_StateChanged;
            if (!_controller.Start()) { /* Log */ }

            await PlayStartupAnimation();
            //await FoxyStartupAnimation();

            LoadUsers();

            if (availableUsers.Count == 0)
            {
                ShowAddUserModal_Click(null, null);
            }
            else
            {
                UserSelectionOverlay.Visibility = Visibility.Visible;
            }
        }

        void LoadUsers()
        {
            availableUsers.Clear();
            if (Directory.Exists(usersDir))
            {
                foreach (var dir in Directory.GetDirectories(usersDir))
                {
                    availableUsers.Add(new UserProfile { Username = new DirectoryInfo(dir).Name });
                }
            }
            UserListBox.ItemsSource = null;
            UserListBox.ItemsSource = availableUsers;
        }

        private void UserButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string username)
            {
                LoginUser(username);
            }
        }

        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            string newName = PromptForText("Novo Utilizador", "Nome do ustilizador:", "");
            if (!string.IsNullOrWhiteSpace(newName))
            {
                string newDir = Path.Combine(usersDir, newName);
                Directory.CreateDirectory(newDir);

                LoginUser(newName);
            }
        }

        private void ShowAddUserModal_Click(object sender, RoutedEventArgs e)
        {
            TxtNewUserName.Text = "";
            TxtNewUserApiKey.Text = "";
            TxtNewSteamLogin.Text = "";

            try
            {
                Steamworks.SteamClient.Init(480);
                if (Steamworks.SteamClient.IsValid)
                {
                    TxtNewUserName.Text = Steamworks.SteamClient.Name;
                }

                string autoUser = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "AutoLoginUser", "") as string;
                if (!string.IsNullOrEmpty(autoUser))
                    TxtNewSteamLogin.Text = autoUser;
            }
            catch { }
            AddUserModal.Visibility = Visibility.Visible;
            UserSelectionOverlay.Visibility = Visibility.Collapsed;
        }

        private void CancelAddUser_Click(object sender, RoutedEventArgs e)
        {
            AddUserModal.Visibility = Visibility.Collapsed;
            if (availableUsers.Count > 0) UserSelectionOverlay.Visibility = Visibility.Visible;
        }

        private void ConfirmAddUser_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtNewUserName.Text.Trim();
            string steamLogin = TxtNewSteamLogin.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show("Nome obrigatório"); return; }
            if (string.IsNullOrWhiteSpace(steamLogin)) { MessageBox.Show("Login Steam obrigatório"); return; }

            AddUserModal.Visibility = Visibility.Collapsed;

            // Cria user e inicia processo de login assistido
            string newDir = Path.Combine(usersDir, name);
            Directory.CreateDirectory(newDir);
            CurrentUserDir = newDir;
            if (!string.IsNullOrWhiteSpace(TxtNewUserApiKey.Text)) SaveSteamApiKey(TxtNewUserApiKey.Text.Trim());
            File.WriteAllText(Path.Combine(newDir, "steam_username.dat"), steamLogin);

            _ = Task.Run(async () =>
            {
                await RegisterNewSteamAccountAsync(steamLogin);
                await Dispatcher.InvokeAsync(() => LoginUser(name));
            });
        }

        void LoginUser(string username)
        {
            currentUser = new UserProfile { Username = username };
            CurrentUserDir = Path.Combine(usersDir, username);
            Directory.CreateDirectory(CurrentUserDir);
            Directory.CreateDirectory(chatLogDir);

            UserSelectionOverlay.Visibility = Visibility.Collapsed;
            StatusLabel.Text = $"Utilizador: {username}";

            InitializeUserData();
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            // Reinicia para escolher outro user
            System.Diagnostics.Process.Start(Environment.ProcessPath!);
            Application.Current.Shutdown();
        }

        void InitializeUserData()
        {
            LoadSavedTheme();

            // INÍCIO DO PROCESSO DE CARREGAMENTO
            _ = Task.Run(async () =>
            {
                string usernameFile = Path.Combine(CurrentUserDir, "steam_username.dat");
                if (File.Exists(usernameFile))
                {
                    string steamLogin = File.ReadAllText(usernameFile).Trim();
                    if (!string.IsNullOrEmpty(steamLogin))
                        await SwitchSteamAccountAsync(steamLogin);
                }

                // 1. Carregar Credenciais
                LoadSteamApiKey();
                var savedId = LoadConnectedSteamId();

                // Atualiza o Header se tivermos ID (para a foto aparecer logo)
                if (!string.IsNullOrEmpty(savedId))
                {
                    connectedSteamId = savedId;
                    await UpdateHeaderUI();
                }

                bool success = false;

                // 1. Se já estiver ligada, não fazemos Init de novo (evita o crash)
                if (Steamworks.SteamClient.IsValid)
                {
                    success = true;
                }
                else
                {
                    // 2. Se não estiver, tentamos ligar (com retries caso a Steam esteja a abrir)
                    for (int i = 0; i < 5; i++) // Tenta durante 5 segundos
                    {
                        try
                        {
                            Steamworks.SteamClient.Init(480);
                            if (Steamworks.SteamClient.IsValid)
                            {
                                success = true;
                                break;
                            }
                        }
                        catch
                        {
                            await Task.Delay(1000); // Espera 1s antes de tentar de novo
                        }
                    }
                }

                if (success)
                {
                    // 3. Configura o Chat (Remove primeiro para não duplicar eventos)
                    Steamworks.SteamFriends.OnChatMessage -= OnSteamChatMessage;
                    Steamworks.SteamFriends.OnChatMessage += OnSteamChatMessage;
                    Steamworks.SteamFriends.ListenForFriendsMessages = true;

                    // 4. Atualiza a UI
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ProfileName.Text = Steamworks.SteamClient.Name;
                        StatusLabel.Text = $"Steam: {Steamworks.SteamClient.Name}";
                        connectedSteamId = Steamworks.SteamClient.SteamId.ToString();
                    });
                }
                else
                {
                    await Dispatcher.InvokeAsync(() => StatusLabel.Text = "Steam: Offline / Web Mode");
                }

                // 2. SCAN AOS JOGOS (CRÍTICO: Isto tem de acontecer ANTES de pedir dados à Steam)
                await RescanGamesAsync();

                // 3. CARREGA ESTATÍSTICAS BÁSICAS (Rápido - 1 Pedido)
                if (!string.IsNullOrEmpty(connectedSteamId))
                {
                    await FetchBasicStatsAsync();
                }

                // 4. MOSTRA O WELCOME SCREEN IMEDIATAMENTE! 🚀
                await Dispatcher.InvokeAsync(() =>
                {
                    if (games.Count > 0)
                    {
                        PopulateGamesPanel();
                        GenerateRecommendation();
                    }
                });

                // 5. Se tivermos login, vamos buscar os dados EXTRA (Playtime, Amigos, etc.)
                if (!string.IsNullOrEmpty(connectedSteamId))
                {
                    if (_cachedFriendsList.Count == 0) await FetchAndShowFriendsAsync();

                    // Carrega detalhes pesados (Reviews, Géneros, etc)
                    await FetchSteamDataForAllGamesAsync();
                }
            });
        }

        async Task SwitchSteamAccountAsync(string steamLoginName)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                LoadingText.Text = $"A preparar Steam para: {steamLoginName}...";
            });

            await Task.Run(async () =>
            {
                try
                {
                    string steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                    if (string.IsNullOrEmpty(steamPath)) throw new Exception("Steam não encontrada.");

                    string vdfPath = Path.Combine(steamPath, "config", "loginusers.vdf");
                    string exePath = Path.Combine(steamPath, "steam.exe");

                    var procs = Process.GetProcessesByName("steam");
                    if (procs.Length > 0)
                    {
                        foreach (var p in procs)
                        {
                            try
                            {
                                p.Kill();
                            }
                            catch { }
                        }

                        foreach (var p in Process.GetProcessesByName("steamwebhelper"))
                        {
                            try
                            {
                                p.Kill();
                            }
                            catch { }
                        }

                        await Task.Delay(2000);
                    }

                    if (File.Exists(vdfPath))
                    {
                        try
                        {
                            var vdf = KeyValue.LoadAsText(vdfPath);
                            bool userFound = false;

                            foreach (var child in vdf.Children)
                            {
                                string accName = child["AccountName"].Value;

                                if (accName.Equals(steamLoginName, StringComparison.OrdinalIgnoreCase))
                                {
                                    child["MostRecent"].Value = "1";
                                    child["AllowAutoLogin"].Value = "1";
                                    child["RememberPassword"].Value = "1";
                                    child["WantsOfflineMode"].Value = "0";
                                    child["SkipOfflineModeWarning"].Value = "1";
                                    child["Timestamp"].Value = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                                    userFound = true;
                                }
                                else
                                {
                                    child["MostRecent"].Value = "0";
                                    child["AllowAutoLogin"].Value = "0"; // Impeque que outros tentem entrar (opcional)
                                }
                            }

                            if (userFound)
                            {
                                vdf.SaveToFile(vdfPath, false);
                            }
                        }
                        catch (Exception ex)
                        {
                            await Dispatcher.InvokeAsync(() => MessageBox.Show($"Aviso VDF: {ex.Message}"));
                        }
                    }

                    Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "AutoLoginUser", steamLoginName);
                    Registry.SetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "RememberPassword", 1);
                    try { Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", true)?.DeleteValue("ActiveProcess", false); } catch { }

                    if (File.Exists(exePath))
                    {
                        Process.Start(new ProcessStartInfo(exePath, $"-silent -login {steamLoginName}") { UseShellExecute = true });
                    }

                    await Dispatcher.InvokeAsync(() => LoadingText.Text = "À espera da Steam...");
                    int retries = 0;
                    while (retries < 60)
                    {
                        await Task.Delay(1000);
                        if (await Task.Run(() => { try { Steamworks.SteamClient.Init(480); return Steamworks.SteamClient.IsValid; } catch { return false; } }))
                        {
                            break;
                        }
                        retries++;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Erro ao trocar conta Steam: " + ex.Message);
                }
            });
            await Dispatcher.InvokeAsync(() => LoadingOverlay.Visibility = Visibility.Collapsed);
        }

        async Task RegisterNewSteamAccountAsync(string username)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                LoadingText.Text = $"A abrir Steam para: {username}...";
            });

            await Task.Run(async () =>
            {
                try
                {
                    var procs = Process.GetProcessesByName("steam");
                    if (procs.Length > 0)
                    {
                        try { Process.Start(new ProcessStartInfo("steam://exit") { UseShellExecute = true }); } catch { }
                        await Task.Delay(3000); // Dá tempo para sincronizar nuvem e fechar

                        // Se ainda estiver aberta, força o fecho
                        foreach (var p in Process.GetProcessesByName("steam")) { try { p.Kill(); } catch { } }
                        foreach (var p in Process.GetProcessesByName("steamwebhelper")) { try { p.Kill(); } catch { } }
                        await Task.Delay(2000);
                    }

                    string steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                    if (string.IsNullOrEmpty(steamPath)) throw new Exception("Steam não encontrada.");
                    string exePath = Path.Combine(steamPath, "steam.exe");

                    //ToggleFullscreen();

                    if (File.Exists(exePath))
                    {
                        throw new Exception($"Steam não encontrada em: {steamPath}");
                    }

                    Process.Start(new ProcessStartInfo(exePath, $"-silent -login \"{username}\"") { UseShellExecute = true });

                    await Dispatcher.InvokeAsync(() => LoadingText.Text = "Por favor, faz login na janela da Steam (QR/Senha).");

                    bool loggedIn = false;

                    for (int i = 0; i < 300; i++)
                    {
                        await Task.Delay(1000);

                        bool isRunning = await Task.Run(() =>
                        {
                            try
                            {
                                Steamworks.SteamClient.Init(480);
                                return Steamworks.SteamClient.IsValid;
                            }
                            catch { return false; }
                        });

                        if (isRunning)
                        {
                            loggedIn = true;
                            break;
                        }
                    }

                    if (loggedIn)
                        ToggleFullscreen();

                    if (!loggedIn)
                    {
                        throw new Exception("Tempo Esgotado. Não foi detetado login.");
                    }
                }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(() => MessageBox.Show("Erro ao registar conta: " + ex.Message));
                }
            });

            await Dispatcher.InvokeAsync(() => LoadingOverlay.Visibility = Visibility.Collapsed);
        }

        private void Window_Closed(object sender, EventArgs e) { _controller?.Stop(); hwMonitor?.Close(); Steamworks.SteamClient.Shutdown(); }

        private void Controller_StateChanged(DualSenseInputState state)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastNav).TotalMilliseconds < 150) return;

            if (_prevDsState != null)
            {
                FireEdge(state.CrossButton, _prevDsState.CrossButton, () => currentInputHandler?.OnAccept());
                FireEdge(state.CircleButton, _prevDsState.CircleButton, () => currentInputHandler?.OnCancel());
                FireEdge(state.R1Button, _prevDsState.R1Button, () => SwapViewRight());
                FireEdge(state.DPadRightButton, _prevDsState.DPadRightButton, () => currentInputHandler?.OnRight());
                FireEdge(state.DPadLeftButton, _prevDsState.DPadLeftButton, () => currentInputHandler?.OnLeft());
                FireEdge(state.DPadUpButton, _prevDsState.DPadUpButton, () => currentInputHandler?.OnUp());
                FireEdge(state.DPadDownButton, _prevDsState.DPadDownButton, () => currentInputHandler?.OnDown());
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
        void ApplyThemeColors(Color[] c, int gradType)
        {
            // A. BACKGROUND
            Brush bgBrush;
            if (gradType == 1)
            {
                var r = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.3), Center = new Point(0.5, 0.5), RadiusX = 1.0, RadiusY = 1.0 };
                r.GradientStops.Add(new GradientStop(c[2], 0.0)); r.GradientStops.Add(new GradientStop(c[3], 1.0)); bgBrush = r;
            }
            else
            {
                var l = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                l.GradientStops.Add(new GradientStop(c[2], 0.0)); l.GradientStops.Add(new GradientStop(c[3], 0.8)); bgBrush = l;
            }
            MainBackground.Background = bgBrush;

            // B. RECURSOS
            SetRes("AccentBrush", new SolidColorBrush(c[0]));
            SetRes("textSecondary", new SolidColorBrush(c[1]));
            SetRes("PanelBackgroundBrush", new SolidColorBrush(c[4]));
            SetRes("TextPrimaryBrush", new SolidColorBrush(c[5]));
            SetRes("HeroTitleBrush", new SolidColorBrush(c[6]));
            SetRes("ChatBrush", new SolidColorBrush(c[7]));
            SetRes("TertiaryBrush", new SolidColorBrush(c[8]));

            // NOVOS RECURSOS
            SetRes("InfoPrimaryBrush", new SolidColorBrush(c[9]));
            SetRes("InfoSecondaryBrush", new SolidColorBrush(c[10]));
            SetRes("FriendNameBrush", new SolidColorBrush(c[11]));
            SetRes("FriendStatusBrush", new SolidColorBrush(c[12]));
            SetRes("ButtonTextNormalBrush", new SolidColorBrush(c[13]));
            SetRes("ButtonTextHoverBrush", new SolidColorBrush(c[14]));
        }
        void SetRes(string k, Brush b) { if (Resources.Contains(k)) Resources.Remove(k); Resources.Add(k, b); }
        private void BtnThemeRed_Click(object sender, RoutedEventArgs e)
        {
            // 1. Definir as Cores Base
            var accent = (Color)ColorConverter.ConvertFromString("#FF4758");
            var bgStart = (Color)ColorConverter.ConvertFromString("#1A0A0D");
            var bgEnd = (Color)ColorConverter.ConvertFromString("#A81826");
            var pnl = (Color)ColorConverter.ConvertFromString("#D9101010");
            var sec = Colors.Gray;
            var pri = Colors.White;

            // 2. Construir o Array de 15 Cores
            Color[] c = new Color[15];
            c[0] = accent;
            c[1] = sec;
            c[2] = bgStart;
            c[3] = bgEnd;
            c[4] = pnl;
            c[5] = pri;          // Global Primary
            c[6] = pri;          // Hero Title
            c[7] = sec;          // Chat
            c[8] = Color.FromArgb(50, 255, 71, 88); // Terciária (Vermelho subtil)

            // --- NOVAS ---
            c[9] = pri;          // Info Primary
            c[10] = sec;         // Info Secondary
            c[11] = pri;         // Friend Name
            c[12] = sec;         // Friend Status
            c[13] = pri;         // Btn Text Normal
            c[14] = Colors.Black;// Btn Text Hover (Preto contrasta bem com o Vermelho)

            ApplyThemeColors(c, 0); // 0 = Linear Gradient
            SaveTheme("Red");
        }

        private void BtnThemeBlue_Click(object sender, RoutedEventArgs e)
        {
            var accent = (Color)ColorConverter.ConvertFromString("#00A8E8");
            var bgStart = (Color)ColorConverter.ConvertFromString("#000814");
            var bgEnd = (Color)ColorConverter.ConvertFromString("#003566");
            var pnl = (Color)ColorConverter.ConvertFromString("#D9051020");
            var sec = Colors.LightBlue; // Texto secundário azulado
            var pri = Colors.White;

            Color[] c = new Color[15];
            c[0] = accent;
            c[1] = sec;
            c[2] = bgStart;
            c[3] = bgEnd;
            c[4] = pnl;
            c[5] = pri;
            c[6] = pri;
            c[7] = sec;
            c[8] = Color.FromArgb(50, 0, 168, 232); // Terciária Azulada

            // --- NOVAS ---
            c[9] = pri;
            c[10] = sec;
            c[11] = pri;
            c[12] = sec;
            c[13] = pri;
            c[14] = Colors.Black; // Texto preto no botão Ciano

            ApplyThemeColors(c, 0);
            SaveTheme("Blue");
        }

        private void BtnThemeDark_Click(object sender, RoutedEventArgs e)
        {
            var accent = Colors.White; // B&W Theme
            var bgStart = Colors.Black;
            var bgEnd = (Color)ColorConverter.ConvertFromString("#111111");
            var pnl = (Color)ColorConverter.ConvertFromString("#E6000000");
            var sec = Colors.DarkGray;
            var pri = Colors.White;

            Color[] c = new Color[15];
            c[0] = accent;
            c[1] = sec;
            c[2] = bgStart;
            c[3] = bgEnd;
            c[4] = pnl;
            c[5] = pri;
            c[6] = pri;
            c[7] = sec;
            c[8] = Color.FromArgb(50, 255, 255, 255); // Terciária Branca translúcida

            // --- NOVAS ---
            c[9] = pri;
            c[10] = sec;
            c[11] = pri;
            c[12] = sec;
            c[13] = pri;
            c[14] = Colors.Black; // Texto preto no botão Branco

            ApplyThemeColors(c, 0);
            SaveTheme("Dark");
        }

        private void BtnThemePichal_Click(object sender, RoutedEventArgs e)
        {
            var accent = (Color)ColorConverter.ConvertFromString("#FFFFC20E"); // Amarelo Ouro
            var bgStart = (Color)ColorConverter.ConvertFromString("#FF004D25"); // Verde Topo
            var bgEnd = (Color)ColorConverter.ConvertFromString("#FF020F05");   // Verde Fundo
            var pnl = (Color)ColorConverter.ConvertFromString("#E60A2610");     // Painel Verde
            var sec = (Color)ColorConverter.ConvertFromString("#FFD4AF37");     // Texto Dourado
            var pri = Colors.White;

            Color[] c = new Color[15];
            c[0] = accent;
            c[1] = sec;
            c[2] = bgStart;
            c[3] = bgEnd;
            c[4] = pnl;
            c[5] = pri;
            c[6] = pri;
            c[7] = sec;
            c[8] = Color.FromArgb(60, 255, 255, 255); // Terciária

            // --- NOVAS ---
            c[9] = pri;          // Info Pri
            c[10] = sec;         // Info Sec (Dourado)
            c[11] = pri;         // Friend Name
            c[12] = sec;         // Friend Status (Dourado)
            c[13] = pri;         // Botão Texto Normal (Branco)
            c[14] = Colors.Black;// Botão Texto Hover (Preto em fundo Amarelo)

            ApplyThemeColors(c, 0);
            SaveTheme("Pichal");
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

        async Task LoadFriendsPlayingGame(string appidStr, CancellationToken token)
        {
            await Dispatcher.InvokeAsync(() => GameFriendsList.ItemsSource = null);
            if (string.IsNullOrEmpty(appidStr) || _cachedFriendsList.Count == 0) return;
            var playing = new List<FriendPlayInfo>();
            foreach (var f in _cachedFriendsList)
            {
                if (token.IsCancellationRequested) return;
                if (f.GameId == appidStr) playing.Add(new FriendPlayInfo { PersonaName = f.PersonaName, AvatarFull = f.AvatarFull, IsPlayingNow = true, StatusText = "A jogar agora" });
            }

            if (!token.IsCancellationRequested)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    GameFriendsList.ItemsSource = playing;
                    AnimateFadeIn(GameFriendsList);
                });
            }
        }

        async Task LoadFriendsWhoOwnGame(string appidStr, CancellationToken token)
        {
            // 1. Prepara a coleção visual
            var ownersCollection = new System.Collections.ObjectModel.ObservableCollection<PlayerSummary>();
            await Dispatcher.InvokeAsync(() => OwnersFriendsList.ItemsSource = ownersCollection);

            if (string.IsNullOrEmpty(appidStr) || string.IsNullOrEmpty(steamApiKey)) return;

            // VERIFICAÇÃO DE CACHE
            if (_gameOwnersCache.TryGetValue(appidStr, out var cachedList) && cachedList.Count > 0)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    foreach (var f in cachedList) ownersCollection.Add(f);
                });
                return;
            }

            if (_cachedFriendsList.Count == 0)
            {
                await FetchAndShowFriendsAsync();
                if (_cachedFriendsList.Count == 0) return;
            }

            var allFriends = _cachedFriendsList.ToList();
            var detectedOwners = new ConcurrentBag<PlayerSummary>();

            // Limita os pedidos simultâneos para não bloquear a API
            var semaphore = new SemaphoreSlim(4);

            var tasks = allFriends.Select(async friend =>
            {
                if (token.IsCancellationRequested) return;

                await semaphore.WaitAsync();
                try
                {
                    if (token.IsCancellationRequested) return;

                    await Task.Delay(50, token);

                    bool hasGame = false;
                    if (friend.GameId == appidStr) hasGame = true;
                    else
                    {
                        try
                        {
                            var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={steamApiKey}&steamid={friend.SteamId}&include_appinfo=0&include_played_free_games=1&appids_filter[0]={appidStr}";
                            var json = await SteamApiGetJson(url);
                            if (json.HasValue && json.Value.TryGetProperty("response", out var r) && r.TryGetProperty("game_count", out var count))
                            {
                                if (count.GetInt32() > 0) hasGame = true;
                            }
                        }
                        catch { }
                    }

                    if (hasGame && !token.IsCancellationRequested)
                    {
                        // Adiciona à UI (Visual)
                        await Dispatcher.InvokeAsync(() => ownersCollection.Add(friend));
                        // Guarda na lista temporária
                        detectedOwners.Add(friend);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Guardar na Cache para a próxima vez
            if (!token.IsCancellationRequested && !detectedOwners.IsEmpty)
                _gameOwnersCache[appidStr] = detectedOwners.ToList();
        }

        async Task LoadGameNews(int appid)
        {
            var news = await GetNewsForAppAsync(appid, 3);
            var clean = new List<GameNewsItem>();
            foreach (var n in news) clean.Add(new GameNewsItem { Title = n.title, Date = n.date.ToShortDateString(), Snippet = n.contents.Substring(0, Math.Min(100, n.contents.Length)) });
            await Dispatcher.InvokeAsync(() =>
            {
                Info_NewsList.ItemsSource = clean;
                AnimateFadeIn(Info_NewsList);
            });
        }

        public void Info_NewsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Info_NewsList.SelectedItem is GameNewsItem news)
            {
                NewsModalTitle.Text = news.Title;
                NewsModalDate.Text = news.Date;
                NewsModalContent.Text = news.FullContent;

                NewsReaderModal.Visibility = Visibility.Visible;

                var btn = FindChild<Button>(NewsReaderModal);
                btn?.Focus();

                Info_NewsList.SelectedIndex = -1;
            }
        }

        public void CloseNewsModal_Click(object sender, RoutedEventArgs e)
        {
            NewsReaderModal.Visibility = Visibility.Collapsed;

            // 1. Devolve o foco à lista de notícias
            Info_NewsList.Focus();

            // 2. Garante que o item selecionado volta a ganhar o foco do teclado (Borda Brilhante)
            if (Info_NewsList.SelectedItem != null)
            {
                Info_NewsList.ScrollIntoView(Info_NewsList.SelectedItem);
                var item = Info_NewsList.ItemContainerGenerator.ContainerFromItem(Info_NewsList.SelectedItem) as ListBoxItem;
                item?.Focus();
            }
        }

        private void OpenGameOptions_Click(object sender, RoutedEventArgs e)
        {
            GameOptionsModal.Visibility = Visibility.Visible;

            // Foca o primeiro botão do modal para o comando funcionar
            Dispatcher.BeginInvoke(() =>
            {
                var btn = FindChild<Button>(GameOptionsModal);
                btn?.Focus();
            }, DispatcherPriority.Input);
        }

        public void CloseGameOptions_Click(object sender, RoutedEventArgs e)
        {
            GameOptionsModal.Visibility = Visibility.Collapsed;
            OptionsBtn.Focus(); // Devolve o foco ao botão "..."
        }

        private void OptionUninstall_Click(object sender, RoutedEventArgs e)
        {
            var game = games[selectedIndex];
            if (game.Source == "Steam" && !string.IsNullOrEmpty(game.SteamAppId))
            {
                if (MessageBox.Show($"Queres desinstalar {game.Title}?", "Desinstalar", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    // Comando Steam para desinstalar
                    Process.Start(new ProcessStartInfo($"steam://uninstall/{game.SteamAppId}") { UseShellExecute = true });
                    CloseGameOptions_Click(null, null);
                }
            }
            else
            {
                MessageBox.Show("A desinstalação automática só funciona para jogos Steam (por agora).", "Info");
            }
        }

        private void OptionFavorite_Click(object sender, RoutedEventArgs e)
        {
            // Aqui implementarias a lógica de salvar numa lista de favoritos
            MessageBox.Show("Adicionado aos Favoritos (Funcionalidade em breve)", "Favoritos");
            CloseGameOptions_Click(null, null);
        }

        private void OptionShortcut_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Atalho criado (Simulado)", "Atalho");
            CloseGameOptions_Click(null, null);
        }

        async Task UpdateHeaderUI()
        {
            if (string.IsNullOrEmpty(connectedSteamId) || string.IsNullOrEmpty(steamApiKey)) return;

            try
            {
                // 1. Busca os dados do perfil (Nome e Avatar)
                var profile = await GetProfileSummary();

                // 2. Atualiza a UI (tem de ser no Dispatcher porque é visual)
                await Dispatcher.InvokeAsync(() =>
                {
                    if (!string.IsNullOrEmpty(profile.PersonaName))
                    {
                        // Atualiza o Nome
                        ProfileName.Text = profile.PersonaName;

                        // Atualiza a Imagem
                        if (!string.IsNullOrEmpty(profile.AvatarFull))
                        {
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.UriSource = new Uri(profile.AvatarFull);
                            bmp.CacheOption = BitmapCacheOption.OnLoad; // Importante para não bloquear o ficheiro
                            bmp.EndInit();
                            ProfileImage.Source = bmp;
                        }

                        StatusLabel.Text = "Steam: Ligado";
                    }
                });
            }
            catch { /* Ignora erros de rede pontuais */ }
        }

        public ICommand InviteFriendCommand => new RelayCommand<string>(steamIdStr =>
        {
            OpenChatForFriend(steamIdStr);
        });

        public void OpenChatForFriend(string steamIdStr)
        {
            if (!ulong.TryParse(steamIdStr, out ulong id)) return;

            _currentChatFriend = new Steamworks.Friend(id);

            ChatFriendName.Text = _currentChatFriend.Name;

            LoadChatHistory(_currentChatFriend.Id.ToString());

            _ = Task.Run(async () =>
            {
                var img = await Steamworks.SteamFriends.GetLargeAvatarAsync(_currentChatFriend.Id);
                if (img.HasValue)
                {
                    var bmp = SteamImageToBitmap(img.Value);
                    bmp.Freeze();
                    await Dispatcher.InvokeAsync(() => ChatFriendAvatar.Source = bmp);
                }
            });

            ChatModal.Visibility = Visibility.Visible;
            ChatInputBox.Focus();

            if (_chatMessages.Count > 0) ChatList.ScrollIntoView(_chatMessages.Last());
        }

        private void SendChat_Click(object sender, RoutedEventArgs e)
        {
            string text = ChatInputBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            bool sent = _currentChatFriend.SendMessage(text);
            if (sent)
            {
                _chatMessages.Add(new ChatMessage
                {
                    SenderName = "Eu",
                    Message = text,
                    Time = DateTime.Now.ToShortTimeString(),
                    Alignment = HorizontalAlignment.Right,
                    BubbleColor = (SolidColorBrush)FindResource("AccentBrush"), // Usa a cor do tema
                    IsMe = true
                });

                SaveChatMessage(_currentChatFriend.Id.ToString(), "Eu", text);

                ChatInputBox.Clear();
                ChatList.ScrollIntoView(_chatMessages.Last());
            }
            else MessageBox.Show("Erro ao enviar. A Steam está aberta?");
        }

        private void OnSteamChatMessage(Friend friend, string type, string message)
        {
            string cleanMessage = message.Replace("\0", "").Trim();
            if (string.IsNullOrWhiteSpace(cleanMessage)) return;

            SaveChatMessage(friend.Id.ToString(), friend.Name, cleanMessage);

            // Só mostra se o modal estiver aberto e for o amigo certo
            if (ChatModal.Visibility == Visibility.Visible && _currentChatFriend.Id == friend.Id)
            {
                Dispatcher.Invoke(() =>
                {
                    var msg = new ChatMessage
                    {
                        SenderName = friend.Name,
                        Message = cleanMessage,
                        Time = DateTime.Now.ToShortTimeString(),
                        Alignment = HorizontalAlignment.Left, // Lado Esquerdo = Amigo
                        BubbleColor = Brushes.Gray,
                        IsMe = false
                    };
                    _chatMessages.Add(msg);
                    ChatList.ScrollIntoView(_chatMessages.Last());
                });
            }

            if (ChatModal.Visibility != Visibility.Visible)
            {
                _ = Task.Run(async () =>
                {
                    var img = await Steamworks.SteamFriends.GetLargeAvatarAsync(friend.Id);
                    if (img.HasValue)
                    {
                        var bmp = SteamImageToBitmap(img.Value);
                        bmp.Freeze();
                        ShowNotification(friend.Name, cleanMessage, bmp);
                    }
                    else
                    {
                        ShowNotification(friend.Name, cleanMessage);
                    }
                });
            }
        }

        // 4. UX: Enviar com Enter
        private void ChatInputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) SendChat_Click(sender, e);
            if (e.Key == Key.Escape) CloseChat_Click(sender, e);
        }

        // 5. FECHAR
        private void CloseChat_Click(object sender, RoutedEventArgs e)
        {
            ChatModal.Visibility = Visibility.Collapsed;
            // Devolve o foco à lista de amigos
            FriendListBox.Focus();
        }

        private ImageSource SteamImageToBitmap(Steamworks.Data.Image img)
        {
            int w = (int)img.Width;
            int h = (int)img.Height;
            byte[] rgba = img.Data;

            // O WPF usa BGRA, a Steam usa RGBA. Temos de trocar os canais.
            byte[] bgra = new byte[rgba.Length];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                bgra[i] = rgba[i + 2];     // Blue
                bgra[i + 1] = rgba[i + 1]; // Green
                bgra[i + 2] = rgba[i];     // Red
                bgra[i + 3] = rgba[i + 3]; // Alpha
            }

            return BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        }

        void SaveChatMessage(string friendId, string sender, string msg)
        {
            lock (_chatFileLock)
            {
                try
                {
                    Directory.CreateDirectory(chatLogDir);
                    string f = Path.Combine(chatLogDir, $"{friendId}.json");
                    var list = new List<ChatLogEntry>();
                    if (File.Exists(f))
                    {
                        try { list = JsonSerializer.Deserialize<List<ChatLogEntry>>(File.ReadAllText(f)) ?? new List<ChatLogEntry>(); } catch { }
                    }
                    list.Add(new ChatLogEntry { Sender = sender, Message = msg, Timestamp = DateTime.Now });
                    if (list.Count > 200) list.RemoveRange(0, list.Count - 200);
                    File.WriteAllText(f, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex) { Dispatcher.Invoke(() => MessageBox.Show("Erro a gravar chat: " + ex.Message)); }
            }
        }

        void LoadChatHistory(string friendId)
        {
            _chatMessages.Clear();
            ChatList.ItemsSource = _chatMessages;

            lock (_chatFileLock)
            {
                try
                {
                    string f = Path.Combine(chatLogDir, $"{friendId}.json");
                    if (File.Exists(f))
                    {
                        var list = JsonSerializer.Deserialize<List<ChatLogEntry>>(File.ReadAllText(f));
                        if (list != null) foreach (var e in list)
                            {
                                _chatMessages.Add(new ChatMessage
                                {
                                    SenderName = e.Sender,
                                    Message = e.Message,
                                    Time = e.Timestamp.ToShortTimeString(),
                                    Alignment = e.Sender == "Eu" ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                                    BubbleColor = e.Sender == "Eu" ? (SolidColorBrush)FindResource("AccentBrush") : Brushes.Gray,
                                    IsMe = (e.Sender == "Eu")
                                });
                            }
                        if (_chatMessages.Count > 0) ChatList.ScrollIntoView(_chatMessages.Last());
                    }
                }
                catch { }
            }
        }

        private void UpdateSteamCallbacks(object? sender, EventArgs e)
        {
            if (Steamworks.SteamClient.IsValid)
            {
                try
                {
                    Steamworks.SteamClient.RunCallbacks();
                }
                catch
                {

                }
            }
        }

        public void ShowNotification(string title, string message, ImageSource? image = null)
        {
            Dispatcher.Invoke(() =>
            {
                //Configurar os Dados
                NotifTitle.Text = title;
                NotifMessage.Text = message;
                if (image != null) NotifImage.Source = image;

                //Cancelar a animação anterior caso haja (pra prevenir que pisque)
                _notificationCts?.Cancel();
                _notificationCts = new CancellationTokenSource();
                var token = _notificationCts.Token;

                //Animação de Entrada
                var slideIn = new ThicknessAnimation
                {
                    From = new Thickness(0, -100, 0, 0),
                    To = new Thickness(0, 30, 0, 0), //Fica abaixo do topo 30px
                    Duration = TimeSpan.FromMilliseconds(400),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                NotificationPopup.BeginAnimation(Border.MarginProperty, slideIn);

                //Esperar e Fechar
                Task.Delay(4000, token).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        //Animação de Saida
                        var slideOut = new ThicknessAnimation
                        {
                            From = new Thickness(0, 30, 0, 0),
                            To = new Thickness(0, -100, 0, 0),
                            Duration = TimeSpan.FromMilliseconds(400),
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                        };
                        NotificationPopup.BeginAnimation(Border.MarginProperty, slideOut);
                    });
                }, TaskScheduler.FromCurrentSynchronizationContext());
            });
        }

        // --- GESTÃO DE TEMAS ---
        void SaveTheme(string themeName)
        {
            try
            {
                File.WriteAllText(themeFilePath, themeName);
            }
            catch { }
        }

        void LoadSavedTheme()
        {
            if (!File.Exists(themeFilePath)) return;

            try
            {
                string theme = File.ReadAllText(themeFilePath);
                switch (theme)
                {
                    case "Blue": BtnThemeBlue_Click(null, null); break;
                    case "Dark": BtnThemeDark_Click(null, null); break;
                    case "Pichal": BtnThemePichal_Click(null, null); break;
                    case "Red": BtnThemeRed_Click(null, null); break;
                    case "Neon_Blue": ThemeEditor("FF00F3FF,FFA0E0E0,FF050A14,FF001524,CC000810,FFFFFFFF,FFFFFFFF,FFA0E0E0,3300F3FF,FFFFFFFF,FFA0E0E0,FFFFFFFF,FFA0E0E0,FFFFFFFF,FF000000,0"); break;
                    case "Hackerman": ThemeEditor("FF00FF41,FF008F11,FF000000,FF0D0D0D,E60A0A0A,FFE0FFE0,FF00FF41,FFE0FFE0,3300FF41,FFE0FFE0,FF008F11,FFE0FFE0,FF008F11,FFE0FFE0,FF000000,0"); break;
                    case "Utopia": ThemeEditor("FFFF4600,FF696969,FFFFFFFF,FFE0E0E0,CCFFFFFF,FF111111,FF111111,FF696969,40000000,FF111111,FF696969,FF111111,FF696969,FF111111,FFFFFFFF,0"); break;
                    case "Test": Test_Click(); break;
                }
            }
            catch { }
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

        async Task PlayStartupAnimation()
        {
            Carousel.Opacity = 0;
            CarouselSlideTransform.Y = 150;

            IntroBackgroundGradient.RadiusX = 0; IntroBackgroundGradient.RadiusY = 0;

            await Task.Delay(300);
            try { System.Media.SystemSounds.Exclamation.Play(); } catch { }

            var bgExpand = new DoubleAnimation(0.0, 1.2, TimeSpan.FromSeconds(1.5)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            IntroBackgroundGradient.BeginAnimation(RadialGradientBrush.RadiusXProperty, bgExpand);
            IntroBackgroundGradient.BeginAnimation(RadialGradientBrush.RadiusYProperty, bgExpand);

            var colorAnim = new ColorAnimationUsingKeyFrames();
            colorAnim.KeyFrames.Add(new EasingColorKeyFrame(Color.FromRgb(0, 0, 0), KeyTime.FromTimeSpan(TimeSpan.Zero)));
            colorAnim.KeyFrames.Add(new EasingColorKeyFrame(Color.FromRgb(0, 100, 0), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.8))));
            colorAnim.KeyFrames.Add(new EasingColorKeyFrame(Color.FromRgb(0, 215, 0), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.4))));
            IntroColorCore.BeginAnimation(GradientStop.ColorProperty, colorAnim);

            await Task.Delay(1200);

            IntroShockwave1.Opacity = 1; IntroShockwave2.Opacity = 1;
            var shock1 = new DoubleAnimation(1, 15, TimeSpan.FromMilliseconds(800));
            var fade1 = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(800));
            ShockScale1.BeginAnimation(ScaleTransform.ScaleXProperty, shock1);
            ShockScale1.BeginAnimation(ScaleTransform.ScaleYProperty, shock1);
            IntroShockwave1.BeginAnimation(System.Windows.Shapes.Ellipse.OpacityProperty, fade1);

            var shock2 = new DoubleAnimation(1, 10, TimeSpan.FromMilliseconds(400));
            var fade2 = new DoubleAnimation(0.8, 0, TimeSpan.FromMilliseconds(400));
            ShockScale2.BeginAnimation(ScaleTransform.ScaleXProperty, shock2);
            ShockScale2.BeginAnimation(ScaleTransform.ScaleYProperty, shock2);
            IntroShockwave2.BeginAnimation(System.Windows.Shapes.Ellipse.OpacityProperty, fade2);

            IntroLogo.Opacity = 1;
            var logoPop = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)) { EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 6 } };
            IntroLogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, logoPop);
            IntroLogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, logoPop);

            var logoSpin = new DoubleAnimation(-180, 0, TimeSpan.FromMilliseconds(900)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            IntroLogoRotate.BeginAnimation(RotateTransform.AngleProperty, logoSpin);

            var glowAnim = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1));
            LogoGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnim);

            await Task.Delay(1800);

            var logoSizeUp = new DoubleAnimation(1, 100, TimeSpan.FromMilliseconds(900)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            IntroLogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, logoSizeUp);
            IntroLogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, logoSizeUp);

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(1000)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            IntroLogo.BeginAnimation(UIElement.OpacityProperty, fadeOut);

            await Task.Delay(450);
            IntroBackgroundGradient.RadiusX = 0;

            Carousel.Opacity = 1;
            CarouselSlideTransform.Y = 0;

            var flashOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1));

            flashOut.Completed += (s, e) =>
            {
                StartupOverlay.Visibility = Visibility.Collapsed;
            };

            IntroFlash.BeginAnimation(Border.OpacityProperty, flashOut);
        }

        async Task FoxyStartupAnimation()
        {
            Carousel.Opacity = 0;
            CarouselSlideTransform.Y = 150;

            IntroBackgroundGradient.RadiusX = 0; IntroBackgroundGradient.RadiusY = 0;

            await Task.Delay(300);
            try { System.Media.SystemSounds.Exclamation.Play(); } catch { }

            var bgExpand = new DoubleAnimation(0.0, 1.2, TimeSpan.FromSeconds(1.5)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            IntroBackgroundGradient.BeginAnimation(RadialGradientBrush.RadiusXProperty, bgExpand);
            IntroBackgroundGradient.BeginAnimation(RadialGradientBrush.RadiusYProperty, bgExpand);

            var colorAnim = new ColorAnimationUsingKeyFrames();
            colorAnim.KeyFrames.Add(new EasingColorKeyFrame(Color.FromRgb(0, 0, 0), KeyTime.FromTimeSpan(TimeSpan.Zero)));
            colorAnim.KeyFrames.Add(new EasingColorKeyFrame(Color.FromRgb(0, 100, 0), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.8))));
            colorAnim.KeyFrames.Add(new EasingColorKeyFrame(Color.FromRgb(0, 215, 0), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.4))));
            IntroColorCore.BeginAnimation(GradientStop.ColorProperty, colorAnim);

            await Task.Delay(1200);

            IntroShockwave1.Opacity = 1; IntroShockwave2.Opacity = 1;
            var shock1 = new DoubleAnimation(1, 15, TimeSpan.FromMilliseconds(800));
            var fade1 = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(800));
            ShockScale1.BeginAnimation(ScaleTransform.ScaleXProperty, shock1);
            ShockScale1.BeginAnimation(ScaleTransform.ScaleYProperty, shock1);
            IntroShockwave1.BeginAnimation(System.Windows.Shapes.Ellipse.OpacityProperty, fade1);

            var shock2 = new DoubleAnimation(1, 10, TimeSpan.FromMilliseconds(400));
            var fade2 = new DoubleAnimation(0.8, 0, TimeSpan.FromMilliseconds(400));
            ShockScale2.BeginAnimation(ScaleTransform.ScaleXProperty, shock2);
            ShockScale2.BeginAnimation(ScaleTransform.ScaleYProperty, shock2);
            IntroShockwave2.BeginAnimation(System.Windows.Shapes.Ellipse.OpacityProperty, fade2);

            IntroLogo.Opacity = 1;
            var logoPop = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)) { EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 6 } };
            IntroLogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, logoPop);
            IntroLogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, logoPop);

            var logoSpin = new DoubleAnimation(-180, 0, TimeSpan.FromMilliseconds(900)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            IntroLogoRotate.BeginAnimation(RotateTransform.AngleProperty, logoSpin);

            var glowAnim = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1));
            LogoGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnim);

            await Task.Delay(1800);

            Foxy.Opacity = 1;
            var logoSizeUp = new DoubleAnimation(0, 100, TimeSpan.FromMilliseconds(450)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            FoxyScale.BeginAnimation(ScaleTransform.ScaleXProperty, logoSizeUp);
            FoxyScale.BeginAnimation(ScaleTransform.ScaleYProperty, logoSizeUp);


            await Task.Delay(440);
            Foxy.Opacity = 0;
            IntroLogo.Opacity = 0;
            StartupOverlay.Visibility = Visibility.Collapsed;
            IntroBackgroundGradient.RadiusX = 0;

            var gamesFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1.5));
            var gamesSlideUp = new DoubleAnimation(150, 0, TimeSpan.FromSeconds(1.9))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            Carousel.BeginAnimation(Border.OpacityProperty, gamesFadeIn);
            CarouselSlideTransform.BeginAnimation(TranslateTransform.YProperty, gamesSlideUp);
        }


        // --- FUNÇÕES DO MODO NATAL ---
        void CheckChristmasSeason()
        {
            var today = DateTime.Now;

            bool isChristmas = (today.Month == 0) || (today.Month == 0);

            if (isChristmas)
                CompositionTarget.Rendering += CheckInactivityForSnow;
        }

        void CheckInactivityForSnow(object? sender, EventArgs e)
        {
            if (currentIndex != 0 || isShowingStoreView)
            {
                if (isSnowing) StopSnowing();
                return;
            }

            if ((DateTime.UtcNow - lastNav).TotalSeconds > 5 && !isSnowing)
                StartSnowing();
            else if ((DateTime.UtcNow - lastNav).TotalSeconds < 0.5 && isSnowing)
                StopSnowing();
        }

        void StartSnowing()
        {
            isSnowing = true;
            ChristmasOverlay.Visibility = Visibility.Visible;

            SnowCanvas.BeginAnimation(OpacityProperty, null);
            SnowWindTransform.BeginAnimation(TranslateTransform.XProperty, null);
            SnowFloor.BeginAnimation(OpacityProperty, null);
            GolemWindSlide.BeginAnimation(TranslateTransform.XProperty, null);

            SnowCanvas.Opacity = 1;
            SnowWindTransform.X = 0;
            GolemWindSlide.X = 0;

            SnowCanvas.Children.Clear();
            activeSnowFlakes.Clear();
            snowTimer.Start();

            SnowFloor.Opacity = 0;
            var floorFade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(10));
            SnowFloor.BeginAnimation(OpacityProperty, floorFade);

            golemSpawned = false;
            FrostGolem.Opacity = 0;

            Task.Delay(8000).ContinueWith(_ =>
            {
                if (isSnowing) Dispatcher.Invoke(() => SpawnGolem());
            });
        }

        void SpawnGolem()
        {
            if (golemSpawned) return;
            golemSpawned = true;
            FrostGolem.Opacity = 1;

            BodyFloat.Y = 100;
            var bodyMove = new DoubleAnimation(100, 0, TimeSpan.FromSeconds(1.5)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut } };
            BodyFloat.BeginAnimation(TranslateTransform.YProperty, bodyMove);

            ChestFloat.X = -50; ChestFloat.Y = 50;
            var chestX = new DoubleAnimation(-50, 0, TimeSpan.FromSeconds(1.8)) { EasingFunction = new CubicEase() };
            var chestY = new DoubleAnimation(50, 0, TimeSpan.FromSeconds(1.8)) { EasingFunction = new CubicEase() };
            ChestFloat.BeginAnimation(TranslateTransform.XProperty, chestX);
            ChestFloat.BeginAnimation(TranslateTransform.YProperty, chestY);

            HeadFloat.Y = -100;
            var headMove = new DoubleAnimation(-100, 0, TimeSpan.FromSeconds(2)) { EasingFunction = new BounceEase { Bounciness = 2, Bounces = 3 } };
            HeadFloat.BeginAnimation(TranslateTransform.YProperty, headMove);

            var particleAnim = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            MagicParticle1.BeginAnimation(OpacityProperty, particleAnim);

            var particleAnim2 = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(0.3) };
            MagicParticle2.BeginAnimation(OpacityProperty, particleAnim2);

            Task.Delay(2000).ContinueWith(_ =>
            {
                if (isSnowing && golemSpawned) Dispatcher.Invoke(() => StartGolemIdle());
            });
        }

        void StartGolemIdle()
        {
            var hoverSlow = new DoubleAnimation(0, -5, TimeSpan.FromSeconds(3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            var hoverFast = new DoubleAnimation(0, -3, TimeSpan.FromSeconds(2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            var hoverHead = new DoubleAnimation(0, -6, TimeSpan.FromSeconds(4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };

            BodyFloat.BeginAnimation(TranslateTransform.YProperty, hoverSlow);
            ChestFloat.BeginAnimation(TranslateTransform.YProperty, hoverFast);
            HeadFloat.BeginAnimation(TranslateTransform.YProperty, hoverHead);
        }

        void StopSnowing()
        {
            if (!isSnowing) return;
            isSnowing = false;
            golemSpawned = false;
            snowTimer.Stop();

            var windMove = new DoubleAnimation(0, 2000, TimeSpan.FromSeconds(0.5)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.3));

            SnowWindTransform.BeginAnimation(TranslateTransform.XProperty, windMove);
            SnowCanvas.BeginAnimation(OpacityProperty, fade);
            SnowFloor.BeginAnimation(OpacityProperty, fade);

            if (FrostGolem.Opacity > 0)
            {
                GolemWindSlide.BeginAnimation(TranslateTransform.XProperty, windMove);

                var shrink = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.4));
                GolemScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
                GolemScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
            }

            fade.Completed += (s, e) =>
            {
                ChristmasOverlay.Visibility = Visibility.Collapsed;
                SnowCanvas.Children.Clear();
                activeSnowFlakes.Clear();

                FrostGolem.Opacity = 0;
                BodyFloat.BeginAnimation(TranslateTransform.YProperty, null);
                ChestFloat.BeginAnimation(TranslateTransform.XProperty, null);
                ChestFloat.BeginAnimation(TranslateTransform.YProperty, null);
                HeadFloat.BeginAnimation(TranslateTransform.YProperty, null);
                GolemScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                GolemScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            };
        }

        void SnowTimer_Tick(object? sender, EventArgs e)
        {
            if (activeSnowFlakes.Count > 300) return;

            double size = rng.NextDouble() * 4 + 2; // 2 a 6px
            double opacity = rng.NextDouble() * 0.5 + 0.2;

            var flake = new System.Windows.Shapes.Ellipse { Width = size, Height = size, Fill = Brushes.White, Opacity = opacity };

            double startX = rng.Next(-200, (int)ActualWidth + 200);
            Canvas.SetLeft(flake, startX);
            Canvas.SetTop(flake, -10);

            // Velocidade baseada no tamanho (Parallax: maiores caem mais depressa)
            double durationSec = 10 - size; // 4s a 8s

            var fall = new DoubleAnimation { To = ActualHeight + 50, Duration = TimeSpan.FromSeconds(durationSec) };

            var drift = new DoubleAnimation { To = rng.Next(-50, 50), Duration = TimeSpan.FromSeconds(durationSec) };
            var trans = new TranslateTransform();
            flake.RenderTransform = trans;
            trans.BeginAnimation(TranslateTransform.XProperty, drift);

            fall.Completed += (s, ev) => { SnowCanvas.Children.Remove(flake); activeSnowFlakes.Remove(flake); };

            SnowCanvas.Children.Add(flake);
            activeSnowFlakes.Add(flake);
            flake.BeginAnimation(Canvas.TopProperty, fall);
        }

        async Task<Color> GetDominantColorAsync(ImageSource imageSource)
        {
            return await Task.Run(() =>
    {
        try
        {
            if (imageSource is BitmapSource bitmap)
            {
                // Se a imagem for muito grande, o cálculo é lento.
                // Na prática, lemos apenas alguns pixels para ser instantâneo.

                // Verifica formato (tem de ser compatível para ler bytes)
                if (bitmap.Format != PixelFormats.Bgra32 && bitmap.Format != PixelFormats.Rgba64)
                {
                    // Se não for compatível, retorna uma cor padrão (ex: Accent original)
                    return (Color)ColorConverter.ConvertFromString("#FF4758");
                }

                int stride = (bitmap.PixelWidth * bitmap.Format.BitsPerPixel + 7) / 8;
                int size = bitmap.PixelHeight * stride;
                byte[] pixels = new byte[size];
                bitmap.CopyPixels(pixels, stride, 0);

                long r = 0, g = 0, b = 0;
                int count = 0;

                // Saltamos pixels para performance (lê 1 a cada 100 pixels)
                for (int i = 0; i < size; i += 400)
                {
                    if (i + 3 >= size) break;

                    // Ignora cores muito escuras ou muito brancas (para apanhar a cor "viva")
                    byte blue = pixels[i];
                    byte green = pixels[i + 1];
                    byte red = pixels[i + 2];

                    // Filtro de luminosidade simples
                    if ((red + green + blue) > 50 && (red + green + blue) < 700)
                    {
                        b += blue;
                        g += green;
                        r += red;
                        count++;
                    }
                }

                if (count > 0)
                {
                    return Color.FromRgb((byte)(r / count), (byte)(g / count), (byte)(b / count));
                }
            }
        }
        catch { }

        // Cor de fallback se falhar
        return (Color)ColorConverter.ConvertFromString("#FF4758");
    });
        }

        void AnimateThemeToColor(Color targetColor)
        {
            // 1. Calcular variações da cor para o Fundo (Gradiente)
            // Fundo Topo: Quase preto, com um toque da cor
            Color bgTop = Color.FromRgb(
                (byte)(targetColor.R * 0.1),
                (byte)(targetColor.G * 0.1),
                (byte)(targetColor.B * 0.1));

            // Fundo Base: Cor escura
            Color bgBottom = Color.FromRgb(
                (byte)(targetColor.R * 0.3),
                (byte)(targetColor.G * 0.3),
                (byte)(targetColor.B * 0.3));

            // 2. Animar o Accent (Botões, Bordas, Seleção)
            // Nota: Para animar recursos dinâmicos, temos de aceder ao SolidColorBrush existente
            if (this.Resources["AccentBrush"] is SolidColorBrush accentBrush)
            {
                var colorAnim = new ColorAnimation(targetColor, TimeSpan.FromSeconds(0.6));
                accentBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
            }
            else
            {
                // Se por algum motivo não for animável, recria (sem animação)
                this.Resources["AccentBrush"] = new SolidColorBrush(targetColor);
            }

            // 3. Animar o Fundo (Gradiente)
            if (MainBackground.Background is LinearGradientBrush grad)
            {
                // Assumindo que tens 2 GradientStops como definimos antes
                if (grad.GradientStops.Count >= 2)
                {
                    var anim1 = new ColorAnimation(bgTop, TimeSpan.FromSeconds(1));
                    var anim2 = new ColorAnimation(bgBottom, TimeSpan.FromSeconds(1));

                    grad.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, anim1);
                    grad.GradientStops[1].BeginAnimation(GradientStop.ColorProperty, anim2);
                }
            }
        }

        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton btn && btn.Tag is string filterType)
            {
                ApplyGameFilter(filterType);
            }
        }

        void ApplyGameFilter(string type)
        {
            if (_allGamesMasterList.Count == 0) return;

            List<GameEntry> filtered = new List<GameEntry>();

            switch (type)
            {
                case "Recent":
                    filtered = _allGamesMasterList.Where(g => g.LastPlayed.HasValue).OrderByDescending(g => g.LastPlayed).ToList();
                    break;
                case "MostPlayed":
                    filtered = _allGamesMasterList.OrderByDescending(g => g.PlaytimeHours).ToList();
                    break;
                case "NeverPlayed":
                    filtered = _allGamesMasterList.Where(g => g.PlaytimeHours < 0.2 && g.LastPlayed == null).OrderBy(g => g.Title).ToList();
                    break;
                case "NotInstalled":
                    filtered = _allGamesMasterList.OrderBy(g => g.Title).Where(g => g.IsInstalled == false).ToList();
                    break;
                case "All":
                default:
                    filtered = _allGamesMasterList.OrderBy(g => g.Title).Where(g => g.IsInstalled == true).ToList();
                    break;
            }

            games = filtered;

            PopulateGamesPanel();
        }

        async Task<string> GetGameGenreAsync(int appid)
        {
            try
            {
                var url = $"https://store.steampowered.com/api/appdetails?appids={appid}&l=portuguese";
                var root = await SteamApiGetJson(url);
                if (root.HasValue && root.Value.TryGetProperty(appid.ToString(), out var appData))
                {
                    if (appData.TryGetProperty("data", out var data))
                    {
                        // Géneros
                        var genresList = new List<string>();
                        if (data.TryGetProperty("genres", out var genresArr))
                        {
                            foreach (var g in genresArr.EnumerateArray())
                                genresList.Add(g.GetProperty("description").GetString() ?? "");
                        }
                        string genresStr = string.Join(" • ", genresList.Take(4));
                        return genresStr;
                    }
                }
            }
            catch { }
            return "";
        }

        // ---- PICHAL BRAIN ----
        string PickRandom(string[] options) => options[rng.Next(options.Length)];
        void GenerateRecommendation()
        {
            if (games.Count == 0) return;

            GameEntry? suggestion = null;
            string reason = "";
            var now = DateTime.Now;

            bool isOutubro = (now.Month == 10);

            if (isOutubro)
            {
                var halloween = games.Where(g => (g.Genre ?? "").Contains("Terror")).OrderBy(x => rng.Next()).FirstOrDefault();

                if (halloween != null)
                {
                    suggestion = halloween;
                    reason = PickRandom(new[]
                    {
                        "hor hor hor hor hor hor",
                        "Halloween?",
                        "Estás com medo? Estás tão cagado que já cheira a merda aqui.",
                        $"Chegou a noite certa para um joguinho de Terror. Vem jogar {halloween.Title}."
                    });
                }
            }
            // --- 1. FATOR SOCIAL (Prioridade Máxima) ---
            // Se amigos estão a jogar, a pressão social ganha.
            if (suggestion == null && _cachedFriendsList != null)
            {
                var playingFriends = _cachedFriendsList
                    .Where(f => !string.IsNullOrEmpty(f.GameId) && f.GameId != "0")
                    .ToList();

                if (playingFriends.Count > 0)
                {
                    var friend = playingFriends[rng.Next(playingFriends.Count)];
                    var match = games.FirstOrDefault(g => g.SteamAppId == friend.GameId);

                    if (match != null)
                    {
                        suggestion = match;
                        reason = PickRandom(new[] {
                    $"O {friend.PersonaName} está a jogar isto agora. Não o deixes jogar sozinho!",
                    $"Parece que o {friend.PersonaName} está viciado nisto. Bora juntar?",
                    $"Junta-te ao {friend.PersonaName}, ele está online agora mesmo.",
                    $"A equipa precisa de ti! O {friend.PersonaName} já está lá."
                });
                    }
                }
            }

            // --- 2. FATOR "O VÍCIO ATUAL" (Recentes + Muito Jogado) ---
            if (suggestion == null)
            {
                var obsession = games
                    .Where(g => g.LastPlayed.HasValue && (now - g.LastPlayed.Value).TotalDays <= 2 && g.PlaytimeHours > 15)
                    .OrderByDescending(g => g.LastPlayed)
                    .FirstOrDefault();

                if (obsession != null && rng.NextDouble() > 0.2)
                {
                    suggestion = obsession;
                    reason = PickRandom(new[] {
                $"Não consegues largar este, pois não? Já levas {obsession.PlaytimeHours:0} horas.",
                $"Estavas no meio de algo importante aqui. Vamos continuar?",
                $"O vício é real. Só mais um bocadinho...",
                $"Ainda tens muito para fazer em {obsession.Title}. De volta à ação!"
            });
                }
            }

            // --- 3. FATOR "GÉNERO FAVORITO" (NOVO - Inteligência Real) ---
            // Analisa a biblioteca toda para ver o que mais gostas
            if (suggestion == null && rng.NextDouble() > 0.3)
            {
                // Dicionário para contar horas por género
                var genreScores = new Dictionary<string, double>();

                foreach (var g in games)
                {
                    if (string.IsNullOrEmpty(g.Genre) || g.PlaytimeHours < 1) continue;

                    // Divide "Action • RPG" em partes
                    var tags = g.Genre.Split(new[] { '•', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var tag in tags)
                    {
                        if (tag.Length < 3) continue; // Ignora palavras curtas
                        if (!genreScores.ContainsKey(tag)) genreScores[tag] = 0;
                        genreScores[tag] += g.PlaytimeHours; // Soma as horas jogadas nesse género
                    }
                }

                // Qual o género vencedor?
                var topGenre = genreScores.OrderByDescending(x => x.Value).FirstOrDefault();

                if (!string.IsNullOrEmpty(topGenre.Key))
                {
                    // Procura um jogo desse género que tenhas JOGADO POUCO (< 5h)
                    var hiddenGem = games
                        .Where(g => (g.Genre ?? "").Contains(topGenre.Key) && g.PlaytimeHours < 5 && g.PlaytimeHours > 0)
                        .OrderBy(x => rng.Next())
                        .FirstOrDefault();

                    if (hiddenGem != null)
                    {
                        suggestion = hiddenGem;
                        reason = PickRandom(new[] {
                    $"A IA detetou que és fã de {topGenre.Key}, mas deixaste este jogo de parte.",
                    $"Gostas de {topGenre.Key}? Então tens de dar uma oportunidade a este.",
                    $"Com base nas tuas {topGenre.Value:0} horas em {topGenre.Key}, vais adorar isto.",
                    $"Uma pérola de {topGenre.Key} escondida na tua biblioteca."
                });
                    }
                }
            }

            // --- 4. FATOR "COMPLETIONIST" ---
            if (suggestion == null)
            {
                var almostDone = games
                    .Where(g => g.ProgressPercent >= 70 && g.ProgressPercent < 100)
                    .OrderByDescending(g => g.LastPlayed)
                    .FirstOrDefault();

                if (almostDone != null && rng.NextDouble() > 0.4)
                {
                    suggestion = almostDone;
                    reason = PickRandom(new[] {
                $"Estás a {almostDone.ProgressPercent}% da perfeição. Não desistas agora!",
                $"Faltam poucas conquistas para a Platina. Vamos a isso?",
                $"Tão perto do fim... Acaba o que começaste!"
            });
                }
            }

            // --- 5. FATOR "CONTEXTO TEMPORAL" (Dia/Hora) ---
            if (suggestion == null)
            {
                string targetTag = "";
                string[] timePhrases = new string[0];

                // Madrugada (Terror)
                if (now.Hour >= 0 && now.Hour < 5)
                {
                    targetTag = "Horror";
                    timePhrases = new[] { "Está escuro lá fora... tens coragem?", "Jogos de terror batem diferente a esta hora.", "Não olhes para trás..." };
                }
                // Manhã Fim de Semana (Aventura/Mundo Aberto)
                else if (now.Hour >= 8 && now.Hour < 13 && (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday))
                {
                    targetTag = "Open World"; // ou Adventure
                    timePhrases = new[] { "Fim de semana de manhã pede uma grande aventura.", "Tens o dia todo. Explora um mundo novo.", "Café e exploração. Combinação perfeita." };
                }
                // Sexta/Sábado Noite (Multiplayer/Ação)
                else if ((now.DayOfWeek == DayOfWeek.Friday || now.DayOfWeek == DayOfWeek.Saturday) && now.Hour >= 20)
                {
                    targetTag = "Action";
                    timePhrases = new[] { "A noite é jovem! Ação intensa para começar o fim de semana.", "Sexta à noite é para destruir tudo.", "Aumenta o volume e entra na ação." };
                }

                if (!string.IsNullOrEmpty(targetTag))
                {
                    var vibeGame = games.Where(g => (g.Genre ?? "").Contains(targetTag)).OrderBy(x => rng.Next()).FirstOrDefault();
                    if (vibeGame != null)
                    {
                        suggestion = vibeGame;
                        reason = PickRandom(timePhrases);
                    }
                }
            }

            // --- 6. FATOR "BACKLOG" (Nunca Jogado) ---
            if (suggestion == null)
            {
                var shame = games
                    .Where(g => g.PlaytimeHours < 0.2 && g.LastPlayed == null)
                    .OrderBy(x => rng.Next())
                    .FirstOrDefault();

                if (shame != null)
                {
                    suggestion = shame;
                    reason = PickRandom(new[] {
                "Compraste este jogo e nunca o abriste. Hoje é o dia!",
                "Está a ganhar pó na biblioteca. Merece uma oportunidade.",
                "Ainda está no plástico. Vamos estrear?",
                "Gastaste dinheiro nisto, convém jogar!"
            });
                }
            }

            // --- 7. FALLBACK (Último Jogado) ---
            if (suggestion == null)
            {
                suggestion = games.OrderByDescending(g => g.LastPlayed).FirstOrDefault();
                reason = PickRandom(new[] { "Bem-vindo de volta.", "Pronto para continuar?", "O teu jogo habitual." });
            }

            // --- APLICAR NA UI ---
            if (suggestion != null)
            {
                _recommendedGame = suggestion;

                string user = currentUser?.Username ?? "Gamer";
                WelcomeUser.Text = user;
                WelcomeReason.Text = reason;
                WelcomeGameTitle.Text = suggestion.Title.ToUpper();

                if (suggestion.Cover != null)
                {
                    WelcomeGameCover.ImageSource = suggestion.Cover;
                    WelcomeBgImage.Source = suggestion.Cover;
                }

                // Prepara o botão para saber qual o índice
                int idx = games.IndexOf(suggestion);
                if (idx != -1) BtnWelcomePlay.Tag = idx;

                WelcomeOverlay.Visibility = Visibility.Visible;

                // Ativa input
                currentInputHandler = welcomeInputHandler;
                welcomeInputHandler.EnterAtStart();
            }
            else
            {
                WelcomeClose_Click(null, null);
            }
        }



        public void WelcomePlay_Click(object sender, RoutedEventArgs e)
        {
            if (_recommendedGame != null)
            {
                int idx = games.IndexOf(_recommendedGame);
                if (idx != -1)
                {
                    SelectIndex(idx);
                    LaunchSelected();
                }
            }
            WelcomeOverlay.Visibility = Visibility.Collapsed;
        }

        public void WelcomeClose_Click(object sender, RoutedEventArgs e)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.5));
            fadeOut.Completed += (s, ev) =>
            {
                WelcomeOverlay.Visibility = Visibility.Collapsed;

                currentInputHandler = gamesInputHandler;
                GamesListBox.Focus();
            };
            WelcomeOverlay.BeginAnimation(OpacityProperty, fadeOut);
        }

        public void ThemeEditor(string themeCode)
        {
            string[] parts = themeCode.Split(',');
            int count = parts.Length;
            if (count < 5) return;

            // AGORA SÃO 15 CORES
            Color[] c = new Color[15];
            int gradType = 0;

            int colorIndex = 0;
            for (int i = 0; i < count; i++)
            {
                string part = parts[i].Trim();
                // Verifica gradiente (último item curto)
                if (i == count - 1 && part.Length < 3 && int.TryParse(part, out int type)) { gradType = type; continue; }

                if (part.Length == 8 && colorIndex < 15)
                {
                    byte a = Convert.ToByte(part.Substring(0, 2), 16);
                    byte r = Convert.ToByte(part.Substring(2, 2), 16);
                    byte g = Convert.ToByte(part.Substring(4, 2), 16);
                    byte b = Convert.ToByte(part.Substring(6, 2), 16);
                    c[colorIndex] = Color.FromArgb(a, r, g, b);
                    colorIndex++;
                }
            }

            // --- LÓGICA DE FALLBACK INTELIGENTE ---
            // Se o tema for antigo (não tem estas cores), usamos as globais

            // Defaults Básicos
            if (c[5] == default) c[5] = Colors.White; // Global Primary
            if (c[6] == default) c[6] = c[5];         // Hero Title
            if (c[7] == default) c[7] = c[1];         // Chat
            if (c[8] == default) c[8] = Color.FromArgb(50, 128, 128, 128); // Tertiary

            // Novos Defaults (Baseados no pedido)
            if (c[9] == default) c[9] = c[5];   // Info Primary -> Global Primary
            if (c[10] == default) c[10] = c[1]; // Info Secondary -> Global Secondary

            if (c[11] == default) c[11] = c[5]; // Friend Name -> Global Primary
            if (c[12] == default) c[12] = c[1]; // Friend Status -> Global Secondary (ou outra cor de destaque se preferires)

            if (c[13] == default) c[13] = c[5]; // Button Normal -> Global Primary
            if (c[14] == default) c[14] = c[5]; // Button Hover -> Global Primary (Normalmente inverte com o fundo, mas começamos com Branco)

            // Chama o aplicador com o array completo
            ApplyThemeColors(c, gradType);
        }

        string ColorToHexConverter(int a, int r, int g, int b)
        {
            return a.ToString("X2") + r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        }

        public string GenerateThemeCode(Color c1, Color c2, Color c3, Color c4, Color c5, Color c6)
        {
            Color[] colors = { c1, c2, c3, c4, c5, c6 };
            List<string> hexCodes = new List<string>();

            foreach (Color c in colors)
            {
                string hex = c.A.ToString("X2") + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
                hexCodes.Add(hex);
            }

            return string.Join(",", hexCodes);
        }

        private void Test_Click()
        {
            // Exemplo de teste
            string str = "FF2D64D2,B4FF3232,FF0AC80A,FFFFFF00,C8505050";

            try
            {
                ThemeEditor(str);
                Console.WriteLine("Tema aplicado com sucesso!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erro ao aplicar tema: " + ex.Message);
            }
        }

        private void OpenThemeCreator_Click(object sender, RoutedEventArgs e)
        {
            // Passamos 'this' para ele conseguir aplicar o tema de volta
            var creator = new ThemeCreatorWindow(this);
            creator.ShowDialog(); // ShowDialog impede que mexas no launcher enquanto crias o tema
        }

        public void UpdateSingleThemeColor(int slot, Color color, int gradType = -1)
        {
            try
            {
                if (gradType != -1) return; // Se mudar gradiente, espera pelo refresh total

                if (slot == 0) SetRes("AccentBrush", new SolidColorBrush(color));
                else if (slot == 1) SetRes("textSecondary", new SolidColorBrush(color));
                else if (slot == 2) UpdateBackgroundStop(0, color);
                else if (slot == 3) UpdateBackgroundStop(1, color);
                else if (slot == 4) SetRes("PanelBackgroundBrush", new SolidColorBrush(color));
                else if (slot == 5) SetRes("TextPrimaryBrush", new SolidColorBrush(color));
                else if (slot == 6) SetRes("HeroTitleBrush", new SolidColorBrush(color));
                else if (slot == 7) SetRes("ChatBrush", new SolidColorBrush(color));
                else if (slot == 8) SetRes("TertiaryBrush", new SolidColorBrush(color));

                // NOVOS SLOTS
                else if (slot == 9) SetRes("InfoPrimaryBrush", new SolidColorBrush(color));
                else if (slot == 10) SetRes("InfoSecondaryBrush", new SolidColorBrush(color));
                else if (slot == 11) SetRes("FriendNameBrush", new SolidColorBrush(color));
                else if (slot == 12) SetRes("FriendStatusBrush", new SolidColorBrush(color));
                else if (slot == 13) SetRes("ButtonTextNormalBrush", new SolidColorBrush(color));
                else if (slot == 14) SetRes("ButtonTextHoverBrush", new SolidColorBrush(color));
            }
            catch { }
        }

        void UpdateBackgroundStop(int index, Color c)
        {
            if (MainBackground.Background is GradientBrush g && g.GradientStops.Count > index) g.GradientStops[index].Color = c;
        }

        // Helper para evitar crashes se o fundo não for Gradiente
        void EnsureGradientBackground()
        {
            if (!(MainBackground.Background is LinearGradientBrush))
            {
                // Recria o gradiente padrão se estiver em falta
                var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                brush.GradientStops.Add(new GradientStop(Colors.Black, 0.0));
                brush.GradientStops.Add(new GradientStop(Colors.Black, 0.8));
                MainBackground.Background = brush;
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