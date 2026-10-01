# Política de Seguridad

**[🇪🇸 Español](SECURITY.es.md) | [🇬🇧 English](SECURITY.md)**

## Versiones Soportadas

| Versión | Soportada          |
| ------- | ------------------ |
| 1.5.x   | :white_check_mark: |

## Privacidad y Seguridad de Datos

DictaMeeting está diseñado bajo un estricto **modelo de privacidad local (*local-first*)**:

- **Audio y Transcripción**: Todos los flujos de audio de micrófono y bucle invertido (*loopback*), procesamiento VAD, diarización de hablantes y transcripción ASR se ejecutan 100% de forma local en tu equipo. Ningún audio sin procesar se transmite jamás a través de la red.
- **Resumen Local**: Los resúmenes en vivo durante las reuniones utilizan un runtime embebido local de `llama.cpp` sin ninguna conectividad con la nube.
- **Generación de Actas con IA (Opcional)**: La generación de actas de reunión mediante IA en la nube solo se conecta a los puntos de enlace (*endpoints*) especificados por el usuario (OpenRouter o servidores compatibles con OpenAI) cuando el usuario lo solicita explícitamente.
- **Almacenamiento de Secretos**: Las claves de API se protegen de forma segura mediante la API de Protección de Datos de Windows (DPAPI) vinculada a la cuenta del usuario de Windows actual (`ProtectedData.Protect`). Las claves nunca se guardan en texto plano en archivos de configuración.

## Notificación de Vulnerabilidades

Si descubres una vulnerabilidad de seguridad o un problema relacionado con la seguridad en DictaMeeting, por favor **NO** abras una incidencia (*issue*) pública.

En su lugar, notifícalo de forma privada:

1. Envía un correo electrónico a los mantenedores del proyecto a través del contacto de GitHub o abre un [Informe de Vulnerabilidad Privado](https://github.com/oakmusic/dictameeting/security/advisories/new) en GitHub.
2. Proporciona pasos detallados para reproducir la vulnerabilidad, indicando tu entorno y versión.
3. Confirmaremos la recepción de tu informe en un plazo máximo de 48 horas y trabajaremos contigo para solucionarlo de manera oportuna antes de cualquier divulgación pública.
