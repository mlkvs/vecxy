#!/usr/bin/env node
import { spawn, spawnSync } from 'node:child_process';
import { createInterface } from 'node:readline/promises';
import { stdin, stdout } from 'node:process';
import { existsSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { homedir, platform } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const VERSION = '0.1.0';
const DOTNET_VERSION = '10.0.110';
const ENGINE_REPOSITORY = 'https://github.com/mlkvs/vecxy.git';
const home = process.env.VECXY_HOME || join(homedir(), '.vecxy');
const engine = resolve(process.env.VECXY_ENGINE_PATH || join(home, 'engine'));
const localDotnet = join(home, 'dotnet', platform() === 'win32' ? 'dotnet.exe' : 'dotnet');
const androidSdk = resolve(process.env.ANDROID_SDK_ROOT || process.env.ANDROID_HOME || join(home, 'android-sdk'));
const args = process.argv.slice(2);

if (args.includes('--version') || args[0] === 'version') {
  console.log(VERSION);
  process.exit(0);
}
if (args.length === 0 || args.includes('--help') || args[0] === 'help') {
  printHelp();
  process.exit(0);
}
if (args[0] === 'doctor') process.exit(doctor(args.slice(1)));
if (args[0] === 'setup') {
  setup(args.slice(1)).then(code => process.exit(code), fail);
} else {
  forward(args);
}

function printHelp() {
  console.log(`Vecxy CLI ${VERSION}

Usage:
  vecxy setup [--yes] [--no-android] [--dry-run]
  vecxy doctor [--no-android]
  vecxy new <name> [--output <directory>]
  vecxy build [dev|release] --project <path> --platform <linux|windows|android>
  vecxy assets <scan|generate|analyze|validate|packages|pack|prepare>

Environment:
  VECXY_HOME         Tool data directory (default: ~/.vecxy)
  VECXY_ENGINE_PATH  Existing Vecxy Engine checkout
  ANDROID_SDK_ROOT   Android SDK location
  JAVA_HOME          JDK location`);
}

function commandExists(command) {
  const probe = platform() === 'win32' ? ['where', [command]] : ['sh', ['-c', `command -v "${command}"`]];
  return spawnSync(probe[0], probe[1], { stdio: 'ignore' }).status === 0;
}

function findDotnet() {
  if (existsSync(localDotnet)) return localDotnet;
  return commandExists('dotnet') ? 'dotnet' : null;
}

function hasSdk(dotnet, major = '10.') {
  if (!dotnet) return false;
  const result = spawnSync(dotnet, ['--list-sdks'], { encoding: 'utf8' });
  return result.status === 0 && result.stdout.split(/\r?\n/).some(line => line.startsWith(major));
}

function doctor(options = []) {
  const android = !options.includes('--no-android');
  const unknown = options.filter(x => x !== '--no-android');
  if (unknown.length) { console.error(`Unknown doctor option: ${unknown[0]}`); return 1; }
  const dotnet = findDotnet();
  const checks = [
    ['Node.js >= 20', Number(process.versions.node.split('.')[0]) >= 20, process.version],
    ['Git', commandExists('git'), commandExists('git') ? 'installed' : 'missing'],
    ['.NET 10 SDK', hasSdk(dotnet), dotnet || 'missing'],
    ['Vecxy Engine', existsSync(join(engine, 'tools', 'Vecxy.Cli', 'Vecxy.Cli.csproj')), engine],
    ['JDK 21+', javaMajor() >= 21, process.env.JAVA_HOME || 'auto-detect'],
    ['Android SDK', existsSync(sdkManager()), androidSdk],
    ['Android platform-tools', existsSync(join(androidSdk, 'platform-tools')), join(androidSdk, 'platform-tools')],
    ['Android NDK', existsSync(join(androidSdk, 'ndk')), join(androidSdk, 'ndk')]
  ];
  for (const [name, ok, detail] of checks) console.log(`${ok ? '✓' : '✗'} ${name}: ${detail}`);
  const desktopReady = checks.slice(0, 4).every(x => x[1]);
  const androidReady = checks.every(x => x[1]);
  console.log(`\nDesktop: ${desktopReady ? 'ready' : 'not ready'}\nAndroid: ${androidReady ? 'ready' : 'not ready'}`);
  return (android ? androidReady : desktopReady) ? 0 : 1;
}

async function setup(options) {
  const yes = options.includes('--yes');
  const android = !options.includes('--no-android');
  const dryRun = options.includes('--dry-run');
  const unknown = options.filter(x => !['--yes', '--no-android', '--dry-run'].includes(x));
  if (unknown.length) throw new Error(`Unknown setup option: ${unknown[0]}`);
  console.log(`Vecxy will configure tools under ${home}`);
  if (dryRun) {
    console.log(`Would install .NET SDK ${DOTNET_VERSION} into ${join(home, 'dotnet')}`);
    console.log(`Would clone/update ${ENGINE_REPOSITORY} in ${engine}`);
    if (android) {
      console.log('Would install JDK 21, the .NET Android workload, Android API/build-tools 36, platform-tools, NDK r28 and CMake.');
      console.log(`Android SDK destination: ${androidSdk}`);
    }
    return 0;
  }
  if (!yes && !await confirm(`Install/update .NET ${DOTNET_VERSION}, Vecxy Engine${android ? ', JDK and Android tools' : ''}?`)) {
    console.log('Setup cancelled.');
    return 1;
  }
  mkdirSync(home, { recursive: true });
  await installDotnet();
  await installEngine();
  if (android) await installAndroid();
  writeEnvironment();
  console.log('\nSetup complete. Restart the terminal or load the environment file shown above, then run: vecxy doctor');
  return doctor(android ? [] : ['--no-android']);
}

async function installDotnet() {
  const available = findDotnet();
  if (hasSdk(available) && sdkIncludes(available, DOTNET_VERSION)) {
    console.log(`✓ .NET SDK ${DOTNET_VERSION}`);
    return;
  }
  console.log(`Installing .NET SDK ${DOTNET_VERSION}...`);
  mkdirSync(join(home, 'dotnet'), { recursive: true });
  if (platform() === 'win32') {
    const script = join(home, 'dotnet-install.ps1');
    run('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', `Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile '${escapePowerShell(script)}'`]);
    run('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', script, '-Version', DOTNET_VERSION, '-InstallDir', join(home, 'dotnet')]);
  } else {
    const script = join(home, 'dotnet-install.sh');
    run('curl', ['-fsSL', 'https://dot.net/v1/dotnet-install.sh', '-o', script]);
    run('sh', [script, '--version', DOTNET_VERSION, '--install-dir', join(home, 'dotnet')]);
  }
  if (!hasSdk(localDotnet)) throw new Error('.NET 10 installation did not produce a usable SDK.');
}

