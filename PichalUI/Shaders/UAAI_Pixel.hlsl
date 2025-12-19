// =========================================================
// UAAI v20 - FRANKENSTEIN TURBO (All Features Restored)
// =========================================================

struct VS_IN { float3 pos : POSITION; float2 uv : TEXCOORD; };
struct PS_IN { float4 pos : SV_POSITION; float2 uv : TEXCOORD; };

PS_IN VS(VS_IN input) {
    PS_IN output;
    output.pos = float4(input.pos, 1.0);
    output.uv = input.uv;
    return output;
}

Texture2D CurTex : register(t0);
Texture2D HistTex : register(t1);
Texture2D MotionTex : register(t2);
Texture2D<float> LutTex : register(t3); // AI LUT

SamplerState sampLinear : register(s0);
SamplerState sampPoint : register(s1);

cbuffer ScreenBuffer : register(b0){
    float ScreenWidth; float ScreenHeight; float2 Pad;
};

// --- FERRAMENTAS BÁSICAS ---
float GetLuma(float3 rgb) { return dot(rgb, float3(0.299, 0.587, 0.114)); }

float3 ApplyVibrance(float3 color, float strength){
    float luma = GetLuma(color);
    float3 chroma = color - luma;
    return luma + chroma * strength;
}

float3 SmartClamp(float3 c, float3 minC, float3 maxC){
    // Relaxamos ligeiramente o clamp (0.05) para não matar o brilho da IA
    return clamp(c, minC - 0.05, maxC + 0.05);
}

// --- MOTORES (ENGINES) ---

// 1. CAS (Turbo Charged - Strength 1.2)
float3 SampleCAS(Texture2D tex, SamplerState samp, float2 uv, float2 texSize, float strength) {
    float2 px = 1.0 / texSize;
    float3 n = tex.SampleLevel(samp, uv + float2(0, -px.y), 0).rgb;
    float3 s = tex.SampleLevel(samp, uv + float2(0, px.y), 0).rgb;
    float3 w = tex.SampleLevel(samp, uv + float2(-px.x, 0), 0).rgb;
    float3 e = tex.SampleLevel(samp, uv + float2(px.x, 0), 0).rgb;
    float3 c = tex.SampleLevel(samp, uv, 0).rgb;
    
    float peak = -1.0 / lerp(8.0, 3.0, saturate(strength));
    float3 wRGB = float3(peak, peak, peak);
    float3 rcpWeight = 1.0 / (1.0 + 4.0 * wRGB);
    return (c + (n + s + w + e) * wRGB) * rcpWeight;
}

// 2. B-SPLINE (Para o Céu/Fundo - Suave)
float3 SampleBSpline(Texture2D tex, SamplerState samp, float2 uv, float2 texSize){
    float2 pixelPos = uv * texSize;
    float2 tc = floor(pixelPos - 0.5) + 0.5;
    float2 f = pixelPos - tc;
    float2 w0 = (1.0/6.0)*(-f*f*f + 3.0*f*f - 3.0*f + 1.0);
    float2 w1 = (1.0/6.0)*(3.0*f*f*f - 6.0*f*f + 4.0);
    float2 w2 = (1.0/6.0)*(-3.0*f*f*f + 3.0*f*f + 3.0*f + 1.0);
    float2 w3 = (1.0/6.0)*(f*f*f);
    
    float2 s0 = w0 + w1; float2 s1 = w2 + w3;
    float2 f0 = w1 / s0; float2 f1 = w3 / s1;
    float2 t0 = tc - 1.0 + f0; float2 t1 = tc + 1.0 + f1;
    float2 invSize = 1.0 / texSize;
    
    return (tex.SampleLevel(samp, float2(t0.x, t0.y)*invSize, 0).rgb * s0.x*s0.y +
            tex.SampleLevel(samp, float2(t1.x, t0.y)*invSize, 0).rgb * s1.x*s0.y +
            tex.SampleLevel(samp, float2(t0.x, t1.y)*invSize, 0).rgb * s0.x*s1.y +
            tex.SampleLevel(samp, float2(t1.x, t1.y)*invSize, 0).rgb * s1.x*s1.y) / ((s0.x+s1.x)*(s0.y+s1.y));
}

