import numpy as np
import cv2
import glob
import os
import random
import time

# Tenta importar CuPy
try:
    import cupy as cp
    print(f"SUCESSO: GPU detetada! Dispositivo: {cp.cuda.runtime.getDeviceProperties(0)['name'].decode('utf-8')}")
except Exception as e:
    print("ERRO FATAL: CuPy não instalado. Corre 'pip install cupy-cuda11x'")
    exit()

# --- CONFIGURAÇÕES ---
TRAIN_IMAGES_DIR = "C:/ConsoleUI_Prototipe/ConsoleUI_WPF/PichalUI/TreinoIA"
OUTPUT_FILE = "RAISR_LUT.bin"

# Definições
Q_ANGLE = 24
KERNEL_SIZE = 5
FILTER_DIM = KERNEL_SIZE * KERNEL_SIZE
NUM_BUCKETS = Q_ANGLE
PAD = KERNEL_SIZE // 2

# Otimização de Velocidade (O Segredo)
# Step 2 = 4x mais rápido, perda de qualidade imperceptível com 800 imagens
STEP = 2 

# Matrizes na VRAM
Q_gpu = cp.zeros((NUM_BUCKETS, FILTER_DIM, FILTER_DIM), dtype=cp.float64)
V_gpu = cp.zeros((NUM_BUCKETS, FILTER_DIM), dtype=cp.float64)
Counts_gpu = cp.zeros(NUM_BUCKETS, dtype=cp.int32)

def add_degradation_cpu(image):
    # Simula TAA (Blur 0.8) + Leve Grão (0.01)
    blurred = cv2.GaussianBlur(image, (3, 3), 0.8)
    row, col = image.shape
    gauss = np.random.normal(0, 0.01, (row, col))
    return np.clip(blurred + gauss, 0, 1)

def im2col_gpu_optimized(img_gpu, block_size, step):
    """Extrai patches de forma eficiente com stride"""
    h, w = img_gpu.shape
    out_h = (h - block_size) // step + 1
    out_w = (w - block_size) // step + 1
    
    shape = (out_h, out_w, block_size, block_size)
    strides = (img_gpu.strides[0] * step, img_gpu.strides[1] * step, img_gpu.strides[0], img_gpu.strides[1])
    
    windows = cp.lib.stride_tricks.as_strided(img_gpu, shape=shape, strides=strides)
    return windows.reshape(-1, block_size * block_size), out_h, out_w

def get_hash_batch_gpu(patches_3x3):
    c = patches_3x3[:, 4]
    n = patches_3x3[:, 1]
    w = patches_3x3[:, 3]
    
    gy = n - c
    gx = w - c
    
    angles = cp.arctan2(gy, gx)
    angles = cp.where(angles < 0, angles + cp.pi, angles)
    
    hash_indices = (angles / cp.pi * Q_ANGLE).astype(cp.int32) % Q_ANGLE
    
    # Noise Gate para estabilidade
    is_flat = (cp.abs(gx) < 0.02) & (cp.abs(gy) < 0.02)
    hash_indices[is_flat] = 0
    return hash_indices

print(f"--- TREINO GPU OTIMIZADO (Step={STEP} | 8x Augmentation) ---")
images = glob.glob(os.path.join(TRAIN_IMAGES_DIR, "*.png"))
random.shuffle(images)

if not images:
    print("ERRO: Pasta vazia!")
    exit()

MAX_IMAGES = 800
start_time = time.time()

