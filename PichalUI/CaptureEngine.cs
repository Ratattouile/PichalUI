using System;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Direct3D;
using SharpGen.Runtime;

namespace PichalUI.Graphics
{
    public class CaptureEngine : IDisposable
    {
        private IDXGIOutputDuplication _duplication;
        public IntPtr GameHandle { get; set; }
        public int TargetProcessId { get; set; }

        public ID3D11Device Device { get; private set; }
        public ID3D11DeviceContext Context { get; private set; }
        public ID3D11Texture2D LatestTexture { get; private set; }

        // Inicializa com um valor seguro
        public NativeUtils.RECT CurrentSourceRect { get; private set; } = new NativeUtils.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };

        public bool IsInitialized => Device != null && _duplication != null;

        public void Initialize()
        {
            if (!FindAndInitAdapter())
            {
                // Se falhar, mostra mensagem para sabermos que o erro foi aqui
                System.Windows.MessageBox.Show("Erro CaptureEngine: Não consegui ligar-me a nenhum monitor.\nVerifica se o jogo está em modo JANELA.");
            }
        }

        private bool FindAndInitAdapter()
        {
            if (DXGI.CreateDXGIFactory1(out IDXGIFactory1 factory) == Result.Fail) return false;

            int adapterIndex = 0;
            IDXGIAdapter1 adapter;

            // Procura em todas as placas gráficas
            while (factory.EnumAdapters1((uint)adapterIndex, out adapter) != Result.Fail)
            {
                int outputIndex = 0;
                IDXGIOutput output;

                // Procura em todos os monitores
                while (adapter.EnumOutputs((uint)outputIndex, out output) != Result.Fail)
                {
                    if (TryCreateDuplication(adapter, output))
                    {
                        return true; // Sucesso!
                    }
                    outputIndex++;
                }
                adapterIndex++;
            }
            return false;
        }

        private bool TryCreateDuplication(IDXGIAdapter1 adapter, IDXGIOutput output)
        {
            try
            {
                D3D11.D3D11CreateDevice(
                    adapter,
                    DriverType.Unknown,
                    DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                    null,
                    out ID3D11Device device,
                    out ID3D11DeviceContext context);

                if (device == null) return false;

                using var output1 = output.QueryInterface<IDXGIOutput1>();
                try
                {
                    _duplication = output1.DuplicateOutput(device);
                }
                catch (SharpGenException)
                {
                    device.Dispose();
                    context.Dispose();
                    return false;
                }

                Device = device;
                Context = context;

                // --- CORREÇÃO DO ERRO CS1061 ---
                // Usamos DesktopCoordinates em vez de DesktopBounds
                var desc = output.Description;
                CurrentSourceRect = new NativeUtils.RECT
                {
                    Left = 0,
                    Top = 0,
                    Right = desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left,
                    Bottom = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top
                };

                return true;
            }
            catch { return false; }
        }

        public void Update()
        {
            if (!IsInitialized) return;

            // Atualiza a posição da janela do jogo (se existir)
            IntPtr foregroundWin = NativeUtils.GetForegroundWindow();
            if (foregroundWin != IntPtr.Zero)
            {
                NativeUtils.GetWindowThreadProcessId(foregroundWin, out uint activeProcessId);
                if (TargetProcessId == 0 || activeProcessId == TargetProcessId)
                {
                    var rect = NativeUtils.GetGameRect(foregroundWin);
                    if (rect.Width > 64 && rect.Height > 64)
                    {
                        CurrentSourceRect = rect;
                        GameHandle = foregroundWin;
                    }
                }
            }

            try
            {
                // Timeout de 10ms
                var result = _duplication.AcquireNextFrame(0, out var frameInfo, out var resource);

                if (result.Success)
                {
                    using (resource)
                    using (var screenTexture = resource.QueryInterface<ID3D11Texture2D>())
                    {
                        lock (Context)
                        {
                            // Cria a textura se ainda não existir
                            if (LatestTexture == null ||
                                LatestTexture.Description.Width != screenTexture.Description.Width ||
                                LatestTexture.Description.Height != screenTexture.Description.Height)
                            {
                                LatestTexture?.Dispose();
                                var desc = screenTexture.Description;
                                desc.BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget;
                                desc.MiscFlags = ResourceOptionFlags.None;
                                desc.CPUAccessFlags = CpuAccessFlags.None;
                                desc.Usage = ResourceUsage.Default;
                                LatestTexture = Device.CreateTexture2D(desc);
                            }

                            Context.CopyResource(LatestTexture, screenTexture);
                        }
                    }
                    _duplication.ReleaseFrame();
                }
                else
                {
                    // Liberta se houve erro ou timeout
                    try { _duplication.ReleaseFrame(); } catch { }
                }
            }
            catch
            {
                // Ignora erros de captura para não crashar o loop
            }
        }

        public void Dispose()
        {
            _duplication?.Dispose();
            LatestTexture?.Dispose();
            Context?.Dispose();
            Device?.Dispose();
        }
    }
}