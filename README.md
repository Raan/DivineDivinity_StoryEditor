# StoryEditor

**StoryEditor** is a Windows desktop tool for inspecting, decompiling, editing, and rebuilding the Osiris Story data used by the classic game **Divine Divinity**.

It combines a WPF editor, a binary Story decompiler, and a build workflow based on the original 32-bit OsirisDLL.dll from the user's own game installation.

> **Status:** MVP 1.0.0. The maintainer tested the Windows x64 artifact produced by GitHub Actions: `story.000` was decompiled, rebuilt, and the rebuilt Story was verified in Divine Divinity. The distributable package with documentation is being prepared for a final clean-extraction check.

[Русская версия / Russian version](README.ru.md)

## Features

- **Binary decompilation:** read Story binaries (.000 / .001) and reconstruct editable .div source.
- **Goal navigation:** browse the Goal hierarchy and inspect INIT, KB, and EXIT sections.
- **Rule editing:** inspect IF and PROC rules, edit conditions and actions, and validate structural changes before applying them.
- **Source preservation:** retain untouched source text, whitespace, comments, and formatting where possible.
- **Cross-references:** search rules and usages across Goals.
- **Symbol Explorer:** inspect declared API symbols, Story procedures, and unresolved call candidates.
- **Goal Links:** explore explicit Goal-state references, Story procedure handler links, and clearly marked heuristic relationships.
- **Build and merge:** compile edited source with the original OsirisDLL and merge it with a selected base Story.
- **Read-only game inputs:** the editor does not overwrite installed game Story files.

## Requirements

### Running the published application

- Windows 10/11, 64-bit.
- A legitimate installation of Divine Divinity.
- The game's original OsirisDLL.dll and base main\startup\story.000.
- No separate .NET runtime installation is required by the self-contained Windows x64 package.

### Building a Story

The original Osiris DLL is a **32-bit** library. The build workflow therefore uses 32-bit Windows PowerShell:

    %WINDIR%\SysWOW64\WindowsPowerShell\v1.0\powershell.exe

This is normally present on 64-bit Windows. StoryEditor does not include or redistribute the game DLL or game data; users select their own installation.

### Building StoryEditor from source

- Windows 10/11.
- .NET 8 SDK.
- Internet access during the first restore to retrieve the AvalonEdit NuGet dependency.

## Installation

1. Download the Windows x64 release archive from the GitHub Releases page.
2. Extract it to a folder where you have read/write access.
3. Run StoryEditor.exe.
4. On first use, click **Game installation...** if the editor does not find the game automatically.
5. Select the game's OsirisDLL.dll. If required, select the base main\startup\story.000 file as well.

The selected installation is stored in:

    %LOCALAPPDATA%\StoryEditor\game-installation.json

Settings from the previous StoryEditor2 folder are migrated when they are read. No administrator rights should be required for normal use.

## Typical workflow

### 1. Decompile a Story binary

1. Start StoryEditor.
2. Click **Decompile Story...**.
3. Select a Story binary, such as the installed main\startup\story.000.
4. Choose where to save the generated .div file.
5. StoryEditor verifies that the binary was fully read and that all reconstructed rules were assigned before saving.
6. The resulting source is opened automatically.

The input binary is read-only and is never modified.

### 2. Inspect and edit

- Select a Goal in the hierarchy.
- Switch between INIT, KB, and EXIT.
- Use the rule list to navigate to IF and PROC rules.
- Use Cross References, Goal Links, and Symbol Explorer to inspect relationships and usages.
- Apply rule edits through the editor's validation workflow.
- Save to a separate .div file.

### 3. Build a separate Story binary

1. Ensure the correct game installation is configured.
2. Open the edited .div source.
3. Click **Build game story...**.
4. Choose a new output filename.
5. StoryEditor invokes the original 32-bit OsirisDLL, compiles the source, merges it with the selected base Story, and performs a read-only structural load check.

By default, build outputs are placed under:

    %USERPROFILE%\Documents\StoryEditor\Builds

The output must be a separate file. StoryEditor refuses to overwrite its input files and does not automatically install a build into the game directory. Back up game data before manually replacing anything.

## Output and diagnostics

The **Output / diagnostics** panel shows a compact summary of the selected Goal's KB:

- IF / PROC rule counts;
- condition and action counts;
- function-call count;
- calls matching declared API symbols;
- parser diagnostic count and details, when present.

Unknown or unresolved call names are review candidates, **not automatically syntax errors**. The complete list is available in **Symbol Explorer → Unresolved calls**. The parser is conservative and does not yet provide complete Osiris semantic or argument-type validation.

## Standalone decompiler CLI

The repository also contains a command-line decompiler.

Decompile a binary:

    dotnet run --project .\StoryDecompiler\StoryDecompiler.csproj -- --decompile <story.000> <output.div>

Export the parsed Rete structure for diagnostics:

    dotnet run --project .\StoryDecompiler\StoryDecompiler.csproj -- --dump-rete <story.000> <output.json>

The CLI and WPF application share StoryDecompiler.Core.

## Build from source

From the repository root:

    dotnet restore .\StoryEditor.sln
    dotnet build .\StoryEditor.sln -c Release

Publish the self-contained single-file Windows x64 application:

    dotnet publish .\StoryEditor\StoryEditor.csproj -c Release -p:PublishProfile=win-x64

The publish profile is in StoryEditor\Properties\PublishProfiles\win-x64.pubxml. The application is written to the project's bin\Release\net8.0-windows\win-x64\publish directory.

The build scripts are embedded in the executable. When a build is requested, StoryEditor extracts them to %LOCALAPPDATA%\StoryEditor\Scripts. Both scripts must remain together because the main script invokes the validation script.

## Repository layout

    StoryEditor.sln
    StoryEditor/
      WPF application, UI, game installation settings, and publish profile
    StoryEditor.Core/
      Lexer, source structure parser, Goal catalog, and rule parser
    StoryDecompiler.Core/
      Shared binary reader, Rete reconstruction, and DIV source generator
    StoryDecompiler/
      Standalone command-line decompiler
    scripts/
      PowerShell source files embedded into the WPF application

The old internal C# namespaces retain the StoryEditor2 prefix for compatibility. The user-facing product, assembly, and executable are named **StoryEditor**.

## Known limitations

- The parser is a conservative implementation, not a complete Osiris grammar or semantic/type checker.
- Some engine calls may not be present in the current API catalogue. Unresolved-call entries are review hints.
- Cross-reference and Goal relationship analysis is source-level analysis; heuristic relationships are labelled as such.
- Automatic Steam/GOG installation discovery is bounded to known locations and registry/library metadata. It has not been verified on every store configuration.
- This MVP does not include every feature from the older WinForms editor.
- The published desktop application targets Windows x64. The Osiris DLL build host remains 32-bit.
- A clean-machine end-to-end test of the final GitHub Release archive is still recommended before announcing the release.

## Safety and compatibility

- Always keep backups of original game files and edited Story sources.
- Do not use the installed story.000 as the output filename.
- StoryEditor reads the selected game DLL and base Story as build inputs; it does not patch the installed game automatically.
- Do not redistribute OsirisDLL.dll, div.exe, or game archives. They are not part of this repository or release package.
- Use this tool only with game files you are legally entitled to use.

## License

StoryEditor is distributed under the [MIT License](LICENSE). You may use, copy, modify, merge, publish, distribute, sublicense, and sell copies of the software, subject to the license terms.

The MIT License applies to this repository's original source code. It does not grant rights to Divine Divinity, OsirisDLL.dll, div.exe, or any other third-party game assets or software.