for i, img_path in enumerate(images):
    if i >= MAX_IMAGES: break
    
    # Carregar
    hr = cv2.imread(img_path, cv2.IMREAD_GRAYSCALE)
    if hr is None: continue
    
    # Cortar para garantir que as dimensões são pares (facilita o resize)
    h, w = hr.shape
    h -= h % 2; w -= w % 2
    hr = hr[:h, :w]
    
    # Criar pares
    lr = cv2.resize(hr, (w//2, h//2), interpolation=cv2.INTER_AREA)
    cheap = cv2.resize(lr, (w, h), interpolation=cv2.INTER_LINEAR)
    
    hr_f = hr.astype(np.float32) / 255.0
    cheap_f = cheap.astype(np.float32) / 255.0
    
    # --- 8x DATA AUGMENTATION ---
    base_pairs = [(hr_f, cheap_f)]
    
    # Rotações
    rotations = []
    for t_hr, t_cheap in base_pairs:
        rotations.append((np.rot90(t_hr, 1), np.rot90(t_cheap, 1)))
        rotations.append((np.rot90(t_hr, 2), np.rot90(t_cheap, 2)))
        rotations.append((np.rot90(t_hr, 3), np.rot90(t_cheap, 3)))
    
    transforms = base_pairs + rotations
    
    # Flips (Espelhos)
    flips = []
    for t_hr, t_cheap in transforms:
        flips.append((np.fliplr(t_hr), np.fliplr(t_cheap)))
        
    all_variations = transforms + flips
    
    # Processar na GPU
    for t_hr, t_cheap in all_variations:
        t_input = add_degradation_cpu(t_cheap)
        
        img_gpu = cp.asarray(t_input)
        target_gpu = cp.asarray(t_hr)
        
        # 1. Extrair Filtros (5x5) com Step
        patches_filter, out_h, out_w = im2col_gpu_optimized(img_gpu, 5, STEP)
        
        # 2. Extrair Hashes (3x3) - Alinhado com o centro do 5x5
        # Cortamos 1px a mais em cada lado para centralizar o 3x3 dentro do 5x5
        img_gpu_cropped = img_gpu[1:-1, 1:-1] 
        patches_hash, _, _ = im2col_gpu_optimized(img_gpu_cropped, 3, STEP)
        
        # 3. Extrair Targets (HR)
        # O centro do patch 5x5 está em (pad, pad). Com stride, saltamos STEP pixels.
        # Precisamos de garantir que o tamanho bate certo com o número de patches
        targets = target_gpu[PAD : PAD + out_h*STEP : STEP, PAD : PAD + out_w*STEP : STEP]
        targets = targets.reshape(-1)
        
        # Segurança de dimensões (corta o excesso se houver desvio de 1px)
        n_pixels = min(patches_filter.shape[0], patches_hash.shape[0], targets.shape[0])
        patches_filter = patches_filter[:n_pixels]
        patches_hash = patches_hash[:n_pixels]
        targets = targets[:n_pixels]
        
        # 4. Hash e Acumulação
        hashes = get_hash_batch_gpu(patches_hash)
        
        # Otimização de Loop: Evita criar 24 máscaras gigantes
        # Ordenamos os dados pelo hash para processar em blocos contíguos (Scatter/Gather)
        sort_idx = cp.argsort(hashes)
        sorted_hashes = hashes[sort_idx]
        sorted_patches = patches_filter[sort_idx]
        sorted_targets = targets[sort_idx]
        
        # Encontra onde cada balde começa e acaba
        unique_hashes, run_starts = cp.unique(sorted_hashes, return_index=True)
        
        # Loop apenas nos baldes existentes nesta imagem
        for idx, h_val in enumerate(unique_hashes):
            start = run_starts[idx]
            end = run_starts[idx+1] if idx+1 < len(run_starts) else len(sorted_hashes)
            
            P = sorted_patches[start:end]
            T = sorted_targets[start:end]
            
            Q_gpu[h_val] += cp.dot(P.T, P)
            V_gpu[h_val] += cp.dot(P.T, T)
            Counts_gpu[h_val] += (end - start)

    elapsed = time.time() - start_time
    avg_per_img = elapsed / (i + 1)
    remaining = (MAX_IMAGES - (i + 1)) * avg_per_img
    print(f"Treino Turbo: {i+1}/{len(images)} - Restam ~{remaining/60:.1f} min", end='\r')

print("\n--- A CALCULAR FILTROS FINAIS ---")
Q = cp.asnumpy(Q_gpu)
V = cp.asnumpy(V_gpu)
Counts = cp.asnumpy(Counts_gpu)
filters = np.zeros((NUM_BUCKETS, FILTER_DIM), dtype=np.float32)

for i in range(NUM_BUCKETS):
    print(f"Balde {i}: {Counts[i]} amostras", end=" ")
    try:
        # Se tiver menos de 2000 amostras (mesmo com 8x), usa filtro seguro
        if Counts[i] < 2000:
            print("[SAFE]")
            f = np.zeros(FILTER_DIM, dtype=np.float32)
            c = FILTER_DIM // 2
            f[c] = 1.3
            f[c-1] = -0.075; f[c+1] = -0.075
            f[c-5] = -0.075; f[c+5] = -0.075
            filters[i] = f
        else:
            # Regularização 0.25 (Equilíbrio ideal nitidez/ruído)
            reg = np.eye(FILTER_DIM) * 0.25
            filters[i] = np.linalg.solve(Q[i] + reg, V[i])
            print("[OK]")
    except:
        print("[ERRO - SAFE]")
        filters[i][FILTER_DIM//2] = 1.0

with open(OUTPUT_FILE, "wb") as f:
    f.write(filters.tobytes())

print(f"\nCONCLUÍDO! Ficheiro .bin gerado em {(time.time() - start_time)/60:.1f} minutos.")