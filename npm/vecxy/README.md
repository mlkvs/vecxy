# Vecxy CLI

Global command-line tools for the Vecxy game engine.

```bash
npm install --global vecxy
vecxy setup
vecxy doctor
vecxy new MyGame
cd MyGame
vecxy engine use develop
vecxy sprite-editor
vecxy build -t linux
```

## Commands

- `vecxy setup [--yes] [--no-android] [--dry-run] [--engine <ref>]` configures the toolchain and installs a tag, branch, or commit.
- `vecxy engine install [ref]` installs or updates an engine version (`develop` by default). Use `latest` for the newest `develop` commit.
- `vecxy engine use <ref>` installs and selects a version for the current project.
- `vecxy engine use <ref> -p <path>` selects a version for another project.
- `vecxy engine use <ref> -g` changes the global default outside configured projects.
- `vecxy engine current` shows the version selected by the current project.
- `vecxy engine list` shows installed versions.
- `vecxy sprite-editor [-p <path>]` opens the native sprite and atlas editor for a project.
- `vecxy doctor [--no-android]` checks desktop and Android requirements.
- `vecxy new <name> [--output <path>]` creates a minimal game project.
- `vecxy generate` prepares asset IDs/code, compiles the project and generates the `[Authoring]` catalog for Blender.
- `vecxy assets scan|generate|analyze|validate|packages|pack|prepare` manages assets.
- `vecxy build [dev|release] --platform linux|windows|android` creates distributable builds.
- `vecxy run [dev|release] [--debug] [--no-build]` builds and starts the game; --debug waits for console confirmation after debugger attachment.

The default selection is stored in `~/.vecxy/config.json`. A project-specific
selection is changed by `engine use` and is stored in `.vecxy/config.json`. Its generated MSBuild
integration in `.vecxy/Engine.props`. Installed versions coexist under
`~/.vecxy/engines`. Use `VECXY_HOME` to move that directory, or
`VECXY_ENGINE_PATH` to temporarily override the project selection.

Refs can be tags (`v0.1.0`), branches (`develop`, `feature/rendering`) or full and
short commit hashes (`f923a88`). Run `vecxy setup` inside a configured project to
install that project's selected version. `Engine.props` contains a machine-local
absolute path and is ignored by Git; `config.json` should be committed so another
computer selects the same ref.

Common short options are `-p` (`--project`), `-o` (`--output`), `-e`
(`--engine`), `-t` (`--platform`), `-r` (`--runtime`), `-f` (`--format`),
`-k` (`--keystore`) and `-a` (`--alias`).

## Developing with a local engine or submodule

Install/link this package from its working checkout, then select the engine in the game directory:

```bash
npm install --global /path/to/Vendors/Vecxy/npm/vecxy
cd /path/to/Game
vecxy engine use --local ../Vendors/Vecxy
vecxy engine current
vecxy build
vecxy update --check
vecxy update
```

The relative checkout path is stored in `.vecxy/config.json` as `engine.path`.
It takes precedence over the shared engine cache; `VECXY_ENGINE_PATH` remains an explicit override.
The generated, ignored Engine.props is refreshed automatically on CLI commands.
`setup` preserves a selected local checkout. A linked npm package uses edited JavaScript
on the next invocation, without publishing or reinstalling it.

`update` (also `engine update`) fetches the current branch's upstream and only fast-forwards.
It refuses uncommitted changes, detached HEAD and diverged history instead of resetting,
stashing or overwriting work. `--check` fetches and reports ahead/behind without moving HEAD,
including when the checkout has local changes. It does not push either repository.
The parent repository's submodule pointer remains yours to commit.

Run local command tests with `npm test` in this package. The tests use disposable local Git
repositories and do not access GitHub. Desktop builds run the project's preparation targets,
including USD cooking, before creating VPack packages.


## First build on another computer

Install Git, Node.js 20+ (including npm), and Python 3.10+ with venv support. Then:

```bash
git clone --recurse-submodules <game-repository>
cd <game-repository>
npm install --global ./Vendors/Vecxy/npm/vecxy
cd Game
vecxy setup --yes --no-android
vecxy build
```

For projects with a local engine path, setup preserves the checkout, installs the exact
.NET SDK, generates machine-local Engine.props, and creates the project OpenUSD/Pillow
virtual environment when USD is referenced. Repeated setup skips USD installation when
the dependency stamp and environment are valid. Blender is only needed for scene editing.
On Linux, a writable npm prefix (for example --prefix "$HOME/.local", with its bin on PATH)
avoids requiring root access. Project .gitmodules must reference accessible engine commits.


## Build and run

```bash
vecxy run                 # Build dev (Debug) and launch
vecxy run --no-build      # Launch the existing dev build
vecxy run release         # Build and launch Release
vecxy run --debug         # Debug build, then wait before game initialization
vecxy run -- --example    # Forward arguments to the game
```

`--debug` (alias `--wait-for-debugger`) prints the **game process PID**. Attach a managed
.NET debugger in your IDE, then press Enter in the original terminal. The engine waits
before creating the desktop application, window or scene. Confirmation is manual: Enter
also permits startup without a detected debugger and reports that fact. Closed stdin
cancels startup. Closing the game returns its exit code to vecxy; Ctrl+C terminates the
launched process. `--debug` requires dev mode. Only the current desktop platform and
architecture can be launched; cross-platform packaging remains available via build.
`-p`, `-o`, `-t` and `-r` work as on build. Game arguments must follow `--`.
