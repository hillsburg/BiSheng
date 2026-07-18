# Contributing to BiSheng

Thank you for your interest in contributing to BiSheng (Latte)! This document explains how to get started, our conventions, and what we expect in pull requests.

> **Languages:** Please write **issues and pull requests in English** when possible, so international contributors can follow along. Code comments remain in **Simplified Chinese** (see [Code style](#code-style)).

---

## Table of contents

- [Before you start](#before-you-start)
- [Development setup](#development-setup)
- [Project structure](#project-structure)
- [Code style](#code-style)
- [Commit messages](#commit-messages)
- [Pull request process](#pull-request-process)
- [Testing](#testing)
- [Areas where help is welcome](#areas-where-help-is-welcome)
- [Reporting bugs](#reporting-bugs)
- [Security issues](#security-issues)

---

## Before you start

1. Check [existing issues](https://github.com/YOUR_ORG/Bisheng/issues) to avoid duplicate work.
2. For large changes (new features, architectural refactors), open an issue first to discuss scope.
3. BiSheng is a **Windows WPF** application. The server (`BiSheng.Server`) is cross-platform (.NET 8), but the main client requires Windows.

---

## Development setup

### Prerequisites

- Windows 10+ (for client development)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 or VS Code with C# Dev Kit (recommended)
- Git

### Build and run

```bash
git clone https://github.com/YOUR_ORG/Bisheng.git
cd Bisheng

dotnet build Bisheng.slnx -c Release

# Desktop client
dotnet run --project src/BiSheng.Latte/BiSheng.Latte.csproj -c Release

# Sync server (optional)
dotnet run --project src/BiSheng.Server/BiSheng.Server.csproj -c Release
```

### Run tests

```bash
dotnet test tests/BiSheng.Server.Tests/BiSheng.Server.Tests.csproj -c Release
dotnet test tests/BiSheng.Latte.Tests/BiSheng.Latte.Tests.csproj -c Release
dotnet test tests/BiSheng.Editor.Tests/BiSheng.Editor.Tests.csproj -c Release
```

All tests should pass before submitting a PR. CI runs on `windows-latest` for Server and Latte test projects.

---

## Project structure

| Project | Role |
|---------|------|
| `BiSheng.Latte` | WPF shell — navigation, sync, settings, themes |
| `BiSheng.Editor` | Markdown instant-rendering editor control |
| `BiSheng.Server` | Self-hosted sync API + admin UI |
| `BiSheng.Shared` | Sync DTOs and protocol helpers (no UI) |
| `ICSharpCode.AvalonEdit` | Vendored text editor core |

Design documents (Chinese) live in `src/docs/`. Read the relevant doc before touching sync, navigation, or rendering subsystems.

---

## Code style

BiSheng follows conventions defined in [`.cursor/rules/code-conventions.mdc`](.cursor/rules/code-conventions.mdc). Key points:

### Comments

- Use **Simplified Chinese** for code comments and XML documentation.
- Add XML doc comments (`/// <summary>`) on **enums, properties, methods**, and public types.
- Comment non-obvious business logic and complex algorithms.

### Formatting

- Place opening and closing **curly braces on their own lines** (Allman style).
- Add a **blank line after every closing brace** `}`.
- Match surrounding code indentation and naming patterns.

### C# general

- Target **.NET 8**; nullable reference types are enabled — avoid suppressing warnings without reason.
- Prefer extending existing services and patterns over introducing parallel abstractions.
- Keep diffs focused; avoid unrelated formatting or drive-by refactors in the same PR.

### XAML

- Follow existing resource naming: `Brush.*` for theme brushes, `Icon.*` for icon glyphs.
- Use `DynamicResource` for theme-aware colors; `StaticResource` for fixed icons/fonts.

### Strings and localization

- UI strings are currently hardcoded in Chinese. When adding new user-visible text:
  - Prefer centralizing in one place (e.g. `AppDialog`, `ConnectionDisplay`, context menu builders).
  - Avoid scattering duplicate literals (toolbar has top and side layouts — extract shared strings).
  - English localization (`Strings.resx`) is planned; use descriptive string keys mentally even if not yet in resx.

---

## Commit messages

Write clear, concise commit messages in **English** or **Chinese** — either is fine, but be consistent within a PR.

Preferred format:

```
<type>: <short summary>

Optional body explaining why, not just what.
```

Types: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `ci`.

Examples:

```
feat: add reveal-active-note button to navigation panel

fix: prevent NullReferenceException in TreeNavigationPanel OnUnloaded

docs: add English README and CONTRIBUTING guide
```

---

## Pull request process

1. **Fork** the repository and create a branch from `main` (or `master`).
2. Make your changes with tests where applicable.
3. Ensure `dotnet build Bisheng.slnx -c Release` succeeds.
4. Run relevant `dotnet test` projects.
5. Open a PR with:
   - **What** changed (brief summary)
   - **Why** (problem or feature motivation)
   - **How to test** (manual steps or test commands)
   - Screenshots for UI changes
6. Link related issues (`Fixes #123`).
7. Wait for review. Address feedback with additional commits or squash as requested.

### PR scope

- One logical change per PR when possible.
- Do not commit build outputs (`bin/`, `obj/`), local databases (`*.db`), uploads, IDE folders (`.vs/`), or user-specific config.
- Do not include unrelated generated files.

---

## Testing

| Project | Framework | Notes |
|---------|-----------|-------|
| `BiSheng.Server.Tests` | xUnit | API, sync, mutations — runs in CI |
| `BiSheng.Latte.Tests` | xUnit + StaFact | WPF/threading-sensitive tests — runs in CI |
| `BiSheng.Editor.Tests` | xUnit | Editor parsing/rendering |

When fixing a bug, add a test if the area already has test coverage. For UI-only changes, describe manual test steps in the PR.

---

## Areas where help is welcome

| Area | Ideas |
|------|-------|
| **Documentation** | English architecture overview, API docs, deployment guide translation |
| **Internationalization** | `en-US` UI strings, `LocalizationService` infrastructure |
| **Tests** | Editor tests in CI, sync edge cases, navigation projection |
| **Server** | Hardening, Docker compose example, OpenAPI improvements |
| **Accessibility** | Keyboard navigation, screen reader labels |
| **Bug fixes** | See open issues |

---

## Reporting bugs

Include:

1. **BiSheng version** or git commit hash
2. **Windows version**
3. **Steps to reproduce**
4. **Expected vs actual behavior**
5. **Logs** — client logs under `log/app-*.log` next to the executable (redact API keys and server URLs)
6. **Screenshots** if UI-related

For sync issues, also note whether you use `BiSheng.Server` self-hosted or offline-only mode.

---

## Security issues

**Do not** report security vulnerabilities in public issues.

Email the maintainers privately or use GitHub Security Advisories once enabled. See [SECURITY.md](SECURITY.md) (planned).

---

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE), the same license that covers the project.

---

## Questions?

Open a [GitHub Discussion](https://github.com/YOUR_ORG/Bisheng/discussions) or issue if Discussions are not enabled yet. We are happy to help you find a good first task.
