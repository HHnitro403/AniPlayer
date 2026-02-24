# Project Mandates

## 1. Zero Scope Creep
- **PROHIBITED:** Implementing features that were not explicitly requested.
- **PROHIBITED:** "Tweaking" or "polishing" code that is already functional.
- **PROHIBITED:** Changing styling or UI behavior unless specifically asked.

## 2. No Unauthorized Refactoring
- **PROHIBITED:** Refactoring code because you "think" it needs it.
- **PROHIBITED:** Cleaning up, reorganizing, or modernizing code without a direct directive.
- **EXCEPTION:** Refactoring is only permitted when explicitly requested by the user.

## 3. Architectural Constraints
- **PROHIBITED:** Using MVVM (Model-View-ViewModel).
- **MANDATORY:** Stick to the existing "Service-Based Passive View" architecture (Code-behind + Services).

## 4. Critical Systems - DO NOT TOUCH
- **PROHIBITED:** Modifying the Player Engine (mpv interop, playback logic) without explicit permission.
- **PROHIBITED:** Modifying the Backbone (Folder Watcher, Scanner, File System logic) without explicit permission.
- **REASON:** These systems are critical and complex. Stability is paramount.

## 5. General Workflow
- **MANDATORY:** Work strictly within the boundaries of the user's request.
- **MANDATORY:** If a change seems necessary but wasn't asked for, **ASK FIRST**.
