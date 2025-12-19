using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Direct3D;
using Vortice.D3DCompiler;
using Vortice.Mathematics;

namespace PichalUI.Graphics
{
    public class SimpleRenderer : IDisposable
    {
        private ID3D11Device _device;
        private ID3D11DeviceContext _context;

        // --- SHADERS (Carregados de Arquivo) ---
        private ID3D11VertexShader _vertexShader;
        private ID3D11PixelShader _pixelShader;    // MNEMOS Visual
        private ID3D11ComputeShader _motionShader; // MNEMOS Motion
        private ID3D11InputLayout _inputLayout;

        // --- BUFFERS GERAIS ---
        private ID3D11Buffer _vertexBuffer;
        private ID3D11Buffer _constantBuffer;
        private ID3D11SamplerState _samplerLinear;
        private ID3D11SamplerState _samplerPoint;
        private ID3D11RasterizerState _rasterizerState;

        // --- SISTEMA TEMPORAL (PING-PONG) ---
        private ID3D11Texture2D _historyTexture;      // Frame renderizado anterior (N-1)
        private ID3D11ShaderResourceView _historySRV;
        private ID3D11RenderTargetView _historyRTV;

        private ID3D11Texture2D _prevInputTexture;    // Frame input anterior (para comparar movimento)
        private ID3D11ShaderResourceView _prevInputSRV;

        // --- MOVIMENTO & IA ---
        private ID3D11Texture2D _motionTexture;       // Mapa de vetores gerado pelo Compute Shader
        private ID3D11UnorderedAccessView _motionUAV; // Permite escrita pelo CS
        private ID3D11ShaderResourceView _motionSRV;  // Permite leitura pelo PS

        private ID3D11Texture2D _raisrLutTexture;     // Tabela de pesos da IA
        private ID3D11ShaderResourceView _raisrLutSRV;

        private bool _isResourcesInit = false;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct Vertex
        {
            public Vector3 Position;
            public Vector2 TexCoord;
            public Vertex(Vector3 p, Vector2 uv) { Position = p; TexCoord = uv; }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct ScreenBuffer
        {
            public float Width;
            public float Height;
            public float Pad1;
            public float Pad2;
        }

        public SimpleRenderer(ID3D11Device device, ID3D11DeviceContext context)
        {
            _device = device;
            _context = context;

            InitializeStates();
            InitializeConstantBuffer();
            InitializeQuadBuffer();

            // Carrega os shaders dos arquivos .hlsl
            InitializeShadersFromFile();

            // Cria a LUT (placeholders por enquanto)
            InitializeLut();
        }

        private void InitializeShadersFromFile()
        {
            string shadersDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders");
            string pixelPath = Path.Combine(shadersDir, "UAAI_Pixel.hlsl");
            string motionPath = Path.Combine(shadersDir, "UAAI_Motion.hlsl");

            if (!File.Exists(pixelPath)) throw new FileNotFoundException($"Shader não encontrado: {pixelPath}");
            if (!File.Exists(motionPath)) throw new FileNotFoundException($"Shader não encontrado: {motionPath}");

            string pixelCode = File.ReadAllText(pixelPath);

            var vsBlob = Compiler.Compile(pixelCode, "VS", "UAAI_Pixel", "vs_5_0", ShaderFlags.OptimizationLevel3);
            _vertexShader = _device.CreateVertexShader(vsBlob.Span);

            var psBlob = Compiler.Compile(pixelCode, "PS", "UAAI_Pixel", "ps_5_0", ShaderFlags.OptimizationLevel3);
            _pixelShader = _device.CreatePixelShader(psBlob.Span);

            _inputLayout = _device.CreateInputLayout(new[] {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 12, 0)
            }, vsBlob.Span);

            string motionCode = File.ReadAllText(motionPath);
            var csBlob = Compiler.Compile(motionCode, "CSMain", "UAAI_Motion", "cs_5_0", ShaderFlags.OptimizationLevel3);
            _motionShader = _device.CreateComputeShader(csBlob.Span);
        }
        private void InitializeLut()
        {
            // Definição da Textura (25 pesos x 24 ângulos)
            var desc = new Texture2DDescription
            {
                Width = 25,
                Height = 24,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.R32_Float, // Float 32-bit puro
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.None
            };

            byte[] fileBytes;
            string lutPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RAISR_LUT.bin");

            if (File.Exists(lutPath))
            {
                // Se o arquivo existe, carregamos os pesos treinados
                fileBytes = File.ReadAllBytes(lutPath);
            }
            else
            {
                // FALLBACK: Se não existir, criamos a "Identidade" (sem efeito)
                // Isto impede o crash se ainda não tiveres treinado
                float[] dummyData = new float[25 * 24];
                for (int i = 0; i < 24; i++) dummyData[i * 25 + 12] = 1.0f; // Centro = 1

                // Converte float[] para byte[]
                fileBytes = new byte[dummyData.Length * 4];
                Buffer.BlockCopy(dummyData, 0, fileBytes, 0, fileBytes.Length);
            }

            unsafe
            {
                fixed (byte* ptr = fileBytes)
                {
                    // Pitch = Largura (25) * Tamanho do Float (4 bytes) = 100 bytes por linha
                    var subRes = new SubresourceData((IntPtr)ptr, 25 * 4);
                    _raisrLutTexture = _device.CreateTexture2D(desc, new[] { subRes });
                }
            }

            _raisrLutSRV = _device.CreateShaderResourceView(_raisrLutTexture);
        }

