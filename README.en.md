# DictaMeeting 🎙 — Local Meeting Transcription & Minutes

**[🇬🇧 English](README.en.md) | [🇪🇸 Español](README.md)**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20x64-blue.svg)](https://microsoft.com/windows)
[![UI: English | Español](https://img.shields.io/badge/UI-English%20%7C%20Espa%C3%B1ol-green.svg)](README.en.md)

**DictaMeeting** is a native Windows desktop application (C# / .NET 9 / WPF) designed to record, transcribe in real time, diarize, and summarize in-person meetings or video conferences (Microsoft Teams, Zoom, Google Meet, Slack, Webex) **100% locally, privately, and without requiring a dedicated graphics card (GPU)**.

Optionally, it generates structured executive meeting minutes with Artificial Intelligence using **OpenRouter AI** or any **OpenAI-compatible API server** (LiteLLM, Ollama, vLLM, or self-hosted endpoints), protecting all API keys and credentials using the **Windows Data Protection API (DPAPI)**.

---

## ✨ Key Features

- **Total Privacy & Local Processing**:
  - All audio processing, voice detection, continuous transcription, and diarization run entirely on your computer's CPU.
  - Your meeting audio is never shared with third parties or stored in the cloud.
- **Bilingual Interface (English / Español)**:
  - Full support for English and Spanish with dynamic runtime language switching without restarting the application.
  - **Automatic OS language detection** (Spanish Windows systems activate Spanish; any other language defaults to English) with persistent user preference storage.
- **Dual Simultaneous Audio Capture**:
  - Physical microphone (in-room participants).
  - System audio via loopback (*WASAPI Loopback Capture*) for remote participants in Teams, Zoom, Slack, or Meet.
  - Real-time VU peak meters and independent device selectors.
- **Two-Phase Transcription Pipeline**:
  - **LIVE Phase (Real-Time)**: Silero VAD v5 + Qwen3-ASR (0.6B INT8 ONNX via sherpa-onnx) with automatic punctuation restoration for lag-free reading.
  - **FINAL Phase (High-Fidelity)**: Full offline reprocessing with Whisper (GGML Tiny, Base, Small, Medium, Large-v3 Turbo) and acoustic diarization with PyAnnote Community-1 for precise speaker turns.
- **Interactive Audio-Text Synchronization**:
  - Integrated audio player that highlights the exact spoken phrase being heard and lets you seek to any point in the recording by clicking on the transcript.
- **Executive Summaries & Meeting Minutes**:
  - **Continuous Local Summary**: Live summary cards generated during the meeting using embedded local LLM models (Qwen 2.5 via LLamaSharp).
  - **AI Minutes Generation**: Complete executive minutes (summary, decisions, action items table, and follow-ups) via OpenRouter or OpenAI-compatible custom servers.
- **Smart Model Management**:
  - **First-Run Wizard**: Guided onboarding to download recommended baseline models on first launch.
  - **Configurable Model Location**: Store neural models in the default location (`%LocalAppData%\DictaMeeting\models`) or on secondary drives with an integrated safe migration tool.
- **Custom Vocabulary & Phonetic Engine**:
  - Custom dictionary to add technical terms, acronyms, or proper names to prevent phonetic hallucinations.
- **Complete History & Export**:
  - Real-time search across meetings by date, title, or spoken text.
  - One-click export to **Markdown (.md)**, **Plain Text (.txt)**, and **Structured JSON (.json)**.

---

## 🛠 System Requirements

- **Operating System**: Windows 10 (version 1809 or higher) or Windows 11 (64-bit).
- **Processor (CPU)**: Multi-core x64 CPU (Intel Core i5/i7/i9 8th Gen or higher, or AMD Ryzen 3000 series or higher). No dedicated GPU required.
- **RAM**: 8 GB minimum (16 GB recommended for medium final models and acoustic diarization).
- **Storage**: ~1.5 GB free disk space for recommended baseline models.
- **Development Environment**: .NET 9 SDK (only needed if building from source code).

---

## 🚀 Building and Running

To build and run the application from source code:

```bash
# 1. Clone the repository
git clone https://github.com/oakmusic/dictameeting.git
cd dictameeting

# 2. Restore NuGet packages
dotnet restore

# 3. Build the solution
dotnet build -c Release

# 4. Run the application
dotnet run --project src/DictaMeeting.App -c Release
```

To run automated unit and integration tests:

```bash
dotnet test tests/DictaMeeting.Tests/DictaMeeting.Tests.csproj
```

---

## 📦 Building the Installer and Portable Package

PowerShell packaging scripts are included to generate self-contained Windows distributions:

```powershell
# Generate win-x64 distribution, portable zip package, and Inno Setup installer (.exe)
.\scripts\build-distribution.ps1 -Configuration Release -Runtime win-x64
```

Build artifacts are cleanly output into the `dist/` directory.

---

## 📂 Solution Structure

```
DictaMeeting/
├── src/
│   ├── DictaMeeting.Meetings/        # Domain models (Meeting, Participant, TranscriptSegment, Vocabulary)
│   ├── DictaMeeting.Audio/           # WASAPI capture (Microphone + Loopback), VU meters, and Mixer
│   ├── DictaMeeting.Transcription/   # Qwen3-ASR (sherpa-onnx), Whisper.net, Silero VAD, and Model Manager
│   ├── DictaMeeting.Diarization/     # Acoustic segmentation and speaker diarization (PyAnnote)
│   ├── DictaMeeting.AI/              # AI clients (OpenRouter, OpenAI-compatible, local LLamaSharp)
│   ├── DictaMeeting.Infrastructure/  # Disk persistence, exporters, and DPAPI security
│   ├── DictaMeeting.Launcher/        # Native Win32 launcher for clean console-less startup
│   └── DictaMeeting.App/             # WPF UI (MVVM, themes, i18n, and onboarding wizard)
├── tests/
│   └── DictaMeeting.Tests/           # Full unit and integration test suite
├── installer/                        # Inno Setup packaging script
├── scripts/                          # Build and distribution packaging scripts
├── THIRD-PARTY-NOTICES.md            # Third-party dependencies and model licenses
├── LICENSE                           # Project MIT License
└── README.md                         # Main project guide (Spanish)
```

---

## 🔒 Privacy and Credential Security

1. **Audio and Transcripts**: All meeting data is stored locally in the folder selected by the user.
2. **API Keys**: External provider credentials (OpenRouter / Custom Server) are encrypted using **Windows DPAPI** (`ProtectedData.Protect` scoped to `CurrentUser`), guaranteeing that only the logged-in Windows user can decrypt the stored secrets.

---

## 📚 Documentation

| Document | English Language 🇬🇧 | Spanish Language 🇪🇸 | Description |
|---|---|---|---|
| **User Guide** | [USER_GUIDE.en.md](USER_GUIDE.en.md) | [USER_GUIDE.md](USER_GUIDE.md) | Step-by-step end-user guide (capture, loopback, diarization, minutes, and export). |
| **System Architecture** | [ARCHITECTURE.en.md](ARCHITECTURE.en.md) | [ARCHITECTURE.md](ARCHITECTURE.md) | Architectural tenets, system layers, two-phase pipeline, and dependency diagrams. |
| **Developer Reference** | [DEVELOPER_GUIDE.en.md](DEVELOPER_GUIDE.en.md) | [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md) | Technical reference for developers, service contracts, threading, and async channels. |
| **AI Models Guide** | [MODELS.en.md](MODELS.en.md) | [MODELS.md](MODELS.md) | Technical details for Silero VAD, Qwen3-ASR, Whisper, PyAnnote, and local Qwen 2.5. |
| **Troubleshooting & FAQ** | [TROUBLESHOOTING.en.md](TROUBLESHOOTING.en.md) | [TROUBLESHOOTING.md](TROUBLESHOOTING.md) | Common questions, audio device troubleshooting, Teams/Zoom loopback, and performance tuning. |
| **Contributing Guide** | [CONTRIBUTING.md](CONTRIBUTING.md) | [CONTRIBUTING.es.md](CONTRIBUTING.es.md) | MVVM code conventions, pull request workflows, and development guidelines. |
| **Security Policy** | [SECURITY.md](SECURITY.md) | [SECURITY.es.md](SECURITY.es.md) | Supported versions, Windows DPAPI encryption model, and vulnerability reporting. |
| **Third-Party Notices** | [THIRD-PARTY-NOTICES.en.md](THIRD-PARTY-NOTICES.en.md) | [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) | Licenses for .NET packages, native C++ runtimes, and neural AI models. |
| **Development Plan** | [PLAN.en.md](PLAN.en.md) | [PLAN.md](PLAN.md) | Technical roadmap and comprehensive history of all 10 completed development phases. |

---

## 📄 License

This project is licensed under the **MIT License**. See the [LICENSE](LICENSE) file for details.

For third-party library licenses and neural model terms, refer to [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) ([English](THIRD-PARTY-NOTICES.en.md)).