function sdkIncludes(dotnet, version) {
  const result = spawnSync(dotnet, ['--list-sdks'], { encoding: 'utf8' });
  return result.status === 0 && result.stdout.split(/\r?\n/).some(line => line.startsWith(`${version} `));
}

async function installEngine() {
  if (!commandExists('git')) throw new Error('Git is required. Install Git and run vecxy setup again.');
  const gitOptions = ['-c', 'credential.helper=', '-c', 'core.askPass='];
  const validCheckout = existsSync(join(engine, '.git')) && existsSync(join(engine, 'tools', 'Vecxy.Cli', 'Vecxy.Cli.csproj'));
  if (validCheckout) {
    console.log(`Updating Vecxy Engine in ${engine}...`);
    run('git', [...gitOptions, '-C', engine, 'pull', '--ff-only'], { GIT_TERMINAL_PROMPT: '0' });
  } else {
    if (existsSync(engine)) {
      if (process.env.VECXY_ENGINE_PATH) throw new Error(`VECXY_ENGINE_PATH is not a valid engine checkout: ${engine}`);
      rmSync(engine, { recursive: true, force: true });
    }
    mkdirSync(dirname(engine), { recursive: true });
    run('git', [...gitOptions, 'clone', '--depth', '1', '--branch', 'develop', ENGINE_REPOSITORY, engine], { GIT_TERMINAL_PROMPT: '0' });
  }
}

async function installAndroid() {
  const dotnet = findDotnet();
  console.log('Installing .NET Android workload...');
  run(dotnet, ['workload', 'install', 'android']);
  let javaHome = detectJavaHome();
  if (javaMajor() < 21 || !javaHome) {
    console.log('Installing Microsoft/OpenJDK 21...');
    installJdk();
    javaHome = detectJavaHome();
  }
  if (!javaHome) throw new Error('JDK 21 was not found after installation. Set JAVA_HOME and rerun setup.');
  mkdirSync(androidSdk, { recursive: true });
  console.log('Installing Android SDK dependencies required by .NET 10...');
  const androidProject = join(engine, 'Code', 'Vecxy.Platforms.Android', 'Vecxy.Platforms.Android.csproj');
  run(dotnet, ['build', androidProject, '-t:InstallAndroidDependencies', '-f', 'net10.0-android',
    `-p:AndroidSdkDirectory=${androidSdk}`, `-p:JavaSdkDirectory=${javaHome}`, '-p:AcceptAndroidSdkLicenses=True']);
  const manager = sdkManager();
  if (!existsSync(manager)) throw new Error(`sdkmanager was not installed at ${manager}`);
  run(manager, [`--sdk_root=${androidSdk}`, 'platform-tools', 'platforms;android-36', 'build-tools;36.0.0', 'ndk;28.0.13004108', 'cmake;3.22.1']);
}

function installJdk() {
  if (platform() === 'win32' && commandExists('winget')) return run('winget', ['install', '--id', 'Microsoft.OpenJDK.21', '--exact', '--accept-package-agreements', '--accept-source-agreements']);
  if (platform() === 'darwin' && commandExists('brew')) return run('brew', ['install', 'openjdk@21']);
  if (commandExists('dnf')) return run('sudo', ['dnf', 'install', '-y', 'java-21-openjdk-devel']);
  if (commandExists('apt-get')) { run('sudo', ['apt-get', 'update']); return run('sudo', ['apt-get', 'install', '-y', 'openjdk-21-jdk']); }
  if (commandExists('pacman')) return run('sudo', ['pacman', '-S', '--needed', '--noconfirm', 'jdk21-openjdk']);
  throw new Error('Could not install JDK automatically. Install Microsoft OpenJDK 21, set JAVA_HOME, and rerun setup.');
}

