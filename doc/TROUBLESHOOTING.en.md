# DictaMeeting 🎙 — Troubleshooting Guide & FAQ

**[🇬🇧 English](TROUBLESHOOTING.en.md) | [🇪🇸 Español](TROUBLESHOOTING.md)**

This guide gathers common questions and step-by-step solutions for technical issues encountered when using **DictaMeeting**.

---

## 🎧 1. Audio Capture Issues

### The microphone VU meter does not move or my voice is not transcribed
1. **Check Windows Microphone Privacy Permissions**:
   - Open *Windows Settings > Privacy & Security > Microphone*.
   - Ensure both **"Microphone access"** and **"Let desktop apps access your microphone"** are toggled **On**.
2. **Device Muted in Windows**:
   - Check if your microphone has a physical hardware mute switch or inline cable button.
   - In *Settings > System > Sound*, verify that the input volume slider is set to 70–100%.
3. **Correct Device Selection**:
   - In DictaMeeting's sidebar, ensure the **Microphone** dropdown has your active recording hardware selected rather than an inactive virtual device.

---

### Remote participants in Teams, Zoom, or Meet are not heard or transcribed
1. **Verify Selected Audio Output Device**:
   - DictaMeeting captures system audio using the device specified under **Output Device / System Audio**.
   - If Microsoft Teams or Zoom is routing audio through your USB headset (e.g. *"Jabra Evolve"*), but DictaMeeting is set to *"Realtek High Definition Audio"*, DictaMeeting cannot capture meeting sound.
   - **Solution**: Select the exact same playback device in DictaMeeting through which you hear other callers.
2. **Recording Mode**:
   - Ensure the selected recording mode is **Video Conference** (both channels active) or **System Only**.

---

## ⚡ 2. Performance & CPU Utilization

### CPU usage is high during meetings
1. **Use the Lightweight Live Model**:
   - Open *Settings > Models* and ensure **Qwen3-ASR 0.6B INT8** is selected.
   - The `1.7B` model provides enhanced technical accuracy but requires a higher-end multi-core CPU.
2. **Throttle or Pause Local Summarization**:
   - On quad-core or entry-level CPUs, the embedded local LLM (`llama.cpp`) may compete for clock cycles. You can pause live summaries or increase the interval in Settings.
3. **Offline Post-Processing Model**:
   - For post-meeting processing, select **Whisper Small** or **Whisper Base** instead of *Medium* or *Large-v3 Turbo*.

---

## 🛡 3. Security, Antivirus, and SmartScreen

### Windows SmartScreen displays "Windows protected your PC"
- **Cause**: As open-source software distributed without an expensive corporate EV (Extended Validation) code-signing certificate, Windows SmartScreen may present an initial caution prompt.
- **Solution**: Click **"More info"** and then **"Run anyway"**.
- DictaMeeting is 100% open-source and auditable: all source code, build scripts, and test suites are publicly accessible in the repository.

### Where are my API keys and secrets stored?
- OpenRouter API keys are encrypted at rest via the native **Windows DPAPI** (`ProtectedData.Protect` scoped to `CurrentUser`).
- Credentials are never stored in plaintext and never leave your workstation.

---

## 🤖 4. AI Meeting Minutes Generation

### Error 401 (Unauthorized) when generating minutes
- **Cause**: The entered OpenRouter API key is invalid or revoked.
- **Solution**: Open *Settings > Artificial Intelligence*, clear the key, and paste a valid key formatted as `sk-or-v1-...`.

### Error 402 (Payment Required)
- **Cause**: Your account balance on OpenRouter is depleted.
- **Solution**: Top up credits in your account dashboard at [OpenRouter.ai](https://openrouter.ai).

### Connection failure to a custom local server (Ollama / LM Studio / LiteLLM)
1. Verify the local inference service is running (e.g. run `ollama list` in PowerShell).
2. Ensure the endpoint includes the `http://` scheme and `/v1` path:
   - Ollama: `http://localhost:11434/v1`
   - LM Studio: `http://localhost:1234/v1`
   - LiteLLM: `http://localhost:4000/v1`
3. Verify that the model name configured in DictaMeeting matches the model installed locally (e.g. `llama3.2`, `qwen2.5:7b`).

---

## 💾 5. Storage and Disk Management

### My C: drive is running low on disk space
- Neural model weights consume between 1 GB and 4 GB of storage.
- Open **Settings > Models** and click **"Change Models Folder"**.
- Pick a directory on an alternate volume (e.g. `D:\DictaMeeting\models`).
- The application will safely migrate all downloaded files.

### Where are meeting recordings and transcripts saved?
- By default, files are saved in the user-configured meetings folder (or `%LocalAppData%\DictaMeeting\Meetings\`).
- You can access any meeting's folder instantly by clicking the **[ 📁 Open Folder ]** button on the main screen or in the History view.
