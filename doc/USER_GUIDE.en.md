# DictaMeeting 🎙 — User Guide

**[🇬🇧 English](USER_GUIDE.en.md) | [🇪🇸 Español](USER_GUIDE.md)**

Welcome to the **DictaMeeting User Guide**, the native Windows desktop application (Windows 10 & 11) designed to record, transcribe in real time, diarize, and summarize in-person meetings and video conferences **100% locally, privately, and without requiring a dedicated graphics card (GPU)**.

---

## 📋 Table of Contents

1. [Installation & First Launch](#1-installation--first-launch)
   - [Installer vs. Portable Version](#installer-vs-portable-version)
   - [First-Run Wizard (Guided Onboarding)](#first-run-wizard-guided-onboarding)
2. [Audio Source Configuration](#2-audio-source-configuration)
   - [Recording Modes](#recording-modes)
   - [Microphone Setup](#microphone-setup)
   - [System Audio Capture (WASAPI Loopback) for Teams, Zoom, or Meet](#system-audio-capture-wasapi-loopback-for-teams-zoom-or-meet)
   - [Testing & VU Meter Calibration](#testing--vu-meter-calibration)
3. [During the Meeting (Live Flow)](#3-during-the-meeting-live-flow)
   - [Starting and Pausing Recording](#starting-and-pausing-recording)
   - [Transcript Display and Smart Auto-Scroll](#transcript-display-and-smart-auto-scroll)
   - [Speaker Diarization and In-Place Renaming](#speaker-diarization-and-in-place-renaming)
   - [Real-Time Running Summary Cards](#real-time-running-summary-cards)
   - [Quick Mute](#quick-mute)
4. [Stopping and Post-Meeting Processing](#4-stopping-and-post-meeting-processing)
   - [Stopping the Meeting](#stopping-the-meeting)
   - [High-Fidelity Offline Processing](#high-fidelity-offline-processing)
   - [Automated Speaker Reconciliation](#automated-speaker-reconciliation)
5. [Audio Player and Audio-Text Synchronization](#5-audio-player-and-audio-text-synchronization)
   - [Interactive Transcript Navigation](#interactive-transcript-navigation)
   - [Search within Transcripts](#search-within-transcripts)
6. [Executive Minutes Generation with AI](#6-executive-minutes-generation-with-ai)
   - [AI Provider Setup (OpenRouter or Custom Endpoint)](#ai-provider-setup-openrouter-or-custom-endpoint)
   - [Detail Levels and Optional Sections](#detail-levels-and-optional-sections)
   - [Editing and Copying Meeting Minutes](#editing-and-copying-meeting-minutes)
7. [Custom Vocabulary and Phonetic Engine](#7-custom-vocabulary-and-phonetic-engine)
   - [Adding Technical Terms and Proper Names](#adding-technical-terms-and-proper-names)
   - [Automatic Phonetic Correction](#automatic-phonetic-correction)
8. [Meeting History and Multi-Format Export](#8-meeting-history-and-multi-format-export)
   - [Supported Formats (Markdown, Plain Text, JSON, MP3)](#supported-formats-markdown-plain-text-json-mp3)
   - [Meeting Folder Layout](#meeting-folder-layout)
9. [Model Management and Storage Paths](#9-model-management-and-storage-paths)
   - [Changing the Models Directory](#changing-the-models-directory)
   - [Modular Download and Removal](#modular-download-and-removal)
10. [Interface Language](#10-interface-language)

---

## 1. Installation & First Launch

### Installer vs. Portable Version

DictaMeeting is distributed in two editions for Windows x64:

- **Official Setup (`DictaMeeting-v1.5.4-Setup.exe`)**:
  - Requires no administrator privileges (installs cleanly into the current user's profile).
  - Creates Start Menu and Desktop shortcuts.
  - Offers a clean uninstaller accessible via Windows *Settings > Apps*.
- **Portable Package (`DictaMeeting-v1.5.4-win-x64-portable.zip`)**:
  - Zero-install required.
  - Extract the ZIP archive to any directory or portable drive and double-click `DictaMeeting.exe`.

### First-Run Wizard (Guided Onboarding)

When launching DictaMeeting for the first time, an onboarding window welcomes you:

1. **Automatic Hardware Detection**: The application inspects your CPU cores and physical RAM to recommend the ideal models.
2. **One-Click Download**: The **"Download Recommended Configuration"** button automatically fetches:
   - **Silero VAD v5** (~4 MB): Intelligent speech activity detector and silence filter.
   - **Qwen3-ASR 0.6B INT8** (~600 MB): Ultra-fast CPU neural speech recognition engine.
3. You may skip the wizard at any time and configure models manually later from the Settings view.

---

## 2. Audio Source Configuration

### Recording Modes

Select which streams to record using the left-hand sidebar:

| Mode | Best Used For | Active Channels |
|---|---|---|
| **Video Conference** (Recommended) | Microsoft Teams, Zoom, Google Meet, Slack, Webex | Physical Microphone + System Audio (Loopback) |
| **Microphone Only** | In-person meetings, voice memos, or personal dictation | Physical Microphone only |
| **System Only** | Webinars, recorded video playback, or calls where you only listen | Windows Speaker Loopback only |

### Microphone Setup

1. Open the **Microphone** dropdown and select your recording hardware (built-in mic, USB headset, or desktop mic).
2. Speak into your microphone: the green/blue level meter will fluctuate to show input signal.

### System Audio Capture (WASAPI Loopback) for Teams, Zoom, or Meet

DictaMeeting uses native Windows **WASAPI Loopback Capture**:

- No virtual audio cables or third-party drivers required.
- Under **Output Device / System Audio**, select the speakers or headphones through which you hear remote callers.
- All remote participants in Teams, Zoom, or your web browser will be captured with high clarity and zero echo.

> [!TIP]
> If you wear headphones during calls, ensure the audio output device selected in DictaMeeting matches the audio device configured in your conferencing app.

### Testing & VU Meter Calibration

Before recording, confirm your volume levels:
- **Green Zone**: Ideal spoken voice level.
- **Yellow Zone**: Loud audio.
- **Red Zone**: Peak amplitude. DictaMeeting applies a soft-clipping limiter (`tanh` curve) to prevent digital harshness and distortion.

---

## 3. During the Meeting (Live Flow)

### Starting and Pausing Recording

1. Optionally enter a **Meeting Title** at the top (defaults to `Meeting YYYY-MM-DD`).
2. Click **[ Start Meeting ]** (or hit the Spacebar if the button has focus).
3. The recording status indicator will pulse red, showing elapsed time (`00:00:00`).
4. To take a recess or discuss confidential matters off-the-record, click **[ Pause ]**. Click **[ Resume ]** to continue.

### Transcript Display and Smart Auto-Scroll

- Spoken words appear on screen with low latency (<1–2 seconds).
- **SmartScroll Behavior**:
  - While scrolled to the bottom, the transcript scrolls smoothly as new utterances arrive.
  - If you scroll up with your mouse wheel to read earlier remarks, auto-scroll pauses automatically so you can read without disruption.
  - Scroll back to the bottom to resume auto-scroll.

### Speaker Diarization and In-Place Renaming

DictaMeeting acoustically distinguishes speakers and assigns interim tags (`SPEAKER_00`, `SPEAKER_01`, etc.):

1. In the right-hand **Participants** panel, view all active speakers, accumulated speaking duration, and turn counts.
2. **In-place renaming**: Click on any tag (e.g. `SPEAKER_00`), type the participant's real name (e.g. `Aritz Villodas`), and hit Enter or click away.
3. **Instant propagation**: All existing and incoming segments for that speaker will immediately display the updated name across the entire interface.

### Real-Time Running Summary Cards

When embedded live summarization is enabled (with the Qwen 2.5 1.5B model downloaded):
- Every ~35 seconds, the embedded LLM processes the rolling context window.
- A running summary card updates at the top of the transcript with a concise 2–4 line synthesis.

### Quick Mute

Click the microphone icon in the header to mute your local input without interrupting the capture of remote participants.

---

## 4. Stopping and Post-Meeting Processing

### Stopping the Meeting

When the session concludes, click **[ Stop Meeting ]**. A confirmation prompt ensures you do not stop recording accidentally.

### High-Fidelity Offline Processing

Upon stopping, DictaMeeting automatically executes the offline post-processing pipeline:
1. **Audio Consolidation**: Finalizes and saves the compressed recording to `audio.mp3`.
2. **High-Fidelity Offline Transcription**: Runs Whisper over the complete audio file for enhanced spelling and phrasing accuracy.
3. **Global Acoustic Diarization**: Re-evaluates speaker embeddings across the entire file to resolve overlaps and refine speaker separation.
4. **Speaker Reconciliation**: Automatically maps real names assigned during the meeting to the final clusters via `SpeakerReconciler`.

An animated progress bar displays completion percentage. You can continue reviewing the transcript while processing finishes.

---

## 5. Audio Player and Audio-Text Synchronization

Once the meeting concludes, DictaMeeting activates the **Interactive Audio Player**:

- **Play / Pause**: Click the playback button or press the Spacebar.
- **Click-to-Seek**: Click on any phrase or word in the transcript; the audio player immediately jumps to the exact second that utterance was spoken.
- **Synchronized Highlighting**: The currently audible phrase highlights visually in real time during playback.
- **Seekbar**: Drag the timeline slider to navigate the recording freely.

---

## 6. Executive Minutes Generation with AI

DictaMeeting can turn the full transcript into **structured executive minutes**:

### AI Provider Setup (OpenRouter or Custom Endpoint)

Navigate to **Settings > Artificial Intelligence**:
- **Option A: OpenRouter AI**:
  - Enter your OpenRouter API Key (`sk-or-...`).
  - Choose a model: *Claude 3.5 Sonnet* (recommended), *GPT-4o*, *Gemini 2.0 Flash*, *Llama 3.3 70B*, *DeepSeek V3*, or enter a custom identifier.
  - The API key is encrypted using **Windows DPAPI** (`ProtectedData.Protect`). Only your Windows user account can decrypt it.
- **Option B: Custom OpenAI-Compatible Server**:
  - Toggle the Custom Server switch.
  - Enter your endpoint URL: e.g. `http://localhost:11434/v1` (Ollama), `http://localhost:1234/v1` (LM Studio), or your enterprise LiteLLM / vLLM gateway.
  - Enter the model identifier (e.g. `llama3.3`, `qwen2.5-72b`).

### Detail Levels and Optional Sections

Click **[ Generate Minutes ]** on any completed meeting to customize:
- **Language**: English or Spanish.
- **Detail Level**:
  - *Brief*: 1-page summary of key highlights and decisions.
  - *Normal*: Balanced summary with action items table.
  - *Detailed*: Comprehensive summary with discussion points and context.
  - *Exhaustive*: Full detailed breakdown of each major topic.
- **Optional Sections**:
  - Attendees & Participation.
  - Key Decisions Agreed.
  - Action Items Table (Task | Assignee | Deadline).
  - Open Questions & Follow-ups.

### Editing and Copying Meeting Minutes

Generated minutes are displayed in an integrated editor:
- Edit text directly to add notes or adjust phrasing.
- Click **[ Copy ]** to copy the formatted Markdown to your clipboard.
- The document is automatically saved as `acta.md` in the meeting folder.

---

## 7. Custom Vocabulary and Phonetic Engine

To prevent speech recognition models from confusing technical terms, company jargon, or proper names:

1. Open **Settings > Vocabulary**.
2. Type custom terms separated by commas or newlines (e.g. `Kubernetes, OAuth2, CRM, WebSockets, PyTorch`).
3. Click **[ Save Vocabulary ]**.
4. The phonetic rule engine (`PhoneticRuleEngine`) automatically adjusts likelihoods and corrects live transcribed segments.

---

## 8. Meeting History and Multi-Format Export

Under the **History** tab:
- Browse all recorded sessions sorted chronologically.
- **Live Search**: Filter sessions by title, date, or text spoken inside transcripts.

### Available Export Formats

One-click access buttons:
- **[ 📁 Open Folder ]**: Opens Windows File Explorer at the session directory.
- **[ 📄 Markdown (.md) ]**: Formatted transcript with metadata and timestamps, ready for Obsidian, Notion, or GitHub.
- **[ 📝 Plain Text (.txt) ]**: Clean plaintext transcript.
- **[ ⚙ JSON (.json) ]**: Machine-readable JSON containing metadata, participants, and segment arrays with millisecond timestamps.

### Meeting Folder Layout

```
Meetings/
└── 2026-10-01_Quarterly-Review/
    ├── meeting.json       # Structured metadata and participant metrics
    ├── transcript.md      # Clean Markdown transcript
    ├── transcript.txt     # Plaintext transcript
    ├── audio.mp3          # Compressed audio recording
    └── acta.md            # AI-generated minutes (if requested)
```

---

## 9. Model Management and Storage Paths

Under **Settings > Models**:

### Changing the Models Directory
- Baseline models are stored in `%LocalAppData%\DictaMeeting\models\`.
- If your `C:` drive has limited space, you can relocate the models directory to a secondary disk (e.g. `D:\AI-Models`). DictaMeeting will safely migrate all files.

### Modular Download and Removal
- View the download status for each model (*Not Downloaded*, *Ready*, or *Downloading*).
- Download or remove models individually with a single click.

---

## 10. Interface Language

DictaMeeting provides full native bilingual support (**English** and **Spanish**):
- Click the language selector in the top-right header to switch between **EN** and **ES**.
- The entire UI updates **instantly without restarting the application**.
- Your language preference is saved and remembered for subsequent sessions.
