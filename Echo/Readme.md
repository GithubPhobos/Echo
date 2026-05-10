# Ech🦻 Voice Assistant

Echo is a blazing-fast, privacy-first, push-to-talk voice assistant. It runs locally on your machine, leveraging the power of **OpenAI's Whisper** to transcribe your speech into text and automatically insert it wherever your text cursor is active.
It features a "Hot Mic" architecture for zero-latency recording and advanced Voice Activity Detection (VAD) to ensure perfect transcriptions without cutting off your first words.

---

## 📊 Average Benchmarks

Performance depends on your hardware and the chosen model. Below are average inference times for my standard 10-20 second speaking session:

| Hardware | Model | Average Inference Time |
| --- | --- | --- |
| **CPU** (Fallback) | `ggml-small` | ~500 ms |
|  | `ggml-base` | ~1 sec |
|  | `ggml-medium` | ~4 sec |
| **CUDA** (RTX 3070 Ti) | `ggml-base` | ~400 ms |
|  | `ggml-small` | ~500 ms |
|  | `ggml-medium` | ~600 ms |
|  | `ggml-large-v3-turbo` | ~500 ms |

---

## ⚙️ Prerequisites

Before running the application, ensure you have:

1. A working **Microphone**.
2. **Whisper Model**: A compatible `.bin` Whisper model file (e.g., `ggml-base.en.bin` or `ggml-medium.bin`). You can download them from HuggingFace: [ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp).

---

## 🚀 Getting Started

### 1. Installation

**Option A: Download Pre-built Release (Recommended)**

1. Go to the **Releases** page of this repository.
2. Download the latest `Echo-win-x64.zip` file.
3. Extract the folder to your preferred location.

**Option B: Build from Source**

1. Clone the repository: `git clone https://github.com/yourusername/Echo.git`
2. Navigate to the folder: `cd Echo`
3. Build the project: `dotnet publish -c Release -r win-x64 --self-contained true`

### 2. Assets Configuration

1. Ensure there is an **`Assets`** folder in the root directory alongside the `Echo.exe` executable. 
That folder contains `start-recording.wav` and `stop-recording.wav` for audible push-to-talk feedback.
2. Place your downloaded Whisper model file (e.g., `ggml-medium.bin`) into the `Assets` folder.
3. Open `appsettings.json` to customize the application (all settings are documented inline). Key settings include:
* `WhisperSettings.ModelName`: **Must match** the exact name of the model you placed in the Assets folder.
* `PushToTalkSettings.Key`: The global hotkey to trigger recording (Default is ```).
* `Serilog.MinimumLevel.Default`: Available log levels are `Debug`, `Information`, `Warning`, `Error`.


### 3. Hardware Acceleration Setup 🚀

For maximum speed, configure the `HardwareBackend` in `appsettings.json` based on your system:

* **NVIDIA (CUDA) - Maximum Speed (Only if you have an NVIDIA graphics card)**
1. Ensure your NVIDIA graphics drivers are up to date.
2. Download the required CUDA redistributable libraries from the [NVIDIA Developer Archive](https://developer.download.nvidia.com/compute/cuda/redist/). 
You will need files from `cuda_cudart` and `libcublas`.
3. Extract and place the following specific `.dll` files next to `Echo.exe`:
* `cublas64_13.dll`
* `cublasLt64_13.dll`
* `cudart64_13.dll`


* **AMD / Intel / Basic NVIDIA (Vulkan)**
* Works with AMD Adrenalin, Intel Arc Graphics, or standard NVIDIA drivers.
* You don't need to install anything, because the required `vulkan-1.dll` is automatically installed by Windows with your GPU drivers.
* Set `"HardwareBackend": "Vulkan"` in `appsettings.json`.


* **CPU Only**
* Set `"HardwareBackend": "CPU"`. No extra steps required.



### 4. Running the App

1. Launch `Echo.exe`.
2. A console window will appear, initializing all the required settings.
3. Follow the logs—they are designed to be extremely readable so you can instantly tell if something goes wrong.
4. Hold down your designated Push-To-Talk key, speak your thoughts, and release the key.
5. The transcribed text will automatically be saved to your clipboard and typed into your active window right under your cursor if you have enabled 'UseAutoInsert'.

---

## 🛠️ Troubleshooting

**Issue:** `Couldn't find recording devices in the system, shutting down...`
* **Cause:** Windows privacy settings are blocking access to your microphone, or no microphone is plugged in.
* **Fix:** Go to Windows Settings -> Privacy & security -> Microphone. Ensure "Let desktop apps access your microphone" is turned ON.

**Issue:** `BadDeviceId calling waveOutOpen` or application crashes on startup.
* **Cause:** You have audio feedback enabled (`"PlaySound": true`), but your system currently has no active audio output devices (speakers/headphones).
* **Fix:** Connect a speaker/headphone or set `"PlaySound": false` in `appsettings.json`.

**Issue:** The application types out random words, hallucinations, or just the prompt instead of what you said.
* **Cause:** Your microphone is too quiet, and the Voice Activity Detection (VAD) cut off your speech, sending a silent audio file to the AI. The AI hallucinated based on the default context prompt.
* **Fix:** Lower the `"SilenceThreshold"` in `appsettings.json` (e.g., from `0.03` to `0.01` or `0.005`). You can also enable `"OutputMicAmplitudeDebugInfo": true` to see your actual mic levels in the console.

**Issue:** The AI transcribes keyboard clicks, mechanical sounds, or the app's own "beep" as random words.
* **Cause:** Your microphone is picking up the physical sound of your Push-To-Talk keystroke or the application's audio feedback. The VAD registers this sharp noise as speech.
* **Fix:** Lower the application's audio feedback `"Volume"` in `appsettings.json` (e.g., to `0.05`) and slightly increase the `"SilenceThreshold"` (e.g., to `0.02`) so the VAD ignores these background noises. Positioning your microphone further away from the keyboard also helps.