// 3. DIRECTIONAL & SUPER-RES (Para Geometria 3D)
float3 SampleDirectional(Texture2D tex, SamplerState samp, float2 uv, float2 texSize, float2 dir, float edgeStrength){
    if(edgeStrength < 0.1) return tex.SampleLevel(samp, uv, 0).rgb;
    float2 dirNorm = normalize(dir);
    float2 offset = dirNorm * (1.0 / texSize);
    float3 p1 = tex.SampleLevel(samp, uv - offset * 0.5, 0).rgb;
    float3 p2 = tex.SampleLevel(samp, uv + offset * 0.5, 0).rgb;
    return lerp(tex.SampleLevel(samp, uv, 0).rgb, (p1 + p2) * 0.5, edgeStrength * 0.3);
}

float3 SampleSuperRes(Texture2D tex, SamplerState samp, float2 uv, float2 texSize) {
    float2 d = (1.0 / texSize) * 0.25;
    float3 s1 = tex.SampleLevel(samp, uv + float2( d.x,  d.y), 0).rgb;
    float3 s2 = tex.SampleLevel(samp, uv + float2(-d.x, -d.y), 0).rgb;
    float3 s3 = tex.SampleLevel(samp, uv + float2(-d.x,  d.y), 0).rgb;
    float3 s4 = tex.SampleLevel(samp, uv + float2( d.x, -d.y), 0).rgb;
    return (s1 + s2 + s3 + s4) * 0.25;
}

// 4. MNEMOS AI (Turbo Edition - Deblur + Boost)
float3 ApplyMnemos(float2 uv, float2 texSize) {
    float2 px = 1.0 / texSize;
    
    // Deblur Inicial
    float3 c = CurTex.SampleLevel(sampPoint, uv, 0).rgb;
    float3 blur = (
        CurTex.SampleLevel(sampLinear, uv + float2(-px.x, 0), 0).rgb +
        CurTex.SampleLevel(sampLinear, uv + float2( px.x, 0), 0).rgb +
        CurTex.SampleLevel(sampLinear, uv + float2( 0, -px.y), 0).rgb +
        CurTex.SampleLevel(sampLinear, uv + float2( 0,  px.y), 0).rgb
    ) * 0.25;
    float3 sharpBase = c + (c - blur) * 0.6; 

    // Análise
    float3 n = CurTex.SampleLevel(sampPoint, uv + float2(0, -px.y), 0).rgb;
    float3 w = CurTex.SampleLevel(sampPoint, uv + float2(-px.x, 0), 0).rgb;
    float lC = GetLuma(c); float lN = GetLuma(n); float lW = GetLuma(w);
    float gy = lN - lC; float gx = lW - lC;
    float edgeStrength = sqrt(gx*gx + gy*gy);
    
    if (edgeStrength < 0.005) return sharpBase;

    // Inferência
    float angle = atan2(gy, gx);
    if (angle < 0) angle += 3.14159;
    int angle_idx = int((angle / 3.14159) * 24.0) % 24;
    
    float3 raisrColor = 0;
    int k = 0;
    [unroll]
    for (int y = -2; y <= 2; y++) {
        for (int x = -2; x <= 2; x++) {
            float weight = LutTex.Load(int3(k, angle_idx, 0));
            // Aplicar sobre a original para evitar artefatos de duplo sharpen
            float3 p = CurTex.SampleLevel(sampLinear, uv + float2(x, y) * px, 0).rgb;
            raisrColor += p * weight;
            k++;
        }
    }
    
    if (dot(raisrColor, 1.0) < 0.001) raisrColor = sharpBase;

    // Boost AI
    float3 diff = raisrColor - c;
    return c + (diff * 1.3);
}

