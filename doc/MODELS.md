# DictaMeeting 🎙 — Catálogo y Guía de Modelos de IA

**[🇪🇸 Español](MODELS.md) | [🇬🇧 English](MODELS.en.md)**

DictaMeeting utiliza una combinación sinérgica de modelos neuronales locales optimizados para ejecutarse en CPU x64 sin requerir tarjetas gráficas dedicadas. Este documento detalla cada modelo, su función en el pipeline, consumo de recursos y procedimiento de instalación manual para entornos aislados (*air-gapped*).

---

## 🧠 1. Resumen de Modelos en el Pipeline

| Modelo | Formato | Tamaño | Motor de Inferencia | Fase | Función Principal |
|---|---|---|---|---|---|
| **Silero VAD v5** | `.onnx` | ~4.2 MB | `Microsoft.ML.OnnxRuntime` | LIVE | Detección de actividad vocal y segmentación limpia de voz. |
| **Qwen3-ASR 0.6B** | INT8 `.onnx` | ~600 MB | `org.k2fsa.sherpa.onnx` | LIVE | Transcripción en vivo ultra-rápida en CPU (<1.5s de retardo). |
| **Qwen3-ASR 1.7B** | INT8 `.onnx` | ~1.6 GB | `org.k2fsa.sherpa.onnx` | LIVE | Transcripción en vivo de alta precisión técnica (requiere 16 GB RAM). |
| **Whisper (Tiny / Base / Small / Medium / Large-v3 Turbo)** | GGML `.bin` | 75 MB – 1.6 GB | `Whisper.net` (C++ AVX2) | FINAL | Transcripción definitiva por lotes sobre el archivo de audio consolidado. |
| **PyAnnote Community-1** | `.onnx` | ~150 MB | `Microsoft.ML.OnnxRuntime` | FINAL / LIVE | Segmentación acústica y extracción de embeddings para diarización. |
| **Qwen 2.5 1.5B Instruct** | GGUF (`Q4_K_M`) | ~986 MB | `LLamaSharp` / `llama.cpp` | LIVE | Generación de tarjetas de resumen acumulativo cada ~35s. |
| **Sherpa Punctuation (CT-Transformer)** | `.onnx` | ~120 MB | `org.k2fsa.sherpa.onnx` | POST | Restauración automática de comas, puntos y mayúsculas. |

---

## 🔍 2. Detalles Técnicos por Modelo

### 2.1 Silero VAD v5 (Detección de Actividad Vocal)
- **Desarrollador**: Silero Team ([Snakers4/silero-vad](https://github.com/snakers4/silero-vad)).
- **Entrada**: Ventanas PCM de 512 muestras a 16 kHz Mono (32 ms).
- **Consumo de CPU**: Prácticamente inapreciable (<1% en un núcleo estándar).
- **Objetivo**: Filtra respiraciones, ruidos de teclado, siseos y silencios prolongados antes de enviar datos al motor de transcripción, evitando gasto innecesario de ciclos de CPU.

### 2.2 Qwen3-ASR (Transcripción en Tiempo Real)
- **Desarrollador**: Alibaba Qwen Team / Adaptado para sherpa-onnx por k2-fsa.
- **Arquitectura**: Codificador conformer + decodificador cuantizado en INT8 para CPU x64.
- **Puntos Fuertes**:
  - Especializado en terminología técnica de desarrollo, ingeniería y negocios.
  - Excelente comportamiento con mezclas bilingües (*Spanglish* habitual en reuniones de software).
  - Inmunidad total a bucles de repetición infinitos en silencios.
- **Recomendación**:
  - `0.6B INT8`: Opción estándar recomendada para portátiles de oficina y CPUs de 4–8 núcleos.
  - `1.7B INT8`: Para CPUs potentes (Intel i7/i9 o Ryzen 7/9) con 16+ GB de RAM.

### 2.3 Whisper (Transcripción Definitiva Post-Reunión)
- **Desarrollador**: OpenAI / Runtimes nativos C++ optimizados (`whisper.cpp`).
- **Formatos Disponibles**:
  - `Tiny` (~75 MB): Pruebas ultra-rápidas.
  - `Base` (~142 MB): Rápido con precisión moderada.
  - `Small` (~466 MB): **Recomendado para post-procesado equilibrado**.
  - `Medium` (~1.5 GB): Máxima precisión sin GPU.
  - `Large-v3 Turbo` (~1.6 GB): Precisión de vanguardia.
- **Optimización de Hardware**: Compilado con soporte nativo para instrucciones AVX, AVX2 y FMA de procesadores modernos.

### 2.4 PyAnnote Community-1 (Diarización de Hablantes)
- **Desarrollador**: pyannote.audio ([pyannote/pyannote-audio](https://github.com/pyannote/pyannote-audio)).
- **Licencia**: PyAnnote Community License.
- **Funcionamiento**: Extrae vectores de características acústicas (embeddings de 512 dimensiones) que representan el timbre de voz de cada persona, permitiendo separarlas rigurosamente mediante similitud coseno y agrupamiento jerárquico.

### 2.5 Qwen 2.5 1.5B Instruct (Resumen Local Embebido)
- **Desarrollador**: Alibaba Qwen Team ([QwenLM](https://github.com/QwenLM/Qwen2.5)).
- **Cuantización**: `Q4_K_M` en formato GGUF (~986 MB).
- **Consumo de RAM**: ~1.1 GB con contexto activo de 2048 tokens.
- **Hilos de Ejecución**: Limitado automáticamente a `Núcleos / 2` (máx. 4) para no restar recursos al audio ni a la transcripción.

---

## 📁 3. Estructura de Directorios en Disco

Por defecto, los modelos se descargan y gestionan en:
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

## 🛡 4. Instalación Manual en Entornos Aislados (Air-Gapped)

Para desplegar DictaMeeting en equipos corporativos sin conexión a internet:

1. En un equipo con conexión, descarga los archivos de los modelos requeridos.
2. Copia la carpeta `models` al equipo de destino en:
   `C:\Users\<TuUsuario>\AppData\Local\DictaMeeting\models\`
   *(o en la ruta personalizada que desees configurar en la aplicación).*
3. Al iniciar DictaMeeting, la aplicación detectará automáticamente los archivos presentes y marcará su estado como **Listo** sin intentar ninguna conexión a internet.
