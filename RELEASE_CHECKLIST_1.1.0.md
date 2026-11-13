# StoryEditor 1.1.0 release checklist

Status: source commit and tag v1.1.0 are published to GitHub. The GitHub Release page and binary assets are still pending.

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
- Archive SHA-256: `2214FA492A65106389ABE8F8B1BAB601325A6E7B21893925F2F17AF5CC7E1B03`
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

- [x] Review final source changes and package contents.
- [x] Commit `ca674ce` and push `main` to `origin`.
- [x] Create and push tag `v1.1.0`.
- [ ] Create the GitHub Release for `v1.1.0` and upload the ZIP plus SHA-256 sidecar. The connected GitHub actions support reading releases but do not expose release creation or asset upload; complete this final step in the GitHub web interface.
