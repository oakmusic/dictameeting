# Contributing to DictaMeeting

**[🇬🇧 English](CONTRIBUTING.md) | [🇪🇸 Español](CONTRIBUTING.es.md)**

Thank you for your interest in contributing to DictaMeeting!

DictaMeeting is an open-source, local-first meeting transcription, diarization, and minutes generation application for Windows.

## Code of Conduct

Please treat everyone with respect, kindness, and professionalism.

## Getting Started

1. **Prerequisites**:
   - Windows 10/11 (x64)
   - [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
   - Visual Studio 2022 or VS Code with C# Dev Kit
   - (Optional) [Inno Setup 6](https://jrsoftware.org/isdl.php) if building the desktop installer.

2. **Clone and Build**:
   ```powershell
   git clone https://github.com/oakmusic/dictameeting.git
   cd dictameeting
   dotnet restore
   dotnet build
   ```

3. **Running Tests**:
   ```powershell
   dotnet test
   ```
   Tests run offline and do not require downloaded AI model weights (tests requiring local models will skip cleanly if not present).

## Development Guidelines

- **Clean Architecture & Separation of Concerns**: Keep WPF UI code strictly in `DictaMeeting.App` using MVVM. Audio, transcription, diarization, and AI logic remain decoupled and unit-testable.
- **Privacy First**: DictaMeeting is local-first. Do not introduce network calls or telemetry unless explicitly configured by the user (such as post-meeting LLM minutes generation).
- **Internationalization (i18n)**: All UI strings and user-facing dialog messages must reside in `Strings.resx` / `Strings.es.resx` / `Strings.en.resx` and be accessed via `LocExtension` or `LocalizationManager`.
- **Code Quality**: Follow standard C# coding conventions and nullable reference types.

## Pull Requests

1. Fork the repository and create a descriptive branch:
   ```bash
   git checkout -b feature/my-improvement
   ```
2. Write clean, self-documenting code and add tests where appropriate.
3. Verify that `dotnet build` and `dotnet test` pass cleanly.
4. Submit a Pull Request describing your changes, motivation, and any testing performed.
