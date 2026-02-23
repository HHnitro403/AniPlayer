---
name: Bug Report
about: Report a bug or issue with AniPlayer
title: '[BUG] '
labels: bug
assignees: ''
---

## ⚠️ Before Submitting

**Have you read the architectural guidelines?**
- [ ] I have read [AniPlayer_ProjectScope.md](../../AniPlayer_ProjectScope.md)
- [ ] I have read [CONTRIBUTING.md](../../CONTRIBUTING.md)
- [ ] I understand that code contributions MUST follow the project architecture (except `PlayerPage.axaml` and `PlayerPage.axaml.cs` which have special exemptions)

**Architecture Requirements (from ProjectScope.md):**
- **No MVVM** — Code-behind pattern only
- **No Entity Framework** — SQLite + Dapper with hand-written SQL
- **No logic in UI files** — Business logic belongs in `Aniplayer.Core/Services/`
- **Avalonia dependencies MUST NOT appear in Core project**

---

## Bug Description

**Describe the bug:**
A clear and concise description of what the bug is.

**To Reproduce:**
Steps to reproduce the behavior:
1. Go to '...'
2. Click on '...'
3. Scroll down to '...'
4. See error

**Expected behavior:**
A clear and concise description of what you expected to happen.

**Screenshots:**
If applicable, add screenshots to help explain your problem.

---

## Environment

**Desktop (please complete the following information):**
- OS: [e.g. Windows 11, Ubuntu 22.04, macOS 14]
- .NET Version: [e.g. .NET 9.0]
- AniPlayer Version/Commit: [e.g. commit hash or release version]
- libmpv Version: [e.g. 0.36.0]

**Additional context:**
Add any other context about the problem here.

---

## Logs

**Debug logs** (from `%APPDATA%/AniPlayer/debug.log` on Windows or `~/.config/AniPlayer/debug.log` on Linux):
```
Paste relevant log entries here
```

**Error messages:**
```
Paste any error messages or stack traces here
```
