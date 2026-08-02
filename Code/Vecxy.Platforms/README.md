# Vecxy.Platforms

`Vecxy.Platforms` contains the platform-neutral application contract and the
shared engine runner. A game implements `IVecxyApplication` once and uses the
same definition on desktop and mobile platforms.

Game projects import `build/Vecxy.Platforms.props`. Desktop is the default:

```bash
dotnet build Game.csproj
```

Select Android through a regular MSBuild property:

```bash
dotnet publish Game.csproj -p:VecxyPlatform=Android -r android-arm64
```

Platform-specific hosts, packaging rules and assets belong to projects such as
`Vecxy.Platforms.Android`; they do not belong to the game project.
