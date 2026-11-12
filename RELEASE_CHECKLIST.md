# MVP 1.0.0 release checklist

Status: candidate package prepared; maintainer end-to-end verification is still pending.

## Automated checks completed

- [x] Clean GitHub staging solution builds in Release with 0 warnings and 0 errors.
- [x] Standalone StoryDecompiler builds from the clean repository.
- [x] Decompiling the clean game's story.000 produces the same SHA-256 output as the previously validated Run47 source.
- [x] Decompiler reports 14,322 objects, 336 API declarations, 478 Goals, 4,918 rules, 24 inferred PROC prototypes, 26 inferred parameters, 0 unassigned rules, and 0 unread bytes.
- [x] Single-file Windows x64 publish produces StoryEditor.exe.
- [x] Published executable starts successfully.
- [x] Embedded build scripts are extracted to %LOCALAPPDATA%\StoryEditor\Scripts by the script provider.
- [x] Release ZIP integrity check passed.
- [x] App starts successfully after extracting the ZIP to a separate folder.
- [x] Release package contains no game DLL or game archives.
- [x] Source repository has bilingual README files, .gitignore, .gitattributes, changelog, solution, and Windows CI workflow.

## Maintainer tests still required

- [ ] Run StoryEditor.exe from the extracted ZIP, not from a build folder.
- [ ] Confirm the window title and executable name are StoryEditor.
- [ ] Open a .div file and select a Goal; verify Output / diagnostics uses real line breaks and the compact summary.
- [ ] Verify unresolved calls are described as review candidates, not parser errors.
- [ ] Configure the game installation and restart the packaged app; confirm settings persist.
- [ ] Decompile the installed story.000 from the packaged app and open the resulting .div.
- [ ] Build a new Story binary from the packaged app.
- [ ] Confirm the output is separate from the installed game's story.000.
- [ ] Confirm the original game files remain unchanged.
- [ ] Test the produced binary in Divine Divinity.

## Package

- ZIP: StoryEditor-1.0.0-win-x64.zip
- Payload: StoryEditor.exe plus English/Russian README and changelog.
- Target: Windows x64, self-contained .NET 8.
- The original 32-bit OsirisDLL.dll and all game data are excluded.
- Story building requires the user's own game installation and 32-bit Windows PowerShell.

## Before publishing the GitHub repository

- [x] MIT License selected and added as LICENSE.
- [ ] Create the GitHub repository and add its remote.
- [ ] Review the final staged source and confirm no game files, generated binaries, local paths, or private research artifacts are included.
