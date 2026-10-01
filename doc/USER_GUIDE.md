# DictaMeeting 🎙 — Manual de Usuario

**[🇪🇸 Español](USER_GUIDE.md) | [🇬🇧 English](USER_GUIDE.en.md)**

Bienvenido al **Manual de Usuario de DictaMeeting**, la aplicación nativa para Windows (10 y 11) diseñada para grabar, transcribir en tiempo real, diarizar y resumir reuniones presenciales y videoconferencias de forma **100% local, privada y sin requerir tarjeta gráfica dedicada**.

---

## 📋 Índice

1. [Instalación y Primer Inicio](#1-instalación-y-primer-inicio)
   - [Instalador vs. Versión Portable](#instalador-vs-versión-portable)
   - [Asistente de Primer Arranque (First-Run Wizard)](#asistente-de-primer-arranque-first-run-wizard)
2. [Configuración de Fuentes de Audio](#2-configuración-de-fuentes-de-audio)
   - [Modos de Grabación](#modos-de-grabación)
   - [Configuración del Micrófono](#configuración-del-micrófono)
   - [Captura del Sistema (WASAPI Loopback) para Teams, Zoom o Meet](#captura-del-sistema-wasapi-loopback-para-teams-zoom-o-meet)
   - [Prueba y Calibración de Vúmetros](#prueba-y-calibración-de-vúmetros)
3. [Durante la Reunión (Flujo en Vivo)](#3-durante-la-reunión-flujo-en-vivo)
   - [Iniciar y Pausar la Grabación](#iniciar-y-pausar-la-grabación)
   - [Visualización de Transcripción y Auto-Scroll Inteligente](#visualización-de-transcripción-y-auto-scroll-inteligente)
   - [Identificación de Hablantes y Renombrado en Caliente](#identificación-de-hablantes-y-renombrado-en-caliente)
   - [Tarjetas de Resumen en Vivo](#tarjetas-de-resumen-en-vivo)
   - [Silenciamiento Rápido (Mute)](#silenciamiento-rápido-mute)
4. [Finalización y Procesamiento Post-Reunión](#4-finalización-y-procesamiento-post-reunión)
   - [Detener la Reunión](#detener-la-reunión)
   - [Procesamiento Definitivo de Alta Calidad](#procesamiento-definitivo-de-alta-calidad)
   - [Reconciliación Automática de Participantes](#reconciliación-automática-de-participantes)
5. [Reproductor y Sincronización Audio-Texto](#5-reproductor-y-sincronización-audio-texto)
   - [Navegación Interactiva por la Transcripción](#navegación-interactiva-por-la-transcripción)
   - [Búsqueda Rápida en el Texto](#búsqueda-rápida-en-el-texto)
6. [Generación de Actas Ejecutivas con IA](#6-generación-de-actas-ejecutivas-con-ia)
   - [Configuración de Proveedor (OpenRouter o Servidor Propio)](#configuración-de-proveedor-openrouter-o-servidor-propio)
   - [Niveles de Detalle y Secciones Opcionales](#niveles-de-detalle-y-secciones-opcionales)
   - [Edición y Copia del Acta Generada](#edición-y-copia-del-acta-generada)
7. [Diccionario y Vocabulario Personalizado](#7-diccionario-y-vocabulario-personalizado)
   - [Añadir Términos Técnicos y Nombres Propios](#añadir-términos-técnicos-y-nombres-propios)
   - [Corrección Fonética Automática](#corrección-fonética-automática)
8. [Historial y Exportación de Reuniones](#8-historial-y-exportación-de-reuniones)
   - [Formatos Disponibles (Markdown, TXT, JSON, MP3)](#formatos-disponibles-markdown-txt-json-mp3)
   - [Estructura de la Carpeta de Reunión](#estructura-de-la-carpeta-de-reunión)
9. [Gestión y Almacenamiento de Modelos](#9-gestión-y-almacenamiento-de-modelos)
   - [Cambio de Directorio de Modelos](#cambio-de-directorio-de-modelos)
   - [Descarga y Eliminación Modular](#descarga-y-eliminación-modular)
10. [Idioma de la Interfaz](#10-idioma-de-la-interfaz)

---

## 1. Instalación y Primer Inicio

### Instalador vs. Versión Portable

DictaMeeting se distribuye en dos modalidades para Windows x64:

- **Instalador Oficial (`DictaMeeting-v1.5.2-Setup.exe`)**:
  - No requiere permisos de administrador (se instala en la cuenta del usuario actual).
  - Crea accesos directos en el menú Inicio y en el Escritorio.
  - Proporciona un desinstalador limpio desde *Configuración > Aplicaciones* de Windows.
- **Paquete Portable (`DictaMeeting-v1.5.2-win-x64-portable.zip`)**:
  - No requiere instalación.
  - Descomprime el archivo ZIP en cualquier carpeta o unidad externa y ejecuta `DictaMeeting.exe`.

### Asistente de Primer Arranque (First-Run Wizard)

La primera vez que abras DictaMeeting, se mostrará el asistente de bienvenida:

1. **Detección Automática de Hardware**: El sistema analiza los núcleos de tu CPU y la memoria RAM para sugerir los modelos ideales.
2. **Descarga en 1 Clic**: El botón **"Descargar configuración recomendada"** descarga automáticamente:
   - **Silero VAD v5** (~4 MB): Filtro inteligente de silencios y ruidos.
   - **Qwen3-ASR 0.6B INT8** (~600 MB): Motor neuronal ultra-rápido para transcripción en vivo en CPU.
3. Puedes omitir el asistente si deseas configurar los modelos manualmente más tarde desde la pantalla de Configuración.

---

## 2. Configuración de Fuentes de Audio

### Modos de Grabación

En la barra lateral izquierda puedes seleccionar qué fuentes capturar:

| Modo | Cuándo utilizarlo | Fuentes activas |
|---|---|---|
| **Videoconferencia** (Recomendado) | Microsoft Teams, Zoom, Google Meet, Slack, Webex | Micrófono físico + Audio del sistema (bucle invertido) |
| **Solo Micrófono** | Reuniones presenciales en sala, notas de voz o dictados individuales | Únicamente tu micrófono seleccionado |
| **Solo Sistema** | Seminarios web (*webinars*), vídeos grabados o llamadas donde no hablas | Únicamente el audio reproducido en Windows |

### Configuración del Micrófono

1. Abre el desplegable **Micrófono** y selecciona tu dispositivo de entrada (micrófono integrado, auriculares USB o micrófono de sobremesa).
2. Habla al micrófono: el vúmetro verde/azul oscilará indicando la señal entrante.

### Captura del Sistema (WASAPI Loopback) para Teams, Zoom o Meet

DictaMeeting utiliza la tecnología **WASAPI Loopback** nativa de Windows:

- No necesitas instalar cables virtuales de audio (Virtual Audio Cable) ni software adicional.
- Selecciona en **Dispositivo de Salida / Sistema** los altavoces o auriculares por los que escuchas a tus compañeros en la llamada.
- Todo lo que oigas a través de Teams, Zoom o tu navegador será capturado fielmente sin eco.

> [!TIP]
> Si utilizas auriculares en la videollamada, asegúrate de que el dispositivo seleccionado en DictaMeeting coincide exactamente con el dispositivo de salida configurado en Teams/Zoom.

### Prueba y Calibración de Vúmetros

Antes de iniciar la reunión, puedes comprobar que los niveles de entrada se encuentran en un rango óptimo:
- **Nivel Verde**: Volumen ideal de habla.
- **Nivel Amarillo**: Volumen alto.
- **Nivel Rojo**: Posible saturación. DictaMeeting incluye un limitador suave (*soft-clipper* con curva `tanh`) para evitar distorsiones digitales.

---

## 3. Durante la Reunión (Flujo en Vivo)

### Iniciar y Pausar la Grabación

1. En la parte superior, escribe opcionalmente un **Título de la Reunión** (por defecto: `Reunión YYYY-MM-DD`).
2. Pulsa el botón principal **[ Iniciar Reunión ]** (o presiona la barra espaciadora si el foco está en el botón).
3. El indicador cambiará a rojo pulsante con el tiempo transcurrido (`00:00:00`).
4. Si necesitas hacer un descanso o tratar un asunto privado fuera de acta, pulsa **[ Pausar ]**. Pulsa **[ Reanudar ]** para continuar.

### Visualización de Transcripción y Auto-Scroll Inteligente

- Las frases transcritas aparecen en pantalla con una latencia inferior a 1–2 segundos.
- **Comportamiento SmartScroll**:
  - Mientras el usuario se encuentra al final de la transcripción, la vista desciende suavemente a medida que se añaden nuevas intervenciones.
  - Si haces scroll hacia arriba con la rueda del ratón para consultar algo dicho minutos antes, el auto-scroll se pausa automáticamente para no interrumpir tu lectura.
  - Para reanudar el auto-scroll automático, simplemente vuelve a desplazarte hasta el final.

### Identificación de Hablantes y Renombrado en Caliente

DictaMeeting detecta acústicamente los diferentes participantes y les asigna provisionalmente etiquetas (`SPEAKER_00`, `SPEAKER_01`, etc.):

1. En el panel derecho de **Participantes**, verás la lista de interlocutores detectados, su tiempo de habla acumulado y el número de intervenciones.
2. **Renombrado en caliente**: Haz clic sobre cualquier nombre (ej. `SPEAKER_00`), escribe el nombre real (ej. `Aritz Villodas`) y presiona Intro o haz clic fuera del campo.
3. **Actualización instantánea**: Automáticamente, todos los segmentos pasados y futuros de ese interlocutor reflejarán el nombre real en pantalla.

### Tarjetas de Resumen en Vivo

Si tienes activado el resumen local (con el modelo Qwen 2.5 1.5B descargado):
- Cada ~35 segundos, el motor embebido analiza la conversación reciente.
- Una tarjeta superior muestra una síntesis ejecutiva concisa (2–4 líneas) para ponerte al día de inmediato si te has distraído o has entrado tarde a la reunión.

### Silenciamiento Rápido (Mute)

Haz clic en el icono del micrófono en la barra superior para silenciar rápidamente tu entrada local sin interrumpir la grabación del audio de los demás participantes.

---

## 4. Finalización y Procesamiento Post-Reunión

### Detener la Reunión

Al concluir la sesión, pulsa el botón **[ Detener ]**. Aparecerá un cuadro de confirmación para evitar cierres accidentales.

### Procesamiento Definitivo de Alta Calidad

Al detener la grabación, DictaMeeting ejecuta el pipeline de **post-procesado definitivo**:
1. **Consolidación de audio**: Cierra y optimiza el archivo `audio.mp3`.
2. **Transcripción definitiva de alta fidelidad**: Ejecuta Whisper sobre el audio completo para maximizar la precisión ortográfica y semántica.
3. **Diarización acústica global**: Analiza los patrones de voz a lo largo de toda la grabación para resolver solapamientos y refinar la separación de hablantes.
4. **Reconciliación de nombres**: Transfiere los nombres que asignaste durante la reunión a los clusters definitivos mediante el algoritmo `SpeakerReconciler`.

Un banner superior animado te mostrará el progreso porcentual. Puedes seguir navegando por la transcripción provisional mientras se completa el procesado.

---

## 5. Reproductor y Sincronización Audio-Texto

Una vez finalizada la reunión, DictaMeeting activa el **Reproductor Interactivo**:

- **Reproducir / Pausa**: Pulsa el botón de reproducción o presiona la barra espaciadora.
- **Salto directo al audio**: Haz clic sobre cualquier frase o palabra en la transcripción; el reproductor saltará instantáneamente al segundo exacto en el que fue pronunciada.
- **Resaltado en tiempo real**: Durante la reproducción, la frase que se está escuchando se resalta visualmente en la pantalla.
- **Barra de desplazamiento temporal**: Arrastra el cursor de la barra de progreso para avanzar o retroceder libremente.

---

## 6. Generación de Actas Ejecutivas con IA

DictaMeeting puede transformar la transcripción completa en un **acta ejecutiva profesional**:

### Configuración de Proveedor (OpenRouter o Servidor Propio)

Ve a **Configuración > Inteligencia Artificial**:
- **Opción A: OpenRouter AI**:
  - Introduce tu API Key de OpenRouter (`sk-or-...`).
  - Elige el modelo deseado: *Claude 3.5 Sonnet* (recomendado), *GPT-4o*, *Gemini 2.0 Flash*, *Llama 3.3 70B*, *DeepSeek V3* o especifica un identificador personalizado.
  - La clave se cifra inmediatamente con **Windows DPAPI** (`ProtectedData.Protect`). Solo tu usuario de Windows puede leerla.
- **Opción B: Servidor Local / Personalizado (Compatible con OpenAI)**:
  - Activa la casilla de servidor personalizado.
  - Endpoint: ej. `http://localhost:11434/v1` (Ollama), `http://localhost:1234/v1` (LM Studio), o tu pasarela corporativa LiteLLM / vLLM.
  - Introduce el nombre del modelo local (ej. `llama3.3`, `qwen2.5-72b`).

### Niveles de Detalle y Secciones Opcionales

Al pulsar **[ Generar Acta ]** en una reunión completada, puedes personalizar:
- **Idioma del Acta**: Español o English.
- **Nivel de Detalle**:
  - *Breve*: Puntos clave y decisiones en 1 página.
  - *Normal*: Resumen equilibrado con tabla de acciones.
  - *Detallada*: Registro completo de debates, argumentos y acuerdos.
  - *Exhaustiva*: Síntesis minuciosa de cada intervención relevante.
- **Secciones Opcionales**:
  - Lista de Asistentes y Participación.
  - Decisiones Firmes Acordadas.
  - Tabla de Compromisos y Tareas (Acción | Responsable | Plazo).
  - Cuestiones Pendientes y Próximos Pasos.

### Edición y Copia del Acta Generada

El acta se presenta en un visor con soporte de edición directa. Puedes:
- Editar el texto directamente para añadir notas o correcciones.
- Copiar todo el contenido al portapapeles con el botón **[ Copiar ]**.
- El acta se guarda automáticamente en `acta.md` dentro de la carpeta de la reunión.

---

## 7. Diccionario y Vocabulario Personalizado

Para evitar que los modelos de voz confundan términos técnicos, nombres propios de la empresa o acrónimos:

1. Dirígete a **Configuración > Vocabulario**.
2. Escribe los términos que sueles utilizar en tus reuniones, separados por comas o líneas (ej. `Kubernetes, OAuth2, CRM, Tecnalia, WebSockets`).
3. Pulsa **[ Guardar Vocabulario ]**.
4. El motor fonético (`PhoneticRuleEngine`) ajustará las probabilidades y corregirá automáticamente las transcripciones en vivo.

---

## 8. Historial y Exportación de Reuniones

En la pestaña **Historial**:
- Consulta todas tus sesiones grabadas ordenadas cronológicamente.
- **Búsqueda en tiempo real**: Busca por título, fecha o palabras habladas dentro del contenido de las reuniones.

### Formatos Disponibles y Botones de Exportación

Con un solo clic puedes abrir o exportar:
- **[ 📁 Abrir Carpeta ]**: Abre el explorador de archivos de Windows en la carpeta de la sesión.
- **[ 📄 Markdown (.md) ]**: Transcripción formateada con metadatos, participantes y marcas de tiempo, ideal para Obsidian, Notion o GitHub.
- **[ 📝 Texto (.txt) ]**: Archivo de texto plano legible.
- **[ ⚙ JSON (.json) ]**: Archivo estructurado con metadatos, participantes, duración y matriz completa de segmentos con marcas de tiempo.

### Estructura de la Carpeta de Reunión

```
Meetings/
└── 2026-10-01_Revision-Trimestral/
    ├── meeting.json       # Datos estructurados y metadatos
    ├── transcript.md      # Transcripción completa en Markdown
    ├── transcript.txt     # Transcripción en texto plano
    ├── audio.mp3          # Grabación de audio comprimida
    └── acta.md            # Acta ejecutiva generada con IA (si se solicitó)
```

---

## 9. Gestión y Almacenamiento de Modelos

En **Configuración > Modelos**:

### Cambio de Directorio de Modelos
- Por defecto, los modelos se almacenan en `%LocalAppData%\DictaMeeting\models\`.
- Si tu unidad `C:` tiene poco espacio libre, puedes reubicar la carpeta a un disco secundario (ej. `D:\AI-Models`). DictaMeeting moverá los archivos descargados de forma segura preservando su integridad.

### Descarga y Eliminación Modular
- Consulta el estado de cada modelo: *No descargado*, *Listo* o *Descargando*.
- Descarga o elimina modelos individualmente con un solo clic.

---

## 10. Idioma de la Interfaz

DictaMeeting incluye soporte bilingüe nativo (**Español** e **Inglés**):
- En la esquina superior derecha, haz clic en el selector de idioma para alternar entre **ES** y **EN**.
- Toda la interfaz se actualiza **al instante sin reiniciar la aplicación**.
- Tu preferencia se almacena y se recordará en los próximos inicios.
