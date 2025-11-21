using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Threading;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using SteamKit2.Internal;
using SteamKit2;
using HidSharp;
using HidSharp.Reports;
using HidSharp.Utility;
using DualSenseAPI;
using DualSenseAPI.State;

namespace PichalUI
{
    public class GameEntry
    {
        public string Title { get; set; }
        public string Source { get; set; }
        public string SteamAppId { get; set; }
        public string ExePath { get; set; }
        public string WorkingDirectory { get; set; }
        public ImageSource Cover { get; set; }
        public ImageSource Icon { get; set; }
        public float Scale = 1.0f;

        public string Description { get; set; }
        public int ProgressPercent { get; set; } = 0;
        public List<string> Achievements { get; set; }
        public double PlaytimeHours { get; set; } = 0.0;
        public DateTime? LastPlayed { get; set; }
        public string StorePlatform { get; set; }
        public string StoreId { get; set; }
    }

    public class PlayerSummary
    {
        public string SteamId { get; set; }
        public string PersonaName { get; set; }
        public string AvatarFull { get; set; }   // url para imagem (Steam avatarfull)
        public string ProfileUrl { get; set; }
    }

    // relay command (simples)
    public class RelayCommand<T> : ICommand
    {
        readonly Action<T> _act;
        public RelayCommand(Action<T> a) { _act = a; }
        public bool CanExecute(object parameter) => true;
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public void Execute(object parameter) => _act((T)parameter);
    }

    public interface IInputHandler
    {
        void OnLeft();
        void OnRight();
        void OnUp();
        void OnDown();
        void OnAccept(); // Enter / A
        void OnCancel(); // Esc / B / Back
    }

    public class GamesInputHandler : IInputHandler
    {
        readonly GameLauncherWindow w;
        public GamesInputHandler(GameLauncherWindow window) { w = window; }
        public void OnLeft() => w.SelectPrevious();
        public void OnRight() => w.SelectNext();
        public void OnUp() { /* opcional: scroll ou nada */ }
        public void OnDown() { w.SwitchToInfo(); }
        public void OnAccept() { w.LaunchSelected(); }
        public void OnCancel() { }
        public void OnLeftBumper() { w.SwapViewLeft(); }
        public void OnRightBumper()
        {
            w.SwapViewRight();
        }

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

        public void OnLeft() { /* optional */ }
        public void OnRight() { /* optional */ }
        public void OnUp()
        {
            if (w.FriendListBox == null) return;
            EnsureList();
            w.FriendListBox.SelectedIndex = Math.Max(0, w.FriendListBox.SelectedIndex - 1);
            if (w.FriendListBox.SelectedItem != null) w.FriendListBox.ScrollIntoView(w.FriendListBox.SelectedItem);
        }
        public void OnDown()
        {
            if (w.FriendListBox == null) return;
            EnsureList();
            w.FriendListBox.SelectedIndex = Math.Min(Math.Max(0, w.FriendListBox.Items.Count - 1), w.FriendListBox.SelectedIndex + 1);
            if (w.FriendListBox.SelectedItem != null) w.FriendListBox.ScrollIntoView(w.FriendListBox.SelectedItem);
        }
        public void OnAccept()
        {
            if (w.FriendListBox?.SelectedItem is PlayerSummary ps && !string.IsNullOrEmpty(ps.ProfileUrl))
                w.OpenFriendProfileCommand.Execute(ps.ProfileUrl);
        }
        public void OnLeftBumper()
        {
            w.SwapViewLeft();
        }
        public void OnRightBumper()
        {
            w.SwapViewRight();
        }

        public void OnCancel() { }
    }

    public class InfoInputHandler : IInputHandler
    {
        readonly GameLauncherWindow w;
        // 0 Start | 1 Install | 2 Achievements | 3 Feed | 4 Media | 5 Screenshots
        private int focusZone = 0;

        public InfoInputHandler(GameLauncherWindow window) { w = window; }

        void EnsureSelections()
        {
            if (w.Info_AchievementsList != null && w.Info_AchievementsList.Items.Count > 0 && w.Info_AchievementsList.SelectedIndex < 0)
                w.Info_AchievementsList.SelectedIndex = 0;

            if (w.Info_MediaItems != null && w.Info_MediaItems.Items.Count > 0 && w.Info_MediaItems.SelectedIndex < 0)
                w.Info_MediaItems.SelectedIndex = 0;

            if (w.Info_Screenshots != null && w.Info_Screenshots.Items.Count > 0 && w.Info_Screenshots.SelectedIndex < 0)
                w.Info_Screenshots.SelectedIndex = 0;
        }

        void FocusZone()
        {
            w.Dispatcher.InvokeAsync(() =>
            {
                EnsureSelections();
                switch (focusZone)
                {
                    case 0:
                        w.StartBtn?.Focus();
                        Keyboard.Focus(w.StartBtn);
                        break;
                    case 1:
                        w.InstallBtn?.Focus();
                        Keyboard.Focus(w.InstallBtn);
                        break;
                    case 2:
                        if (w.Info_AchievementsList != null)
                        {
                            w.Info_AchievementsList.Focus();
                            Keyboard.Focus(w.Info_AchievementsList);
                            if (w.Info_AchievementsList.SelectedItem != null) w.Info_AchievementsList.ScrollIntoView(w.Info_AchievementsList.SelectedItem);
                        }
                        break;
                    case 3:
                        w.Info_Feed?.Focus();
                        Keyboard.Focus(w.Info_Feed);
                        break;
                    case 4:
                        if (w.Info_MediaItems != null)
                        {
                            w.Info_MediaItems.Focus();
                            Keyboard.Focus(w.Info_MediaItems);
                            if (w.Info_MediaItems.SelectedItem != null) w.Info_MediaItems.ScrollIntoView(w.Info_MediaItems.SelectedItem);
                        }
                        break;
                    case 5:
                        if (w.Info_Screenshots != null)
                        {
                            w.Info_Screenshots.Focus();
                            Keyboard.Focus(w.Info_Screenshots);
                            if (w.Info_Screenshots.SelectedItem != null) w.Info_Screenshots.ScrollIntoView(w.Info_Screenshots.SelectedItem);
                        }
                        break;
                }
            });
        }

        public void OnLeft()
        {
            if (focusZone == 4 && w.Info_MediaItems != null)
            {
                w.Info_MediaItems.SelectedIndex = Math.Max(0, w.Info_MediaItems.SelectedIndex - 1);
                w.Info_MediaItems.ScrollIntoView(w.Info_MediaItems.SelectedItem);
                return;
            }
            if (focusZone == 5 && w.Info_Screenshots != null)
            {
                w.Info_Screenshots.SelectedIndex = Math.Max(0, w.Info_Screenshots.SelectedIndex - 1);
                w.Info_Screenshots.ScrollIntoView(w.Info_Screenshots.SelectedItem);
                return;
            }

            // navegar entre zonas
            if (focusZone > 0) { focusZone--; FocusZone(); }
        }

        public void OnRight()
        {
            if (focusZone == 4 && w.Info_MediaItems != null)
            {
                w.Info_MediaItems.SelectedIndex = Math.Min(w.Info_MediaItems.Items.Count - 1, w.Info_MediaItems.SelectedIndex + 1);
                w.Info_MediaItems.ScrollIntoView(w.Info_MediaItems.SelectedItem);
                return;
            }
            if (focusZone == 5 && w.Info_Screenshots != null)
            {
                w.Info_Screenshots.SelectedIndex = Math.Min(w.Info_Screenshots.Items.Count - 1, w.Info_Screenshots.SelectedIndex + 1);
                w.Info_Screenshots.ScrollIntoView(w.Info_Screenshots.SelectedItem);
                return;
            }

            // navegar entre zonas
            if (focusZone < 5) { focusZone++; FocusZone(); }
        }

        public void OnUp()
        {
            // mover dentro das listas se possível, senão sobe zona
            if (focusZone == 2 && w.Info_AchievementsList != null)
            {
                if (w.Info_AchievementsList.SelectedIndex > 0)
                {
                    w.Info_AchievementsList.SelectedIndex--;
                    w.Info_AchievementsList.ScrollIntoView(w.Info_AchievementsList.SelectedItem);
                    return;
                }
            }

            if (focusZone == 4)
            {
                // subir uma linha lógica no grid de media: simplificado -> mover -3 (ajusta se necessário)
                int step = 3;
                w.Info_MediaItems.SelectedIndex = Math.Max(0, w.Info_MediaItems.SelectedIndex - step);
                w.Info_MediaItems.ScrollIntoView(w.Info_MediaItems.SelectedItem);
                return;
            }

            // zonas normais: sobe
            if (focusZone > 0) { focusZone--; FocusZone(); }
            else
            {
                // se já estiver em Start e subir, volta para games
                OnCancel();
            }
        }

        public void OnDown()
        {
            if (focusZone == 2 && w.Info_AchievementsList != null)
            {
                if (w.Info_AchievementsList.SelectedIndex < w.Info_AchievementsList.Items.Count - 1)
                {
                    w.Info_AchievementsList.SelectedIndex++;
                    w.Info_AchievementsList.ScrollIntoView(w.Info_AchievementsList.SelectedItem);
                    return;
                }
            }

            if (focusZone == 4)
            {
                int step = 3;
                w.Info_MediaItems.SelectedIndex = Math.Min(w.Info_MediaItems.Items.Count - 1, w.Info_MediaItems.SelectedIndex + step);
                w.Info_MediaItems.ScrollIntoView(w.Info_MediaItems.SelectedItem);
                return;
            }

            if (focusZone < 5) { focusZone++; FocusZone(); }
        }

        public void OnAccept()
        {
            // se um botão tem foco, dispara click
            var focused = Keyboard.FocusedElement as FrameworkElement;
            if (focused is Button btn)
            {
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return;
            }

            // itens selecionados
            if (w.Info_AchievementsList != null && w.Info_AchievementsList.IsKeyboardFocusWithin)
            {
                var ach = w.Info_AchievementsList.SelectedItem;
                if (ach != null) w.ShowAchievementDetails(ach);
                return;
            }

            if (w.Info_MediaItems != null && w.Info_MediaItems.IsKeyboardFocusWithin)
            {
                var item = w.Info_MediaItems.SelectedItem as string;
                if (!string.IsNullOrEmpty(item)) w.OpenUrl(item);
                return;
            }

            if (w.Info_Screenshots != null && w.Info_Screenshots.IsKeyboardFocusWithin)
            {
                var item = w.Info_Screenshots.SelectedItem as string;
                if (!string.IsNullOrEmpty(item)) w.OpenUrl(item);
                return;
            }

            // feed: nada por defeito
        }

        public void OnCancel()
        {
            // volta para jogos
            w.SwitchToGamesFromInfo();
        }

        // opcional: expor método para iniciar no StartBtn
        public void EnterAtStart()
        {
            focusZone = 0;
            FocusZone();
        }
    }

    public class StoreInputHandler : IInputHandler
    {
        readonly GameLauncherWindow w;
        public StoreInputHandler(GameLauncherWindow window) { w = window; }

        public void OnLeft() => w.StoreSelectPrevious();
        public void OnRight() => w.StoreSelectNext();
        public void OnUp()    // jump up a row (uses your existing logic)
        {
            // reuse the existing StoreSelectIndex logic with negative offset
            // emulate KeyUp behaviour by computing columns like in GameLauncherWindow_KeyDown
            try
            {
                int cols = 1;
                if (w.storeWrapPanel != null && w.storeWrapPanel.ActualWidth > 0)
                    cols = Math.Max(1, (int)(w.storeWrapPanel.ActualWidth / (GameLauncherWindow.StoreCardW + GameLauncherWindow.StoreCardMargin)));
                else if (w.storeScrollViewer != null && w.storeScrollViewer.ViewportWidth > 0)
                    cols = Math.Max(1, (int)(w.storeScrollViewer.ViewportWidth / (GameLauncherWindow.StoreCardW + GameLauncherWindow.StoreCardMargin)));
                int baseIndex = w.storeSelectedIndex < 0 ? 0 : w.storeSelectedIndex;
                int target = baseIndex - cols;
                target = Math.Max(0, Math.Min(target, w.storeItemControls.Count - 1));
                w.StoreSelectIndex(target);
            }
            catch { }
        }
        public void OnDown()
        {
            try
            {
                int cols = 1;
                if (w.storeWrapPanel != null && w.storeWrapPanel.ActualWidth > 0)
                    cols = Math.Max(1, (int)(w.storeWrapPanel.ActualWidth / (GameLauncherWindow.StoreCardW + GameLauncherWindow.StoreCardMargin)));
                else if (w.storeScrollViewer != null && w.storeScrollViewer.ViewportWidth > 0)
                    cols = Math.Max(1, (int)(w.storeScrollViewer.ViewportWidth / (GameLauncherWindow.StoreCardW + GameLauncherWindow.StoreCardMargin)));
                int baseIndex = w.storeSelectedIndex < 0 ? 0 : w.storeSelectedIndex;
                int target = baseIndex + cols;
                target = Math.Max(0, Math.Min(target, w.storeItemControls.Count - 1));
                w.StoreSelectIndex(target);
            }
            catch { }
        }
        public void OnAccept() => w.StoreActivateSelected();
        public void OnCancel() => w.ToggleStoreView(false);
    }

    public partial class GameLauncherWindow : Window
    {
        //Inputs Handlers
        IInputHandler currentInputHandler;
        GamesInputHandler gamesInputHandler;
        FriendsInputHandler friendsInputHandler;
        StoreInputHandler storeInputHandler;
        InfoInputHandler infoInputHandler;

        private PlayStationController _controller;

        // Data
        List<GameEntry> games = new List<GameEntry>();
        public int selectedIndex = -1;

        DispatcherTimer animTimer;
        DispatcherTimer xinputTimer;

        readonly string appCache;
        static readonly HttpClient http = new HttpClient();