        // Chamado sempre que a resolução muda ou no arranque
        private void InitializeTemporalBuffers(int w, int h)
        {
            if (_isResourcesInit && _historyTexture.Description.Width == w && _historyTexture.Description.Height == h) return;

            DisposeTemporalBuffers();

            var texDesc = new Texture2DDescription
            {
                Width = (uint)w,
                Height = (uint)h,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource
            };

            // Histórico
            _historyTexture = _device.CreateTexture2D(texDesc);
            _historySRV = _device.CreateShaderResourceView(_historyTexture);
            _historyRTV = _device.CreateRenderTargetView(_historyTexture);
            _context.ClearRenderTargetView(_historyRTV, new Color4(0, 0, 0, 1)); // Limpa a preto

            // Prev Input
            _prevInputTexture = _device.CreateTexture2D(texDesc);
            _prevInputSRV = _device.CreateShaderResourceView(_prevInputTexture);

            // Motion Vectors (Float para precisão)
            var motionDesc = new Texture2DDescription
            {
                Width = (uint)w,
                Height = (uint)h,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.R16G16_Float, // 2 canais (X, Y)
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess
            };
            _motionTexture = _device.CreateTexture2D(motionDesc);
            _motionUAV = _device.CreateUnorderedAccessView(_motionTexture);
            _motionSRV = _device.CreateShaderResourceView(_motionTexture);

            _isResourcesInit = true;
        }

        public void Draw(ID3D11ShaderResourceView inputSRV, ID3D11Texture2D inputTextureResource, NativeUtils.RECT gameRect, int w, int h)
        {
            if (inputSRV == null || w <= 0 || h <= 0) return;

            InitializeTemporalBuffers(w, h);
            UpdateConstantBuffer(w, h);

            // ---------------------------------------------------------
            // PASSO 1: ESTIMATIVA DE MOVIMENTO (Compute Shader)
            // ---------------------------------------------------------
            _context.CSSetShader(_motionShader);
            _context.CSSetShaderResources(0, new[] { inputSRV, _prevInputSRV });
            _context.CSSetUnorderedAccessViews(0, new[] { _motionUAV });

            // Dispatch (Grupos de 16x16)
            int groupX = (int)Math.Ceiling(w / 16.0);
            int groupY = (int)Math.Ceiling(h / 16.0);
            _context.Dispatch((uint)groupX, (uint)groupY, 1);

            // Unbind para permitir leitura no Pixel Shader
            _context.CSSetUnorderedAccessViews(0, new ID3D11UnorderedAccessView[] { null });
            _context.CSSetShaderResources(0, new ID3D11ShaderResourceView[] { null, null });

            // ---------------------------------------------------------
            // PASSO 2: RENDERIZAÇÃO VISUAL (Pixel Shader)
            // ---------------------------------------------------------
            _context.IASetInputLayout(_inputLayout);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            uint stride = (uint)Unsafe.SizeOf<Vertex>();
            uint offset = 0;
            _context.IASetVertexBuffers(0, 1, new[] { _vertexBuffer }, new uint[] { stride }, new uint[] { offset });

            _context.VSSetShader(_vertexShader);
            _context.PSSetShader(_pixelShader);

            // t0=Cur, t1=Hist, t2=Motion, t3=LUT
            _context.PSSetShaderResources(0, new[] {
                inputSRV,
                _historySRV,
                _motionSRV,
                _raisrLutSRV
            });
            _context.PSSetSamplers(0, new[] { _samplerLinear, _samplerPoint });

            // Atualiza constantes
            UpdateQuad(gameRect, w, h);

            // Desenha
            _context.Draw(6, 0);

            // ---------------------------------------------------------
            // PASSO 3: PING-PONG (Preparar próximo frame)
            // ---------------------------------------------------------
            // Copia o que acabámos de desenhar (BackBuffer) para o Histórico
            // Nota: O SimpleRenderer desenha no RenderTargetView ativo (definido no SilkGameWindow).
            // Para capturar o resultado, precisamos de copiar do BackBuffer atual.
            // Mas como não temos acesso fácil ao BackBuffer aqui, vamos fazer algo inteligente:
            // Vamos copiar o INPUT atual para o PREV INPUT para o Motion vector funcionar no próximo frame.

            if (inputTextureResource != null)
            {
                _context.CopyResource(_prevInputTexture, inputTextureResource);
            }

            // O Histórico idealmente seria o output do shader. 
            // Como estamos a desenhar na tela, uma solução simples é copiar o input atual para histórico 
            // se quisermos "simular" persistência, mas o correto é capturar o output.
            // Por agora, para não complicar com RenderTargets extra, vamos usar o próprio Input como "Histórico" 
            // para estabilizar flickering (não é TAA perfeito, mas ajuda).
            // Numa versão v2, renderizamos para uma textura intermédia primeiro.
            _context.CopyResource(_historyTexture, inputTextureResource);

            // Limpeza
            _context.PSSetShaderResources(0, new ID3D11ShaderResourceView[] { null, null, null, null });
        }

