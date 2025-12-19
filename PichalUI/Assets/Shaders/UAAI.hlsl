// =========================================================
// UAAI v11.1 - TUNED FOR SHARPNESS
// =========================================================

struct VS_IN { float3 pos : POSITION; float2 uv : TEXCOORD; };
struct PS_IN { float4 pos : SV_POSITION; float2 uv : TEXCOORD; };

PS_IN VS(VS_IN input) {
    PS_IN output;
    output.pos = float4(input.pos, 1.0);
    output.uv = input.uv;
    return output;
}

Texture2D tex : register(t0);
SamplerState sampLinear : register(s0);
SamplerState sampPoint : register(s1);

cbuffer ScreenBuffer : register(b0){
    float ScreenWidth;
    float ScreenHeight;
    float2 Padding;
};

// [AJUSTE 1] Aumentei a força de 0.6 para 1.0. 
// O FSR costuma usar valores altos porque o 'amp' protege contra artefactos.
static float RazorStrength = 1.0; 

float GetLuma(float3 rgb) {
    return sqrt(dot(rgb, float3(0.299, 0.587, 0.114)));
}

// [AJUSTE 2] Lanczos mais agressivo
// Mudei ligeiramente a curva para preservar mais nitidez no centro
float GetLanczosWeight(float distSq) {
    if(distSq < 4.0) 
        return (1.0 - 0.25 * distSq) * (1.0 - 0.0625 * distSq);
    return 0.0;
}

float3 BicubicSample(Texture2D tex, SamplerState samp, float2 uv, float2 texSize) {
    float2 pixelPos = uv * texSize;
    float2 tc = floor(pixelPos - 0.5) + 0.5;
    float2 f = pixelPos - tc;
    
    float2 f2 = f * f;
    float2 f3 = f2 * f;
    
    // Pesos ajustados para manter a nitidez no centro (evita o blur)
    float2 w0 = f2 - 0.5 * (f3 + f);
    float2 w1 = 1.5 * f3 - 2.5 * f2 + 1.0;
    float2 w2 = -1.5 * f3 + 2.0 * f2 + 0.5 * f;
    float2 w3 = 0.5 * (f3 - f2);
    
    float2 s0 = w0 + w1;
    float2 s1 = w2 + w3;
    float2 f0 = w1 / s0;
    float2 f1 = w3 / s1;
    
    float2 t0 = tc - 1.0 + f0;
    float2 t1 = tc + 1.0 + f1;
    
    float2 invSize = 1.0 / texSize;
    
    float3 c0 = tex.SampleLevel(samp, float2(t0.x, t0.y) * invSize, 0).rgb;
    float3 c1 = tex.SampleLevel(samp, float2(t1.x, t0.y) * invSize, 0).rgb;
    float3 c2 = tex.SampleLevel(samp, float2(t0.x, t1.y) * invSize, 0).rgb;
    float3 c3 = tex.SampleLevel(samp, float2(t1.x, t1.y) * invSize, 0).rgb;
    
    return (c0*s0.x*s0.y + c1*s1.x*s0.y + c2*s0.x*s1.y + c3*s1.x*s1.y) / ((s0.x+s1.x)*(s0.y+s1.y));
}

// Função Sigmoide (S-Curve) para apertar curvas
// Transforma cinzentos em pretos ou brancos, definindo a borda
float3 SigmoidContrast(float3 color, float strength) {
    // Curva S-Curve simples: x * x * (3 - 2 * x)
    // Mas ajustada pela força desejada
    float3 s_curve = color * color * (3.0 - 2.0 * color);
    return lerp(color, s_curve, strength);
}

