# Security Policy

**[🇬🇧 English](SECURITY.md) | [🇪🇸 Español](SECURITY.es.md)**

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 1.5.x   | :white_check_mark: |

## Privacy and Data Security

DictaMeeting is designed with a strict **local-first privacy model**:

- **Audio and Transcription**: All microphone and loopback audio streams, VAD processing, speaker diarization, and ASR transcription run 100% locally on your machine. No raw audio is ever transmitted over the network.
- **Local Summarization**: In-meeting live summaries use an embedded local `llama.cpp` runtime with zero cloud connectivity.
- **AI Minutes Generation (Optional)**: Cloud-based meeting minutes generation only connects to user-specified endpoints (OpenRouter or OpenAI-compatible gateways) when explicitly requested by the user.
- **Secret Storage**: API keys are securely protected using the Windows Data Protection API (DPAPI) bound to the current Windows user account (`ProtectedData.Protect`). Keys are never saved in plaintext configuration files.

## Reporting a Vulnerability

If you discover a security vulnerability or security-related issue in DictaMeeting, please do **NOT** open a public issue.

Instead, please report it privately:

1. Send an email to the project maintainers via GitHub contact or open a [Private Vulnerability Report](https://github.com/oakmusic/dictameeting/security/advisories/new) on GitHub.
2. Provide detailed steps to reproduce the vulnerability, including your environment and version.
3. We will acknowledge receipt of your report within 48 hours and work with you on a timely resolution before public disclosure.
