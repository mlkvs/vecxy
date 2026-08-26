# Vecxy.Platforms

`Vecxy.Platforms` contains the platform-neutral application contract and shared
engine hosts. Applications normally keep bootstrap settings in
`Assets/Configs/Application.yaml`; game code is identical on desktop and Android.

## Application bootstrap

Enable the generated desktop host in the game project. Android already provides
its host through `VecxyActivity`:

```xml
<PropertyGroup>
  <VecxyGenerateEntryPoint>true</VecxyGenerateEntryPoint>
</PropertyGroup>
```

The complete application class is:

```csharp
using Vecxy.Kernel;
using Vecxy.Platforms;

[VecxyApplication]
public sealed class Application : ConfiguredApplication;
```

There is no game-owned `Main` method and no platform conditional. Both hosts find
the marked application and run it through `PlatformRunner`.

Create `Assets/Configs/Application.yaml`:

```yaml
application:
  title: My Vecxy Game

window:
  width: 1280
  height: 720

engine:
  targetFrameRate: 60
  showSplashScreen: true
  splashScreenLogoPath: Textures/Logo.png

assets:
  hotReload: true
  hotReloadDelayMilliseconds: 150

layers:
  - engine
  - game
```

`Application.yaml` is a bootstrap asset: desktop reads it from the configured
assets directory and Android reads it directly from the APK before packaged
assets are extracted. This allows splash-screen settings to take effect during
Android startup.

The built-in `engine` layer is always available. Register game layers with a
stable id on their parameterless definition:

```csharp
[AppLayerDefinition("game")]
public sealed class Definition : ADefinition<GameLayer>
{
}
```

Unknown ids, duplicate ids, duplicate entries, invalid dimensions, and invalid
frame-rate values fail at startup with configuration errors. Do not put CLR type
names in YAML; layer ids are intentionally stable across refactors.

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

The engine splash screen is enabled by default and uses
`Assets/Textures/Logo.png`. Packaged builds fall back to the logo embedded in the
engine assembly. It reports layer initialization progress, remains visible until
the first successful frame, and fades out on every supported platform. Configure
it through the `engine` section of `Application.yaml`.
