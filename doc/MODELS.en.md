# DictaMeeting 🎙 — AI Models Catalog & Guide

**[🇬🇧 English](MODELS.en.md) | [🇪🇸 Español](MODELS.md)**

DictaMeeting employs a synergistic suite of local neural models specifically optimized for x64 CPU inference without requiring dedicated graphics cards (GPUs). This document details each model, its role in the processing pipeline, hardware footprints, and offline manual installation procedures for air-gapped environments.

---

## 🧠 1. Pipeline Models Summary

| Model | Format | Size | Inference Engine | Pipeline Phase | Core Role |
|---|---|---|---|---|---|
| **Silero VAD v5** | `.onnx` | ~4.2 MB | `Microsoft.ML.OnnxRuntime` | LIVE | Voice activity detection and clean speech windowing. |
| **Qwen3-ASR 0.6B** | INT8 `.onnx` | ~600 MB | `org.k2fsa.sherpa.onnx` | LIVE | Real-time speech transcription on CPU (<1.5s latency). |
| **Qwen3-ASR 1.7B** | INT8 `.onnx` | ~1.6 GB | `org.k2fsa.sherpa.onnx` | LIVE | High-precision live technical speech recognition (16 GB RAM recommended). |
| **Whisper (Tiny / Base / Small / Medium / Large-v3 Turbo)** | GGML `.bin` | 75 MB – 1.6 GB | `Whisper.net` (C++ AVX2) | FINAL | Offline batch transcription over the consolidated audio recording. |
| **PyAnnote Community-1** | `.onnx` | ~150 MB | `Microsoft.ML.OnnxRuntime` | FINAL / LIVE | Acoustic segmentation and speaker embedding extraction. |
| **Qwen 2.5 1.5B Instruct** | GGUF (`Q4_K_M`) | ~986 MB | `LLamaSharp` / `llama.cpp` | LIVE | Running cumulative executive summaries generated every ~35s. |
| **Sherpa Punctuation (CT-Transformer)** | `.onnx` | ~120 MB | `org.k2fsa.sherpa.onnx` | POST | Automated restoration of commas, periods, and capitalization. |

---

## 🔍 2. Technical Breakdown by Model

### 2.1 Silero VAD v5 (Voice Activity Detection)
- **Developer**: Silero Team ([Snakers4/silero-vad](https://github.com/snakers4/silero-vad)).
- **Input**: 512-sample PCM chunks at 16 kHz Mono (32 ms).
- **CPU Footprint**: Negligible (<1% of a single CPU core).
- **Role**: Discards keyboard clicks, breaths, pauses, and room silence before routing audio to the ASR model, saving substantial CPU cycles.

### 2.2 Qwen3-ASR (Real-Time Live Transcription)
- **Developer**: Alibaba Qwen Team / Ported to sherpa-onnx by k2-fsa.
- **Architecture**: Conformer encoder + INT8 quantized decoder optimized for x64 CPU architectures.
- **Key Strengths**:
  - Pre-trained and fine-tuned for engineering, IT, software, and business terminology.
  - Robust handling of mixed Spanglish terminology commonly found in technical meetings.
  - Immune to infinite repetition loops during pauses or background murmurs.
- **Model Sizing Recommendation**:
  - `0.6B INT8`: Standard default choice for modern laptops and quad-core CPUs.
  - `1.7B INT8`: Recommended for workstation-class processors (Intel i7/i9 or Ryzen 7/9) with 16+ GB RAM.

### 2.3 Whisper (Final Offline Batch Processing)
- **Developer**: OpenAI / Native C++ AVX2 runtimes (`whisper.cpp`).
- **Available Sizing Profiles**:
  - `Tiny` (~75 MB): Ultra-fast diagnostic testing.
  - `Base` (~142 MB): Fast processing with moderate accuracy.
  - `Small` (~466 MB): **Recommended sweet spot for balanced post-processing**.
  - `Medium` (~1.5 GB): High accuracy on CPU.
  - `Large-v3 Turbo` (~1.6 GB): State-of-the-art transcription fidelity.
- **Hardware Optimization**: Built with native SIMD instructions (AVX, AVX2, FMA).

### 2.4 PyAnnote Community-1 (Speaker Diarization)
- **Developer**: pyannote.audio ([pyannote/pyannote-audio](https://github.com/pyannote/pyannote-audio)).
- **License**: PyAnnote Community License.
- **Operation**: Extracts 512-dimensional voiceprint embeddings capturing acoustic vocal tract characteristics, allowing accurate clustering and separation of distinct participants via cosine similarity.

### 2.5 Qwen 2.5 1.5B Instruct (Embedded Local Running Summaries)
- **Developer**: Alibaba Qwen Team ([QwenLM](https://github.com/QwenLM/Qwen2.5)).
- **Quantization**: `Q4_K_M` GGUF (~986 MB).
- **RAM Overhead**: ~1.1 GB with active 2048-token context.
- **CPU Throttling**: Automatically capped to `Cores / 2` (maximum 4 worker threads) to guarantee audio capture and transcription threads are never starved.

---

## 📁 3. Disk Directory Layout

By default, neural models reside under:
```
%LocalAppData%\DictaMeeting\models\
│
├── silero\
│   └── silero_vad.onnx
│
├── qwen3-asr-0.6b-int8\
│   ├── model.int8.onnx
│   ├── tokens.txt
│   └── ...
│
├── whisper\
│   ├── ggml-base.bin
│   └── ggml-small.bin
│
├── diarization\
│   └── pyannote_community1.onnx
│
└── summary\
    └── Qwen2.5-1.5B-Instruct-Q4_K_M.gguf
```

---

## 🛡 4. Manual Installation in Air-Gapped / Isolated Environments

To deploy DictaMeeting on secure corporate workstations without internet access:

1. Download the required model archives on an internet-connected workstation.
2. Transfer and place the `models` folder onto the target machine at:
   `C:\Users\<YourUser>\AppData\Local\DictaMeeting\models\`
   *(or your chosen custom storage path).*
3. When launched, DictaMeeting will detect the pre-existing files, verify their integrity, and mark them as **Ready** without attempting any outbound network connections.
