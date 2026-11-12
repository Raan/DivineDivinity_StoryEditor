# Changelog

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
