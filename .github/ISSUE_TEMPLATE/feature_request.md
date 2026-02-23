---
name: Feature Request
about: Suggest an idea or new feature for AniPlayer
title: '[FEATURE] '
labels: enhancement
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
- **Service-based DI** — All services are singletons resolved from `App.Services`
- **Pages receive data via typed method calls**, not constructor parameters

---

## Feature Description

**Is your feature request related to a problem? Please describe.**
A clear and concise description of what the problem is. Ex. I'm always frustrated when [...]

**Describe the solution you'd like:**
A clear and concise description of what you want to happen.

**Describe alternatives you've considered:**
A clear and concise description of any alternative solutions or features you've considered.

---

## Implementation Considerations

**Which layer would this feature belong to?**
- [ ] **Core** (`Aniplayer.Core/Services/`) — Business logic, database operations, API calls
- [ ] **UI** (`AniPlayer.UI/Views/`) — User interface, rendering only
- [ ] **Both** — Please explain why

**Would this feature require:**
- [ ] New database tables/columns
- [ ] New Core service interface and implementation
- [ ] Changes to existing services
- [ ] New UI pages or controls
- [ ] External dependencies (NuGet packages, native libraries)

**Architectural compliance:**
Please explain how this feature would fit within the existing architecture without violating the "No MVVM", "No EF", and "No business logic in UI" rules.

---

## Additional Context

Add any other context, mockups, or screenshots about the feature request here.
