[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)

# Ech👂 Voice Typing Assistant


https://github.com/user-attachments/assets/2b4b0ff9-55df-4087-9850-7fcc9ed0f6ea


Echo is a blazing-fast, privacy-first, push-to-talk voice typing assistant for Windows and Linux. It runs locally on your machine, leveraging the power of **local speech recognition models** to transcribe your speech into text and automatically insert it wherever your text cursor is active.

Hold a key, speak, release: the text appears in the window you are working in. Echo lives in your terminal as the `echooo` command (plain `echo` is already taken by every shell 😢).

**It also features seamless auto-translation to English:** speak in your native language (Russian, Spanish, German, etc.), and Echo will translate it to English. This makes it an ideal tool for bilingual workflows, coding, and writing documentation!

---

## 🚀 Installation

Echo runs on 64-bit Windows and on 64-bit Linux (Intel or AMD processors).

**Windows** (PowerShell):

```powershell
irm https://githubphobos.github.io/Echo/install.ps1 | iex
```

**Linux:**

```sh
curl -fsSL https://githubphobos.github.io/Echo/install.sh | sh
```

Alternatively, use `wget`:

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

---

## 🧭 First Start

The first `echooo` opens an interactive setup wizard that guides you through the settings:

1. **Languages:** the language of the interface and the language you dictate in.
2. **Hardware acceleration:** NVIDIA (CUDA), a compatible graphics card (Vulkan), or the processor.
3. **Model:** the wizard suggests a Whisper model for your hardware and downloads it. You can also use a model file you already have.
4. **Hotkey:** the push-to-talk key (`` ` `` by default).
5. **Sound:** the microphone, the silence level and a test dictation.
6. **Additional settings:** auto-insertion, translation into English, updates, and more.

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
| `/restart` | Restart Echo |
| `/help` | List the commands |
| `/quit` | Quit Echo (Ctrl+C does the same) |

### Linux desktops

* **Wayland** (for example GNOME): the desktop gives Echo its key. The first time, the desktop asks you to allow it. Typing the text into other windows also needs the desktop's permission; without it, Echo copies the text to the clipboard.
* **X11** (Xfce, MATE and others): Echo takes the key and types the text itself.

---

## 🧠 How to Choose the Right Model

Echo uses Whisper GGML models. The model you choose determines the speed and accuracy of your dictation. The setup wizard suggests one for your hardware.

### Model Memory Usage

| Model | Reference memory usage |
| --- | ---: |
| Tiny | ~273 MB |
| Base | ~388 MB |
| Small | ~852 MB |
| Medium | ~2.1 GB |
| Large | ~3.9 GB |

These are [whisper.cpp reference figures](https://github.com/ggml-org/whisper.cpp#memory-usage), not minimum VRAM requirements. Actual memory usage depends on the backend and configuration.

Try several models to find the balance of recognition quality and responsiveness that works for your hardware.

Recognition time depends on your hardware, model, and audio length. Echo shows the recognition time next to each result.

* Tiny and Base make many more mistakes, so the wizard marks them as not recommended.

**English vs. Multilingual:**
* If you **only dictate in English**, use models with the `.en` suffix (e.g., `small.en`). They are faster, more accurate, and hallucinate much less.
* If you dictate in **other languages** (or mix them), use the standard models (e.g., `small`). *Note: Large models do not have `.en` versions as they are inherently multilingual.*
* Translation into English needs a multilingual model. The `large-v3-turbo` model cannot translate.

---

<details>
<summary>Uninstalling</summary>

**Windows:** delete `%LOCALAPPDATA%\Programs\echooo`, and remove `%LOCALAPPDATA%\Programs\echooo\current` from your user PATH (Settings → System → About → Advanced system settings → Environment Variables). To remove the settings and models too, delete `%APPDATA%\echooo` and `%LOCALAPPDATA%\echooo`.

**Linux:**

```sh
rm -rf ~/.local/share/echooo ~/.local/bin/echooo ~/.config/echooo ~/.local/state/echooo
```

</details>

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
* **Fix:** Use the `wget` command from [Installation](#-installation).

**Issue:** `Couldn't find recording devices in the system, shutting down...`
* **Cause:** Windows privacy settings are blocking access to your microphone, or no microphone is plugged in.
* **Fix:** Go to Windows Settings -> Privacy & security -> Microphone. Ensure "Let desktop apps access your microphone" is turned ON.

**Issue:** Echo outputs phrases you did not say, such as “Thanks for watching”.
* **Cause:** Whisper can generate invented text from silence, background noise, or unclear speech.
* **Fix:** Check the selected microphone and repeat silence calibration in `/setup`. If the beginning of your speech is missing, lower the silence threshold. Try a larger model if recognition remains unreliable.

**Issue:** The AI transcribes keyboard clicks, mechanical sounds, or the app's own "beep" as random words.
* **Cause:** Your microphone is picking up the physical sound of your push-to-talk keystroke or the application's audio feedback. The VAD registers this sharp noise as speech.
* **Fix:** Lower the recording sound volume in the additional settings (e.g., to 5%) and slightly raise the silence level (e.g., to `0.02`). Positioning your microphone further away from the keyboard also helps.

**Issue:** On Linux (Wayland) the key does nothing.
* **Cause:** The desktop has not given Echo the key, or another program uses the same one.
* **Fix:** Run `/setup` and choose the key again; allow it when the desktop asks. On GNOME, logging out and in again helps if the desktop's portal service was restarted.
