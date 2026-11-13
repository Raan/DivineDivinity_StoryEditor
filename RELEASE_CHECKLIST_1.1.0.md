# StoryEditor 1.1.0 release checklist

Status: local release candidate prepared. No Git commit, push, tag, or GitHub Release has been created.

## Release contents

- [x] Version metadata and window title identify 1.1.0.
- [x] Normal startup opens no hard-coded Story file; explicit command-line file paths remain supported.
- [x] English and Russian README files describe the 1.1.0 functionality.
- [x] Changelog includes the 1.1.0 changes.
- [x] Windows CI package and artifact names identify 1.1.0.

## Automated validation

- [x] Release solution build succeeds without warnings or errors.
- [x] Full regression test suite passes (18/18).
- [x] Full game Story passes grammar validation.
- [x] `git diff --check` succeeds.
- [x] Published single-file Windows x64 executable is produced.
- [x] SHA-256 checksums are recorded.

## Local package

- Directory: `C:\\StoryEditor_Builds\\StoryEditor_1.1.0_20261113_publish`
- Archive: `C:\\StoryEditor_Builds\\StoryEditor-1.1.0-win-x64.zip`
- Archive SHA-256: `1AF946784B3D32D8A8FED0BFF4CF060AB231B4F4A3A53A4F67081535AAA7C6E0`
- Executable SHA-256: `0F51199552AC03FBD7C963D856D32FB84CAFCA10FFC85C9C6D01EEE7CE6A7433`

## Maintainer verification before public announcement

- [ ] Start the published executable and confirm it opens without loading a document automatically.
- [ ] Open a `.div` file manually and verify Goal navigation and editing.
- [ ] Verify Grammar diagnostics and double-click navigation.
- [ ] Verify Help language switching and editor assistance.
- [ ] Decompile a game Story and build to a separate output file.
- [ ] Confirm installed game files remain unchanged.
- [x] Test the release build in Divine Divinity — user confirmed that everything works.

## Publication

- [ ] Review final source changes and package contents.
- [ ] Create commit and push only after explicit approval.
- [ ] Create GitHub tag/release only after explicit approval.
