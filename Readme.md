[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)

# Ech👂 Voice Typing Assistant


https://github.com/user-attachments/assets/bf917071-653f-4bbb-a704-57d1402ab8eb


Echo is a blazing-fast, privacy-first, push-to-talk voice typing assistant for Windows and Linux. It runs locally on your machine, leveraging the power of **OpenAI's Whisper** to transcribe your speech into text and automatically insert it wherever your text cursor is active. Nothing you say leaves your computer.

Hold a key, speak, release: the text appears in the window you are working in. Echo lives in your terminal as the `echooo` command (plain `echo` is already taken by every shell).

**It also features seamless auto-translation to English:** speak in your native language (Russian, Spanish, German, etc.), and Echo will instantly translate it to English. This makes it an ideal tool for bilingual workflows, coding, and writing documentation!

It features a "Hot Mic" architecture for zero-latency recording and Voice Activity Detection (VAD) to ensure transcriptions without cutting off your first words.

---

## 🚀 Installation

One line in a terminal, no administrator rights needed. Echo runs on 64-bit Windows and on 64-bit Linux (Intel or AMD processors).

**Windows** (PowerShell):

```powershell
irm https://githubphobos.github.io/Echo/install.ps1 | iex
```

**Linux:**

```sh
curl -fsSL https://githubphobos.github.io/Echo/install.sh | sh
```

A fresh Ubuntu desktop has no `curl`, only `wget`. Use this line there:

```sh
wget -qO- https://githubphobos.github.io/Echo/install.sh | sh
```

Then start Echo:

```sh
echooo
```

Where Echo goes:

| | Windows | Linux |
| --- | --- | --- |
| Program | `%LOCALAPPDATA%\Programs\echooo` | `~/.local/share/echooo` |
| Command | added to your PATH by the script | `~/.local/bin/echooo` |

On Linux, if `~/.local/bin` is not on your PATH yet, the script says which line to add to `~/.profile`. Ubuntu adds it by itself at the next login.

### Installing a specific version

The same line with a version number installs that version. This is also the way back to an earlier version:

```sh
curl -fsSL https://githubphobos.github.io/Echo/install.sh | sh -s 2.0.1
```

```powershell
& ([scriptblock]::Create((irm https://githubphobos.github.io/Echo/install.ps1))) 2.0.1
```

