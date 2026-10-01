# DictaMeeting 🎙 — Transcripción y Actas Locales de Reuniones

**[🇪🇸 Español](README.md) | [🇬🇧 English](README.en.md)**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20x64-blue.svg)](https://microsoft.com/windows)
[![UI: Español | English](https://img.shields.io/badge/UI-Espa%C3%B1ol%20%7C%20English-green.svg)](README.md)

**DictaMeeting** es una aplicación de escritorio nativa para Windows (C# / .NET 9 / WPF) diseñada para grabar, transcribir en tiempo real, diarizar y resumir reuniones presenciales o videoconferencias (Microsoft Teams, Zoom, Google Meet, Slack, Webex) de forma **100% local, privada y sin requerir tarjeta gráfica dedicada (GPU)**.

Opcionalmente, permite generar actas ejecutivas estructuradas con Inteligencia Artificial utilizando **OpenRouter AI** o cualquier **servidor compatible con la API de OpenAI** (LiteLLM, Ollama, vLLM o endpoints propios), protegiendo todas las claves y credenciales mediante la **Windows Data Protection API (DPAPI)**.

---

## ✨ Características Principales

- **Privacidad Total y Procesamiento Local**:
  - Todo el procesamiento de audio, detección de voz, transcripción continua y diarización se realiza en el procesador de tu equipo.
  - El audio de tus reuniones nunca se comparte con terceros ni se almacena en la nube.
- **Interfaz Bilingüe (Español / English)**:
  - Soporte completo en Español e Inglés con cambio dinámico en caliente sin reiniciar la aplicación.
  - **Detección automática** del idioma del sistema operativo (Windows en español activa Español; cualquier otro idioma activa Inglés) con persistencia de la preferencia manual del usuario.
- **Captura Dual Simultánea**:
  - Micrófono físico (participantes en sala).
  - Audio del sistema vía bucle invertido (*WASAPI Loopback Capture*) para interlocutores remotos en Teams, Zoom, Slack o Meet.
  - Vúmetros de volumen en tiempo real y selector independiente de dispositivos.
- **Pipeline de Transcripción en Dos Fases**:
  - **Fase LIVE (Tiempo Real)**: Silero VAD v5 + Qwen3-ASR (0.6B INT8 ONNX vía sherpa-onnx) con restauración automática de puntuación para lectura instantánea sin retardo.
  - **Fase FINAL (Alta Fidelidad)**: Procesamiento completo con Whisper (GGML Tiny, Base, Small, Medium, Large-v3 Turbo) y diarización acústica con PyAnnote Community-1 para asignación rigurosa de turnos.
- **Sincronización Interactiva Audio-Texto**:
  - Reproductor de audio integrado que resalta la frase exacta que se está escuchando y permite saltar a cualquier punto de la grabación haciendo clic en la transcripción.
- **Resumen Ejecutivo y Actas de Reunión**:
  - **Resumen Local Continuo**: Generación de tarjetas de resumen en vivo durante la reunión mediante modelos LLM locales (Qwen 2.5 vía LLamaSharp).
  - **Generación de Actas con IA**: Generación de actas ejecutivas completas (resumen, decisiones, acciones y compromisos) mediante OpenRouter o servidores locales/propios compatibles con OpenAI.
- **Gestión Inteligente de Modelos**:
  - **Asistente de Primera Ejecución (*First-Run Wizard*)**: Descarga guiada de los modelos recomendados en el primer inicio.
  - **Ubicación Configurable de Modelos**: Permite almacenar los modelos neuronales en la ubicación predeterminada (`%LocalAppData%\DictaMeeting\models`) o en unidades externas/secundarias con herramienta de migración segura integrada.
- **Vocabulario y Corrección Fonética**:
  - Diccionario personalizado para añadir términos técnicos, acrónimos o nombres propios y evitar alucinaciones fonéticas.
- **Historial Completo y Exportación**:
  - Búsqueda en tiempo real por fecha, título o texto hablado.
  - Exportación en un clic a **Markdown (.md)**, **Texto Plano (.txt)** y **JSON estructurado (.json)**.

---

## 🛠 Requisitos del Sistema

- **Sistema Operativo**: Windows 10 (versión 1809 o superior) o Windows 11 (64 bits).
- **Procesador (CPU)**: CPU multinúcleo x64 (Intel Core i5/i7/i9 de 8ª gen o superior, o AMD Ryzen serie 3000 o superior). No se requiere GPU dedicada.
- **Memoria RAM**: 8 GB mínimo (16 GB recomendado para modelos finales medianos y diarización).
- **Almacenamiento**: ~1.5 GB de espacio libre para los modelos esenciales recomendados.
- **Entorno de Desarrollo**: .NET 9 SDK (solo necesario para compilar el código fuente).

---

## 🚀 Compilación y Ejecución

Para compilar y ejecutar la aplicación desde el código fuente:

```bash
# 1. Clonar el repositorio
git clone https://github.com/oakmusic/dictameeting.git
cd dictameeting

# 2. Restaurar paquetes NuGet
dotnet restore

# 3. Compilar la solución
dotnet build -c Release

# 4. Ejecutar la aplicación
dotnet run --project src/DictaMeeting.App -c Release
```

Para ejecutar las pruebas automatizadas:

```bash
dotnet test tests/DictaMeeting.Tests/DictaMeeting.Tests.csproj
```

---

## 📦 Generación del Instalador y Paquete Portable

El proyecto incluye scripts en PowerShell para empaquetar la distribución autónoma para Windows:

```powershell
# Generar distribución win-x64, paquete portable (.zip) e instalador (.exe con Inno Setup)
.\scripts\build-distribution.ps1 -Configuration Release -Runtime win-x64
```

Los artefactos se depositan de forma limpia en el directorio `dist/`.

---

## 📂 Estructura de la Solución

```
DictaMeeting/
├── src/
│   ├── DictaMeeting.Meetings/        # Modelos de dominio (Meeting, Participant, TranscriptSegment, Vocabulary)
│   ├── DictaMeeting.Audio/           # Captura WASAPI (Micrófono + Loopback), Vúmetros y Mezclador
│   ├── DictaMeeting.Transcription/   # Qwen3-ASR (sherpa-onnx), Whisper.net, Silero VAD y Model Manager
│   ├── DictaMeeting.Diarization/     # Segmentación acústica y diarización de interlocutores (PyAnnote)
│   ├── DictaMeeting.AI/              # Clientes de IA (OpenRouter, OpenAI-compatible, LLamaSharp local)
│   ├── DictaMeeting.Infrastructure/  # Persistencia en disco, exportadores y seguridad DPAPI
│   ├── DictaMeeting.Launcher/        # Lanzador nativo Win32 para arranque sin consola
│   └── DictaMeeting.App/             # Interfaz de usuario WPF (MVVM, temas, i18n y asistente de inicio)
├── tests/
│   └── DictaMeeting.Tests/           # Suite completa de pruebas unitarias e integración
├── installer/                        # Script de empaquetado para Inno Setup
├── scripts/                          # Scripts de compilación y empaquetado de distribución
├── THIRD-PARTY-NOTICES.md            # Avisos y licencias de dependencias y modelos de terceros
├── LICENSE                           # Licencia MIT del proyecto
└── README.md                         # Guía principal del proyecto
```

---

## 🔒 Privacidad y Seguridad de Credenciales

1. **Audio y Transcripciones**: Toda la información de reuniones se almacena localmente en la carpeta seleccionada por el usuario.
2. **Claves de API**: Las credenciales de proveedores externos (OpenRouter / Servidor Propio) se cifran mediante **Windows DPAPI** (`ProtectedData.Protect` con ámbito `CurrentUser`), garantizando que solo el usuario que inició sesión en Windows pueda descifrar los datos.

---

## 📚 Documentación

| Documento | Idioma Español 🇪🇸 | English Language 🇬🇧 | Descripción |
|---|---|---|---|
| **Manual de Usuario** | [USER_GUIDE.md](USER_GUIDE.md) | [USER_GUIDE.en.md](USER_GUIDE.en.md) | Guía paso a paso para usuarios finales (captura, loopback, diarización, actas y exportación). |
| **Arquitectura del Sistema** | [ARCHITECTURE.md](ARCHITECTURE.md) | [ARCHITECTURE.en.md](ARCHITECTURE.en.md) | Principios de diseño, capas, pipeline dual y diagramas de arquitectura. |
| **Referencia Técnica y Dev** | [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md) | [DEVELOPER_GUIDE.en.md](DEVELOPER_GUIDE.en.md) | Documentación para desarrolladores, contratos de interfaz, threading y canales asíncronos. |
| **Catálogo de Modelos de IA** | [MODELS.md](MODELS.md) | [MODELS.en.md](MODELS.en.md) | Detalle técnico de Silero VAD, Qwen3-ASR, Whisper, PyAnnote y Qwen 2.5 local. |
| **Solución de Problemas y FAQ** | [TROUBLESHOOTING.md](TROUBLESHOOTING.md) | [TROUBLESHOOTING.en.md](TROUBLESHOOTING.en.md) | Resolución de dudas frecuentes sobre micrófonos, loopback de Teams/Zoom y rendimiento. |
| **Guía de Contribución** | [CONTRIBUTING.es.md](CONTRIBUTING.es.md) | [CONTRIBUTING.md](CONTRIBUTING.md) | Estándares de código MVVM, pull requests y flujo de trabajo para colaboradores. |
| **Política de Seguridad** | [SECURITY.es.md](SECURITY.es.md) | [SECURITY.md](SECURITY.md) | Versiones soportadas, cifrado DPAPI y reporte responsable de vulnerabilidades. |
| **Avisos de Terceros** | [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) | [THIRD-PARTY-NOTICES.en.md](THIRD-PARTY-NOTICES.en.md) | Licencias de dependencias .NET, runtimes nativos y modelos de IA. |
| **Plan de Desarrollo** | [PLAN.md](PLAN.md) | [PLAN.en.md](PLAN.en.md) | Hoja de ruta técnica e historial detallado de las 10 fases de desarrollo. |

---

## 📄 Licencia

Este proyecto está publicado bajo la licencia **MIT License**. Consulta el archivo [LICENSE](LICENSE) para más detalles.

Para información sobre bibliotecas de terceros y términos de los modelos neuronales, consulta [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) ([English](THIRD-PARTY-NOTICES.en.md)).

