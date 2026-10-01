# DictaMeeting — Third-Party Notices and Licenses

**[🇬🇧 English](THIRD-PARTY-NOTICES.en.md) | [🇪🇸 Español](THIRD-PARTY-NOTICES.md)**

This document lists the open-source libraries, native runtimes, and Artificial Intelligence models used by **DictaMeeting**, their associated licenses, and attribution and redistribution terms to ensure regulatory compliance.

---

## 1. .NET Libraries and Runtimes

### CommunityToolkit.Mvvm (v8.4.2)
- **Repository**: [CommunityToolkit/dotnet](https://github.com/CommunityToolkit/dotnet)
- **License**: MIT License
- **Usage**: MVVM architecture, reactive commands (`RelayCommand`), observable properties (`ObservableProperty`), and messaging in the UI layer.

### Microsoft.Extensions.* (v10.0.12)
- **Components**: `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Logging.Console`, `Microsoft.Extensions.Logging.Abstractions`
- **Repository**: [dotnet/runtime](https://github.com/dotnet/runtime)
- **License**: MIT License
- **Usage**: Standardized dependency injection, service configuration, and structured logging to console and disk.

### System.Security.Cryptography.ProtectedData (v10.0.12)
- **Repository**: [dotnet/runtime](https://github.com/dotnet/runtime)
- **License**: MIT License
- **Usage**: Secure encryption and decryption of user credentials (API Keys for OpenRouter and OpenAI-compatible servers) via the **Windows Data Protection API (DPAPI)** tied to the local user profile.

### NAudio / NAudio.Wasapi (v3.1.0)
- **Repository**: [naudio/NAudio](https://github.com/naudio/NAudio)
- **License**: MIT License
- **Usage**: Real-time audio capture from microphones and loopback capture (*WASAPI Loopback Capture*) from speakers/system audio output, resampling to 16 kHz mono, and peak VU meters.

### Microsoft.ML.OnnxRuntime (v1.30.0)
- **Repository**: [microsoft/onnxruntime](https://github.com/microsoft/onnxruntime)
- **License**: MIT License
- **Usage**: Neural inference engine optimized for x64 CPU used for executing Silero VAD as well as acoustic segmentation and embedding models for speaker diarization.

### Microsoft.ML.Tokenizers (v2.0.0)
- **Repository**: [dotnet/machinelearning](https://github.com/dotnet/machinelearning)
- **License**: MIT License
- **Usage**: Text tokenization and decoding for neural language models.

### Whisper.net / Whisper.net.Runtime (v1.9.1)
- **Repository**: [sandrohanea/whisper.net](https://github.com/sandrohanea/whisper.net) / [ggerganov/whisper.cpp](https://github.com/ggerganov/whisper.cpp)
- **License**: MIT License
- **Usage**: High-accuracy speech recognition engine for final offline meeting post-processing in GGML format optimized for x64 CPU with AVX/AVX2 extensions.

### org.k2fsa.sherpa.onnx / sherpa-onnx.runtime.win-x64 (v1.13.8)
- **Repository**: [k2-fsa/sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx)
- **License**: Apache License 2.0
- **Usage**: Real-time inference engine for continuous speech recognition (Qwen3-ASR) and neural punctuation and capitalization restoration model.

### LLamaSharp / LLamaSharp.Backend.Cpu (v0.27.0)
- **Repository**: [SciSharp/LLamaSharp](https://github.com/SciSharp/LLamaSharp) / [ggerganov/llama.cpp](https://github.com/ggerganov/llama.cpp)
- **License**: MIT License
- **Usage**: Local execution of lightweight language models in GGUF format (Qwen 2.5) for real-time continuous summary generation without leaving the machine.

### SharpZipLib (v1.4.2)
- **Repository**: [icsharpcode/SharpZipLib](https://github.com/icsharpcode/SharpZipLib)
- **License**: MIT License
- **Usage**: Secure decompression of tar.bz2 and zip archives when downloading model packages from remote repositories.

---

## 2. Artificial Intelligence Models

*Important note*: The DictaMeeting repository **does not include binary model weights** in its version control. Models are downloaded on demand by the user via the integrated catalog or configured in custom local directories.

### Silero VAD (v5 ONNX)
- **Developer**: Silero Team ([Snakers4/silero-vad](https://github.com/snakers4/silero-vad))
- **License**: MIT License
- **Usage**: Real-time Voice Activity Detection (VAD) to segment clean voice chunks and filter noise and silence.

### Qwen3-ASR (0.6B INT8 ONNX)
- **Developer**: Alibaba Qwen Team / Ported to sherpa-onnx by k2-fsa
- **License**: Apache License 2.0 / Qwen Community License
- **Usage**: Ultra-low-latency real-time live speech transcription on CPU.

### Qwen 2.5 (0.5B / 1.5B GGUF)
- **Developer**: Alibaba Qwen Team ([QwenLM](https://github.com/QwenLM/Qwen2.5))
- **License**: Apache License 2.0
- **Usage**: Local live executive summarization executed via LLamaSharp.

### Sherpa-onnx Punctuation Model (CT-Transformer)
- **Developer**: k2-fsa / Alibaba DAMO Academy (FunASR)
- **License**: Apache License 2.0
- **Usage**: Automatic restoration of commas, periods, and capitalization in live transcription.

### PyAnnote Community-1 (Diarization and Embeddings)
- **Developer**: pyannote.audio ([pyannote/pyannote-audio](https://github.com/pyannote/pyannote-audio))
- **License**: PyAnnote Community License / Attribution required
- **Usage**: Acoustic segmentation and speaker embedding extraction for speaker clustering and diarization.
- **Required Attribution**:
  > The PyAnnote Community-1 models used for speaker segmentation and identification were developed by the pyannote.audio team.  
  > Reference: Hervé Bredin et al., *"pyannote.audio: neural building blocks for speaker diarization"*, ICASSP 2020.  
  > DictaMeeting downloads model weights on demand by the end user, respecting pyannote's community license terms.

---

## 3. Windows Native Operating System APIs

- **Windows CoreAudio / WASAPI**: Audio hardware access via native Windows COM interfaces (`MMDevApi.dll`, `AudioSes.dll`).
- **Windows DPAPI (Data Protection API)**: User-level secret encryption and decryption backed by the Windows user profile cryptographic key (`Crypt32.dll`).
- **Windows Media Foundation**: Standard system audio encoding (`mfplat.dll`).
