# Contribuir a DictaMeeting

**[🇪🇸 Español](CONTRIBUTING.es.md) | [🇬🇧 English](CONTRIBUTING.md)**

¡Gracias por tu interés en contribuir a DictaMeeting!

DictaMeeting es una aplicación de código abierto para Windows orientada a la privacidad local (*local-first*) para grabación, transcripción, diarización y generación de actas de reuniones.

## Código de Conducta

Por favor, trata a todas las personas con respeto, amabilidad y profesionalidad.

## Primeros Pasos

1. **Requisitos previos**:
   - Windows 10/11 (x64)
   - [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
   - Visual Studio 2022 o VS Code con C# Dev Kit
   - (Opcional) [Inno Setup 6](https://jrsoftware.org/isdl.php) para generar el instalador de escritorio.

2. **Clonar y compilar**:
   ```powershell
   git clone https://github.com/oakmusic/dictameeting.git
   cd dictameeting
   dotnet restore
   dotnet build
   ```

3. **Ejecutar pruebas unitarias**:
   ```powershell
   dotnet test
   ```
   Las pruebas se ejecutan sin conexión y no requieren descargar los pesos de los modelos neuronales (los tests que requieran modelos locales se omitirán limpiamente si no están descargados).

## Directrices de Desarrollo

- **Arquitectura Limpia y Separación de Responsabilidades**: Mantén el código de interfaz WPF estrictamente en `DictaMeeting.App` utilizando MVVM. La lógica de audio, transcripción, diarización e IA debe permanecer desacoplada y ser testeable mediante pruebas unitarias.
- **Privacidad ante todo**: DictaMeeting es una herramienta orientada a la privacidad local. No introduzcas llamadas de red ni telemetría a menos que el usuario lo configure explícitamente (como la generación de actas con LLM tras la reunión).
- **Internacionalización (i18n)**: Todas las cadenas de la UI y los mensajes de diálogo orientados al usuario deben residir en `Strings.resx` / `Strings.es.resx` / `Strings.en.resx` y accederse mediante `LocExtension` o `LocalizationManager`.
- **Calidad de código**: Sigue las convenciones estándar de C# y el uso de tipos de referencia que admiten valores nulos (*nullable reference types*).

## Solicitudes de Extracción (Pull Requests)

1. Haz un fork del repositorio y crea una rama descriptiva:
   ```bash
   git checkout -b feature/mi-mejora
   ```
2. Escribe código limpio y autodocumentado, y añade pruebas donde sea oportuno.
3. Verifica que `dotnet build` y `dotnet test` pasen correctamente.
4. Envía un Pull Request describiendo tus cambios, la motivación y las pruebas realizadas.
