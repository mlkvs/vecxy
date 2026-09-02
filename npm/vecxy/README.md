# Vecxy CLI

Global command-line tools for the Vecxy game engine.

```bash
npm install --global vecxy
vecxy setup
vecxy doctor
vecxy new MyGame
cd MyGame
vecxy build --platform linux
```

## Commands

- `vecxy setup [--yes] [--no-android] [--dry-run]` configures the engine and development toolchain.
- `vecxy doctor [--no-android]` checks desktop and Android requirements.
- `vecxy new <name> [--output <path>]` creates a minimal game project.
- `vecxy assets scan|generate|analyze|validate|packages|pack|prepare` manages assets.
- `vecxy build [dev|release] --platform linux|windows|android` creates distributable builds.

Use `VECXY_HOME` to change the default `~/.vecxy` tool directory, or
`VECXY_ENGINE_PATH` to use an existing engine checkout.
