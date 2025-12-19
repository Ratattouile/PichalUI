import cv2
import numpy as np
import os
from scipy.linalg import lstsq

# =========================================================
# CONFIGURAÇÃO "GOLD MASTER"
# =========================================================
# Caminho validado pelo diagnóstico
IMAGE_PATH = r"C:\ConsoleUI_Prototipe\ConsoleUI_WPF\TreinoIA\image.png"

# FORÇAR 5x5 (Fundamental para não crashar o Shader!)
PATCH_SIZE = 5      
Q_ANGLE = 24         
Q_STRENGTH = 3       
TOTAL_BUCKETS = Q_ANGLE * Q_STRENGTH

print(f"=== MNEMOS TRAINER vFINAL ===")
print(f"[CONFIG] Patch: {PATCH_SIZE}x{PATCH_SIZE} | Buckets: {TOTAL_BUCKETS}")
print(f"[TARGET] {IMAGE_PATH}")

# Matrizes de Acumulação
Q_A = [np.zeros((PATCH_SIZE**2, PATCH_SIZE**2)) for _ in range(TOTAL_BUCKETS)]
Q_b = [np.zeros((PATCH_SIZE**2, 1)) for _ in range(TOTAL_BUCKETS)]

def get_hash(gx_val, gy_val):
    # Calcular Ângulo
    angle = np.arctan2(gy_val, gx_val)
    if angle < 0: angle += np.pi
    
    # Calcular Força
    strength = np.sqrt(gx_val**2 + gy_val**2)
    
    # Hashing (Converter para índice)
    angle_idx = int((angle / np.pi) * Q_ANGLE) % Q_ANGLE
    strength_idx = min(int(strength), Q_STRENGTH - 1)
    
    # Índice Final
    # Usamos apenas 2 fatores para simplificar
    hash_idx = angle_idx + (strength_idx * Q_ANGLE)
    
    # Proteção extra contra indices inválidos
    return max(0, min(hash_idx, TOTAL_BUCKETS - 1))

# --- CARREGAMENTO ---
if not os.path.exists(IMAGE_PATH):
    print("ERRO: Imagem desapareceu!")
    exit()

hr = cv2.imread(IMAGE_PATH)
h, w, _ = hr.shape
print(f"[OK] Imagem carregada: {w}x{h}")

# Criar Low-Res e Upscaled
lr = cv2.resize(hr, (w//2, h//2), interpolation=cv2.INTER_LINEAR)
lr_upscaled = cv2.resize(lr, (w, h), interpolation=cv2.INTER_LINEAR)
lr_gray = cv2.cvtColor(lr_upscaled, cv2.COLOR_BGR2GRAY)

# Gradientes
print(" -> A calcular gradientes...")
grad_x = cv2.Sobel(lr_gray, cv2.CV_64F, 1, 0, ksize=3)
grad_y = cv2.Sobel(lr_gray, cv2.CV_64F, 0, 1, ksize=3)

# --- LOOP DE TREINO (SEM TRY-EXCEPT) ---
margin = PATCH_SIZE // 2
count = 0
print(" -> A treinar IA (Aguarde)...")

# Passo de 4 em 4 pixeis para ser rápido
for y in range(margin + 2, h - margin - 2, 4): 
    for x in range(margin + 2, w - margin - 2, 4):
        
        # 1. Obter Hash
        gx = grad_x[y, x]
        gy = grad_y[y, x]
        bucket_id = get_hash(gx, gy)
        
        # 2. Extrair Patch
        patch = lr_upscaled[y-margin:y+margin+1, x-margin:x+margin+1]
        
        # Validação de Segurança
        if patch.shape != (PATCH_SIZE, PATCH_SIZE, 3):
            continue

        # 3. Matemática
        patch_vector = patch.reshape(-1, 3)
        # Converter RGB para Luma (1 canal)
        patch_luma = np.dot(patch_vector, [0.299, 0.587, 0.114]).reshape(-1, 1)

        pixel_hr = hr[y, x]
        pixel_luma = np.dot(pixel_hr, [0.299, 0.587, 0.114])

        # 4. Acumular
        Q_A[bucket_id] += np.dot(patch_luma, patch_luma.T)
        Q_b[bucket_id] += np.dot(patch_luma, np.array([[pixel_luma]]))
        count += 1

print(f"[OK] Processados {count} pixeis.")

if count == 0:
    print("ERRO: Zero pixeis processados. Algo muito estranho passa-se.")
    exit()

# --- RESOLUÇÃO E EXPORTAÇÃO INTELIGENTE ---
print("\n[MNEMOS] A resolver equações e a tapar buracos...")

hlsl_code = ""
hlsl_code += f"// MNEMOS FILTER BANK (Hybrid: AI + Fallback)\n"
hlsl_code += f"// Total Filters: 24 | Size: 25 floats (5x5)\n"
hlsl_code += f"static const float mnemos_weights[24][25] = {{\n"

# Filtro de Recurso (Um Sharpen 5x5 Simples para quando a IA falha)
# Este filtro garante que nunca vês preto, vês sempre nitidez.
fallback_filter = np.array([
    -0.01, -0.02, -0.04, -0.02, -0.01,
    -0.02, -0.05, -0.08, -0.05, -0.02,
    -0.04, -0.08,  1.80, -0.08, -0.04,
    -0.02, -0.05, -0.08, -0.05, -0.02,
    -0.01, -0.02, -0.04, -0.02, -0.01
])

filters_generated = 0
filters_patched = 0

for i in range(Q_ANGLE):
    # Tenta buscar o filtro de Força Média (Index 1)
    bucket_id = i + (1 * Q_ANGLE)
    if bucket_id >= TOTAL_BUCKETS: bucket_id = 0
    
    A = Q_A[bucket_id] + np.eye(PATCH_SIZE**2) * 0.1 
    b = Q_b[bucket_id]
    
    try:
        weights = lstsq(A, b)[0].flatten()
    except:
        weights = np.zeros(PATCH_SIZE**2)

    # VERIFICAÇÃO DE VAZIO:
    # Se a soma dos pesos for quase zero, a IA não aprendeu nada aqui.
    # Usamos o Fallback!
    if np.sum(np.abs(weights)) < 0.1:
        weights = fallback_filter
        filters_patched += 1
    else:
        filters_generated += 1
    
    # Formatar para HLSL
    weight_str = ", ".join([f"{w:.5f}" for w in weights])
    hlsl_code += f"    {{ {weight_str} }},\n"

hlsl_code += "};\n"

print("="*60)
print(hlsl_code)
print("="*60)
print(f"RELATÓRIO FINAL:")
print(f"   - Filtros aprendidos pela IA: {filters_generated}")
print(f"   - Filtros preenchidos (Fallback): {filters_patched}")
print(f"   - Total: 24")
print("Copia o código acima para o HLSL e o jogo vai ficar PERFEITO!")