        XInputNative.XINPUT_STATE xInputPrevState;
        DateTime lastNav = DateTime.MinValue;

        CancellationTokenSource _dsCts;

        int innerTargetLeft = 0;
        bool isPopulating = false;
        bool isLayoutDirty = false;

        Point floatingTargetLocation = new Point();
        double floatingAlpha = 0.0;
        double floatingAlphaTarget = 0.0;
        double floatingScale = 0.96;
        double floatingScaleTarget = 1.0;

        bool isScanning = false;
        bool firstScan = true;
        double scanAnimTime = 0;
        int scanBars = 6;

        const double FloatingLerp = 0.8;
        const double FloatingFadeLerpAnim = 0.08;
        const double FloatingScaleLerpAnim = 0.08;


        // Steam integration
        string connectedSteamId = null;    // steamid64 após login
        string steamApiKey = null;         // podes pedir ao user
        bool steamConnected => !string.IsNullOrEmpty(connectedSteamId);

        bool anyPadInput = false;

        readonly string configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PichalnovenseUI");
        readonly string apiKeyFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PichalnovenseUI", "steam_key.dat");

        public bool isShowingStoreView = false;
        List<(int appid, string name, ImageSource cover)> storeNotInstalled = new List<(int, string, ImageSource)>();

        readonly string loginFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PichalnovenseUI", "steam_login.dat");

        // store view state
        public int storeSelectedIndex = -1;
        public List<FrameworkElement> storeItemControls = new List<FrameworkElement>();
        public ScrollViewer storeScrollViewer = null;
        public WrapPanel storeWrapPanel = null;

        // store card size
        public const int StoreCardW = 240;
        public const int StoreCardH = 320;
        public const int StoreCardMargin = 12;

        public int currentIndex = 0;
        public bool isFriendMenu = false;

        private DualSenseInputState _prevDsState = null!;
        private DateTime _lastNav = DateTime.MinValue;


        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_VIBRATION
        {
            public ushort wLeftMotorSpeed;   // 0..65535
            public ushort wRightMotorSpeed;  // 0..65535
        }

        public GameLauncherWindow()
        {
            InitializeComponent();
            gamesInputHandler = new GamesInputHandler(this);
            friendsInputHandler = new FriendsInputHandler(this);
            storeInputHandler = new StoreInputHandler(this);
            infoInputHandler = new InfoInputHandler(this);
            currentInputHandler = gamesInputHandler;
            BtnConnectSteam.Click += (s, e) => _ = Task.Run(() => ConnectSteamFlowAsync());
            TestPlaystationController();

            appCache = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PichalnovenseUI", "covercache");
            Directory.CreateDirectory(appCache);

            BtnRefresh.Click += (s, e) => Task.Run(() => RescanGamesAsync());
            StartBtn.Click += (s, e) => LaunchSelected();

            animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            animTimer.Tick += AnimTimer_Tick;
            animTimer.Start();

            xinputTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            xinputTimer.Tick += XinputTimer_Tick;
            xinputTimer.Start();

            this.KeyDown += GameLauncherWindow_KeyDown;
            // garante que setas/enter são capturadas mesmo se um child control tem foco
            this.PreviewKeyDown += GameLauncherWindow_PreviewKeyDown;
            this.SizeChanged += (s, e) => { if (games.Count > 0) PopulateGamesPanel(); };

            try
            {
                var loaded = LoadSteamApiKey();
                if (!string.IsNullOrEmpty(loaded))
                {
                    StatusLabel.Text = "Steam API Key carregada.";
                }
                else
                {
                    StatusLabel.Text = "Steam not connected.";
                }
            }
            catch { }

            // dentro do construtor, depois de LoadSteamApiKey():
            try
            {
                var savedId = LoadConnectedSteamId(); // retorna null se não existir
                if (!string.IsNullOrEmpty(savedId))
                {
                    connectedSteamId = savedId;
                    StatusLabel.Text = $"Steam: connected (cached) {savedId}";
                    // refresca os dados em background
                    _ = Task.Run(async () =>
                    {

                        var profileSummary = await GetProfileSummary();
                        await FetchSteamDataForAllGamesAsync();
                        await Dispatcher.InvokeAsync(() =>
                        {
                            PopulateGamesPanel();
                            if (games.Count > 0) SelectIndex(0);
                            ProfileName.Text = profileSummary.PersonaName;
                            var bitmapImage = new BitmapImage();
                            bitmapImage.BeginInit();
                            bitmapImage.UriSource = new Uri(profileSummary.AvatarFull);
                            bitmapImage.EndInit();

                            ProfileImage.Source = bitmapImage;
                        });
                    });
                }
            }
            catch { }

            Task.Run(() => RescanGamesAsync());
            Debug_AddDummyTiles();

            uint controllerIndex = 0;

            // Valores 0..65535
            ushort left = (ushort)(65535 * 0.60);   // 60% força no motor esquerdo
            ushort right = (ushort)(65535 * 0.80);  // 80% força no motor direito
            int durationMs = 2000; // 2000ms = 2 segundos

            bool ok = VibrateFor(controllerIndex, left, right, durationMs);
        }

        public void SwitchToInfo()
        {
            currentInputHandler = infoInputHandler;
            // garante que o handler foca o primeiro elemento (Start)
            Dispatcher.InvokeAsync(() => infoInputHandler.EnterAtStart());
        }

        public void SwitchToGamesFromInfo()
        {
            currentInputHandler = gamesInputHandler; // ou como o teu router esteja
            Dispatcher.InvokeAsync(() =>
            {
                // foca a viewport ou item actual nos jogos
                GamesViewport?.Focus();
                Keyboard.Focus(GamesViewport);
            });
        }