function javaMajor() {
  const java = process.env.JAVA_HOME ? join(process.env.JAVA_HOME, 'bin', platform() === 'win32' ? 'java.exe' : 'java') : 'java';
  const result = spawnSync(java, ['-version'], { encoding: 'utf8' });
  const match = `${result.stderr || ''}${result.stdout || ''}`.match(/version "(\d+)/);
  return match ? Number(match[1]) : 0;
}

function detectJavaHome() {
  if (process.env.JAVA_HOME && existsSync(join(process.env.JAVA_HOME, 'bin'))) return resolve(process.env.JAVA_HOME);
  const java = spawnSync(platform() === 'win32' ? 'where' : 'sh', platform() === 'win32' ? ['java'] : ['-c', 'command -v java'], { encoding: 'utf8' });
  if (java.status !== 0) return null;
  const executable = java.stdout.trim().split(/\r?\n/)[0];
  if (!executable) return null;
  if (platform() === 'darwin') {
    const result = spawnSync('/usr/libexec/java_home', ['-v', '21'], { encoding: 'utf8' });
    if (result.status === 0) return result.stdout.trim();
  }
  try {
    const result = spawnSync(executable, ['-XshowSettings:properties', '-version'], { encoding: 'utf8' });
    const match = `${result.stderr || ''}${result.stdout || ''}`.match(/java\.home\s*=\s*(.+)/);
    return match ? match[1].trim() : resolve(executable, '..', '..');
  } catch { return null; }
}

function sdkManager() {
  const extension = platform() === 'win32' ? '.bat' : '';
  return join(androidSdk, 'cmdline-tools', 'latest', 'bin', `sdkmanager${extension}`);
}

function writeEnvironment() {
  const javaHome = detectJavaHome();
  if (platform() === 'win32') {
    const file = join(home, 'env.cmd');
    writeFileSync(file, `@set "VECXY_HOME=${home}"\r\n@set "VECXY_ENGINE_PATH=${engine}"\r\n@set "DOTNET_ROOT=${join(home, 'dotnet')}"\r\n@set "ANDROID_SDK_ROOT=${androidSdk}"\r\n${javaHome ? `@set "JAVA_HOME=${javaHome}"\r\n` : ''}@set "PATH=%DOTNET_ROOT%;%ANDROID_SDK_ROOT%\\platform-tools;%PATH%"\r\n`);
    console.log(`Environment file: ${file}`);
  } else {
    const file = join(home, 'env.sh');
    writeFileSync(file, `export VECXY_HOME=${shellQuote(home)}\nexport VECXY_ENGINE_PATH=${shellQuote(engine)}\nexport DOTNET_ROOT=${shellQuote(join(home, 'dotnet'))}\nexport ANDROID_SDK_ROOT=${shellQuote(androidSdk)}\n${javaHome ? `export JAVA_HOME=${shellQuote(javaHome)}\n` : ''}export PATH="$DOTNET_ROOT:$ANDROID_SDK_ROOT/platform-tools:$PATH"\n`);
    console.log(`Environment file: ${file}\nAdd this line to your shell profile:\n  source ${shellQuote(file)}`);
  }
}

function forward(values) {
  const dotnet = findDotnet();
  if (!hasSdk(dotnet)) fail(new Error('The .NET 10 SDK is missing. Run: vecxy setup'));
  const project = join(engine, 'tools', 'Vecxy.Cli', 'Vecxy.Cli.csproj');
  if (!existsSync(project)) fail(new Error(`Vecxy Engine is missing at ${engine}. Run: vecxy setup`));
  const forwarded = [...values];
  if (values[0] === 'new' && !values.includes('--engine')) forwarded.push('--engine', engine);
  const child = spawn(dotnet, ['run', '--project', project, '--', ...forwarded], { stdio: 'inherit' });
  child.on('exit', (code, signal) => process.exit(signal ? 1 : code ?? 1));
  child.on('error', fail);
}

function run(command, values, extraEnvironment = {}) {
  if (!command) throw new Error('Required command was not found.');
  const result = spawnSync(command, values, { stdio: 'inherit', env: { ...process.env, DOTNET_ROOT: join(home, 'dotnet'), ANDROID_SDK_ROOT: androidSdk, ...extraEnvironment } });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`${command} failed with exit code ${result.status}.`);
}

async function confirm(question) {
  if (!stdin.isTTY) return false;
  const reader = createInterface({ input: stdin, output: stdout });
  const answer = await reader.question(`${question} [y/N] `);
  reader.close();
  return /^y(es)?$/i.test(answer.trim());
}

function shellQuote(value) { return `'${value.replaceAll("'", "'\\''")}'`; }
function escapePowerShell(value) { return value.replaceAll("'", "''"); }
function fail(error) { console.error(`vecxy: ${error.message || error}`); process.exit(1); }
