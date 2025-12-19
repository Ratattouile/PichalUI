using System;
using System.Runtime.InteropServices;
using Silk.NET.Windowing;
using Silk.NET.Maths;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Direct3D;
using Vortice.Mathematics;
using System.IO;
using Silk.NET.OpenGL;
using System.Windows;
using Window = Silk.NET.Windowing.Window;

namespace PichalUI.Graphics
{
    public class SilkGameWindow : IDisposable
    {
        private IWindow _window;
        private CaptureEngine _engine;
        private SimpleRenderer _renderer;
        private ID3D11Device _device;
        private ID3D11DeviceContext _context;
        private IDXGISwapChain _swapChain;
        private ID3D11RenderTargetView _renderView;

        private ID3D11ShaderResourceView _cachedSRV;
        private ID3D11Texture2D _lastFrameTexture;

        // --- API NATIVA DO WINDOWS ---
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        // --- A SALVAÇÃO: WDA_EXCLUDEFROMCAPTURE ---
        [DllImport("user32.dll")] static extern uint SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        const int GWL_STYLE = -16;
        const int WS_CAPTION = 0x00C00000;
        const int WS_THICKFRAME = 0x00040000;
        const int WS_SYSMENU = 0x00080000;
        const uint SWP_FRAMECHANGED = 0x0020;
        const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011; // O Código Mágico

        public SilkGameWindow(CaptureEngine engine)
        {
            _engine = engine;
            var options = WindowOptions.Default;

            // VOLTAMOS AO FULLSCREEN (Obrigatório para o efeito final)
            var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            options.Size = new Vector2D<int>(screen.Width, screen.Height);

            options.Title = "UAAI - Overlay";
            options.VSync = false;
            options.TopMost = true; // Volta a ser TopMost
            options.WindowBorder = WindowBorder.Hidden; // Sem bordas
            options.WindowState = Silk.NET.Windowing.WindowState.Maximized;

            _window = Window.Create(options);
            _window.Load += OnLoad;
            _window.Render += OnRender;
            _window.Resize += OnResize;
        }

        public void Run() => _window.Run();

        private void OnLoad()
        {
            var hwnd = _window.Native.Win32.Value.Hwnd;

            // 1. Remover Bordas (Estilo Visual)
            int style = GetWindowLong(hwnd, GWL_STYLE);
            style &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU);
            SetWindowLong(hwnd, GWL_STYLE, style);

            // 2. Forçar Fullscreen Real
            var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, screen.Width, screen.Height, SWP_FRAMECHANGED);

            // 3. Click-Through (Para o rato passar para o jogo)
            NativeUtils.SetClickThrough(hwnd);

            // 4. --- PROTEÇÃO CRÍTICA ---
            // Isto impede que o CaptureEngine capture esta janela.
            // Sem isto, tens o crash de "Espelho Infinito".
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);

            InitializeDirectX(hwnd);
        }

        private void InitializeDirectX(IntPtr hwnd)
        {
            _device = _engine.Device;
            _context = _engine.Context;

            using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            using var factory = adapter.GetParent<IDXGIFactory>();

            var desc = new SwapChainDescription
            {
                BufferCount = 2,
                BufferDescription = new ModeDescription((uint)_window.Size.X, (uint)_window.Size.Y, new Rational(60, 1), Format.B8G8R8A8_UNorm),
                BufferUsage = Usage.RenderTargetOutput,
                OutputWindow = hwnd,
                SampleDescription = new SampleDescription(1, 0),
                Windowed = true, // Windowed = true é importante para overlays!
                SwapEffect = SwapEffect.FlipDiscard
            };
            _swapChain = factory.CreateSwapChain(_device, desc);
            CreateRenderTarget();
            UpdateViewport((int)_window.Size.X, (int)_window.Size.Y);

            // Renderizador Estável v1
            _renderer = new SimpleRenderer(_device, _context);
        }

        private void UpdateViewport(int width, int height)
        {
            if (_context != null)
            {
                var viewport = new Viewport(0, 0, width, height);
                _context.RSSetViewports(new[] { viewport });
            }
        }

        private void CreateRenderTarget()
        {
            _renderView?.Dispose();
            using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
            _renderView = _device.CreateRenderTargetView(backBuffer);
        }

        private void OnRender(double delta)
        {
            _engine.Update();

            _context.OMSetRenderTargets(_renderView);
            _context.ClearRenderTargetView(_renderView, new Color4(0, 0, 0, 1));

            if (_engine.LatestTexture != null)
            {
                try
                {
                    // (Código de cache SRV mantém-se igual...)
                    if (_engine.LatestTexture != _lastFrameTexture)
                    {
                        _cachedSRV?.Dispose();
                        _cachedSRV = _device.CreateShaderResourceView(_engine.LatestTexture);
                        _lastFrameTexture = _engine.LatestTexture;
                    }

                    if (_cachedSRV != null)
                    {
                        // CORREÇÃO 3: Passar _engine.LatestTexture como 2º argumento
                        _renderer.Draw(
                          _cachedSRV,
                          _engine.LatestTexture, // <--- O NOVO ARGUMENTO OBRIGATÓRIO
                          _engine.CurrentSourceRect,
                          (int)_engine.LatestTexture.Description.Width,
                          (int)_engine.LatestTexture.Description.Height
                        );
                    }
                }
                catch
                {
                    _cachedSRV?.Dispose();
                    _cachedSRV = null;
                    _lastFrameTexture = null;
                }
            }

            _swapChain.Present(0, PresentFlags.None);
        }

        private void OnResize(Vector2D<int> size)
        {
            if (_swapChain == null) return;
            _context.OMSetRenderTargets((ID3D11RenderTargetView)null, null);
            _renderView?.Dispose();
            try
            {
                _swapChain.ResizeBuffers(2, (uint)size.X, (uint)size.Y, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
                CreateRenderTarget();
                UpdateViewport(size.X, size.Y);
            }
            catch { }
        }

        public void Dispose()
        {
            _cachedSRV?.Dispose();
            _renderer?.Dispose();
            _renderView?.Dispose();
            _swapChain?.Dispose();
            _window?.Dispose();
        }
    }
}