        public void OpenUrl(string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url)) return;
                var psi = new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
        }

        private void GameLauncherWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var focused = Keyboard.FocusedElement as FrameworkElement;
            if (focused is TextBox || focused is PasswordBox) return;

            // store mode has its own handler
            if (isShowingStoreView)
            {
                // route to store handler
                if (currentInputHandler != storeInputHandler) currentInputHandler = storeInputHandler;
            }
            else
            {
                // if we are in friend view ensure handler is friendsHandler
                if (isFriendMenu && currentInputHandler != friendsInputHandler) currentInputHandler = friendsInputHandler;
                else if (!isFriendMenu && currentInputHandler != gamesInputHandler && !isShowingStoreView) currentInputHandler = gamesInputHandler;
            }

            bool consumed = false;

            switch (e.Key)
            {
                case Key.Left:
                    currentInputHandler?.OnLeft(); consumed = true; break;
                case Key.Right:
                    currentInputHandler?.OnRight(); consumed = true; break;
                case Key.Up:
                    currentInputHandler?.OnUp(); consumed = true; break;
                case Key.Down:
                    currentInputHandler?.OnDown(); consumed = true; break;
                case Key.Enter:
                    currentInputHandler?.OnAccept(); consumed = true; break;
                case Key.Escape:
                    currentInputHandler?.OnCancel(); consumed = true; break;
                case Key.F5:
                    _ = Task.Run(() => RescanGamesAsync()); consumed = true; break;
                case Key.F11:
                    ToggleFullscreen(); consumed = true; break;
                case Key.RightShift:
                    // toggle view (already handled by SwapView) — keep behavior
                    SwapViewRight();
                    consumed = true;
                    break;
            }

            if (consumed) { e.Handled = true; anyPadInput = false; }
        }

        public ICommand OpenFriendProfileCommand => new RelayCommand<string>(url =>
        {
            try
            {
                if (!string.IsNullOrEmpty(url))
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        });


        void SaveConnectedSteamId(string steamid)
        {
            try
            {
                EnsureConfigDir();
                if (string.IsNullOrEmpty(steamid))
                {
                    if (File.Exists(loginFilePath)) File.Delete(loginFilePath);
                    connectedSteamId = null;
                    return;
                }
                var bytes = Encoding.UTF8.GetBytes(steamid);
                var protectedData = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(loginFilePath, protectedData);
                connectedSteamId = steamid;
                Dispatcher.Invoke(() => StatusLabel.Text = "Steam login saved.");
            }
            catch { }
        }

        string LoadConnectedSteamId()
        {
            try
            {
                if (!File.Exists(loginFilePath)) return null;
                var protectedData = File.ReadAllBytes(loginFilePath);
                var bytes = ProtectedData.Unprotect(protectedData, null, DataProtectionScope.CurrentUser);
                var id = Encoding.UTF8.GetString(bytes);
                connectedSteamId = id;
                return id;
            }
            catch { return null; }
        }


        async Task<string> DoOpenIdViaExternalBrowserAsync(int timeoutSeconds = 120)
        {
            // escolhe uma porta dinâmica disponível
            var listener = new HttpListener();
            int port = GetFreePort();
            var redirectUri = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(redirectUri);
            listener.Start();

            // build OpenID request
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

            // open system browser (should reuse cookies -> avoid login)
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            // wait for callback
            var ctxTask = listener.GetContextAsync();
            var finished = await Task.WhenAny(ctxTask, Task.Delay(timeoutSeconds * 1000));
            if (finished != ctxTask)
            {
                listener.Stop();
                return null;
            }

            var ctx = ctxTask.Result;
            // reply a simple page so the browser shows something
            var responseString = "<html><body><h2>Logged in — you may close this window.</h2></body></html>";
            var buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
            ctx.Response.ContentLength64 = buffer.Length;
            ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
            ctx.Response.OutputStream.Close();

            // parse query string returned by Steam (openid.* fields)
            var qs = ctx.Request.QueryString;
            // Steam returns openid.claimed_id like https://steamcommunity.com/openid/id/<steamid>
            var claimed = qs["openid.claimed_id"];
            listener.Stop();

            // Note: for production should validate the OpenID response by sending a 'check_authentication' request back to provider.
            // For brevity we're trusting Steam here; if you want full security, do the OpenID verification step.
            return claimed;
        }

        int GetFreePort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        Dictionary<string, string> ParseQueryString(string q)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(q)) return dict;
            if (q.StartsWith("?") || q.StartsWith("#")) q = q.Substring(1);
            var parts = q.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var kv = p.Split(new[] { '=' }, 2);
                var k = Uri.UnescapeDataString(kv[0]);
                var v = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
                dict[k] = v;
            }
            return dict;
        }

        string PromptForText(string title, string prompt, string defaultValue = "")
        {
            var w = new Window()
            {
                Title = title,
                Width = 520,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = this
            };

            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tbPrompt = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) };
            Grid.SetRow(tbPrompt, 0);
            grid.Children.Add(tbPrompt);

            var txt = new TextBox { Text = defaultValue ?? "", Margin = new Thickness(0, 0, 0, 8) };
            Grid.SetRow(txt, 1);
            grid.Children.Add(txt);

            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "OK", Width = 90, Margin = new Thickness(6, 0, 0, 0) };
            var cancel = new Button { Content = "Cancelar", Width = 90, Margin = new Thickness(6, 0, 0, 0) };
            panel.Children.Add(cancel); panel.Children.Add(ok);
            Grid.SetRow(panel, 2);
            grid.Children.Add(panel);

            ok.Click += (s, e) => { w.DialogResult = true; w.Close(); };
            cancel.Click += (s, e) => { w.DialogResult = false; w.Close(); };

            w.Content = grid;
            var res = w.ShowDialog();
            return res == true ? txt.Text : null;
        }

        // Mantém apenas UMA versão de ConnectSteamFlowAsync!!! Claramente nao tive erros pq tinha este aqui e um na linha 2000
        async Task ConnectSteamFlowAsync()
        {
            try
            {
                // opcional: pedir API key (helper PromptForText deve existir)
                string key = null;
                await Dispatcher.InvokeAsync(() =>
                {
                    // só perguntar se não temos key carregada
                    if (steamApiKey == null)
                        key = PromptForText("Steam Web API Key (opcional)", "Enter Steam Web API Key (or leave blank):", "");
                    else
                        key = steamApiKey;
                });

                // Se o utilizador abriu o prompt e colocou algo (não cancelou)
                // PromptForText devolve null se o utilizador cancelar — só guardamos se string != null
                if (key != null)
                {
                    key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
                    // guarda/remova a key conforme o valor
                    SaveSteamApiKey(key);
                    // steamApiKey agora já foi actualizada dentro do SaveSteamApiKey
                }
                // se key == null => utilizador cancelou; mantemos steamApiKey como estava

                // faz o fluxo OpenID (abre browser e recebe claimed)
                var claimed = await DoOpenIdViaExternalBrowserAsync(120);
                if (string.IsNullOrEmpty(claimed))
                {
                    await Dispatcher.InvokeAsync(() => StatusLabel.Text = "Steam login cancelled / failed.");
                    return;
                }

                var parts = claimed.TrimEnd('/').Split('/');
                var steamId = parts.LastOrDefault();
                if (string.IsNullOrEmpty(steamId))
                {
                    await Dispatcher.InvokeAsync(() => StatusLabel.Text = "Steam login failed: steamid not found.");
                    return;
                }

                connectedSteamId = steamId;
                SaveConnectedSteamId(connectedSteamId);   // <--- grava para disco (proteção DPAPI)
                await Dispatcher.InvokeAsync(() => StatusLabel.Text = $"Connected to Steam: {connectedSteamId}");

                if (!string.IsNullOrEmpty(steamApiKey))
                {
                    var summary = await GetPlayerSummariesAsync(connectedSteamId);
                    if (summary != null && summary.TryGetValue("personaname", out var name))
                        await Dispatcher.InvokeAsync(() => StatusLabel.Text = $"Connected: {name}");
                }

                // after setting connectedSteamId and updating status...
                await Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        this.Activate();
                        this.Topmost = true; this.Topmost = false;
                        await Task.Delay(150); // pequena espera para garantir foco volta do browser
                        FocusManager.SetFocusedElement(this, GamesViewport);
                        Keyboard.Focus(GamesViewport);
                        // fallback para inner/primeiro elemento
                        if (GamesInner != null && GamesInner.Children.Count > 0)
                        {
                            var first = GamesInner.Children[Math.Min(selectedIndex >= 0 ? selectedIndex : 0, GamesInner.Children.Count - 1)] as UIElement;
                            first?.Focus();
                        }
                    }
                    catch { }
                });

                // trigger full Steam data refresh for known games (fire-and-forget)
                _ = Task.Run(async () =>
                {
                    await FetchSteamDataForAllGamesAsync();

                    await FetchAndShowFriendsAsync();

                    // after fetch, refresca UI
                    await Dispatcher.InvokeAsync(() =>
                    {
                        PopulateGamesPanel();
                        if (selectedIndex >= 0) SelectIndex(selectedIndex); else if (games.Count > 0) SelectIndex(0);
                    });
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => MessageBox.Show(this, "Steam connect failed: " + ex.Message));
            }
        }

        async Task FetchSteamDataForAllGamesAsync()
        {
            if (string.IsNullOrEmpty(connectedSteamId))
            {
                await Dispatcher.InvokeAsync(() => StatusLabel.Text = "Steam not connected.");
                return;
            }

            try
            {
                await Dispatcher.InvokeAsync(() => StatusLabel.Text = "Fetching Steam data...");

                // Optional: get owned games to speed playtime lookup (one call)
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
                                    int appid = aid.GetInt32();
                                    int minutes = pt.GetInt32();
                                    playtimeByApp[appid] = minutes;
                                }
                            }
                        }
                    }
                    catch { /* ignore per-app fallback handled below */ }
                }

                // iterate games with SteamAppId
                var steamGames = games.Where(g => !string.IsNullOrEmpty(g.SteamAppId)).ToList();
                var sem = new SemaphoreSlim(6); // throttle concurrency
                var tasks = new List<Task>();
                foreach (var g in steamGames)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        if (!int.TryParse(g.SteamAppId, out int appid)) return;
                        await sem.WaitAsync();
                        try
                        {
                            // playtime from bulk map if available
                            if (playtimeByApp.TryGetValue(appid, out var minutes))
                            {
                                g.PlaytimeHours = minutes / 60.0;
                            }
                            else
                            {
                                // fallback: call GetOwnedGames for specific app
                                var play = await GetPlaytimeHoursForAppAsync(connectedSteamId, appid);
                                if (play.HasValue) g.PlaytimeHours = play.Value;
                            }

                            // achievements if API key available
                            if (!string.IsNullOrEmpty(steamApiKey))
                            {
                                var ach = await GetPlayerAchievementsAsync(connectedSteamId, appid);
                                if (ach != null && ach.Count > 0) g.Achievements = ach;
                            }

                            // local screenshots
                            var shots = TryGetLocalSteamScreenshots(connectedSteamId, appid, max: 6);
                            // update UI for this game index (best-effort)
                            await Dispatcher.InvokeAsync(() =>
                            {
                                int idx = games.FindIndex(x => x.SteamAppId == g.SteamAppId && (x.ExePath ?? "") == (g.ExePath ?? ""));
                                if (idx >= 0)
                                {
                                    // update info if currently selected
                                    if (idx == selectedIndex) SelectIndex(idx);
                                    // update cards: if background is ImageBrush, keep it
                                    if (idx < GamesInner.Children.Count)
                                    {
                                        var card = GamesInner.Children[idx] as Border;
                                        if (card != null && g.Cover != null)
                                        {
                                            if (card.Background is ImageBrush ib) ib.ImageSource = g.Cover;
                                            else card.Background = new ImageBrush(g.Cover) { Stretch = Stretch.UniformToFill };
                                        }
                                    }
                                }
                            });
                        }
                        catch { }
                        finally { sem.Release(); }
                    }));
                }

                await Task.WhenAll(tasks);
                await Dispatcher.InvokeAsync(() => StatusLabel.Text = $"Steam data fetched ({steamGames.Count} apps).");
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => StatusLabel.Text = "Steam fetch error: " + ex.Message);
            }
        }

        void AnimTimer_Tick(object sender, EventArgs e)
        {
            double cur = Canvas.GetLeft(GamesInner);
            double target = innerTargetLeft;
            double diff = target - cur;
            if (Math.Abs(diff) > 1)
                Canvas.SetLeft(GamesInner, cur + diff * 0.28);
            else
                Canvas.SetLeft(GamesInner, target);

            if (isLayoutDirty)
            {
                UpdateCardLayout();
                isLayoutDirty = false;
            }

            if (Math.Abs(floatingAlpha - floatingAlphaTarget) > 0.001)
            {
                floatingAlpha += (floatingAlphaTarget - floatingAlpha) * FloatingFadeLerpAnim;
                if (Math.Abs(floatingAlphaTarget - floatingAlpha) < 0.01) floatingAlpha = floatingAlphaTarget;
                FloatingTitleBorder.Opacity = floatingAlpha;
                FloatingOpenBorder.Opacity = floatingAlpha;
            }
            if (Math.Abs(floatingScale - floatingScaleTarget) > 0.001)
            {
                floatingScale += (floatingScaleTarget - floatingScale) * FloatingScaleLerpAnim;
                if (Math.Abs(floatingScaleTarget - floatingScale) < 0.005) floatingScale = floatingScaleTarget;
                FloatingTitleBorder.RenderTransform = new ScaleTransform(floatingScale, floatingScale);
                FloatingOpenBorder.RenderTransform = new ScaleTransform(floatingScale, floatingScale);
            }

            var curLoc = new Point(Canvas.GetLeft(FloatingTitleBorder), Canvas.GetTop(FloatingTitleBorder));
            var curLoc2 = new Point(Canvas.GetLeft(FloatingOpenBorder), Canvas.GetTop(FloatingOpenBorder));
            double dx = floatingTargetLocation.X - curLoc.X;
            double dy = floatingTargetLocation.Y - curLoc.Y;
            double dxO = floatingTargetLocation.X - curLoc2.X;
            double dyO = floatingTargetLocation.Y - curLoc2.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 1)
            {
                double stepX = curLoc.X + dx * FloatingLerp;
                double stepY = curLoc.Y + dy * FloatingLerp;
                double stepXO = curLoc2.X + dxO * FloatingLerp;
                double stepYO = curLoc2.Y + dyO * FloatingLerp;
                Canvas.SetLeft(FloatingTitleBorder, stepX);
                Canvas.SetTop(FloatingTitleBorder, stepY);
                Canvas.SetLeft(FloatingOpenBorder, stepXO);
                Canvas.SetTop(FloatingOpenBorder, stepYO);
            }
            else
            {
                Canvas.SetLeft(FloatingTitleBorder, floatingTargetLocation.X);
                Canvas.SetTop(FloatingTitleBorder, floatingTargetLocation.Y);
            }

            if (floatingAlpha <= 0.01 && floatingAlphaTarget == 0.0 && FloatingTitleBorder.Visibility == Visibility.Visible)
                FloatingTitleBorder.Visibility = Visibility.Collapsed;

            if (isScanning && ScanOverlay.Visibility == Visibility.Visible)
            {
                scanAnimTime += animTimer.Interval.TotalSeconds;
                UpdateScanOverlay();
            }
        }

        void XinputTimer_Tick(object sender, EventArgs e)
        {
            if (!XInputNative.GetState(0, out var st)) { xInputPrevState = st; return; }
            var now = DateTime.UtcNow;
            var buttons = XInputNative.ButtonsFromState(st);
            if ((now - lastNav).TotalMilliseconds < 160) { xInputPrevState = st; return; }

            bool left = (buttons & XInputNative.GamepadButtons.DPadLeft) != 0 || st.Gamepad.sThumbLX < -16000;
            bool right = (buttons & XInputNative.GamepadButtons.DPadRight) != 0 || st.Gamepad.sThumbLX > 16000;
            bool up = (buttons & XInputNative.GamepadButtons.DPadUp) != 0 || st.Gamepad.sThumbLY > 16000;
            bool down = (buttons & XInputNative.GamepadButtons.DPadDown) != 0 || st.Gamepad.sThumbLY < -16000;
            bool a = (buttons & XInputNative.GamepadButtons.A) != 0;
            bool b = (buttons & XInputNative.GamepadButtons.B) != 0;
            bool start = (buttons & XInputNative.GamepadButtons.Start) != 0;
            bool leftBumper = (buttons & XInputNative.GamepadButtons.LeftShoulder) != 0;
            bool rightBumper = (buttons & XInputNative.GamepadButtons.RightShoulder) != 0;

            // garante que o handler corresponde ao modo
            if (isShowingStoreView) currentInputHandler = storeInputHandler;
            else if (isFriendMenu) currentInputHandler = friendsInputHandler;
            else currentInputHandler = gamesInputHandler;

            if (left && !((xInputPrevState.Gamepad.wButtons & (ushort)XInputNative.GamepadButtons.DPadLeft) != 0))
            {
                currentInputHandler?.OnLeft();
                lastNav = now; anyPadInput = true;
            }
            else if (right && !((xInputPrevState.Gamepad.wButtons & (ushort)XInputNative.GamepadButtons.DPadRight) != 0))
            {
                currentInputHandler?.OnRight();
                lastNav = now; anyPadInput = true;
            }
            else if (up && !((xInputPrevState.Gamepad.wButtons & (ushort)XInputNative.GamepadButtons.DPadUp) != 0))
            {
                currentInputHandler?.OnUp();
                lastNav = now; anyPadInput = true;
            }
            else if (down && !((xInputPrevState.Gamepad.wButtons & (ushort)XInputNative.GamepadButtons.DPadDown) != 0))
            {
                currentInputHandler?.OnDown();
                lastNav = now; anyPadInput = true;
            }
            else if (a && !((xInputPrevState.Gamepad.wButtons & (ushort)XInputNative.GamepadButtons.A) != 0))
            {
                currentInputHandler?.OnAccept();
                lastNav = now; anyPadInput = true;
            }
            else if (b && !((xInputPrevState.Gamepad.wButtons & (ushort)XInputNative.GamepadButtons.B) != 0))
            {
                currentInputHandler?.OnCancel();
                lastNav = now; anyPadInput = true;
            }
            else if (start && !((xInputPrevState.Gamepad.wButtons & (ushort)XInputNative.GamepadButtons.Start) != 0))
            {
                _ = Task.Run(() => RescanGamesAsync());
                lastNav = now; anyPadInput = true;
            }
            else if (leftBumper && !((xInputPrevState.Gamepad.wButtons & (ushort)XInputNative.GamepadButtons.LeftShoulder) != 0))
            {
                SwapViewLeft();
            }
            else if (rightBumper && !((xInputPrevState.Gamepad.wButtons & (ushort)XInputNative.GamepadButtons.RightShoulder) != 0))
            {
                SwapViewRight();
            }

            xInputPrevState = st;
        }
        public void GameLauncherWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (isShowingStoreView)
            {
                try
                {
                    // se não há itens, só permite ESC para voltar (Não sei se realmente isto está a funcionar mas vou supôr que sim)
                    if (storeItemControls == null || storeItemControls.Count == 0)
                    {
                        if (e.Key == Key.Escape) { ToggleStoreView(false); e.Handled = true; }
                        return;
                    }

                    switch (e.Key)
                    {
                        case Key.Escape:
                            ToggleStoreView(false);
                            e.Handled = true;
                            break;
                        case Key.Right:
                            StoreSelectNext();
                            e.Handled = true;
                            break;
                        case Key.Left:
                            StoreSelectPrevious();
                            e.Handled = true;
                            break;
                        case Key.Enter:
                            StoreActivateSelected();
                            e.Handled = true;
                            break;
                        case Key.Down or Key.Up:
                            // calcula colunas com fallback seguro (adivinha... não funciona :( )
                            int cols = 1;
                            try
                            {
                                if (storeWrapPanel != null && storeWrapPanel.ActualWidth > 0)
                                    cols = Math.Max(1, (int)(storeWrapPanel.ActualWidth / (StoreCardW + StoreCardMargin)));
                                else if (storeScrollViewer != null && storeScrollViewer.ViewportWidth > 0)
                                    cols = Math.Max(1, (int)(storeScrollViewer.ViewportWidth / (StoreCardW + StoreCardMargin)));
                            }
                            catch (Exception exCols) { LogException(exCols, "CalcCols"); cols = 1; }

                            int offset = (e.Key == Key.Down) ? cols : -cols;
                            int baseIndex = storeSelectedIndex < 0 ? 0 : storeSelectedIndex;
                            int target = baseIndex + offset;
                            if (target < 0) target = 0;
                            if (target >= storeItemControls.Count) target = storeItemControls.Count - 1;
                            StoreSelectIndex(target);
                            e.Handled = true;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    LogException(ex, "KeyDown.StoreMode");
                    e.Handled = true;
                }

                // este return é que faz a magia acontecer
                return;
            }

            if (isFriendMenu == true)
            {
                try
                {
                    if (e.Key == Key.RightShift)
                    {
                        SwapViewRight();
                        e.Handled = true;
                    }
                    if (e.Key == Key.LeftShift)
                    {
                        SwapViewLeft();
                        e.Handled = true;
                    }
                }
                catch (Exception ex)
                {
                    e.Handled = true;
                }
                return;
            }

            switch (e.Key)
            {
                case Key.F11:
                    ToggleFullscreen();
                    e.Handled = true;
                    return;
                case Key.Escape:
                    if (this.WindowState == WindowState.Maximized) ToggleFullscreen();
                    return;
                case Key.Right:
                    SelectNext();
                    e.Handled = true;
                    return;
                case Key.Left:
                    SelectPrevious();
                    e.Handled = true;
                    return;
                case Key.Enter:
                    LaunchSelected();
                    e.Handled = true;
                    return;
                case Key.F5:
                    Task.Run(() => RescanGamesAsync());
                    e.Handled = true;
                    return;
                case Key.RightShift:
                    SwapViewRight();
                    e.Handled = true;
                    return;
                case Key.LeftShift:
                    SwapViewLeft();
                    e.Handled = true;
                    return;
            }
            anyPadInput = false;
        }

        bool isFullScreen = false;
        WindowState prevState; WindowStyle prevStyle; Rect prevBounds;
        public void ToggleFullscreen()
        {
            if (!isFullScreen)
            {
                prevState = this.WindowState; prevStyle = this.WindowStyle; prevBounds = new Rect(this.Left, this.Top, this.Width, this.Height);
                this.WindowStyle = WindowStyle.None; this.WindowState = WindowState.Maximized; this.Topmost = true; isFullScreen = true;
            }
            else
            {
                this.Topmost = false; this.WindowStyle = prevStyle; this.WindowState = prevState; this.Left = prevBounds.X; this.Top = prevBounds.Y; this.Width = prevBounds.Width; this.Height = prevBounds.Height; isFullScreen = false;
            }
        }

        private readonly (Visibility CarouselVis, Visibility InfoVis, Visibility FriendVis, Brush BannerBrush, Brush FriendsBrush)[] states =
        {
            (Visibility.Visible, Visibility.Visible, Visibility.Collapsed, Brushes.White, Brushes.Gray),
            (Visibility.Collapsed, Visibility.Collapsed, Visibility.Visible, Brushes.Gray,  Brushes.White),
            (Visibility.Collapsed, Visibility.Collapsed, Visibility.Collapsed, Brushes.Gray,  Brushes.Gray)
        };


        public void SwapViewLeft()
        {
            if (currentIndex <= 0)
            {
                currentIndex = 3;
            }
            currentIndex = (currentIndex - 1) % states.Length;
            var s = states[currentIndex];

            switch (currentIndex)
            {
                case 0:
                    isFriendMenu = false;
                    currentInputHandler = gamesInputHandler;
                    // restaura foco para a viewport dos jogos
                    Dispatcher.InvokeAsync(() =>
                    {
                        try { GamesViewport?.Focus(); Keyboard.Focus(GamesViewport); }
                        catch { }
                    });
                    break;
                case 1:
                    FriendsMenu_Initialize();
                    isFriendMenu = true;
                    currentInputHandler = friendsInputHandler;
                    Dispatcher.InvokeAsync(() =>
                    {
                        try { FriendListBox?.Focus(); Keyboard.Focus(FriendListBox); if (FriendListBox?.Items.Count > 0) FriendListBox.SelectedIndex = 0; }
                        catch { }
                    });
                    break;
                case 2:
                    isFriendMenu = false;
                    currentInputHandler = gamesInputHandler;
                    Dispatcher.InvokeAsync(() =>
                    {
                        try { GamesViewport?.Focus(); Keyboard.Focus(GamesViewport); }
                        catch { }
                    });
                    break;
            }

            // atualiza visibilidades + brushes
            Carousel.Visibility = s.CarouselVis;
            InfoArea.Visibility = s.InfoVis;
            FriendMenu.Visibility = s.FriendVis;
            BannerText.Foreground = s.BannerBrush;
            FriendsMenu.Foreground = s.FriendsBrush;
        }

        public void SwapViewRight()
        {
            currentIndex = (currentIndex + 1) % states.Length;
            var s = states[currentIndex];

            switch (currentIndex)
            {
                case 0:
                    isFriendMenu = false;
                    currentInputHandler = gamesInputHandler;
                    // restaura foco para a viewport dos jogos
                    Dispatcher.InvokeAsync(() =>
                    {
                        try { GamesViewport?.Focus(); Keyboard.Focus(GamesViewport); }
                        catch { }
                    });
                    break;
                case 1:
                    FriendsMenu_Initialize();
                    isFriendMenu = true;
                    currentInputHandler = friendsInputHandler;
                    Dispatcher.InvokeAsync(() =>
                    {
                        try { FriendListBox?.Focus(); Keyboard.Focus(FriendListBox); if (FriendListBox?.Items.Count > 0) FriendListBox.SelectedIndex = 0; }
                        catch { }
                    });
                    break;
                case 2:
                    isFriendMenu = false;
                    currentInputHandler = gamesInputHandler;
                    Dispatcher.InvokeAsync(() =>
                    {
                        try { GamesViewport?.Focus(); Keyboard.Focus(GamesViewport); }
                        catch { }
                    });
                    break;
            }

            // actualiza visibilidades + brushes (mantém tua lógica)
            Carousel.Visibility = s.CarouselVis;
            InfoArea.Visibility = s.InfoVis;
            FriendMenu.Visibility = s.FriendVis;
            BannerText.Foreground = s.BannerBrush;
            FriendsMenu.Foreground = s.FriendsBrush;
        }

        private async void FriendsMenu_Initialize()
        {
            try
            {
                await FetchAndShowFriendsAsync();
            }
            catch
            {

            }
        }

        public void SelectIndex(int idx)
        {
            // total items in carousel = games.Count (games) + 1 (store card)
            int totalItems = games.Count + 1;

            if (totalItems == 0) return;
            // clamp and normalize into [0 .. totalItems-1]
            idx = Math.Max(0, Math.Min(idx, totalItems - 1));
            selectedIndex = idx;

            // If selectedIndex == games.Count => store card selected
            if (selectedIndex == games.Count)
            {
                // Show store UI (no game data available)
                Dispatcher.Invoke(() =>
                {
                    BannerImage.Source = MakePlaceholderBitmap(1000, 400); // placeholder
                    HeroCover.Source = MakePlaceholderBitmap(800, 450);
                    HeroTitle.Text = "Loja — Jogos na tua conta (não instalados)";
                    StatusLabel.Text = "Seleccionado: Loja";
                    Info_OwnedOn.Text = "Store / Not installed";
                    Info_CompletionPercent.Text = "—";
                    Info_Playtime.Text = "—";
                    Info_LastPlayed.Text = "—";
                    Info_Feed.Text = "Clica ENTER ou usa o botão para ver os jogos da tua conta que não estão instalados.";
                    Info_AchievementsList.Items.Clear();
                    Info_AchievementsList.Items.Add(new TextBlock { Text = "Open the store folder", Foreground = Brushes.LightGray });
                    Info_Screenshots.ItemsSource = null;
                });

                // update layout and floating title
                isLayoutDirty = true;
                UpdateFloatingTitlePosition();
                return;
            }

            // Otherwise treat as real game
            var g = games[selectedIndex];

            Dispatcher.Invoke(() =>
            {
                BannerImage.Source = g.Cover ?? g.Icon ?? MakePlaceholderBitmap(1000, 400);
                HeroCover.Source = g.Cover ?? g.Icon ?? MakePlaceholderBitmap(800, 450);
                HeroTitle.Text = g.Title ?? "Unknown";
                StatusLabel.Text = $"Selected: {g.Title} ({g.Source})";
                Info_OwnedOn.Text = $"You own this game on {g.Source}";
            });

            // after UI update...
            if (steamConnected && !string.IsNullOrEmpty(g.SteamAppId))
            {
                // existing fire-and-forget task to fetch playtime/achievements/screenshots...
                _ = Task.Run(async () =>
                {
                    try
                    {
                        int appid = int.TryParse(g.SteamAppId, out var x) ? x : 0;
                        if (appid > 0)
                        {
                            var play = await GetPlaytimeHoursForAppAsync(connectedSteamId, appid);
                            if (play.HasValue) g.PlaytimeHours = play.Value;

                            if (!string.IsNullOrEmpty(steamApiKey))
                            {
                                var ach = await GetPlayerAchievementsAsync(connectedSteamId, appid);
                                g.Achievements = ach;
                            }

                            var shots = TryGetLocalSteamScreenshots(connectedSteamId, appid, max: 6);

                            await Dispatcher.InvokeAsync(() =>
                            {
                                Info_Playtime.Text = $"{g.PlaytimeHours:0.0} hrs";
                                Info_LastPlayed.Text = g.LastPlayed.HasValue ? g.LastPlayed.Value.ToString("g") : "—";
                                Info_AchievementsList.Items.Clear();
                                if (g.Achievements != null && g.Achievements.Count > 0)
                                {
                                    foreach (var a in g.Achievements) Info_AchievementsList.Items.Add(new TextBlock { Text = a, Foreground = Brushes.White });
                                }
                                else Info_AchievementsList.Items.Add(new TextBlock { Text = "No achievements", Foreground = Brushes.LightGray });

                                Info_Screenshots.ItemsSource = shots;
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        await Dispatcher.InvokeAsync(() => StatusLabel.Text = "Steam info fetch failed: " + ex.Message);
                    }
                });
            }

            // fetch completion & news as you already do
            if (!string.IsNullOrEmpty(g.SteamAppId) && int.TryParse(g.SteamAppId, out int parsedApp))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        int? comp = await GetGameCompletionPercentAsync(connectedSteamId, parsedApp);
                        if (comp.HasValue)
                        {
                            g.ProgressPercent = comp.Value;
                            await Dispatcher.InvokeAsync(() => Info_CompletionPercent.Text = $"{comp.Value}%");
                        }

                        var news = await GetNewsForAppAsync(parsedApp, count: 3, maxlength: 600);
                        if (news != null && news.Count > 0)
                        {
                            var sb = new System.Text.StringBuilder();
                            foreach (var n in news)
                            {
                                sb.AppendLine(n.title);
                                sb.AppendLine(n.date != DateTime.MinValue ? n.date.ToString("g") : "");
                                sb.AppendLine();
                                var txt = n.contents?.Replace("\n", " ").Trim() ?? "";
                                if (txt.Length > 300) txt = txt.Substring(0, 300) + "…";
                                sb.AppendLine(txt);
                                sb.AppendLine("—");
                            }
                            await Dispatcher.InvokeAsync(() => Info_Feed.Text = sb.ToString());
                        }
                        else
                        {
                            await Dispatcher.InvokeAsync(() => { if (string.IsNullOrWhiteSpace(Info_Feed.Text)) Info_Feed.Text = "No recent news."; });
                        }
                    }
                    catch (Exception ex)
                    {
                        await Dispatcher.InvokeAsync(() => StatusLabel.Text = "Steam extra fetch failed: " + ex.Message);
                    }
                });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Info_Feed.Text)) Info_Feed.Text = "No news available.";
            }

            isLayoutDirty = true;
            UpdateFloatingTitlePosition();
        }

        public void SelectNext()
        {
            int totalItems = games.Count + 1; // games + store card
            if (selectedIndex + 1 < totalItems)
            {
                selectedIndex = (selectedIndex + 1) % Math.Max(1, totalItems);
                SelectIndex(selectedIndex);
            }
        }

        public void SelectPrevious()
        {
            int totalItems = games.Count + 1;
            if (selectedIndex > 0)
            {
                selectedIndex = (selectedIndex - 1 + totalItems) % Math.Max(1, totalItems);
                SelectIndex(selectedIndex);
            }
        }

        public void LaunchSelected()
        {
            int totalItems = games.Count + 1;
            if (selectedIndex < 0 || selectedIndex >= totalItems) return;

            if (selectedIndex == games.Count)
            {
                // Store card action
                _ = Task.Run(async () => await LoadStoreNotInstalledAsync());
                ToggleStoreView(true);
                return;
            }
            else
            {
                ToggleStoreView(false);
            }

            var g = games[selectedIndex];
            try
            {
                if (g.Source == "Steam" && !string.IsNullOrEmpty(g.SteamAppId))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"steam://run/{g.SteamAppId}") { UseShellExecute = true });
                    return;
                }
                if (!string.IsNullOrEmpty(g.ExePath) && File.Exists(g.ExePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = g.ExePath, WorkingDirectory = g.WorkingDirectory ?? Path.GetDirectoryName(g.ExePath), UseShellExecute = true });
                    return;
                }
                if (!string.IsNullOrEmpty(g.WorkingDirectory) && Directory.Exists(g.WorkingDirectory))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = g.WorkingDirectory, UseShellExecute = true });
                    return;
                }
                MessageBox.Show("Não foi possível lançar o jogo.", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao lançar: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void PopulateGamesPanel()
        {
            if (isPopulating) return;
            isPopulating = true;
            Dispatcher.Invoke(() =>
            {
                try
                {
                    GamesInner.Children.Clear();
                    int margin = 10; int baseW = 150; int x = margin;
                    for (int i = 0; i < games.Count; i++)
                    {
                        var g = games[i];
                        var brush = new ImageBrush((ImageSource)(g.Cover ?? g.Icon ?? MakePlaceholderBitmap(600, 400)))
                        {
                            Stretch = Stretch.UniformToFill,
                            AlignmentX = AlignmentX.Center,
                            AlignmentY = AlignmentY.Center
                        };

                        var border = new Border
                        {
                            Width = baseW,
                            Height = baseW,
                            CornerRadius = new CornerRadius(6),
                            Background = brush,
                            Tag = i
                        };

                        // Mouse
                        /*border.MouseEnter += (s, e) => SelectIndex((int)border.Tag);
                        border.MouseLeftButtonUp += (s, e) => SelectIndex((int)border.Tag);*/

                        Canvas.SetLeft(border, x);
                        Canvas.SetTop(border, 20);
                        GamesInner.Children.Add(border);
                        x += baseW + margin;
                    }

                    int totalWidth = Math.Max((int)this.ActualWidth, x + margin);
                    GamesInner.Width = totalWidth; GamesInner.Height = (int)GamesViewport.Height;

                    int folderIndex = games.Count; // important: index after last game

                    var folderBorder = new Border
                    {
                        Width = baseW,
                        Height = baseW,
                        CornerRadius = new CornerRadius(6),
                        Background = new SolidColorBrush(Color.FromArgb(32, 30, 30, 30)),
                        Tag = folderIndex, // <-- TAG is integer index (games.Count)
                        Cursor = Cursors.Hand
                    };

                    var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                    var tbIcon = new TextBlock { Text = "⌂", FontSize = 28, Foreground = Brushes.LightGray, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
                    var tbTitle = new TextBlock { Text = "Loja", FontSize = 12, Foreground = Brushes.LightGray, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
                    sp.Children.Add(tbIcon);
                    sp.Children.Add(tbTitle);
                    folderBorder.Child = sp;

                    // capture local copy of index for handlers (avoid closure pitfalls)
                    int capturedIndex = folderIndex;
                    folderBorder.MouseEnter += (s, e) =>
                    {
                        StatusLabel.Text = "Ver jogos da tua conta que não estão instalados.";
                    };
                    folderBorder.MouseLeftButtonUp += (s, e) =>
                    {
                        // select the folder index so keyboard navigation can land here too
                        SelectIndex(capturedIndex);
                    };

                    Canvas.SetLeft(folderBorder, x);
                    Canvas.SetTop(folderBorder, 20);
                    GamesInner.Children.Add(folderBorder);
                    x += baseW + margin;
                    if (x < this.ActualWidth)
                    {
                        int leftPad = ((int)this.ActualWidth - x) / 2;
                        foreach (UIElement c in GamesInner.Children)
                        {
                            double curLeft = Canvas.GetLeft((UIElement)c);
                            Canvas.SetLeft((UIElement)c, curLeft + leftPad);
                        }
                    }
                    UpdateCardLayout();
                }
                finally { isPopulating = false; }
            });
        }

        async Task ShowNotInstalledStoreFolderAsync()
        {
            // precisa de steamApiKey e connectedSteamId
            if (string.IsNullOrEmpty(connectedSteamId))
            {
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(this, "Conecta a tua conta Steam primeiro (OpenID).", "Não ligado", MessageBoxButton.OK, MessageBoxImage.Information));
                return;
            }

            // chama GetOwnedGames include_appinfo=1
            try
            {
                await Dispatcher.InvokeAsync(() => StatusLabel.Text = "A obter biblioteca Steam...");

                var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={steamApiKey}&steamid={connectedSteamId}&include_appinfo=1&include_played_free_games=1";
                using var res = await http.GetAsync(url);
                if (!res.IsSuccessStatusCode)
                {
                    await Dispatcher.InvokeAsync(() => MessageBox.Show(this, "Falha a obter jogos: " + res.ReasonPhrase));
                    return;
                }
                using var st = await res.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(st);
                var list = new List<(int appid, string name)>();
                if (doc.RootElement.TryGetProperty("response", out var resp) && resp.TryGetProperty("games", out var gamesArr))
                {
                    foreach (var g in gamesArr.EnumerateArray())
                    {
                        int appid = g.GetProperty("appid").GetInt32();
                        string name = g.TryGetProperty("name", out var nm) ? nm.GetString() : $"App {appid}";
                        list.Add((appid, name));
                    }
                }

                // filtra os que não estão instalados
                var notInstalled = new List<(int appid, string name)>();
                foreach (var t in list)
                {
                    if (!IsSteamGameInstalled(t.appid))
                        notInstalled.Add(t);
                }

                // abre a janela modal com a lista
                await Dispatcher.InvokeAsync(() =>
                {
                    var wnd = new OwnedGamesWindow(notInstalled, StartSteamInstall); // OwnedGamesWindow construtor recebe a lista e o callback
                    wnd.Owner = this;
                    wnd.ShowDialog();
                });

                await Dispatcher.InvokeAsync(() => StatusLabel.Text = $"Loja carregada. ({notInstalled.Count} não instalados)");
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => MessageBox.Show(this, "Erro a obter lista: " + ex.Message));
            }
        }

        void StartSteamInstall(int appid)
        {
            try
            {
                var uri = $"steam://install/{appid}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
                Dispatcher.Invoke(() => StatusLabel.Text = $"Abrindo Steam para instalar {appid}...");
                // opcional: monitorizar manifest e refresh
                _ = Task.Run(async () =>
                {
                    await WaitForInstallAndRefreshAsync(appid);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => MessageBox.Show(this, "Erro ao iniciar instalação: " + ex.Message));
            }
        }

        async Task WaitForInstallAndRefreshAsync(int appid, int timeoutSeconds = 600)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < timeoutSeconds)
            {
                if (IsSteamGameInstalled(appid))
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        StatusLabel.Text = $"Instalado: {appid}";
                        // re-scan e refresh UI
                        _ = Task.Run(() => RescanGamesAsync());
                    });
                    return;
                }
                await Task.Delay(2000);
            }
            await Dispatcher.InvokeAsync(() => StatusLabel.Text = "Instalação não detectada (timeout).");
        }


        async Task RescanGamesAsync()
        {
            Dispatcher.Invoke(() => StatusLabel.Text = "Scanning Steam and shortcuts...");
            Dispatcher.Invoke(() => { if (firstScan) { isScanning = true; ScanOverlay.Visibility = Visibility.Visible; } });
            try
            {
                var found = new List<GameEntry>();
                found.AddRange(ScanSteamLibraries());
                found.AddRange(ScanStartMenuShortcuts());
                found = found.GroupBy(x =>
                {
                    var steam = (x.SteamAppId ?? "").Trim();
                    var exe = string.IsNullOrEmpty(x.ExePath) ? "" : Path.GetFileName(x.ExePath).ToLowerInvariant();
                    var t = (x.Title ?? "").Trim().ToLowerInvariant();
                    return $"{steam}|{exe}|{t}";
                }).Select(g => g.First()).ToList();

                foreach (var e in found.Where(x => !string.IsNullOrEmpty(x.SteamAppId)))
                {
                    var cachePath = Path.Combine(appCache, $"{e.SteamAppId}.jpg");
                    if (File.Exists(cachePath))
                    {
                        try { e.Cover = LoadBitmapImageFromFile(cachePath); } catch { File.Delete(cachePath); e.Cover = null; }
                    }
                }

                games = found; isLayoutDirty = true;
                Dispatcher.Invoke(() => { PopulateGamesPanel(); if (games.Count > 0) SelectIndex(0); StatusLabel.Text = $"Found {games.Count} games"; });

                var tasks = new List<Task>();
                foreach (var g in found.Where(x => !string.IsNullOrEmpty(x.SteamAppId)))
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        var cachePath = Path.Combine(appCache, $"{g.SteamAppId}.jpg");
                        if (!File.Exists(cachePath))
                        {
                            var img = await TryDownloadSteamCoverAsync(g.SteamAppId);
                            if (img != null)
                            {
                                try { SaveBitmapImageToFile(img, cachePath); g.Cover = img; } catch { g.Cover = img; }
                            }
                        }
                        else if (g.Cover == null)
                        {
                            try { g.Cover = LoadBitmapImageFromFile(cachePath); } catch { g.Cover = null; }
                        }
                        Dispatcher.Invoke(() =>
                        {
                            int idx = games.FindIndex(x => x.SteamAppId == g.SteamAppId && (x.ExePath ?? "") == (g.ExePath ?? ""));
                            if (idx >= 0 && idx < GamesInner.Children.Count)
                            {
                                var card = GamesInner.Children[idx] as Border;
                                if (card != null && g.Cover != null)
                                {
                                    var img = card.Child as Image; if (img != null) img.Source = g.Cover;
                                }
                            }
                        });
                    }));
                }
                await Task.WhenAll(tasks);
                Dispatcher.Invoke(() => { PopulateGamesPanel(); if (selectedIndex >= 0) SelectIndex(selectedIndex); });
            }
            catch (Exception ex) { Dispatcher.Invoke(() => StatusLabel.Text = "Scan failed: " + ex.Message); }
            finally { Dispatcher.Invoke(() => { isScanning = false; ScanOverlay.Visibility = Visibility.Collapsed; firstScan = false; }); }
        }

        List<GameEntry> ScanSteamLibraries()
        {
            var results = new List<GameEntry>();
            try
            {
                var steamPath = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath)) return results;
                var libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                var libs = new List<string> { Path.Combine(steamPath, "steamapps") };
                if (File.Exists(libraryFile))
                {
                    var content = File.ReadAllText(libraryFile);
                    var matches = Regex.Matches(content, @"\""\\d+\""\s*\{[^}]*\""path\""\s*\""([^\""]+)\""|\""path\""\s*\""([^\""]+)\""", RegexOptions.Singleline);
                    foreach (Match m in matches)
                    {
                        var path = m.Groups[1].Success ? m.Groups[1].Value : (m.Groups[2].Success ? m.Groups[2].Value : null);
                        if (!string.IsNullOrEmpty(path))
                        {
                            var sa = Path.Combine(path, "steamapps"); if (Directory.Exists(sa) && !libs.Contains(sa)) libs.Add(sa);
                        }
                    }
                }
                foreach (var lib in libs.Distinct())
                {
                    try
                    {
                        var manifests = Directory.GetFiles(lib, "appmanifest_*.acf");
                        foreach (var mf in manifests)
                        {
                            try
                            {
                                var txt = File.ReadAllText(mf);
                                var appid = Regex.Match(txt, @"\""appid\""\s*\""(\d+)\""")?.Groups[1]?.Value;
                                var name = Regex.Match(txt, @"\""name\""\s*\""([^\""]+)\""")?.Groups[1]?.Value;
                                var installdir = Regex.Match(txt, @"\""installdir\""\s*\""([^\""]+)\""")?.Groups[1]?.Value;
                                var e = new GameEntry { Title = name ?? installdir ?? $"App {appid}", Source = "Steam", SteamAppId = appid };
                                if (!string.IsNullOrEmpty(installdir))
                                {
                                    var common = Path.Combine(lib, "common", installdir);
                                    if (Directory.Exists(common))
                                    {
                                        var exes = Directory.GetFiles(common, "*.exe", SearchOption.AllDirectories);
                                        if (exes.Length > 0)
                                        {
                                            var guess = exes.OrderByDescending(x => new FileInfo(x).Length).First();
                                            e.ExePath = guess; e.WorkingDirectory = Path.GetDirectoryName(guess);
                                            try { e.Icon = TryExtractIconBitmapImage(guess); } catch { }
                                        }
                                    }
                                }
                                results.Add(e);
                            }
                            catch { }
                        }
                    }
                    catch { }
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
                var paths = new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) }.Where(p => !string.IsNullOrEmpty(p)).Distinct();
                foreach (var p in paths)
                {
                    foreach (var lnk in Directory.GetFiles(p, "*.lnk", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var target = ResolveShortcutTarget(lnk);
                            if (string.IsNullOrEmpty(target)) continue;
                            if (!File.Exists(target)) continue;
                            var info = new FileInfo(target);
                            if (info.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                list.Add(new GameEntry { Title = Path.GetFileNameWithoutExtension(lnk), Source = "Shortcut", ExePath = target, WorkingDirectory = Path.GetDirectoryName(target), Icon = TryExtractIconBitmapImage(target) });
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return list;
        }

        ImageSource TryExtractIconBitmapImage(string exe)
        {
            try
            {
                var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                if (ico == null) return null;
                var src = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(64, 64));
                src.Freeze(); return src;
            }
            catch { return null; }
        }

        string ResolveShortcutTarget(string lnk)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object shortcut = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                var typeShortcut = shortcut.GetType();
                var path = typeShortcut.InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
                return path;
            }
            catch { return null; }
        }

        async Task<ImageSource> TryDownloadSteamCoverAsync(string appid)
        {
            try
            {
                if (string.IsNullOrEmpty(appid)) return null;
                var urls = new[] {
                    $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appid}/header.jpg",
                    $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appid}/library_600x900.jpg"
                };
                foreach (var u in urls)
                {
                    using var res = await http.GetAsync(u);
                    if (res.IsSuccessStatusCode)
                    {
                        using var st = await res.Content.ReadAsStreamAsync();
                        return LoadBitmapImageFromStream(st);
                    }
                }
            }
            catch { }
            return null;
        }

        ImageSource MakePlaceholderBitmap(int w, int h)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(22, 28, 38)), null, new Rect(0, 0, w, h));
                var ft = new FormattedText("GAME", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 32, Brushes.White, 1.0);
                dc.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
            }
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv); bmp.Freeze(); return bmp;
        }

        void UpdateCardLayout()
        {
            int margin = 10; int baseW = 150; int baseH = 150; float selectedScale = 1.88f;
            int x = margin;
            for (int i = 0; i < GamesInner.Children.Count; i++)
            {
                var ctrl = GamesInner.Children[i] as Border; if (ctrl == null) continue;
                bool isSel = i == selectedIndex; int newW = isSel ? (int)(baseW * selectedScale) : baseW; int newH = isSel ? (int)(baseH * selectedScale) : baseH;
                ctrl.Width = newW; ctrl.Height = newH; Canvas.SetLeft(ctrl, x); Canvas.SetTop(ctrl, 20);
                var img = ctrl.Child as Image; if (img != null) { img.Width = Math.Max(16, newW - 16); img.Height = Math.Max(16, newH - 16); }
                x += newW + margin;
            }
            int totalWidth = Math.Max((int)this.ActualWidth, x + margin); GamesInner.Width = totalWidth;
            if (x < this.ActualWidth)
            {
                int leftPad = ((int)this.ActualWidth - x) / 2; foreach (UIElement c in GamesInner.Children) { double curLeft = Canvas.GetLeft((UIElement)c); Canvas.SetLeft((UIElement)c, curLeft + leftPad); }
                innerTargetLeft = 0; Canvas.SetLeft(GamesInner, 0);
            }
            else
            {
                if (selectedIndex >= 0 && selectedIndex < GamesInner.Children.Count)
                {
                    var card = GamesInner.Children[selectedIndex] as FrameworkElement;
                    int desiredCenter = (int)(Canvas.GetLeft(card) + card.Width / 2);
                    int maxShift = Math.Max(0, (int)GamesInner.Width - (int)this.ActualWidth);
                    int targetLeft = Math.Clamp(desiredCenter - (int)this.ActualWidth / 2, 0, maxShift);
                    innerTargetLeft = -targetLeft;
                }
                else innerTargetLeft = 0;
            }
            UpdateFloatingTitlePosition();
        }

        void UpdateFloatingTitlePosition()
        {
            if (selectedIndex < 0 || selectedIndex >= games.Count) { floatingAlphaTarget = 0.0; floatingScaleTarget = 0.96; return; }
            var card = GamesInner.Children.Cast<UIElement>().FirstOrDefault(c => (int)((FrameworkElement)c).Tag == selectedIndex) as FrameworkElement;
            if (card == null) { floatingAlphaTarget = 0.0; floatingScaleTarget = 0.96; return; }
            int desiredCenter = (int)(Canvas.GetLeft(card) + card.Width / 2);
            int maxShift = Math.Max(0, (int)GamesInner.Width - (int)this.ActualWidth);
            int targetLeft = Math.Clamp(desiredCenter - (int)this.ActualWidth / 2, 0, maxShift);
            innerTargetLeft = -targetLeft;
            string title = games[selectedIndex].Title ?? "Unknown";
            ResizeFloatingTitleToText(title, Math.Max(220, (int)(card.Width * 1.1)));
            int relX = 292; int posX = (int)(Canvas.GetLeft(card) + innerTargetLeft + relX); int posY = (int)(Canvas.GetTop(card) + card.Height - 120);
            if (posX < 0) posX = 0; floatingTargetLocation = new Point(posX, posY);
            floatingAlphaTarget = 1.0; floatingScaleTarget = 1.0; if (FloatingTitleBorder.Visibility != Visibility.Visible) FloatingTitleBorder.Visibility = Visibility.Visible; Canvas.SetZIndex(FloatingTitleBorder, 1000);
        }

        void ResizeFloatingTitleToText(string text, int maxWidthPx = 420)
        {
            FloatingTitleText.Text = text;
            var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FloatingTitleText.FontFamily, FloatingTitleText.FontStyle, FloatingTitleText.FontWeight, FloatingTitleText.FontStretch), FloatingTitleText.FontSize, Brushes.Black, 1.0);
            int paddingX = 24; int paddingY = 12; int targetW = Math.Min(maxWidthPx, (int)ft.Width + paddingX); int targetH = Math.Max(28, (int)ft.Height + paddingY);
            FloatingTitleBorder.Width = targetW; FloatingTitleBorder.Height = targetH;
        }

        void UpdateScanOverlay()
        {
            if (ScanCanvas.Children.Count > 0) ScanCanvas.Children.Clear();
            double rcW = this.ActualWidth; double rcH = GamesViewport.Height; ScanOverlay.Width = rcW; ScanOverlay.Height = rcH;
            int bars = scanBars; int bw = 18; int spacing = 12; int totalW = bars * bw + (bars - 1) * spacing; int startX = (int)((rcW - totalW) / 2); double baseY = rcH * 0.55;
            for (int i = 0; i < bars; i++)
            {
                double phase = (scanAnimTime * 3.0) + (i * 0.5);
                double h = Math.Abs(Math.Sin(phase)) * (rcH * 0.12) + 8.0;
                var rect = new System.Windows.Shapes.Rectangle { Width = bw, Height = h, RadiusX = 6, RadiusY = 6, Fill = new SolidColorBrush(Color.FromRgb(24, 119, 242)) };
                Canvas.SetLeft(rect, startX + i * (bw + spacing)); Canvas.SetTop(rect, baseY - h / 2); ScanCanvas.Children.Add(rect);
            }
            var txt = new TextBlock { Text = "Procurando na Steam / Atalhos…", Foreground = Brushes.LightGray };
            Canvas.SetLeft(txt, (rcW - 240) / 2); Canvas.SetTop(txt, baseY + rcH * 0.12); ScanCanvas.Children.Add(txt);
        }

        ImageSource LoadBitmapImageFromFile(string path)
        {
            var bmp = new BitmapImage(); using (var fs = File.OpenRead(path)) { bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = new MemoryStream(); fs.CopyTo(bmp.StreamSource); bmp.StreamSource.Position = 0; bmp.EndInit(); bmp.Freeze(); }
            return bmp;
        }

        ImageSource LoadBitmapImageFromStream(Stream st)
        {
            var bmp = new BitmapImage(); var ms = new MemoryStream(); st.CopyTo(ms); ms.Position = 0; bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = ms; bmp.EndInit(); bmp.Freeze(); return bmp;
        }

        void SaveBitmapImageToFile(ImageSource src, string path)
        {
            if (src is BitmapSource bsrc)
            {
                var encoder = new JpegBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bsrc)); using var fs = File.Open(path, FileMode.Create); encoder.Save(fs);
            }
        }

        void Debug_AddDummyTiles()
        {
            var list = new List<GameEntry>(); for (int i = 1; i <= 8; i++) list.Add(new GameEntry { Title = "Dummy Game " + i, Source = "Debug", Cover = MakePlaceholderBitmap(800, 450) }); games = list; PopulateGamesPanel(); SelectIndex(0);
        }

        ImageSource TryExtractIconBitmapImage_NoThrow(string exe) { try { return TryExtractIconBitmapImage(exe); } catch { return null; } }

        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);

        // Helpers for imaging icons
        ImageSource TryExtractIconBitmapImageSafe(string path)
        {
            try
            {
                var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (ico == null) return null;
                var b = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(64, 64)); b.Freeze(); return b;
            }
            catch { return null; }
        }

        // XInput native
        static class XInputNative
        {
            [StructLayout(LayoutKind.Sequential)] public struct XINPUT_STATE { public uint dwPacketNumber; public XINPUT_GAMEPAD Gamepad; }
            [StructLayout(LayoutKind.Sequential)] public struct XINPUT_GAMEPAD { public ushort wButtons; public byte bLeftTrigger; public byte bRightTrigger; public short sThumbLX; public short sThumbLY; public short sThumbRX; public short sThumbRY; }
            [Flags] public enum GamepadButtons : ushort { DPadUp = 0x0001, DPadDown = 0x0002, DPadLeft = 0x0004, DPadRight = 0x0008, Start = 0x0010, Back = 0x0020, LeftThumb = 0x0040, RightThumb = 0x0080, LeftShoulder = 0x0100, RightShoulder = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000 }
            [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern uint XInputGetState14(uint i, out XINPUT_STATE s);
            [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")] static extern uint XInputGetState13(uint i, out XINPUT_STATE s);
            public static bool GetState(int userIndex, out XINPUT_STATE state) { try { return XInputGetState14((uint)userIndex, out state) == 0; } catch { try { return XInputGetState13((uint)userIndex, out state) == 0; } catch { state = default; return false; } } }
            public static GamepadButtons ButtonsFromState(XINPUT_STATE s) => (GamepadButtons)s.Gamepad.wButtons;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
        static extern uint XInputSetState(uint dwUserIndex, ref XINPUT_VIBRATION pVibration);

        static bool VibrateFor(uint index, ushort leftMotor, ushort rightMotor, int durationMs)
        {
            // Configura vibração
            XINPUT_VIBRATION vibration = new XINPUT_VIBRATION
            {
                wLeftMotorSpeed = leftMotor,
                wRightMotorSpeed = rightMotor
            };

            uint result = XInputSetState(index, ref vibration);
            const uint ERROR_SUCCESS = 0;

            if (result != ERROR_SUCCESS)
            {
                return false; // controller não conectado ou erro
            }

            // Cria thread que para a vibração após durationMs (não bloqueia o Main)
            new Thread(() =>
            {
                try
                {
                    Thread.Sleep(durationMs);

                    // Zera os motores para parar
                    XINPUT_VIBRATION stop = new XINPUT_VIBRATION { wLeftMotorSpeed = 0, wRightMotorSpeed = 0 };
                    XInputSetState(index, ref stop);
                }
                catch
                {
                    // ignorar erros silenciosamente aqui
                }
            })
            { IsBackground = true }.Start();

            return true;
        }
        string PromptForText(string title, string prompt)
        {
            var w = new Window
            {
                Title = title,
                Width = 480,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };
            var tb = new TextBox { Margin = new Thickness(10), VerticalAlignment = VerticalAlignment.Center };
            var ok = new Button { Content = "OK", Width = 90, IsDefault = true, Margin = new Thickness(6) };
            var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true, Margin = new Thickness(6) };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(10, 6, 10, 0) });
            panel.Children.Add(tb);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            w.Content = panel;
            string result = null;
            ok.Click += (s, e) => { result = tb.Text; w.DialogResult = true; w.Close(); };
            cancel.Click += (s, e) => { w.DialogResult = false; w.Close(); };
            w.ShowDialog();
            return result;
        }

        async Task<(string claimedId, bool success)> ShowSteamOpenIdWindowAsync()
        {
            var tcs = new TaskCompletionSource<(string, bool)>();
            await Dispatcher.InvokeAsync(() =>
            {
                var win = new Window { Title = "Steam Login", Width = 980, Height = 720, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this };
                var web = new System.Windows.Controls.WebBrowser();
                win.Content = web;

                // return_to must be same as we will detect (can be any URL)
                string returnTo = "https://steamcommunity.com/login/home/"; // Steam will redirect here with params
                string openidUrl = "https://steamcommunity.com/openid/login" +
                    "?openid.ns=http://specs.openid.net/auth/2.0" +
                    "&openid.mode=checkid_setup" +
                    "&openid.return_to=" + Uri.EscapeDataString(returnTo) +
                    "&openid.realm=" + Uri.EscapeDataString(returnTo) +
                    "&openid.identity=http://specs.openid.net/auth/2.0/identifier_select" +
                    "&openid.claimed_id=http://specs.openid.net/auth/2.0/identifier_select";

                web.Navigating += (s, e) =>
                {
                    // detect redirect to returnTo
                };

                web.Navigated += (s, e) =>
                {
                    try
                    {
                        var u = e.Uri;
                        if (u != null && u.AbsoluteUri.StartsWith(returnTo, StringComparison.OrdinalIgnoreCase))
                        {
                            // parse query or fragment for openid.claimed_id
                            var qs = ParseQueryString(u.Query);
                            if (qs.TryGetValue("openid.claimed_id", out var claimed))
                            {
                                tcs.TrySetResult((claimed, true));
                                win.Close();
                            }
                            else
                            {
                                // sometimes parameters may be in fragment
                                var frag = u.Fragment;
                                if (!string.IsNullOrEmpty(frag))
                                {
                                    var q2 = ParseQueryString(frag.TrimStart('#'));
                                    if (q2.TryGetValue("openid.claimed_id", out var claimed2))
                                    {
                                        tcs.TrySetResult((claimed2, true));
                                        win.Close();
                                    }
                                }
                            }
                        }
                    }
                    catch { /* ignore parsing errors */ }
                };

                win.Closed += (s, e) =>
                {
                    if (!tcs.Task.IsCompleted) tcs.TrySetResult((null, false));
                };

                win.Show();
                web.Navigate(openidUrl);
            });

            return await tcs.Task;
        }

        async Task<JsonElement?> SteamApiGetJson(string url)
        {
            try
            {
                using var res = await http.GetAsync(url);
                if (!res.IsSuccessStatusCode) return null;
                using var st = await res.Content.ReadAsStreamAsync();
                var doc = await JsonDocument.ParseAsync(st);
                return doc.RootElement.Clone();
            }
            catch { return null; }
        }

        async Task<double?> GetPlaytimeHoursForAppAsync(string steamId64, int appid)
        {
            if (string.IsNullOrEmpty(steamApiKey)) return null;
            var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={steamApiKey}&steamid={steamId64}&include_appinfo=1&include_played_free_games=1";
            var root = await SteamApiGetJson(url);
            if (root == null) return null;
            if (root.Value.TryGetProperty("response", out var resp) && resp.TryGetProperty("games", out var games))
            {
                foreach (var g in games.EnumerateArray())
                {
                    if (g.TryGetProperty("appid", out var aid) && aid.GetInt32() == appid)
                    {
                        var minutes = g.TryGetProperty("playtime_forever", out var pm) ? pm.GetInt32() : 0;
                        return minutes / 60.0;
                    }
                }
            }
            return null;
        }

        async Task<Dictionary<string, string>> GetPlayerSummariesAsync(string steamId64)
        {
            var dict = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(steamApiKey)) return dict;
            var url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={steamApiKey}&steamids={steamId64}";
            var root = await SteamApiGetJson(url);
            if (root == null) return dict;
            if (root.Value.TryGetProperty("response", out var resp) && resp.TryGetProperty("players", out var players))
            {
                var p = players.EnumerateArray().FirstOrDefault();
                if (p.ValueKind != JsonValueKind.Undefined)
                {
                    if (p.TryGetProperty("personaname", out var pn)) dict["personaname"] = pn.GetString();
                    if (p.TryGetProperty("avatarfull", out var av)) dict["avatarfull"] = av.GetString();
                }
            }
            return dict;
        }

        public void ShowAchievementDetails(object achievement)
        {
            try
            {
                string text = achievement?.ToString() ?? "No details available";
                MessageBox.Show(text, "Achievement details", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { /* swallow for now */ }
        }

        async Task<List<string>> GetPlayerAchievementsAsync(string steamId64, int appid)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(steamApiKey)) return list;
            var url = $"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/?appid={appid}&key={steamApiKey}&steamid={steamId64}";
            var root = await SteamApiGetJson(url);
            if (root == null) return list;
            if (root.Value.TryGetProperty("playerstats", out var ps) && ps.TryGetProperty("achievements", out var ach))
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

        // 1) devolve steamids dos amigos (GetFriendList)
        // Atenção: GetFriendList só funciona se o perfil/friends estiver visível para a conta que está a pedir.
        async Task<List<string>> GetPlayerFriendsSteamIdList(string steamId64)
        {
            var list = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(steamApiKey) || string.IsNullOrEmpty(steamId64)) return list;
                var url = $"https://api.steampowered.com/ISteamUser/GetFriendList/v1/?key={Uri.EscapeDataString(steamApiKey)}&steamid={Uri.EscapeDataString(steamId64)}&relationship=friend";
                var root = await SteamApiGetJson(url);
                if (root == null) return list;

                if (root.Value.TryGetProperty("friendslist", out var friendslist) && friendslist.TryGetProperty("friends", out var friends))
                {
                    foreach (var f in friends.EnumerateArray())
                    {
                        if (f.TryGetProperty("steamid", out var sid))
                        {
                            var s = sid.GetString();
                            if (!string.IsNullOrEmpty(s)) list.Add(s);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogException(ex, "GetPlayerFriendsSteamIdList");
            }

            return list;
        }

        // Popula Info_FriendsList com os nomes (e permite clicar para abrir o perfil)
        // 1) obter steamids dos friends
        // 1) obter steamids dos friends
        async Task FetchAndShowFriendsAsync()
        {
            try
            {
                // 1) obter steamids dos friends
                var friendIds = await GetPlayerFriendsSteamIdList(connectedSteamId);
                if (friendIds == null || friendIds.Count == 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        FriendListBox.ItemsSource = null;
                    });
                    return;
                }

                // 2) obter summaries (faz batching internamente)
                var summaries = await GetPlayerSummariesForSteamIds(friendIds);


                // 3) carrega imagens (opcional) e define ItemsSource no UI thread
                await Dispatcher.InvokeAsync(() =>
                {
                    FriendListBox.ItemsSource = summaries;
                });
            }
            catch (Exception ex)
            {
                LogException(ex, "FetchAndShowFriendsAsync");
            }
        }

        private void FriendsMenu_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FriendListBox.SelectedItem is PlayerSummary ps && !string.IsNullOrEmpty(ps.ProfileUrl))
            {
                // usa o teu comando existente
                OpenFriendProfileCommand.Execute(ps.ProfileUrl);
            }
        }


        // 2) Get player summaries (personaname) para um conjunto de steamids (faz batching)
        // devolve lista de personanames (na mesma ordem aproximada - não garantida)
        async Task<List<PlayerSummary>> GetPlayerSummariesForSteamIds(List<string> steamIds)
        {
            var result = new List<PlayerSummary>();
            try
            {
                if (string.IsNullOrEmpty(steamApiKey) || steamIds == null || steamIds.Count == 0) return result;

                const int batchSize = 100; // Steam API aceita até 100 steamids por pedido
                for (int i = 0; i < steamIds.Count; i += batchSize)
                {
                    var batch = steamIds.Skip(i).Take(batchSize);
                    var idsParam = string.Join(",", batch);
                    var url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={Uri.EscapeDataString(steamApiKey)}&steamids={Uri.EscapeDataString(idsParam)}";
                    var root = await SteamApiGetJson(url);
                    if (root == null) continue;

                    if (root.Value.TryGetProperty("response", out var response) && response.TryGetProperty("players", out var playersElem))
                    {
                        foreach (var p in playersElem.EnumerateArray())
                        {
                            var ps = new PlayerSummary
                            {
                                SteamId = p.TryGetProperty("steamid", out var sid) ? sid.GetString() : null,
                                PersonaName = p.TryGetProperty("personaname", out var pn) ? pn.GetString() : null,
                                AvatarFull = p.TryGetProperty("avatarfull", out var af) ? af.GetString() : null,
                                ProfileUrl = p.TryGetProperty("profileurl", out var pu) ? pu.GetString() : $"https://steamcommunity.com/profiles/{(p.TryGetProperty("steamid", out var s2) ? s2.GetString() : "")}"
                            };
                            result.Add(ps);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogException(ex, "GetPlayerSummariesForSteamIds");
            }

            return result;
        }

        async Task<PlayerSummary> GetProfileSummary()
        {
            var result = new PlayerSummary();
            try
            {
                if (string.IsNullOrEmpty(steamApiKey)) return result;
                var url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={steamApiKey}&steamids={connectedSteamId}";
                var root = await SteamApiGetJson(url);

                if (root.Value.TryGetProperty("response", out var response) && response.TryGetProperty("players", out var playersElem))
                {
                    foreach (var p in playersElem.EnumerateArray())
                    {
                        var ps = new PlayerSummary
                        {
                            SteamId = p.TryGetProperty("steamid", out var sid) ? sid.GetString() : null,
                            PersonaName = p.TryGetProperty("personaname", out var pn) ? pn.GetString() : null,
                            AvatarFull = p.TryGetProperty("avatarfull", out var af) ? af.GetString() : null,
                            ProfileUrl = p.TryGetProperty("profileurl", out var pu) ? pu.GetString() : $"https://steamcommunity.com/profiles/{(p.TryGetProperty("steamid", out var s2) ? s2.GetString() : "")}"
                        };
                        result = ps;
                    }
                }

            }
            catch (Exception ex)
            {
                LogException(ex, "GetProfileSummary");
            }
            return result;
        }

        List<ImageSource> TryGetLocalSteamScreenshots(string steamId64, int appid, int max = 6)
        {
            var res = new List<ImageSource>();
            try
            {
                var steamPath = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath)) return res;

                var userdata = Path.Combine(steamPath, "userdata");
                if (!Directory.Exists(userdata)) return res;

                // try the folder for the specific steamId
                var userFolder = Path.Combine(userdata, steamId64);
                var candidates = new List<string>();
                if (Directory.Exists(userFolder))
                {
                    candidates.Add(Path.Combine(userFolder, "760", "remote", appid.ToString(), "screenshots"));
                }

                // also probe all user subfolders for remote/<appid>/screenshots (in case steamId isn't folder name)
                foreach (var d in Directory.GetDirectories(userdata))
                {
                    var sc = Path.Combine(d, "760", "remote", appid.ToString(), "screenshots");
                    if (Directory.Exists(sc)) candidates.Add(sc);
                }

                foreach (var c in candidates.Distinct())
                {
                    foreach (var f in Directory.GetFiles(c, "*.jpg").OrderByDescending(f => File.GetLastWriteTime(f)).Take(max))
                    {
                        try { res.Add(LoadBitmapImageFromFile(f)); }
                        catch { }
                    }
                    if (res.Count > 0) break;
                }
            }
            catch { }
            return res;
        }

        // retorna o número total de achievements que um app declara (schema), ou null se não disponível
        async Task<int?> GetTotalAchievementsForAppAsync(int appid)
        {
            try
            {
                if (string.IsNullOrEmpty(steamApiKey)) return null; // schema endpoint requires API key
                var url = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={steamApiKey}&appid={appid}";
                using var res = await http.GetAsync(url);
                if (!res.IsSuccessStatusCode) return null;
                using var st = await res.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(st);
                if (doc.RootElement.TryGetProperty("game", out var game) && game.TryGetProperty("availableGameStats", out var ags)
                    && ags.TryGetProperty("achievements", out var ach))
                {
                    int count = 0;
                    foreach (var a in ach.EnumerateArray()) count++;
                    return count;
                }
            }
            catch { }
            return null;
        }

        // retorna lista de achievements desbloqueadas (usarás GetPlayerAchievementsAsync já existente)
        // (re-uses GetPlayerAchievementsAsync)

        async Task<int?> GetGameCompletionPercentAsync(string steamId64, int appid)
        {
            try
            {
                // se não houver API key, tentamos apenas obter player achievements (alguns endpoints may still require key)
                var playerAch = await GetPlayerAchievementsAsync(steamId64, appid); // já fornecido anteriormente
                int unlocked = playerAch?.Count ?? 0;

                // tenta obter total achievements via schema (precisa de api key)
                int? total = await GetTotalAchievementsForAppAsync(appid);
                if (total.HasValue && total.Value > 0)
                {
                    double pct = (double)unlocked / total.Value * 100.0;
                    return (int)Math.Round(pct);
                }
                else
                {
                    return 0;
                }

                // fallback: se não houver schema, mas API devolveu achievements list com "achieved" flag, 
                //     podes usar playerAch + try obter global achievement list? fallback não exacto.
                // Se não for possível determinar total, devolve null.
                return null;
            }
            catch { return null; }
        }

        async Task<List<(string title, string contents, string url, DateTime date)>> GetNewsForAppAsync(int appid, int count = 5, int maxlength = 1000)
        {
            var list = new List<(string, string, string, DateTime)>();
            try
            {
                // este endpoint normalmente funciona sem key; optional param key can be appended if you have one
                var url = $"https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid={appid}&count={count}&maxlength={maxlength}";
                if (!string.IsNullOrEmpty(steamApiKey)) url += $"&key={steamApiKey}";
                using var res = await http.GetAsync(url);
                if (!res.IsSuccessStatusCode) return list;
                using var st = await res.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(st);
                if (doc.RootElement.TryGetProperty("appnews", out var appnews) && appnews.TryGetProperty("newsitems", out var items))
                {
                    foreach (var it in items.EnumerateArray())
                    {
                        var title = it.TryGetProperty("title", out var t) ? t.GetString() : "";
                        var contents = it.TryGetProperty("contents", out var c) ? c.GetString() : "";
                        var urlp = it.TryGetProperty("url", out var u) ? u.GetString() : "";
                        DateTime date = DateTime.MinValue;
                        if (it.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.Number)
                        {
                            var unix = d.GetInt64();
                            date = DateTimeOffset.FromUnixTimeSeconds(unix).DateTime;
                        }
                        list.Add((title, contents, urlp, date));
                    }
                }
            }
            catch { }
            return list;
        }

        void EnsureConfigDir()
        {
            try { Directory.CreateDirectory(configDir); } catch { }
        }

        void SaveSteamApiKey(string apiKey)
        {
            try
            {
                EnsureConfigDir();
                if (string.IsNullOrEmpty(apiKey))
                {
                    if (File.Exists(apiKeyFilePath)) File.Delete(apiKeyFilePath);
                    steamApiKey = null;
                    return;
                }

                var plain = Encoding.UTF8.GetBytes(apiKey);
                var protectedData = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(apiKeyFilePath, protectedData);
                steamApiKey = apiKey;
                Dispatcher.Invoke(() => StatusLabel.Text = "Steam API Key guardada.");
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => MessageBox.Show(this, "Erro a guardar API key: " + ex.Message));
            }
        }

        string LoadSteamApiKey()
        {
            try
            {
                if (!File.Exists(apiKeyFilePath)) return null;
                var protectedData = File.ReadAllBytes(apiKeyFilePath);
                var plain = ProtectedData.Unprotect(protectedData, null, DataProtectionScope.CurrentUser);
                var key = Encoding.UTF8.GetString(plain);
                steamApiKey = key;
                return key;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Retorna true se o appid tem um appmanifest_*.acf num das steamapps folders.
        /// </summary>
        bool IsSteamGameInstalled(int appid)
        {
            return GetSteamInstallDir(appid) != null;
        }

        /// <summary>
        /// Tenta descobrir a pasta de instalação do appid (p.ex. C:\Program Files (x86)\Steam\steamapps\common\<installdir>).
        /// Retorna null se não estiver instalado (ou não for possível detectar).
        /// </summary>
        string GetSteamInstallDir(int appid)
        {
            try
            {
                var libs = GetSteamLibraryFolders();
                foreach (var libSteamApps in libs)
                {
                    // procura manifest diretamente
                    var manifestPath = Path.Combine(libSteamApps, $"appmanifest_{appid}.acf");
                    if (!File.Exists(manifestPath)) continue;

                    try
                    {
                        var txt = File.ReadAllText(manifestPath);
                        // tenta extrair installdir
                        var m = Regex.Match(txt, "\"installdir\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            var installdir = m.Groups[1].Value;
                            var commonPath = Path.Combine(Path.GetDirectoryName(libSteamApps) ?? libSteamApps, "common", installdir);
                            if (Directory.Exists(commonPath)) return commonPath;
                            // fallback: às vezes o jogo pode ter outra estrutura; ainda assim retornamos commonPath if manifest exists
                            return commonPath;
                        }
                        else
                        {
                            // manifest existe mas não tem installdir (invulgar) -> devolve steamapps/common/<appid> como tentativa
                            var guess = Path.Combine(Path.GetDirectoryName(libSteamApps) ?? libSteamApps, "common", $"App_{appid}");
                            if (Directory.Exists(guess)) return guess;
                            // se manifest existe, considera instalado (mesmo sem pasta encontrada) — devolve o diretório do steamapps como sinal
                            return libSteamApps;
                        }
                    }
                    catch
                    {
                        // se leitura falhar, continua para outras libs
                        continue;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Retorna lista de paths até as pastas steamapps em todas as libraries descobertas.
        /// Exemplos: C:\Program Files (x86)\Steam\steamapps, D:\SteamLibrary\steamapps, ...
        /// </summary>
        List<string> GetSteamLibraryFolders()
        {
            var result = new List<string>();
            try
            {
                // tenta obter SteamPath da registry (HKCU)
                var steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (!string.IsNullOrEmpty(steamPath) && Directory.Exists(steamPath))
                {
                    var defaultSteamApps = Path.Combine(steamPath, "steamapps");
                    if (Directory.Exists(defaultSteamApps)) result.Add(defaultSteamApps);
                }

                // tenta ler libraryfolders.vdf (tem de estar no steamPath\steamapps\libraryfolders.vdf)
                if (!string.IsNullOrEmpty(steamPath))
                {
                    var libfile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(libfile))
                    {
                        var content = File.ReadAllText(libfile);
                        // regex para capturar "path" : "C:\\Some\\Path" OR numeric sections with "path"
                        // suporta formats antigos e alguns novos
                        var matches = Regex.Matches(content, "\"\\d+\"\\s*\\{[^}]*\"path\"\\s*\"([^\"]+)\"|\"path\"\\s*\"([^\"]+)\"", RegexOptions.Singleline);
                        foreach (Match m in matches)
                        {
                            var p = m.Groups[1].Success ? m.Groups[1].Value : (m.Groups[2].Success ? m.Groups[2].Value : null);
                            if (!string.IsNullOrEmpty(p))
                            {
                                // normaliza e concatena steamapps
                                var sa = Path.Combine(p, "steamapps");
                                if (Directory.Exists(sa) && !result.Contains(sa, StringComparer.OrdinalIgnoreCase))
                                    result.Add(sa);
                            }
                        }
                    }
                }

                // fallback: se não encontrou nada, tenta procurar numa localização padrão comum
                if (result.Count == 0)
                {
                    var prog = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    if (!string.IsNullOrEmpty(prog))
                    {
                        var candidate = Path.Combine(prog, "Steam", "steamapps");
                        if (Directory.Exists(candidate)) result.Add(candidate);
                    }
                }
            }
            catch { /* swallow */ }

            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public void ToggleStoreView(bool show)
        {
            isShowingStoreView = show;

            if (show)
            {
                // prepara e mostra a store na mesma área do carousel
                PopulateStoreView();
                // ajustar foco para permitir navegação por teclado
                Keyboard.Focus(GamesInner);
                StatusLabel.Text = "Loja: jogos da conta não instalados (use Enter para instalar, Esc para voltar).";
                InfoArea.Visibility = Visibility.Collapsed;
            }
            else
            {
                // volta ao carousel original
                PopulateGamesPanel();
                StatusLabel.Text = "Carousel";
                if (games.Count > 0) SelectIndex(Math.Min(selectedIndex < 0 ? 0 : selectedIndex, games.Count - 1));
                InfoArea.Visibility = Visibility.Visible;
            }
        }

        void PopulateStoreView()
        {
            int margin = 10; int baseW = 150; int x = 0;
            // marca que estamos no modo store
            isShowingStoreView = true;
            storeItemControls.Clear();
            storeSelectedIndex = -1;

            // limpa o inner canvas e cria um ScrollViewer com WrapPanel (mais prático que posicionar manualmente)
            GamesInner.Children.Clear();

            // cria ScrollViewer + WrapPanel
            storeWrapPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemHeight = StoreCardH,
                ItemWidth = StoreCardW,
                Margin = new Thickness(StoreCardMargin),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            storeScrollViewer = new ScrollViewer
            {
                Content = storeWrapPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Width = this.ActualWidth - 40, // keep some padding
                Height = GamesViewport.Height,
                Focusable = true
            };


            // colocar o ScrollViewer dentro do GamesInner (canvas)
            Canvas.SetLeft(storeScrollViewer, x);
            Canvas.SetTop(storeScrollViewer, 20);
            GamesInner.Children.Add(storeScrollViewer);

            // agora popula os cards
            foreach (var item in storeNotInstalled)
            {
                var border = new Border
                {
                    Width = StoreCardW,
                    Height = StoreCardH,
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromArgb(28, 10, 10, 10)),
                    Tag = item.appid, // guarda appid
                    Cursor = Cursors.Hand,
                    Focusable = true // permite receber foco
                };

                // visual interno
                var sp = new StackPanel { Margin = new Thickness(0) };

                var img = new Image
                {
                    Source = item.cover ?? MakePlaceholderBitmap(600, 400),
                    Height = 140,
                    Stretch = Stretch.UniformToFill
                };
                var tbName = new TextBlock
                {
                    Text = item.name,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(8, 8, 8, 0),
                    FontWeight = FontWeights.SemiBold,
                    MaxHeight = 56
                };

                var btnPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                var install = new Button { Content = "Install", Width = 90, Margin = new Thickness(6) };
                var store = new Button { Content = "Store", Width = 90, Margin = new Thickness(6) };

                // eventos – mouse / click / focus
                /*border.MouseEnter += (s, e) =>
                {
                    // ao passar com mouse, selecciona
                    var idx = storeItemControls.IndexOf(border);
                    if (idx >= 0) StoreSelectIndex(idx);
                };
                border.MouseLeftButtonUp += (s, e) =>
                {
                    var idx = storeItemControls.IndexOf(border);
                    if (idx >= 0) StoreSelectIndex(idx);
                };*/
                border.GotFocus += (s, e) =>
                {
                    var idx = storeItemControls.IndexOf(border);
                    if (idx >= 0) StoreSelectIndex(idx);
                };

                install.Click += (s, e) =>
                {
                    if (border.Tag is int a) StartSteamInstall(a);
                };
                store.Click += (s, e) =>
                {
                    if (border.Tag is int a)
                    {
                        var uri = $"https://store.steampowered.com/app/{a}/";
                        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                    }
                };

                btnPanel.Children.Add(install);
                btnPanel.Children.Add(store);
                sp.Children.Add(img);
                sp.Children.Add(tbName);
                sp.Children.Add(btnPanel);
                border.Child = sp;

                // adiciona ao painel (WrapPanel) e guarda referência
                storeWrapPanel.Children.Add(border);
                storeItemControls.Add(border);
                x += baseW + margin;
            }

            // marca dimensões do GamesInner (o scrollviewer controla o necessário)
            GamesInner.Width = Math.Max((int)this.ActualWidth, (storeWrapPanel.Children.Count * (StoreCardW + StoreCardMargin)));
            GamesInner.Height = (int)GamesViewport.Height;
            Canvas.SetLeft(GamesInner, x);
            innerTargetLeft = 0;
            x += baseW + margin;

            // se existirem items, seleciona o primeiro
            if (storeItemControls.Count > 0)
                StoreSelectIndex(0);

            // foca o scrollviewer para capturar teclas
            Dispatcher.BeginInvoke(new Action(() =>
            {
                storeScrollViewer?.Focus();
                Keyboard.Focus(storeScrollViewer);
            }), DispatcherPriority.ApplicationIdle);
        }

        public void StoreSelectIndex(int idx)
        {
            try
            {
                if (storeItemControls == null || storeItemControls.Count == 0)
                {
                    storeSelectedIndex = -1;
                    return;
                }

                // clamp seguro
                idx = Math.Max(0, Math.Min(idx, storeItemControls.Count - 1));
                storeSelectedIndex = idx;

                // UI work must run on UI thread
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        UpdateStoreSelectionVisual();

                        var el = storeItemControls[storeSelectedIndex];
                        // garante que está visível no ScrollViewer
                        el.BringIntoView();

                        // dá foco ao cartão
                        el.Focus();
                    }
                    catch (Exception inner)
                    {
                        LogException(inner, "StoreSelectIndex.Dispatcher");
                    }
                });
            }
            catch (Exception ex)
            {
                LogException(ex, "StoreSelectIndex");
            }
        }

        public void StoreSelectNext()
        {
            try
            {
                if (storeItemControls == null || storeItemControls.Count == 0) return;
                int next = (storeSelectedIndex < 0) ? 0 : (storeSelectedIndex + 1) % storeItemControls.Count;
                StoreSelectIndex(next);
            }
            catch (Exception ex) { LogException(ex, "StoreSelectNext"); }
            Console.WriteLine("AAAAAAAAAAAAAAAA");
        }

        public void StoreSelectPrevious()
        {
            try
            {
                if (storeItemControls == null || storeItemControls.Count == 0)
                {
                    Console.WriteLine("Erro 'if'");
                    return;
                }
                int prev = (storeSelectedIndex - 1 + storeItemControls.Count) % storeItemControls.Count;
                StoreSelectIndex(prev);
            }
            catch (Exception ex) { LogException(ex, "StoreSelectPrevious"); }
            Console.WriteLine("Erro Prev");
        }

        public void StoreActivateSelected()
        {
            try
            {
                if (storeSelectedIndex < 0 || storeItemControls == null || storeSelectedIndex >= storeItemControls.Count) return;
                var el = storeItemControls[storeSelectedIndex];
                if (el?.Tag is int aid)
                {
                    StartSteamInstall(aid);
                }
            }
            catch (Exception ex) { LogException(ex, "StoreActivateSelected"); }

        }

        public void UpdateStoreSelectionVisual()
        {
            // aplica borda + sombra ao seleccionado
            for (int i = 0; i < storeItemControls.Count; i++)
            {
                if (storeItemControls[i] is Border b)
                {
                    if (i == storeSelectedIndex)
                    {
                        b.BorderThickness = new Thickness(3);
                        b.BorderBrush = new SolidColorBrush(Color.FromRgb(24, 119, 242));
                        b.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 12, Direction = 270, ShadowDepth = 6, Opacity = 0.6 };
                    }
                    else
                    {
                        b.BorderThickness = new Thickness(0);
                        b.BorderBrush = null;
                        b.Effect = null;
                    }
                }
            }
        }

        async Task LoadStoreNotInstalledAsync()
        {
            if (string.IsNullOrEmpty(steamApiKey))
            {
                // pede key se não disponível
                string input = null;
                await Dispatcher.InvokeAsync(() => input = PromptForText("API key necessária", "Coloca a Steam Web API key:", ""));
                if (input == null) return;
                SaveSteamApiKey(string.IsNullOrWhiteSpace(input) ? null : input.Trim());
            }

            // pede owned games
            var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={steamApiKey}&steamid={connectedSteamId}&include_appinfo=1&include_played_free_games=1";
            using var res = await http.GetAsync(url);
            if (!res.IsSuccessStatusCode) { await Dispatcher.InvokeAsync(() => StatusLabel.Text = "GetOwnedGames failed"); return; }
            using var st = await res.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(st);

            var list = new List<(int appid, string name)>();
            if (doc.RootElement.TryGetProperty("response", out var resp) && resp.TryGetProperty("games", out var gamesArr))
            {
                foreach (var g in gamesArr.EnumerateArray())
                {
                    int appid = g.GetProperty("appid").GetInt32();
                    string name = g.TryGetProperty("name", out var nm) ? nm.GetString() : $"App {appid}";
                    list.Add((appid, name));
                }
            }

            // filtra os que não estão instalados
            var notInstalled = new List<(int, string, ImageSource)>();
            foreach (var t in list)
            {
                if (!IsSteamGameInstalled(t.appid))
                {
                    ImageSource cover = null;
                    try
                    {
                        var uri = new Uri($"https://cdn.cloudflare.steamstatic.com/steam/apps/{t.appid}/header.jpg");
                        var bi = new BitmapImage(uri);
                        bi.Freeze();
                        cover = bi;
                    }
                    catch { cover = MakePlaceholderBitmap(600, 400); }
                    notInstalled.Add((t.appid, t.name, cover));
                }
            }

            storeNotInstalled = notInstalled;
            await Dispatcher.InvokeAsync(() => ToggleStoreView(true));
        }

        void LogException(Exception ex, string context = null)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PichalnovenseUI");
                Directory.CreateDirectory(dir);
                var p = Path.Combine(dir, "crash.log");
                var txt = DateTime.Now.ToString("s") + " [" + (context ?? "unknown") + "] " + ex.ToString() + Environment.NewLine;
                File.AppendAllText(p, txt);
            }
            catch { /* não faças nada se logging falhar */ }
        }

        async void TestPlaystationController()
        {

        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _controller = new PlayStationController();
            Console.WriteLine("Olá");

            _controller.StateChanged += Controller_StateChanged;

            if (!_controller.Start())
            {
                MessageBox.Show("Não foi possível ligar ao DualSense.");
            }
        }

        private void Controller_StateChanged(DualSenseInputState state)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastNav).TotalMilliseconds < 80) return; // debounce 160ms
            _lastNav = now;

            if (_prevDsState != null)
            {
                // edge detection: apenas dispara se o botão mudou de false -> true
                FireEdge(state.CrossButton, _prevDsState.CrossButton, () => currentInputHandler?.OnAccept());
                FireEdge(state.CircleButton, _prevDsState.CircleButton, () => currentInputHandler?.OnCancel());
                FireEdge(state.MenuButton, _prevDsState.MenuButton, async () => await RescanGamesAsync());
                FireEdge(state.L1Button, _prevDsState.L1Button, () => SwapViewLeft());
                FireEdge(state.R1Button, _prevDsState.R1Button, () => SwapViewRight());

                // DPad + analog
                FireEdge(state.LeftAnalogStick.X < -0.6f, _prevDsState.LeftAnalogStick.X < -0.6f, () => currentInputHandler?.OnLeft());
                FireEdge(state.LeftAnalogStick.X > 0.6f, _prevDsState.LeftAnalogStick.X > 0.6f, () => currentInputHandler?.OnRight());
                FireEdge(state.LeftAnalogStick.Y > 0.6f, _prevDsState.LeftAnalogStick.Y > 0.6f, () => currentInputHandler?.OnUp());
                FireEdge(state.LeftAnalogStick.Y < -0.6f, _prevDsState.LeftAnalogStick.Y < -0.6f, () => currentInputHandler?.OnDown());

                FireEdge(state.DPadLeftButton, _prevDsState.DPadLeftButton, () => currentInputHandler?.OnLeft());
                FireEdge(state.DPadRightButton, _prevDsState.DPadRightButton, () => currentInputHandler?.OnRight());
                FireEdge(state.DPadUpButton, _prevDsState.DPadUpButton, () => currentInputHandler?.OnUp());
                FireEdge(state.DPadDownButton, _prevDsState.DPadDownButton, () => currentInputHandler?.OnDown());
            }
            _prevDsState = state;
        }

        private void FireEdge(bool nowPressed, bool prevPressed, Action action)
        {
            if (nowPressed && !prevPressed)
            {
                Dispatcher.Invoke(action); // garante thread UI
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _controller?.Stop();
        }
    }
}