# DictaMeeting 🎙 — Guía de Solución de Problemas y Preguntas Frecuentes

**[🇪🇸 Español](TROUBLESHOOTING.md) | [🇬🇧 English](TROUBLESHOOTING.en.md)**

Esta guía recopila las dudas más comunes y las soluciones a problemas técnicos frecuentes al utilizar **DictaMeeting**.

---

## 🎧 1. Problemas de Captura de Audio

### El vúmetro del micrófono no se mueve o no transcribe mi voz
1. **Comprueba los permisos de micrófono en Windows**:
   - Abre *Configuración de Windows > Privacidad y seguridad > Micrófono*.
   - Asegúrate de que la opción **"Permitir que las aplicaciones accedan al micrófono"** y **"Permitir que las aplicaciones de escritorio accedan al micrófono"** estén activadas.
2. **Dispositivo en silencio en Windows**:
   - Comprueba que el micrófono no esté silenciado físicamente mediante un botón en el cable o en los auriculares.
   - En *Configuración > Sistema > Sonido*, verifica que el volumen de entrada del micrófono esté al 70–100%.
3. **Selección del dispositivo correcto**:
   - En la barra lateral de DictaMeeting, asegúrate de que el desplegable **Micrófono** tiene seleccionado tu dispositivo real y no un controlador virtual inactivo.

---

### No se escucha o no se transcribe a los interlocutores de Teams, Zoom o Meet
1. **Comprueba el dispositivo de salida seleccionado**:
   - DictaMeeting captura el audio del sistema mediante el dispositivo configurado en **Salida / Sistema**.
   - Si en Microsoft Teams o Zoom tienes configurados tus auriculares USB (ej. *"Jabra Evolve"*), pero en DictaMeeting está seleccionado *"Altavoces Realtek"*, DictaMeeting no podrá capturar el audio de la llamada.
   - **Solución**: Selecciona en DictaMeeting exactamente el mismo dispositivo de salida por el que escuchas a tus compañeros.
2. **Modo de grabación adecuado**:
   - Asegúrate de que el modo de grabación seleccionado sea **Videoconferencia** (ambos canales activos) o **Solo Sistema**.

---

## ⚡ 2. Rendimiento y Uso de CPU

### El uso de CPU es elevado durante la reunión
1. **Utiliza el modelo ligero de tiempo real**:
   - Ve a *Configuración > Modelos* y asegúrate de utilizar **Qwen3-ASR 0.6B INT8**.
   - El modelo `1.7B` ofrece mayor precisión técnica pero requiere una CPU de gama alta.
2. **Desactiva o ajusta el resumen local**:
   - Si tu equipo tiene 4 núcleos o menos, el resumen en vivo local (`llama.cpp`) puede competir por ciclos de reloj. Puedes pausarlo o aumentar el intervalo de actualización desde la configuración.
3. **Modelo de post-procesado**:
   - Para la fase final tras detener la reunión, utiliza **Whisper Small** o **Whisper Base** en lugar de *Medium* o *Large-v3 Turbo*.

---

## 🛡 3. Seguridad, Antivirus y SmartScreen

### Windows Defender o SmartScreen muestra "Windows protegió su PC"
- **Causa**: Al ser una aplicación de código abierto distribuida directamente sin un certificado de firma digital comercial EV (Extended Validation), Windows SmartScreen puede mostrar una advertencia preventiva en la primera ejecución.
- **Solución**: Haz clic en **"Más información"** y luego en **"Ejecutar de todas formas"**.
- DictaMeeting es 100% de código abierto y auditable: todo el código fuente y los scripts de compilación están disponibles públicamente para inspección en el repositorio.

### ¿Dónde se guardan mis contraseñas y claves de API?
- Las claves de API de OpenRouter se almacenan cifradas en tu perfil de usuario mediante **Windows DPAPI** (`ProtectedData.Protect` con ámbito `CurrentUser`).
- Nunca se almacenan en texto plano en ningún archivo ni se comparten a través de la red.

---

## 🤖 4. Generación de Actas con IA

### Error 401 (Unauthorized) al generar acta
- **Causa**: La API Key de OpenRouter introducida es incorrecta o ha sido revocada.
- **Solución**: Ve a *Configuración > Inteligencia Artificial*, borra la clave y vuelve a pegar una clave válida con formato `sk-or-v1-...`.

### Error 402 (Payment Required)
- **Causa**: Tu saldo de créditos en OpenRouter se ha agotado.
- **Solución**: Recarga créditos en tu cuenta de [OpenRouter.ai](https://openrouter.ai).

### Error al conectar con un servidor local (Ollama / LM Studio / LiteLLM)
1. Comprueba que el servidor local esté ejecutándose (ej. ejecuta `ollama list` en una terminal).
2. Asegúrate de incluir el prefijo `http://` y el sufijo `/v1` en el endpoint:
   - Ollama: `http://localhost:11434/v1`
   - LM Studio: `http://localhost:1234/v1`
   - LiteLLM: `http://localhost:4000/v1`
3. Comprueba que el nombre del modelo configurado en DictaMeeting coincide exactamente con el modelo descargado en tu servidor local (ej. `llama3.2`, `qwen2.5:7b`).

---

## 💾 5. Almacenamiento y Archivos

### Mi disco C: se está quedando sin espacio
- Los modelos neuronales de IA ocupan entre 1 GB y 4 GB en disco.
- Ve a **Configuración > Modelos** y haz clic en **"Cambiar carpeta de modelos"**.
- Selecciona una carpeta en otra unidad (ej. `D:\DictaMeeting\models`).
- La aplicación migrará automáticamente todos los archivos existentes sin pérdida de datos.

### ¿Dónde se guardan las grabaciones y transcripciones de mis reuniones?
- Por defecto se guardan en la carpeta de reuniones configurada por el usuario (o en `%LocalAppData%\DictaMeeting\Meetings\`).
- Puedes acceder inmediatamente a los archivos de cualquier reunión haciendo clic en el botón **[ 📁 Abrir Carpeta ]** en la pantalla principal o en el Historial.