// =========================================================
// PIPELINE PRINCIPAL
// =========================================================
float4 PS(PS_IN input) : SV_Target {
    float2 texSize = float2(ScreenWidth, ScreenHeight);
    float2 pxSize = 1.0 / texSize;

    // --- 1. CORTEX: ANÁLISE ---
    float3 c00 = CurTex.SampleLevel(sampPoint, input.uv, 0).rgb;
    float3 cN = CurTex.SampleLevel(sampPoint, input.uv + float2(0, -pxSize.y), 0).rgb;
    float3 cS = CurTex.SampleLevel(sampPoint, input.uv + float2(0, pxSize.y), 0).rgb;
    float3 cW = CurTex.SampleLevel(sampPoint, input.uv + float2(-pxSize.x, 0), 0).rgb;
    float3 cE = CurTex.SampleLevel(sampPoint, input.uv + float2(pxSize.x, 0), 0).rgb;

    float l00 = GetLuma(c00);
    float lN = GetLuma(cN); float lS = GetLuma(cS);
    float lW = GetLuma(cW); float lE = GetLuma(cE);

    // Métricas
    float gH = abs(lW - lE);
    float gV = abs(lN - lS);
    float edgeTotal = gH + gV;
    float laplace = abs(lN + lS + lW + lE - 4.0 * l00);
    float variance = abs(l00 - ((lN + lS + lW + lE) * 0.25)) * 12.0; 

    // === PESOS (Restaurados) ===
    float wSky = 1.0 - smoothstep(0.0, 0.05, variance + edgeTotal); // Fundo liso
    float wTexture = smoothstep(0.001, 0.30, variance);             // Chão/Detalhe
    float wGeo = smoothstep(0.3, 0.7, edgeTotal);                   // Geometria 3D
    float wSharp = smoothstep(0.02, 0.30, edgeTotal + laplace * 2.0); // Borda Fina (AI)
    
    // Prioridades
    wTexture = max(wTexture, 0.2); // Garante textura mínima
    wGeo = saturate(wGeo - wSharp * 2.0); // Se for borda fina, a AI manda, não a Geo.

    // --- 2. LAYERS ---
    
    // Layer 1: Fundo (BSpline)
    float3 layerBack = SampleBSpline(CurTex, sampLinear, input.uv, texSize);

    // Layer 2: Textura (CAS Turbo 1.2)
    float3 layerTex = SampleCAS(CurTex, sampLinear, input.uv, texSize, 1.2);

    // Layer 3: Geometria (Directional + SuperRes)
    float3 cDir = SampleDirectional(CurTex, sampLinear, input.uv, texSize, float2(gV, -gH), edgeTotal);
    float3 cSuper = SampleSuperRes(CurTex, sampLinear, input.uv, texSize);
    float3 layerGeo = lerp(cDir, cSuper, 0.5);

    // Layer 4: AI (Mnemos Turbo)
    float3 layerAI = ApplyMnemos(input.uv, texSize);


    // --- 3. FUSION ---
    float3 finalColor = layerBack;
    
    // Mistura o Céu/Fundo primeiro (Base)
    // Se wSky for baixo, deixamos passar o original c00
    finalColor = lerp(c00, layerBack, wSky);

    // Aplica Textura
    finalColor = lerp(finalColor, layerTex, wTexture);

    // Aplica Geometria (apenas onde há formas 3D)
    finalColor = lerp(finalColor, layerGeo, wGeo);

    // Aplica AI (Prioridade Máxima em Bordas)
    // Usamos o 'pow' para uma transição agressiva nas letras
    float blendAI = pow(wSharp, 0.6);
    finalColor = lerp(finalColor, layerAI, blendAI);


    // --- 4. RAZOR (RESTAURADO) ---
    
    // SmartClamp (Impedir Halos, mas com folga de 0.05 para a AI brilhar)
    float3 minC = min(c00, min(min(cN, cS), min(cW, cE)));
    float3 maxC = max(c00, max(max(cN, cS), max(cW, cE)));
    finalColor = SmartClamp(finalColor, minC, maxC);

    // Vibrance (Vida às cores)
    finalColor = ApplyVibrance(finalColor, 1.15);

    // Split Screen
    if (input.uv.x < 0.5) return float4(c00, 1.0);
    if (input.uv.x < 0.502) return float4(1, 0, 0, 1);

    return float4(finalColor, 1.0);
}