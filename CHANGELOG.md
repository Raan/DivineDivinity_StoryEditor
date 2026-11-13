# Changelog

## 1.1.0 — Osiris editing and grammar validation

- Added a whole-Story grammar validator for lexical, structural, IF/PROC rule, and INIT/EXIT syntax checks.
- Added debounced background validation that includes unsaved editor changes.
- Added a Grammar diagnostics panel with navigation to the exact source location of an issue.
- Improved parser recovery so a malformed condition does not cascade into hundreds of misleading diagnostics.
- Reworked KB rule editing around direct source-text editing instead of condition/action move buttons.
- Added context-menu insertion and symbol-aware autocomplete for API actions and Story procedures.
- Added matching-bracket highlighting and consistent syntax assistance in KB, INIT, and EXIT.
- Added bilingual English/Russian Osiris syntax help.
- Removed the temporary debug startup that opened a hard-coded game Story file; the editor now starts without a document unless a path is explicitly supplied on the command line.
- Added a package-free regression test project and integrated it into the Windows CI workflow.

## 1.0.0 — MVP release candidate

- Added integrated binary Story decompilation in the WPF editor.
- Added a standalone StoryDecompiler CLI backed by the shared Core library.
- Added Goal hierarchy navigation and INIT / KB / EXIT inspection.
- Added IF / PROC rule navigation and structural editing workflow.
- Added Cross References, Goal Links, and Symbol Explorer.
- Added original OsirisDLL-based compile, merge, and read-only structural validation.
- Added game installation discovery and manual configuration.
- Published as a self-contained Windows x64 single-file application.
- Embedded the build and validation PowerShell scripts in the application.
- Confirmed that a rebuilt Story binary works in Divine Divinity (maintainer test).

- Renamed the user-facing application and executable to StoryEditor.
- Reworked Output / diagnostics into a compact summary with proper line breaks.
- Embedded the two PowerShell build scripts so the release can be a single executable.
- Added bilingual GitHub documentation and a Windows CI workflow.