All versions are listed on the [Releases](https://github.com/GithubPhobos/Echo/releases) page.

---

## 🧭 First Start

The first `echooo` opens the setup wizard. It takes a few minutes and asks about everything Echo needs:

1. **Languages:** the language of the interface (English, Русский, 中文, Español, Deutsch) and the language you dictate in.
2. **Hardware acceleration:** NVIDIA (CUDA), any graphics card (Vulkan), or the processor. The wizard checks what your computer has and downloads the NVIDIA libraries for CUDA if you choose it.
3. **Model:** the wizard suggests a Whisper model for your hardware and downloads it. You can also use a model file you already have.
4. **Hotkey:** the push-to-talk key (`` ` `` by default).
5. **Sound:** the microphone, the silence level and a test dictation.
6. **Additional settings:** auto-insertion, the recording sound, translation into English, updates, and more.

On Linux the wizard also checks the system packages Echo needs (such as `libgomp1` and `libpulse0`) and offers to install the missing ones with `sudo`.

---

## 🎙️ Using Echo

1. Hold down your push-to-talk key, speak, and release the key.
2. The text is typed into the active window right under your cursor. With auto-insertion off, it is only copied to the clipboard and you paste it with Ctrl+V.

While Echo runs, the terminal shows its working screen: what Echo is doing, a sound meter while you speak, the log, and a command line. Type `/` to see the commands:

| Command | What it does |
| --- | --- |
| `/setup` | Change the settings in the setup wizard |
| `/info` | Show the main settings, the version and where Echo keeps its files |
| `/copy` | Copy the last recognized text again |
| `/pause`, `/resume` | Stop and start listening to the key |
| `/log` | Change how detailed the log is |
| `/update` | Download the new version of Echo |
| `/restart` | Restart Echo, in the new version if it is downloaded |
| `/help` | List the commands |
| `/quit` | Quit Echo (Ctrl+C does the same) |

### Linux desktops

* **Wayland** (for example GNOME): the desktop gives Echo its key. The first time, the desktop asks you to allow it. Typing the text into other windows also needs the desktop's permission; without it, Echo copies the text to the clipboard.
* **X11** (Xfce, MATE and others): Echo takes the key and types the text itself.

---

## 🔄 Updates

Echo checks for a new version at every start and once a day. The top line of the working screen always shows the version you have:

| Top line | Meaning |
| --- | --- |
| `√ 2.0.1` | You have the latest version |
| `2.0.1 (↑)` | A new version is out: type `/update` |
| `2.0.1 (⠹ 38%)` | The new version is downloading |
| `2.0.1 → 2.0.2 (/restart)` | Downloaded: `/restart` switches to it now, otherwise it starts next time |

Updates download only when you type `/update`, unless you turn on **Download updates automatically** in the additional settings. Echo updates itself only when it was installed with the script above.

---

## 🧠 How to Choose the Right Model

Echo uses Whisper GGML models. The model you choose determines the speed and accuracy of your dictation. The setup wizard suggests one for your hardware.

**English vs. Multilingual:**
* If you **only dictate in English**, use models with the `.en` suffix (e.g., `small.en`). They are faster, more accurate, and hallucinate much less.
* If you dictate in **other languages** (or mix them), use the standard models (e.g., `small`). *Note: Large models do not have `.en` versions as they are inherently multilingual.*
* Translation into English needs a multilingual model. The `large-v3-turbo` model cannot translate.

### Model Hardware Requirements (My advice is to try multiple models)

| Model Size | Approx. VRAM / RAM | Speed | Accuracy | Hardware Recommendation |
| :--- | :--- | :--- | :--- | :--- |
| **Tiny** | ~500 MB | Blazing Fast | Basic | Potato PCs, quick testing. |
| **Base** | ~1 GB | Very Fast | Acceptable | Older laptops or CPU-only inference. |
| **Small** | ~2 GB | Fast | Good | **Recommended.** Best balance for most modern PCs. |
| **Medium** | ~3.5 GB | Moderate | Very Good | Dedicated GPUs (e.g., GTX 1060 or better). |
| **Large** | ~6 GB+ | Slow | Excellent | Modern GPUs (RTX 3060+). Perfect for complex jargon. |

* The better your system is, the better results you will get. I am getting 300-400ms on Large model on my RTX5070Ti.
* Tiny and Base make many more mistakes, so the wizard marks them as not recommended.

---

## 📊 Average Benchmarks

Performance depends on your hardware and the chosen model. Below are average inference times for my standard session:

| Hardware | Model | Average Inference Time (very short session: 10-20 sec) |
| --- | --- | --- |
| **CPU** | `ggml-small` | ~500 ms |
|  | `ggml-base` | ~1 sec |
|  | `ggml-medium` | ~4 sec |
| **CUDA** (tested on RTX 5070 Ti) | `ggml-base` | ~400 ms |
|  | `ggml-small` | ~500 ms |
|  | `ggml-medium` | ~600 ms |
|  | `ggml-large-v3-turbo` | ~500 ms |

---

## 📁 Where Echo Keeps Its Files

Settings, models and logs live in your user folders, so updates never touch them. `/info` shows where the settings and the logs are.

| | Windows | Linux |
| --- | --- | --- |
| Settings | `%APPDATA%\echooo\settings.json` | `~/.config/echooo/settings.json` |
| Models | `%LOCALAPPDATA%\echooo\models` | `~/.local/share/echooo/models` |
| Logs | `%APPDATA%\echooo\logs` | `~/.local/state/echooo/logs` |

The `ECHOOO_HOME` environment variable moves all of them into one folder.

---

## 🗑️ Uninstalling

**Windows:** delete `%LOCALAPPDATA%\Programs\echooo`, and remove `%LOCALAPPDATA%\Programs\echooo\current` from your user PATH (Settings → System → About → Advanced system settings → Environment Variables). To remove the settings and models too, delete `%APPDATA%\echooo` and `%LOCALAPPDATA%\echooo`.

**Linux:**

```sh
rm -rf ~/.local/share/echooo ~/.local/bin/echooo ~/.config/echooo ~/.local/state/echooo
```

---

## 🛠️ Building from Source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```sh
git clone https://github.com/GithubPhobos/Echo
cd Echo
dotnet run --project Echo
```

A self-contained build: `dotnet publish Echo/Echo.csproj -c Release -r win-x64 --self-contained` (or `-r linux-x64`). A build made this way does not update itself; install Echo with the script to get updates.

---

## 🛠️ Troubleshooting

**Issue:** `curl: command not found` when installing on Linux.
* **Fix:** Use the `wget` line from [Installation](#-installation).

**Issue:** `Couldn't find recording devices in the system, shutting down...`
* **Cause:** Windows privacy settings are blocking access to your microphone, or no microphone is plugged in.
* **Fix:** Go to Windows Settings -> Privacy & security -> Microphone. Ensure "Let desktop apps access your microphone" is turned ON.

**Issue:** The application types out random words, hallucinations, or just the prompt instead of what you said.
* **Cause:** Your microphone is too quiet, and the Voice Activity Detection (VAD) cut off your speech, sending a silent audio file to the AI. The AI hallucinated based on the context prompt.
* **Fix:** Measure the silence level again: `/setup`, step **Sound**. You can also set it by hand in the additional settings (e.g., `0.01` or `0.005`).

**Issue:** The AI transcribes keyboard clicks, mechanical sounds, or the app's own "beep" as random words.
* **Cause:** Your microphone is picking up the physical sound of your push-to-talk keystroke or the application's audio feedback. The VAD registers this sharp noise as speech.
* **Fix:** Lower the recording sound volume in the additional settings (e.g., to 5%) and slightly raise the silence level (e.g., to `0.02`). Positioning your microphone further away from the keyboard also helps.

**Issue:** On Linux (Wayland) the key does nothing.
* **Cause:** The desktop has not given Echo the key, or another program uses the same one.
* **Fix:** Run `/setup` and choose the key again; allow it when the desktop asks. On GNOME, logging out and in again helps if the desktop's portal service was restarted.
