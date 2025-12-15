# PichalUI
PichalUI is a windows game launcher developed in WPF (.NET 9.0), designed to provide a console-like experience on your PC. Optimized for full control with a controller (Dualsense/XInput), it offers a fluid interface for managing your games library, friends, and system setting.

## Key Features
- **Unified Library**: View and launch all your installed games (Steam, Epic (not implemented yet) and much more to come) in a modern carousel.

- **Deep Steam Integration**:
    - Automatic login via local detection (no need to enter credentials if Steam is open).
    - Real-time friend synchronization.
    - Integrated chat (direct messaging).
    - Display of achievements and playtime.
    - Game news and updates.

- **Native DualSense Support**: Full navigation using the PlayStation 5 controller, with vibration and feedback support.

- **Hardware Monitoring**: View CPU, GPU, and RAM usage directly in the interface (via LibreHardwareMonitor).

- **Customization**: Various visual themes to choose included, and in the future you can make your own theme.

- **System Management**: Controls volume, Wi-Fi, and power options (Shutdown/Restard) without leaving the launcher.

- **Integrated Browser**: An light weigth integrated browser to use while not exiting the app.

- **User Selection**: You can put as many users as you want and you can easily change qhat profilw are you using.

- **UAAI (Upscaler de Auto-Ajustamento Indireto)**: The UAAI (Upscaler de Auto-Ajustamento Indereto) or Indirect Self-Adjusting Upscaler in english is the non-AI "inteligent" integrated Upscaler.

- **Future Features**: Some key features will be added in the future like:
    - **Epic Games and other Stores Integration**
    - **Windows Optimization**
    - **Native Discord Implementation**
    - **Native Spotify Implementation**
    - **And much more to come...** 



## System Minimum
- **Operating System**: Windows 10 or 11 (x64).
- **Runtime**: [.NET 9.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0).
- **Steam**: The Steam client must be installed and running in the background for the integration to fully works.

## How to Compile and Run
### 1. Clonning and Install Dependencies
Certify that you have .NET 9.0 SDK installed.
```bash
git clone https://github.com/Ratattouile/PichalUI.git
cd PichalUI
dotnet restore
```

### 2. Critical Configuration
For the Steam integration to work in dev mode (Debug), is necessary create a file in the folder where the executable runs.
1. Go to the folder `bin/Debug/net9.0-windows` (after the first compilation).
2. Create a file text named `steam_appid.txt`.
3. Write only the number `480` inside the file and save.
4. Certify that the file `steam_api64.dll` is also on this folder (normaly is pasted automatically by NuGet).

### 3. Run
To run the program eithout compiling a final exe file, you need to be on the folder with the `ConsoleUI_WPF.sln` file.Then rus this command:
```bash
dotnet run --project PichalUI
```
### 4. Compile
To compile and create a exe file optimized, you need to be also on the folder with the `ConsoleUI_WPF.sln` file and then run this command:
```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false -o ./publish
```

##  Gamepad Inputs
Button | Action
------ | ------
D-Pad / LeftStick | Navegation (Up, Down, Left, Rigth)
X (DualSense) / A (XInput) | Accept / Play / Enter 
O (DualSense) / B (XInput) | Back / Cancel
R1 (DualSense) / RB (XInput) | Change Tab (Right)
L1 (DualSense) / LB (XInput) | Change Tab (Left)
Option (Dualsense) / Start (XInput) | Rescan Library

## Used Technologies
- **C# / WPF**: Graphic interface and backend (for the C#).
- **Facepunch.Steamworks**: Native and light integration with Steam API.
- **DualSenseAPI**: PS5 controller native support.
- **LibreHardwareMonitor**: Hardware ...
- **System.Net.Http**: Comunication with Web API (fallback).
- **Steam Web API**

## UAAI - Upscaler de Auto-Ajustamento Indireto
**Current Version**: v5.0 "GEOMETRIC"

The **UAAI** is a proprietary, **non-AI upscaling engine** built entirely in **HLSL** (High-Level Shading Language) and **DirectX 11**. Unlike traditional upscalers that apply a single algorithm to the entire screen, UAAI uses a Hybrid Adaptive Architecture. It analyzes the local complexity of every pixel in real-time to decide the best reconstruction method, bridging the gap between performance and visual fidelity without the need for tensor cores or motion vectors.

### Core Architecture
The engine operates in three distinct stages per frame:

1. **CORTEX (Analysis Module)**
The "brain" of the shader. Before drawing a pixel, Cortex samples the 3x3 neighborhood to calculate Luma Variance and Covariance.
- **Variance Analysis**: Determines if a region is a flat gradient (e.g., sky, skin) or a high-frequency texture (e.g., foliage, text).
- **Covariance Analysis (NEDI)**: Analyzes diagonal correlations to detect the orientation of edges, allowing for geometric reconstruction of lines.

2. **FUSION (Synthesis Module)**
Based on Cortex's data, Fusion dynamically blends between four distinct resampling engines:
- **Safe Bilinear**: Used for low-variance areas to ensure smooth, noise-free gradients.
- **Bicubic (Catmull-Rom)**: Used for general image balancing.
- **Lanczos-2**: A mathematically precise filter using Sinc functions to preserve high-frequency details and sharpness in textures.
- **Real NEDI (New Edge-Directed Interpolation)**: The flagship feature of v5.0. It reconstructs geometry by interpolating along edges rather than across them, effectively eliminating "stair-stepping" (aliasing) on diagonal lines.

3. **RAZOR (Post-Processing)**
A modified RCAS (Robust Contrast Adaptive Sharpening) pass. Unlike standard sharpeners that create white halos, Razor is context-aware: it applies sharpening strength proportionally to the detail level detected by Cortex, leaving flat areas untouched to prevent grain.


### Quality Modes
Users can select the processing power allocated to the upscaler:

Mode | Engine Used | Description
---- | ----------- | -----------
Performance | Bilinear + Razor | "Minimal cost. Smooths out the image, ideal for low-end hardware."
Balanced | Bicubic Catmull-Rom | Standard upscaling with better sharpness than linear methods.
Quality | Adaptive Hybrid | Dynamically mixes Bilinear (for gradients) and Lanczos-2 (for edges) per pixel. Best visual balance.
Ultra | Geometric (NEDI),Uses covariance math to mathematically reconstruct edges | Best for 3D geometry and text clarity.


### Technical Workflow
```mermaid
graph TD
    Input[Input Frame] --> CORTEX{CORTEX Analysis}
    
    CORTEX -- "Flat Area (Low Variance)" --> BILINEAR[Safe Bilinear]
    CORTEX -- "Texture (High Variance)" --> LANCZOS[Lanczos-2]
    CORTEX -- "Edge Geometry (Covariance)" --> NEDI[Real NEDI]
    
    BILINEAR -. "Dynamic Blending" .- LANCZOS
    LANCZOS -. "Geometric Override" .- NEDI
    
    NEDI --> RAZOR[RAZOR (RCAS)]
    RAZOR --> Output[Final Display]
```

## Developed by 
Ratatouille (@Ratattouile), @joaomgleitao, @Echo4Cells
