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

    public class PlayerSummary
    {
        public string SteamId { get; set; } = "";
        public string PersonaName { get; set; } = "";
        public string AvatarFull { get; set; } = "";
        public string ProfileUrl { get; set; } = "";
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
        // 0=Start, 1=Install, 2=Achievs, 3=Feed, 4=Screenshots
        private int focusZone = 0;

        public InfoInputHandler(GameLauncherWindow window) { w = window; }

        void FocusZone()
        {
            w.Dispatcher.InvokeAsync(() =>
            {
                switch (focusZone)
                {
                    case 0: w.StartBtn?.Focus(); break;
                    case 1: w.InstallBtn?.Focus(); break;
                    case 2:
                        if (w.Info_AchievementsList != null)
                        {
                            w.Info_AchievementsList.Focus();
                            if (w.Info_AchievementsList.Items.Count > 0 && w.Info_AchievementsList.SelectedIndex < 0)
                                w.Info_AchievementsList.SelectedIndex = 0;
                        }
                        break;
                    case 3: w.Info_Feed?.Focus(); break;
                    case 4: w.Info_Screenshots?.Focus(); break;
                }
            });
        }

        public void OnLeft() { if (focusZone > 0) { focusZone--; FocusZone(); } }
        public void OnRight() { if (focusZone < 4) { focusZone++; FocusZone(); } } // Max é 4 agora
        public void OnUp()
        {
            if (focusZone == 0 || focusZone == 1) w.SwitchToGamesFromInfo();
            else { focusZone--; FocusZone(); }
        }
        public void OnDown() { if (focusZone < 4) { focusZone++; FocusZone(); } }
        public void OnAccept()
        {
            var focused = Keyboard.FocusedElement as FrameworkElement;
            if (focused is Button btn) btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        public void OnCancel() => w.SwitchToGamesFromInfo();
        public void EnterAtStart() { focusZone = 0; FocusZone(); }
    }

    // --- JANELA PRINCIPAL ---
    public partial class GameLauncherWindow : Window
    {
        // Inputs
        IInputHandler currentInputHandler;
        GamesInputHandler gamesInputHandler;
        FriendsInputHandler friendsInputHandler;
        InfoInputHandler infoInputHandler;
        private PlayStationController? _controller;

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

        // Comando para abrir perfil
        public ICommand OpenFriendProfileCommand => new RelayCommand<string>(url =>
        {
            if (!string.IsNullOrEmpty(url))
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
            }
        });

        public GameLauncherWindow()
        {
            InitializeComponent();

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
            if (currentIndex == 0) // Home
            {
                Carousel.Visibility = Visibility.Visible;
                InfoArea.Visibility = Visibility.Visible;
                FriendMenu.Visibility = Visibility.Collapsed;

                isFriendMenu = false;
                currentInputHandler = gamesInputHandler;
                GamesListBox.Focus();
            }
            else if (currentIndex == 1) // Friends
            {
                Carousel.Visibility = Visibility.Collapsed;
                InfoArea.Visibility = Visibility.Collapsed;
                FriendMenu.Visibility = Visibility.Visible;

                isFriendMenu = true;
                currentInputHandler = friendsInputHandler;
                FriendListBox.Focus();
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
            catch (Exception ex) { Dispatcher.Invoke(() => StatusLabel.Text = "Erro scan: " + ex.Message); }
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

                        // 3. Achievements (Cálculo da Percentagem) - NOVO
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
                                SteamId = p.GetProperty("steamid").GetString()!,
                                PersonaName = p.GetProperty("personaname").GetString()!,
                                AvatarFull = p.GetProperty("avatarfull").GetString()!,
                                ProfileUrl = p.GetProperty("profileurl").GetString()!
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
            }
            catch (Exception ex) { await Dispatcher.InvokeAsync(() => MessageBox.Show(this, "Erro connect: " + ex.Message)); }
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
            return Microsoft.VisualBasic.Interaction.InputBox(prompt, title, def);
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

        private void Window_Closed(object sender, EventArgs e) => _controller?.Stop();

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
            if (Keyboard.FocusedElement is TextBox) return;
            bool handled = false;
            switch (e.Key)
            {
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

            // Se não encontrar o container, tenta forçar scroll para o trazer à vista primeiro
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
                    // Calcula a posição relativa do item dentro do ScrollViewer
                    Point relativePoint = container.TransformToAncestor(scrollViewer).Transform(new Point(0, 0));

                    double currentOffset = scrollViewer.HorizontalOffset;

                    // Math: Onde estou + Onde o item está - Metade do Ecrã + Metade do Item
                    double targetOffset = currentOffset + relativePoint.X - (scrollViewer.ViewportWidth / 2) + (container.ActualWidth / 2);

                    // Impede scroll para valores negativos (antes do inicio da lista)
                    if (targetOffset < 0) targetOffset = 0;

                    // Impede scroll para além do máximo (opcional, o WPF trata disto, mas fica mais limpo)
                    if (targetOffset > scrollViewer.ScrollableWidth) targetOffset = scrollViewer.ScrollableWidth;

                    _ = AnimateScroll(scrollViewer, targetOffset);
                }
            }
        }
        private async Task AnimateScroll(ScrollViewer scroll, double targetOffset)
        {
            double current = scroll.HorizontalOffset;
            double diff = targetOffset - current;

            // Se a distância for muito pequena, não anima, apenas salta
            if (Math.Abs(diff) < 1)
            {
                scroll.ScrollToHorizontalOffset(targetOffset);
                return;
            }

            // Animação manual (suave)
            int steps = 30; // Quantidade de frames da animação
            for (int i = 1; i <= steps; i++)
            {
                // Fórmula matemática "Ease Out" para o movimento começar rápido e travar suavemente
                double t = (double)i / steps;
                double ease = 1 - Math.Pow(1 - t, 3);

                scroll.ScrollToHorizontalOffset(current + (diff * ease));

                // Espera um pouco para criar o efeito de vídeo (aprox 60fps)
                await Task.Delay(10);
            }

            // Garante que no final fica exatamente na posição certa
            scroll.ScrollToHorizontalOffset(targetOffset);
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