# DictaMeeting — Avisos y Licencias de Terceros

**[🇪🇸 Español](THIRD-PARTY-NOTICES.md) | [🇬🇧 English](THIRD-PARTY-NOTICES.en.md)**

Este documento recopila las bibliotecas de código abierto, runtimes nativos y modelos de Inteligencia Artificial utilizados por **DictaMeeting**, sus licencias asociadas y las condiciones de atribución y redistribución para garantizar el cumplimiento normativo.

---

## 1. Bibliotecas y Runtimes .NET

### CommunityToolkit.Mvvm (v8.4.2)
- **Repositorio**: [CommunityToolkit/dotnet](https://github.com/CommunityToolkit/dotnet)
- **Licencia**: MIT License
- **Uso**: Arquitectura MVVM, comandos reactivos (`RelayCommand`), propiedades observables (`ObservableProperty`) y mensajería en la capa de interfaz.

### Microsoft.Extensions.* (v10.0.12)
- **Componentes**: `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Logging.Console`, `Microsoft.Extensions.Logging.Abstractions`
- **Repositorio**: [dotnet/runtime](https://github.com/dotnet/runtime)
- **Licencia**: MIT License
- **Uso**: Inyección de dependencias estandarizada, configuración de servicios y registro estructurado de eventos en consola y disco.

### System.Security.Cryptography.ProtectedData (v10.0.12)
- **Repositorio**: [dotnet/runtime](https://github.com/dotnet/runtime)
- **Licencia**: MIT License
- **Uso**: Cifrado y descifrado seguro de credenciales de usuario (API Keys de OpenRouter y servidores compatibles con OpenAI) mediante la **Windows Data Protection API (DPAPI)** vinculada al usuario local.

### NAudio / NAudio.Wasapi (v3.1.0)
- **Repositorio**: [naudio/NAudio](https://github.com/naudio/NAudio)
- **Licencia**: MIT License
- **Uso**: Captura de audio en tiempo real desde micrófonos y captura en bucle invertido (*WASAPI Loopback Capture*) desde altavoces/salida del sistema, remuestreo a 16 kHz mono y vúmetros de pico.

### Microsoft.ML.OnnxRuntime (v1.30.0)
- **Repositorio**: [microsoft/onnxruntime](https://github.com/microsoft/onnxruntime)
- **Licencia**: MIT License
- **Uso**: Motor de inferencia neuronal optimizado para CPU x64 utilizado para la ejecución de Silero VAD y modelos de segmentación y embedding acústico de diarización.

### Microsoft.ML.Tokenizers (v2.0.0)
- **Repositorio**: [dotnet/machinelearning](https://github.com/dotnet/machinelearning)
- **Licencia**: MIT License
- **Uso**: Tokenización y decodificación de texto para modelos neuronales de lenguaje.

### Whisper.net / Whisper.net.Runtime (v1.9.1)
- **Repositorio**: [sandrohanea/whisper.net](https://github.com/sandrohanea/whisper.net) / [ggerganov/whisper.cpp](https://github.com/ggerganov/whisper.cpp)
- **Licencia**: MIT License
- **Uso**: Motor de reconocimiento de voz de alta precisión para el post-procesado final de reuniones en formato GGML optimizado para CPU x64 con extensiones AVX/AVX2.

### org.k2fsa.sherpa.onnx / sherpa-onnx.runtime.win-x64 (v1.13.8)
- **Repositorio**: [k2-fsa/sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx)
- **Licencia**: Apache License 2.0
- **Uso**: Motor de inferencia en tiempo real para reconocimiento de voz continuo (Qwen3-ASR) y modelo neuronal de restauración de signos de puntuación y capitalización.

### LLamaSharp / LLamaSharp.Backend.Cpu (v0.27.0)
- **Repositorio**: [SciSharp/LLamaSharp](https://github.com/SciSharp/LLamaSharp) / [ggerganov/llama.cpp](https://github.com/ggerganov/llama.cpp)
- **Licencia**: MIT License
- **Uso**: Ejecución local de modelos de lenguaje ligeros en formato GGUF (Qwen 2.5) para generación de resúmenes continuos en tiempo real sin salir del equipo.

### SharpZipLib (v1.4.2)
- **Repositorio**: [icsharpcode/SharpZipLib](https://github.com/icsharpcode/SharpZipLib)
- **Licencia**: MIT License
- **Uso**: Descompresión segura de archivos tar.bz2 y zip al descargar paquetes de modelos desde repositorios remotos.

---

## 2. Modelos de Inteligencia Artificial

*Nota importante*: El repositorio de DictaMeeting **no incluye pesos binarios de modelos** en su control de versiones. Los modelos son descargados bajo demanda por el usuario mediante el catálogo integrado o configurados en directorios locales personalizados.

### Silero VAD (v5 ONNX)
- **Desarrollador**: Silero Team ([Snakers4/silero-vad](https://github.com/snakers4/silero-vad))
- **Licencia**: MIT License
- **Uso**: Detección de actividad vocal (Voice Activity Detection) en tiempo real para segmentar fragmentos con voz y filtrar ruidos/silencios.

### Qwen3-ASR (0.6B INT8 ONNX)
- **Desarrollador**: Alibaba Qwen Team / Adaptado para sherpa-onnx por k2-fsa
- **Licencia**: Apache License 2.0 / Qwen Community License
- **Uso**: Transcripción de habla en vivo de bajísima latencia en CPU.

### Qwen 2.5 (0.5B / 1.5B GGUF)
- **Desarrollador**: Alibaba Qwen Team ([QwenLM](https://github.com/QwenLM/Qwen2.5))
- **Licencia**: Apache License 2.0
- **Uso**: Resumen ejecutivo local en vivo ejecutado vía LLamaSharp.

### Sherpa-onnx Punctuation Model (CT-Transformer)
- **Desarrollador**: k2-fsa / Alibaba DAMO Academy (FunASR)
- **Licencia**: Apache License 2.0
- **Uso**: Restauración automática de comas, puntos y mayúsculas en la transcripción en vivo.

### PyAnnote Community-1 (Diarización y Embeddings)
- **Desarrollador**: pyannote.audio ([pyannote/pyannote-audio](https://github.com/pyannote/pyannote-audio))
- **Licencia**: PyAnnote Community License / Atribución requerida
- **Uso**: Segmentación acústica y extracción de embeddings de hablante para agrupamiento y diarización de interlocutores.
- **Atribución requerida**:
  > Los modelos PyAnnote Community-1 utilizados para la separación e identificación de interlocutores han sido desarrollados por el equipo de pyannote.audio.  
  > Referencia: Hervé Bredin et al., *"pyannote.audio: neural building blocks for speaker diarization"*, ICASSP 2020.  
  > DictaMeeting descarga los pesos bajo demanda del usuario final respetando los términos de la licencia comunitaria de pyannote.

---

## 3. APIs Nativas del Sistema Operativo Windows

- **Windows CoreAudio / WASAPI**: Acceso al hardware de audio mediante interfaces nativas COM de Windows (`MMDevApi.dll`, `AudioSes.dll`).
- **Windows DPAPI (Data Protection API)**: Cifrado y descifrado de secretos a nivel de usuario respaldado por la clave criptográfica del perfil de usuario de Windows (`Crypt32.dll`).
- **Windows Media Foundation**: Codificación de audio en formatos estándar del sistema (`mfplat.dll`).
