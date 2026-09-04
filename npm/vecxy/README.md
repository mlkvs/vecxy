# Vecxy CLI

Global command-line tools for the Vecxy game engine.

```bash
npm install --global vecxy
vecxy setup
vecxy doctor
vecxy new MyGame
cd MyGame
vecxy engine use develop
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
- `vecxy doctor [--no-android]` checks desktop and Android requirements.
- `vecxy new <name> [--output <path>]` creates a minimal game project.
- `vecxy assets scan|generate|analyze|validate|packages|pack|prepare` manages assets.
- `vecxy build [dev|release] --platform linux|windows|android` creates distributable builds.

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
