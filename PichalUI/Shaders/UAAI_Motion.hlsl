// =========================================================
// UAAI MOTION ESTIMATOR (Compute Shader)
// Arquivo: Shaders/UAAI_Motion.hlsl
// =========================================================

Texture2D<float4> CurFrame : register(t0);
Texture2D<float4> PrevFrame : register(t1);
RWTexture2D<float2> MotionVectors : register(u0);

cbuffer Params : register(b0)
{
    float ScreenWidth;
    float ScreenHeight;
    float2 Padding;
};

// Tamanho do Macrobloco (16x16)
#define BLOCK_SIZE 16
// Raio de busca (procura 8 pixeis para cada lado)
#define SEARCH_RADIUS 8 

[numthreads(16, 16, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)ScreenWidth || id.y >= (uint)ScreenHeight) return;

    // Otimização: Apenas 1 thread por bloco faz o trabalho pesado
    // Os pixeis (0,0) de cada bloco de 16x16 comandam
    uint2 localID = uint2(id.x % BLOCK_SIZE, id.y % BLOCK_SIZE);
    
    // Se não for o líder do bloco, espera
    if (localID.x != 0 || localID.y != 0) return;

    uint2 basePos = id.xy;
    
    float minSAD = 1000000.0; // Sum of Absolute Differences
    float2 bestVector = float2(0, 0);
    
    // Busca na vizinhança do Frame Anterior
    // Tenta encontrar onde este bloco estava
    
    [unroll(4)]
    for (int y = -SEARCH_RADIUS; y <= SEARCH_RADIUS; y += 4) // Passo largo para performance
    {
        [unroll(4)]
        for (int x = -SEARCH_RADIUS; x <= SEARCH_RADIUS; x += 4)
        {
            float currentSAD = 0;
            
            // Compara 4 pontos de amostra dentro do bloco (Sub-sampling)
            // Não comparamos os 256 pixeis do bloco, seria lento demais
            
            // Ponto 1 (Topo-Esq)
            float3 c1 = CurFrame[basePos].rgb;
            float3 p1 = PrevFrame[basePos + int2(x,y)].rgb;
            currentSAD += abs(dot(c1, 0.33) - dot(p1, 0.33));
            
            // Ponto 2 (Centro)
            float3 c2 = CurFrame[basePos + uint2(8,8)].rgb;
            float3 p2 = PrevFrame[basePos + uint2(8,8) + int2(x,y)].rgb;
            currentSAD += abs(dot(c2, 0.33) - dot(p2, 0.33));

            if (currentSAD < minSAD)
            {
                minSAD = currentSAD;
                bestVector = float2(x, y);
            }
        }
    }
    
    // Normaliza o vetor (UV Space 0..1)
    float2 normVector = bestVector / float2(ScreenWidth, ScreenHeight);

    // Escreve o resultado para TODOS os pixeis deste bloco
    // Como estamos num Compute Shader, temos acesso aleatório de escrita
    for(uint i=0; i<BLOCK_SIZE; i++)
    {
        for(uint j=0; j<BLOCK_SIZE; j++)
        {
            uint2 targetPos = basePos + uint2(i, j);
            if(targetPos.x < (uint)ScreenWidth && targetPos.y < (uint)ScreenHeight)
            {
                MotionVectors[targetPos] = normVector;
            }
        }
    }
}