        private void UpdateConstantBuffer(float w, float h)
        {
            var data = new ScreenBuffer { Width = w, Height = h, Pad1 = 0, Pad2 = 0 };
            unsafe
            {
                var ms = _context.Map(_constantBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                Unsafe.Copy(ms.DataPointer.ToPointer(), ref data);
                _context.Unmap(_constantBuffer, 0);
            }
            _context.VSSetConstantBuffers(0, new[] { _constantBuffer });
            _context.PSSetConstantBuffers(0, new[] { _constantBuffer });
            _context.CSSetConstantBuffers(0, new[] { _constantBuffer });
        }

        private void UpdateQuad(NativeUtils.RECT r, int sw, int sh)
        {
            float l = (float)r.Left / sw; float t = (float)r.Top / sh;
            float right = (float)r.Right / sw; float b = (float)r.Bottom / sh;

            var verts = new[]
            {
                new Vertex(new Vector3(-1, 1, 0), new Vector2(l, t)),
                new Vertex(new Vector3(1, 1, 0), new Vector2(right, t)),
                new Vertex(new Vector3(-1, -1, 0), new Vector2(l, b)),
                new Vertex(new Vector3(-1, -1, 0), new Vector2(l, b)),
                new Vertex(new Vector3(1, 1, 0), new Vector2(right, t)),
                new Vertex(new Vector3(1, -1, 0), new Vector2(right, b)),
            };

            unsafe
            {
                var ms = _context.Map(_vertexBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                fixed (Vertex* p = verts) Unsafe.CopyBlock(ms.DataPointer.ToPointer(), p, (uint)(Unsafe.SizeOf<Vertex>() * 6));
                _context.Unmap(_vertexBuffer, 0);
            }
        }

        private void InitializeStates()
        {
            var rDesc = new RasterizerDescription(CullMode.None, FillMode.Solid) { DepthClipEnable = false };
            _rasterizerState = _device.CreateRasterizerState(rDesc);
            _context.RSSetState(_rasterizerState);

            var sDescLin = new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, TextureAddressMode.Clamp, TextureAddressMode.Clamp, 0, 0, ComparisonFunction.Never, new Color4(0, 0, 0, 0), 0, float.MaxValue);
            _samplerLinear = _device.CreateSamplerState(sDescLin);

            var sDescPt = new SamplerDescription(Filter.MinMagMipPoint, TextureAddressMode.Clamp, TextureAddressMode.Clamp, TextureAddressMode.Clamp, 0, 0, ComparisonFunction.Never, new Color4(0, 0, 0, 0), 0, float.MaxValue);
            _samplerPoint = _device.CreateSamplerState(sDescPt);
        }

        private void InitializeConstantBuffer()
        {
            var desc = new BufferDescription
            {
                Usage = ResourceUsage.Dynamic,
                ByteWidth = 16, // 16 bytes align
                BindFlags = BindFlags.ConstantBuffer,
                CPUAccessFlags = CpuAccessFlags.Write
            };
            _constantBuffer = _device.CreateBuffer(desc);
        }

        private void InitializeQuadBuffer()
        {
            var desc = new BufferDescription
            {
                Usage = ResourceUsage.Dynamic,
                ByteWidth = (uint)(Unsafe.SizeOf<Vertex>() * 6),
                BindFlags = BindFlags.VertexBuffer,
                CPUAccessFlags = CpuAccessFlags.Write
            };
            _vertexBuffer = _device.CreateBuffer(desc);
        }

        private void DisposeTemporalBuffers()
        {
            _historyRTV?.Dispose(); _historySRV?.Dispose(); _historyTexture?.Dispose();
            _prevInputSRV?.Dispose(); _prevInputTexture?.Dispose();
            _motionUAV?.Dispose(); _motionSRV?.Dispose(); _motionTexture?.Dispose();
        }

        public void Dispose()
        {
            DisposeTemporalBuffers();
            _raisrLutSRV?.Dispose(); _raisrLutTexture?.Dispose();
            _rasterizerState?.Dispose(); _samplerLinear?.Dispose(); _samplerPoint?.Dispose();
            _vertexBuffer?.Dispose(); _constantBuffer?.Dispose();
            _pixelShader?.Dispose(); _vertexShader?.Dispose(); _motionShader?.Dispose();
            _inputLayout?.Dispose();
        }
    }
}