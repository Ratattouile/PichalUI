using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace PichalUI
{
    public partial class OwnedGamesWindow : Window
    {
        // Modelo interno corrigido
        public class OwnedGameModel
        {
            public int AppId { get; set; }
            public string Name { get; set; } = ""; // Inicializado
            public BitmapImage? Cover { get; set; } // Nullable
        }

        readonly List<(int appid, string name)> games;
        readonly Action<int> installCallback;

        public OwnedGamesWindow(List<(int appid, string name)> gamesList, Action<int> installCallback)
        {
            InitializeComponent(); // Adicionado se faltar
            this.games = gamesList;
            this.installCallback = installCallback;
            LoadItems();
        }

        // Removido 'async' pois não tem await (CS1998)
        void LoadItems()
        {
            var models = new List<OwnedGameModel>();
            foreach (var g in games)
            {
                var m = new OwnedGameModel { AppId = g.appid, Name = g.name };
                try
                {
                    var url = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{g.appid}/header.jpg";
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(url);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    m.Cover = bi;
                }
                catch { m.Cover = null; }
                models.Add(m);
            }
            // GamesItemsControl.ItemsSource = models; // Descomentar se o XAML tiver este controlo
        }

        private void Install_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is int aid) installCallback?.Invoke(aid);
        }

        private void Store_Click(object sender, RoutedEventArgs e)
        {
             // Lógica Store
        }

        private void Close_Click(object sender, RoutedEventArgs e) => this.Close();
    }
}