float4 PS(PS_IN input) : SV_Target {
    // SETUP
    float2 texSize = float2(ScreenWidth, ScreenHeight);
    float2 pxSize = 1.0 / texSize;

    // --- DEBUG SPLIT ---
    if(input.uv.x < 0.5) return tex.Sample(sampLinear, input.uv); 

    // =========================================================
    // CAMADA 1: FUSION (Base)
    // =========================================================
    float3 cBase = BicubicSample(tex, sampLinear, input.uv, texSize);

    // =========================================================
    // CAMADA 2: CORTEX + GEOMETRY (Análise de Curvas)
    // =========================================================
    // Ler vizinhos estendidos para entender a geometria
    float3 nN = tex.SampleLevel(sampPoint, input.uv + float2(0, -pxSize.y), 0).rgb;
    float3 nS = tex.SampleLevel(sampPoint, input.uv + float2(0, pxSize.y), 0).rgb;
    float3 nW = tex.SampleLevel(sampPoint, input.uv + float2(-pxSize.x, 0), 0).rgb;
    float3 nE = tex.SampleLevel(sampPoint, input.uv + float2(pxSize.x, 0), 0).rgb;
    
    // Diagonais (Crucial para curvas)
    float3 nNW = tex.SampleLevel(sampPoint, input.uv + float2(-pxSize.x, -pxSize.y), 0).rgb;
    float3 nSE = tex.SampleLevel(sampPoint, input.uv + float2(pxSize.x, pxSize.y), 0).rgb;
    float3 nNE = tex.SampleLevel(sampPoint, input.uv + float2(pxSize.x, -pxSize.y), 0).rgb;
    float3 nSW = tex.SampleLevel(sampPoint, input.uv + float2(-pxSize.x, pxSize.y), 0).rgb;

    // Calcular Lumas
    float lC = GetLuma(cBase);
    float lN = GetLuma(nN); float lS = GetLuma(nS);
    float lW = GetLuma(nW); float lE = GetLuma(nE);

    // Deteção de Geometria: Estamos numa borda reta ou curva?
    // Gradientes H/V
    float edgeH = abs(lE - lW);
    float edgeV = abs(nN - nS);
    float edgeTotal = edgeH + edgeV;

    // Se houver uma borda forte, aplicamos o Steepness
    // Isto aperta a transição da cor, fazendo a curva parecer vetorizada
    // em vez de borrada.
    float curveFactor = smoothstep(0.05, 0.5, edgeTotal);
    
    // Aplicar S-Curve apenas onde existe geometria (curveFactor)
    // Isto protege as texturas planas de ficarem com contraste estranho
    if(curveFactor > 0.0) {
        cBase = SigmoidContrast(cBase, curveFactor * 0.5); 
    }

    // Limits para Clamp (Proteção contra halos)
    float3 minN = min(cBase, min(min(nN, nS), min(nW, nE)));
    float3 maxN = max(cBase, max(max(nN, nS), max(nW, nE)));
    
    // Overshoot controlado (Mantivemos da v12, funciona bem)
    float contrast = length(maxN - minN);
    float overshoot = 0.05 + (0.05 * (1.0 - contrast)); 
    float3 minLimit = minN - overshoot;
    float3 maxLimit = maxN + overshoot;

    cBase = clamp(cBase, minLimit, maxLimit);

    // =========================================================
    // CAMADA 3: RAZOR (De-Blur Reforçado)
    // =========================================================
    
    // Média dos vizinhos (Blur natural)
    float3 blur = (nN + nS + nW + nE) * 0.25;
    
    // High Pass (Detalhe puro)
    float3 detail = cBase - blur;
    
    // Aumentei a força base ligeiramente (0.5 -> 0.6)
    // E adicionei um boost específico para onde detetámos curvas
    float baseStrength = RazorStrength + 0.6; 
    float curveBoost = curveFactor * 0.4; // +40% de força nas curvas!
    
    float totalStrength = baseStrength + curveBoost;

    // Filtro de Ruído
    float detailLuma = length(detail);
    float weight = smoothstep(0.02, 0.15, detailLuma); 
    
    float3 cFinal = cBase + detail * (totalStrength * weight);

    // Proteção Final
    cFinal = clamp(cFinal, 0.0, 1.0);

    // Linha vermelha
    if(abs(input.uv.x - 0.5) < 0.002) return float4(1, 0, 0, 1);

    return float4(cFinal, 1.0);
}