# Vecxy CLI

## Установка

```bash
npm install --global vecxy
vecxy setup
vecxy doctor
```

`setup` устанавливает пользовательский .NET 10 SDK, скачивает Vecxy Engine и по
умолчанию готовит Android toolchain: JDK 21, .NET Android workload, Android SDK
API 36, build-tools, platform-tools, NDK r28 и CMake. Перед изменениями команда
просит подтверждение. Для CI есть `--yes`, посмотреть план без изменений можно
через `--dry-run`, а Android можно пропустить через `--no-android`.

GitHub-аккаунт и Git credentials для публичного репозитория не нужны. `setup`
принудительно выполняет clone анонимно и игнорирует сохранённые credentials.
Файлы инструментов хранятся в `~/.vecxy`. Путь переопределяется переменной
`VECXY_HOME`, существующий checkout движка — `VECXY_ENGINE_PATH`.

После setup на Linux/macOS добавьте созданный `~/.vecxy/env.sh` в профиль shell:

```bash
source ~/.vecxy/env.sh
```

На Windows можно выполнить `%USERPROFILE%\.vecxy\env.cmd`.

## Новый проект

```bash
vecxy new MyGame
cd MyGame
dotnet run
```

Для другого каталога используйте `--output`. CLI никогда не перезаписывает
непустой каталог.

## Ассеты

```bash
vecxy assets scan
vecxy assets generate
vecxy assets analyze
vecxy assets validate
vecxy assets prepare
vecxy assets packages
vecxy assets pack --platform linux
```

В каталоге с несколькими проектами укажите `--project <directory|csproj>`.

## Дистрибутивы

Self-contained desktop build:

```bash
vecxy build release --platform linux
vecxy build release --platform windows --runtime win-x64
```

Debug build для разработки:

```bash
vecxy build dev --platform linux --output artifacts/dev
```

Android APK и App Bundle:

```bash
vecxy build release --platform android --format both \
  --version 1.2.0 --version-code 42
```

Для подписанного Android build передайте keystore и alias. Секреты намеренно
читаются только из окружения:

```bash
export VECXY_ANDROID_STORE_PASSWORD='...'
export VECXY_ANDROID_KEY_PASSWORD='...'
vecxy build release --platform android \
  --keystore ./signing/game.keystore --alias game
```

Перед publish CLI выполняет `scan → generate → analyze → validate → VPack`.

## Диагностика

```bash
vecxy doctor
vecxy doctor --no-android
```

Первая команда проверяет полный Android toolchain, вторая — только окружение для
desktop-разработки.
