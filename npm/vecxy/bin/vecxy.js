#!/usr/bin/env node
import { spawn, spawnSync } from 'node:child_process';
import { createInterface } from 'node:readline/promises';
import { stdin, stdout } from 'node:process';
import { existsSync, mkdirSync, readFileSync, readdirSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { homedir, platform } from 'node:os';
import { dirname, join, resolve, relative, delimiter } from 'node:path';
import { fileURLToPath } from 'node:url';
import { cachedRunCli } from '../lib/run-cache.js';
import { canReuseCliRestore } from '../lib/restore-cache.js';

const VERSION = JSON.parse(readFileSync(new URL('../package.json', import.meta.url), 'utf8')).version;
const DOTNET_VERSION = '10.0.110';
const ENGINE_REPOSITORY = 'https://github.com/mlkvs/vecxy.git';
const home = process.env.VECXY_HOME || join(homedir(), '.vecxy');
const defaultEngineRef = 'develop';
const localDotnet = join(home, 'dotnet', platform() === 'win32' ? 'dotnet.exe' : 'dotnet');
const androidSdk = resolve(process.env.ANDROID_SDK_ROOT || process.env.ANDROID_HOME || join(home, 'android-sdk'));
const args = expandShortOptions(process.argv.slice(2));

const cliArgs = args.slice(0, args.indexOf('--') < 0 ? args.length : args.indexOf('--'));
if (cliArgs.includes('--version') || args[0] === 'version') {
  console.log(VERSION);
  process.exit(0);
}
if (args.length === 0 || cliArgs.includes('--help') || args[0] === 'help') {
  printHelp();
  process.exit(0);
}
if (args[0] === 'doctor') process.exit(doctor(args.slice(1)));
if (args[0] === 'update') {
  engineCommand(['update', ...args.slice(1)]).then(code => process.exit(code), fail);
} else if (args[0] === 'engine') {
  engineCommand(args.slice(1)).then(code => process.exit(code), fail);
} else if (args[0] === 'setup') {
  setup(args.slice(1)).then(code => process.exit(code), fail);
} else if (args[0] === 'sprite-editor') {
  launchSpriteEditor(args.slice(1));
} else {
  forward(args);
}

function printHelp() {
  console.log(`Vecxy CLI ${VERSION}

Usage:
  vecxy setup [-y|--yes] [--no-android] [--dry-run] [-e|--engine <ref>]
  vecxy doctor [--no-android]
  vecxy new <name> [-o|--output <directory>]
  vecxy engine install [latest|tag|branch|commit]
  vecxy engine use <latest|tag|branch|commit> [-p|--project <directory>]
  vecxy engine use --local <checkout-or-submodule> [-p|--project <directory>]
  vecxy update [--check] [-p|--project <directory>]
  vecxy engine use <latest|tag|branch|commit> -g|--global
  vecxy engine current [-p|--project <directory>] [-g|--global]
  vecxy engine list
  vecxy sprite-editor [-p|--project <directory>]
  vecxy run [dev|release] [--debug|--wait-for-debugger] [--no-build|--rebuild] [-- <game arguments>]
  vecxy build [dev|release] [-p|--project <path>] -t|--platform <linux|windows|android>
  vecxy generate [-p|--project <path>]
  vecxy navigation bake <input.json> --output <map.navigation.json>
  vecxy assets <scan|generate|analyze|validate|packages|pack|prepare>

Environment:
  VECXY_HOME         Tool data directory (default: ~/.vecxy)
  VECXY_ENGINE_PATH  Existing checkout (overrides the project selection)
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
  const selectedEngine = resolveEngine(undefined, false);
  const checks = [
    ['Node.js >= 20', Number(process.versions.node.split('.')[0]) >= 20, process.version],
    ['Git', commandExists('git'), commandExists('git') ? 'installed' : 'missing'],
    ['.NET 10 SDK', hasSdk(dotnet), dotnet || 'missing'],
    ['Vecxy Engine', isEngine(selectedEngine), selectedEngine],
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
  const project = findProjectRoot(process.cwd(), false);
  const projectConfig = project && readProjectConfig(project);
  const engineRef = takeOption(options, '--engine') || projectConfig?.engine.ref || defaultEngineRef;
  const yes = options.includes('--yes');
  const android = !options.includes('--no-android');
  const dryRun = options.includes('--dry-run');
  const unknown = options.filter(x => !['--yes', '--no-android', '--dry-run'].includes(x));
  if (unknown.length) throw new Error(`Unknown setup option: ${unknown[0]}`);
  console.log(`Vecxy will configure tools under ${home}`);
  if (dryRun) {
    console.log(`Would install .NET SDK ${DOTNET_VERSION} into ${join(home, 'dotnet')}`);
    if (projectConfig?.engine.path) console.log(`Would use local Vecxy checkout ${resolveEngine(project)} without changing its branch or files.`);
    else console.log(`Would install Vecxy Engine ref '${engineRef}' in ${engineDirectory(engineRef)}`);
    if (usesUsd(project)) console.log('Would prepare OpenUSD Python dependencies in the project .vecxy/usd-env directory.');
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
  const installedEngine = projectConfig?.engine.path ? resolveEngine(project) : await installEngine(engineRef);
  if (projectConfig?.engine.path) writeEngineProps(project, installedEngine);
  else if (projectConfig) configureProject(project, engineRef, installedEngine);
  else writeGlobalConfig(engineRef);
  if (usesUsd(project)) installUsdTools(project, installedEngine);
  if (android) await installAndroid(installedEngine);
  writeEnvironment();
  console.log('\nSetup complete. Restart the terminal or load the environment file shown above, then run: vecxy doctor');
  return doctor(android ? [] : ['--no-android']);
}

function usesUsd(project) {
  return !!project && readdirSync(project).filter(x => x.endsWith('.csproj'))
    .some(file => readFileSync(join(project, file), 'utf8').includes('Vecxy.Usd'));
}

function installUsdTools(project, engine) {
  const environment = join(project, '.vecxy', 'usd-env');
  const python = platform() === 'win32' ? join(environment, 'Scripts', 'python.exe') : join(environment, 'bin', 'python');
  if (!existsSync(python)) {
    const candidates = platform() === 'win32' ? [['py', ['-3']], ['python', []], ['python3', []]] : [['python3', []], ['python', []]];
    const available = candidates.find(([command, prefix]) => spawnSync(command,
      [...prefix, '-c', 'import sys; sys.exit(0 if sys.version_info >= (3, 10) else 1)'], { stdio: 'ignore' }).status === 0);
    if (!available) throw new Error('This USD project needs Python 3.10 or newer. Install Python with venv support and rerun vecxy setup --yes --no-android.');
    run(available[0], [...available[1], '-m', 'venv', environment]);
  }
  const requirements = join(engine, 'Code', 'Vecxy.Usd', 'Tools', 'requirements.txt');
  const expected = createHash('sha256').update(readFileSync(requirements)).digest('hex');
  const stamp = join(environment, '.vecxy-requirements.sha256');
  const healthy = spawnSync(python, ['-c', 'from pxr import Usd; from PIL import Image'], { stdio: 'ignore' }).status === 0;
  if (!healthy || !existsSync(stamp) || readFileSync(stamp, 'utf8').trim() !== expected) {
    run(python, ['-m', 'pip', 'install', '-r', requirements]);
    writeFileSync(stamp, `${expected}\n`);
  }
  console.log(`✓ OpenUSD Python: ${python}`);
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

async function installEngine(ref = defaultEngineRef) {
  if (!commandExists('git')) throw new Error('Git is required. Install Git and run vecxy setup again.');
  validateRef(ref);
  const engine = engineDirectory(ref);
  const gitOptions = ['-c', 'credential.helper=', '-c', 'core.askPass='];
  const validCheckout = existsSync(join(engine, '.git')) && isEngine(engine);
  if (validCheckout) {
    console.log(`Updating Vecxy Engine '${ref}' in ${engine}...`);
  } else {
    mkdirSync(dirname(engine), { recursive: true });
    const temporary = `${engine}.install-${process.pid}-${Date.now()}`;
    try {
      run('git', [...gitOptions, 'clone', '--filter=blob:none', '--no-checkout', ENGINE_REPOSITORY, temporary], { GIT_TERMINAL_PROMPT: '0' });
      run('git', [...gitOptions, '-C', temporary, 'fetch', '--prune', 'origin', '+refs/heads/*:refs/remotes/origin/*', '+refs/tags/*:refs/tags/*'], { GIT_TERMINAL_PROMPT: '0' });
      const commit = resolveGitRef(temporary, ref);
      if (!commit) throw new Error(`Vecxy Engine ref '${ref}' was not found.`);
      run('git', [...gitOptions, '-C', temporary, 'checkout', '--detach', '--force', commit], { GIT_TERMINAL_PROMPT: '0' });
      if (!isEngine(temporary)) throw new Error(`Git ref '${ref}' does not contain a valid Vecxy Engine.`);
      writeFileSync(join(temporary, '.vecxy-ref'), `${ref}\n`);
      if (existsSync(engine)) rmSync(engine, { recursive: true, force: true });
      renameSync(temporary, engine);
      console.log(`✓ Vecxy Engine ${ref}: ${engineCommit(engine)}`);
      return engine;
    } finally {
      if (existsSync(temporary)) rmSync(temporary, { recursive: true, force: true });
    }
  }
  run('git', [...gitOptions, '-C', engine, 'fetch', '--prune', 'origin', '+refs/heads/*:refs/remotes/origin/*', '+refs/tags/*:refs/tags/*'], { GIT_TERMINAL_PROMPT: '0' });
  const commit = resolveGitRef(engine, ref);
  if (!commit) throw new Error(`Vecxy Engine ref '${ref}' was not found.`);
  run('git', [...gitOptions, '-C', engine, 'checkout', '--detach', '--force', commit], { GIT_TERMINAL_PROMPT: '0' });
  if (!isEngine(engine)) throw new Error(`Git ref '${ref}' does not contain a valid Vecxy Engine.`);
  writeFileSync(join(engine, '.vecxy-ref'), `${ref}\n`);
  console.log(`✓ Vecxy Engine ${ref}: ${engineCommit(engine)}`);
  return engine;
}

async function installAndroid(engine) {
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
    writeFileSync(file, `@set "VECXY_HOME=${home}"\r\n@set "DOTNET_ROOT=${join(home, 'dotnet')}"\r\n@set "ANDROID_SDK_ROOT=${androidSdk}"\r\n${javaHome ? `@set "JAVA_HOME=${javaHome}"\r\n` : ''}@set "PATH=%DOTNET_ROOT%;%ANDROID_SDK_ROOT%\\platform-tools;%PATH%"\r\n`);
    console.log(`Environment file: ${file}`);
  } else {
    const file = join(home, 'env.sh');
    writeFileSync(file, `export VECXY_HOME=${shellQuote(home)}\nexport DOTNET_ROOT=${shellQuote(join(home, 'dotnet'))}\nexport ANDROID_SDK_ROOT=${shellQuote(androidSdk)}\n${javaHome ? `export JAVA_HOME=${shellQuote(javaHome)}\n` : ''}export PATH="$DOTNET_ROOT:$ANDROID_SDK_ROOT/platform-tools:$PATH"\n`);
    console.log(`Environment file: ${file}\nAdd this line to your shell profile:\n  source ${shellQuote(file)}`);
  }
}

function forward(values) {
  const dotnet = findDotnet();
  if (!hasSdk(dotnet)) fail(new Error('The .NET 10 SDK is missing. Run: vecxy setup'));
  const root = findProjectRoot(projectDirectoryFromArgs(values), false);
  const engine = resolveEngine(projectDirectoryFromArgs(values));
  if (root && readProjectConfig(root)?.engine.path) writeEngineProps(root, engine);
  const project = join(engine, 'tools', 'Vecxy.Cli', 'Vecxy.Cli.csproj');
  if (!existsSync(project)) fail(new Error(`Vecxy Engine is missing at ${engine}. Run: vecxy setup`));
  const forwarded = [...values];
  if (values[0] === 'new' && !values.includes('--engine')) forwarded.push('--engine', engine);
  const environment = toolEnvironment(engine);
  delete environment.VECXY_CLI_BOOTSTRAP;
  const cached = cachedRunCli(root, engine, environment);
  let invocation;
  if (cached) {
    invocation = [cached, ...forwarded];
  } else {
    console.log('Preparing Vecxy CLI...');
    const started = Date.now();
    const reuse = canReuseCliRestore(dotnet, project, environment);
    if (!reuse) console.log('Restoring CLI dependencies if needed...');
    invocation = ['run', ...(reuse ? ['--no-restore'] : []), '--project', project, '--', ...forwarded];
    environment.VECXY_CLI_BOOTSTRAP = String(started);
  }
  const child = spawn(dotnet, invocation, { stdio: 'inherit', env: environment });
  child.on('exit', (code, signal) => process.exit(signal ? 1 : code ?? 1));
  child.on('error', fail);
}

function toolEnvironment(engine) {
  const dotnet = findDotnet();
  const sdk = dotnet && dotnet !== 'dotnet' ? dirname(dotnet) : null;
  const architectureRoot = { x64: 'DOTNET_ROOT_X64', arm64: 'DOTNET_ROOT_ARM64', ia32: 'DOTNET_ROOT_X86' }[process.arch];
  return { ...process.env,
    ...(sdk ? { DOTNET_ROOT: sdk, ...(architectureRoot ? { [architectureRoot]: sdk } : {}), PATH: `${sdk}${delimiter}${process.env.PATH || ''}` } : {}),
    VecxyEnginePath: engine, VECXY_ENGINE_PATH: engine, VECXY_ENGINE_REF: installedRef(engine) };
}

function launchSpriteEditor(values) {
  const projectOption = takeOption(values, '--project');
  if (values.length > 1) throw new Error('Usage: vecxy sprite-editor [-p|--project <directory>]');
  const project = findProjectRoot(projectOption || values[0] || process.cwd());
  const engine = resolveEngine(project);
  const editor = join(engine, 'tools', 'Vecxy.SpriteEditor', 'Vecxy.SpriteEditor.csproj');
  if (!existsSync(editor))
    fail(new Error(`Sprite Editor is missing in Vecxy ${installedRef(engine)}. Update it with: vecxy engine use latest -p "${project}"`));
  const dotnet = findDotnet();
  if (!hasSdk(dotnet)) fail(new Error('The .NET 10 SDK is missing. Run: vecxy setup'));
  const child = spawn(dotnet, ['run', '--project', editor, '--', project], {
    stdio: 'inherit',
    env: toolEnvironment(engine)
  });
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

async function engineCommand(values) {
  const projectOption = takeOption(values, '--project');
  const global = takeFlag(values, '--global');
  const local = takeOption(values, '--local');
  const check = takeFlag(values, '--check');
  if (local) {
    if (global || check || values.length !== 1 || values[0] !== 'use')
      throw new Error('Usage: vecxy engine use --local <checkout-or-submodule> [-p <project>]');
    const project = findProjectRoot(projectOption || process.cwd());
    const engine = resolve(local);
    if (!isEngine(engine)) throw new Error(`Not a Vecxy checkout: ${engine}`);
    configureLocalProject(project, engine);
    console.log(`Project ${project}\nuses local Vecxy (${engineCommit(engine)})\n${engine}`);
    return 0;
  }
  if (values.length === 1 && values[0] === 'update') {
    if (global) throw new Error('Use engine install to update a global engine version.');
    return updateProjectEngine(findProjectRoot(projectOption || process.cwd()), check);
  }
  if (check) throw new Error('--check is only supported by vecxy update.');
  if (projectOption && global) throw new Error('Use either --project or --global, not both.');
  if ((values.length === 1 || values.length === 2) && values[0] === 'install') {
    await installEngine(values[1] || defaultEngineRef);
    return 0;
  }
  if (values.length === 2 && values[0] === 'use') {
    const ref = values[1];
    const installed = await installEngine(ref);
    if (global) {
      writeGlobalConfig(ref);
      console.log(`Default engine: Vecxy ${ref} (${engineCommit(installed)})\n${installed}`);
    } else {
      const project = findProjectRoot(projectOption || process.cwd());
      configureProject(project, ref, installed);
      console.log(`Project ${project}\nuses Vecxy ${ref} (${engineCommit(installed)})`);
    }
    return 0;
  }
  if (values.length === 1 && values[0] === 'current') {
    const project = global ? null : findProjectRoot(projectOption || process.cwd(), false);
    const config = global ? readGlobalConfig() : project && readProjectConfig(project);
    const selected = global ? resolveGlobalEngine() : resolveEngine(project || undefined);
    console.log(config ? `${config.engine.ref}\n${selected}\n${engineCommit(selected)}` : `default (${defaultEngineRef})\n${selected}\n${engineCommit(selected)}`);
    return 0;
  }
  if (values.length === 1 && values[0] === 'list' && !projectOption) {
    const root = join(home, 'engines');
    if (!existsSync(root)) { console.log('No engine versions installed.'); return 0; }
    const activeRef = readGlobalConfig()?.engine?.ref || defaultEngineRef;
    const directories = readdirSync(root, { withFileTypes: true }).filter(x => x.isDirectory()).sort((a, b) => a.name.localeCompare(b.name));
    for (const directory of directories) {
      const path = join(root, directory.name);
      if (!isEngine(path)) continue;
      const refFile = join(path, '.vecxy-ref');
      const ref = existsSync(refFile) ? readFileSync(refFile, 'utf8').trim() : directory.name;
      console.log(`${ref === activeRef ? '*' : ' '} ${ref}\t${engineCommit(path)}\t${path}`);
    }
    return 0;
  }
  throw new Error('Usage: vecxy engine <install|use|current|list> [ref] [--project <directory>|--global]');
}

function configureLocalProject(project, engine) {
  const branch = spawnSync('git', ['-C', engine, 'branch', '--show-current'], { encoding: 'utf8' });
  const ref = branch.status === 0 && branch.stdout.trim() || engineCommit(engine);
  configureProject(project, ref, engine);
  const config = { engine: { repository: ENGINE_REPOSITORY, ref,
    path: relative(project, engine).replaceAll('\\', '/') || '.' } };
  writeIfChanged(join(project, '.vecxy', 'config.json'), `${JSON.stringify(config, null, 2)}\n`);
}

function gitText(engine, values) {
  const result = spawnSync('git', ['-C', engine, ...values], { encoding: 'utf8' });
  if (result.status !== 0) throw new Error((result.stderr || '').trim() || `git ${values[0]} failed`);
  return result.stdout.trim();
}

async function updateProjectEngine(project, check) {
  const config = readProjectConfig(project);
  if (!config) throw new Error('Select an engine first: vecxy engine use <ref> or --local <path>');
  if (!config.engine.path) {
    if (check) throw new Error('--check currently requires a local checkout.');
    const engine = await installEngine(config.engine.ref);
    writeEngineProps(project, engine);
    return 0;
  }
  const engine = resolveEngine(project);
  if (!check && gitText(engine, ['status', '--porcelain']))
    throw new Error('Local engine has uncommitted changes. Commit or stash them before updating; no files were changed. Use vecxy update --check to inspect upstream.');
  const branch = gitText(engine, ['branch', '--show-current']);
  if (!branch) throw new Error('Local engine is on a detached HEAD. Check out a branch with an upstream before updating.');
  const remote = gitText(engine, ['config', `branch.${branch}.remote`]);
  gitText(engine, ['rev-parse', '--abbrev-ref', '--symbolic-full-name', '@{upstream}']);
  if (remote !== '.') run('git', ['-C', engine, 'fetch', '--prune', remote]);
  const [ahead, behind] = gitText(engine, ['rev-list', '--left-right', '--count', 'HEAD...@{upstream}']).split(/\s+/).map(Number);
  console.log(`Local engine ${engine}: ${ahead} ahead, ${behind} behind upstream.`);
  if (check) return 0;
  if (ahead && behind) throw new Error('Engine history diverged. Reconcile it manually; automatic update only fast-forwards.');
  if (behind) run('git', ['-C', engine, 'merge', '--ff-only', '@{upstream}']);
  writeEngineProps(project, engine);
  console.log(`Vecxy ${engineCommit(engine)}. A CLI linked from this checkout uses updated code on its next invocation.`);
  return 0;
}

function configureProject(project, ref, engine) {
  const settings = join(project, '.vecxy');
  mkdirSync(settings, { recursive: true });
  writeIfChanged(join(settings, 'config.json'), `${JSON.stringify({ engine: { repository: ENGINE_REPOSITORY, ref } }, null, 2)}\n`);
  writeEngineProps(project, engine);
  for (const file of readdirSync(project).filter(x => x.endsWith('.csproj'))) migrateProjectFile(join(project, file));
  const ignoreFile = join(project, '.gitignore');
  const ignore = existsSync(ignoreFile) ? readFileSync(ignoreFile, 'utf8') : '';
  if (!ignore.split(/\r?\n/).includes('.vecxy/Engine.props')) writeIfChanged(ignoreFile, `${ignore}${ignore.endsWith('\n') || !ignore ? '' : '\n'}.vecxy/Engine.props\n`);
}

function writeEngineProps(project, engine) {
  const slash = value => value.replaceAll('\\', '/');
  const escape = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
  const root = escape(slash(engine));
  writeIfChanged(join(project, '.vecxy', 'Engine.props'), `<Project>\n  <PropertyGroup>\n    <VecxyEnginePath>${root}</VecxyEnginePath>\n  </PropertyGroup>\n  <Import Project="$(VecxyEnginePath)/Code/Vecxy.Platforms/build/Vecxy.Platforms.props" />\n  <ItemGroup>\n    <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Assets/Vecxy.Assets.csproj" />\n    <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Audio/Vecxy.Audio.csproj" />\n    <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Diagnostics/Vecxy.Diagnostics.csproj" />\n    <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Engine/Vecxy.Engine.csproj" />\n    <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Input/Vecxy.Input.csproj" />\n    <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Kernel/Vecxy.Kernel.csproj" />\n    <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Messaging/Vecxy.Messaging.csproj" />\n    <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Networking/Vecxy.Networking.csproj" />\n    <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Pathfinding/Vecxy.Pathfinding.csproj" />\n    <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Physics/Vecxy.Physics.csproj" />\n    <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Rendering/Vecxy.Rendering.csproj" />\n    <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Scene/Vecxy.Scene.csproj" />\n    <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Scripting/Vecxy.Scripting.csproj" />\n    <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.UI/Vecxy.UI.csproj" />\n  </ItemGroup>\n</Project>\n`);
}

function migrateProjectFile(file) {
  let xml = readFileSync(file, 'utf8');
  const importPattern = /^\s*<Import\s+Project="[^"]*Vecxy\.Platforms[\\/]build[\\/]Vecxy\.Platforms\.props"\s*\/>\s*$/m;
  if (importPattern.test(xml)) xml = xml.replace(importPattern, '  <Import Project=".vecxy/Engine.props" />');
  else if (!xml.includes('.vecxy/Engine.props')) xml = xml.replace(/<\/Project>\s*$/, '  <Import Project=".vecxy/Engine.props" />\n</Project>\n');
  xml = xml.replace(/^[ \t]*<ProjectReference\s+Include="[^"]*Vecxy\.(?:Engine|Assets|Kernel)[\\/][^"]+\.csproj"\s*\/>[ \t]*\r?\n?/gm, '');
  writeIfChanged(file, xml);
}

function resolveEngine(project) {
  if (process.env.VECXY_ENGINE_PATH) return resolve(process.env.VECXY_ENGINE_PATH);
  const root = project ? findProjectRoot(project, false) : findProjectRoot(process.cwd(), false);
  const config = root && readProjectConfig(root);
  if (config) {
    if (config.engine.path) {
      const local = resolve(root, config.engine.path);
      if (!isEngine(local)) throw new Error(`Local Vecxy Engine is missing at ${local}. Initialize the submodule or select another checkout.`);
      return local;
    }
    const selected = engineDirectory(config.engine.ref);
    if (!isEngine(selected)) throw new Error(`Vecxy Engine '${config.engine.ref}' is not installed. Run: vecxy engine install ${config.engine.ref}`);
    return selected;
  }
  return resolveGlobalEngine();
}

function resolveGlobalEngine() {
  const globalConfig = readGlobalConfig();
  const selectedRef = globalConfig?.engine?.ref || defaultEngineRef;
  const versioned = engineDirectory(selectedRef);
  const legacy = join(home, 'engine');
  if (globalConfig && !isEngine(versioned))
    throw new Error(`Vecxy Engine '${selectedRef}' is not installed. Run: vecxy engine install ${selectedRef}`);
  return isEngine(versioned) ? versioned : legacy;
}

function readGlobalConfig() {
  const file = join(home, 'config.json');
  if (!existsSync(file)) return null;
  const config = JSON.parse(readFileSync(file, 'utf8'));
  if (!config?.engine?.ref || typeof config.engine.ref !== 'string') throw new Error(`Invalid Vecxy settings: ${file}`);
  return config;
}

function writeGlobalConfig(ref) {
  mkdirSync(home, { recursive: true });
  writeIfChanged(join(home, 'config.json'), `${JSON.stringify({ engine: { repository: ENGINE_REPOSITORY, ref } }, null, 2)}\n`);
}

function readProjectConfig(project) {
  const file = join(project, '.vecxy', 'config.json');
  if (!existsSync(file)) return null;
  const config = JSON.parse(readFileSync(file, 'utf8'));
  if (!config?.engine?.ref || typeof config.engine.ref !== 'string') throw new Error(`Invalid Vecxy project settings: ${file}`);
  return config;
}

function findProjectRoot(start, required = true) {
  let current = resolve(start);
  if (!existsSync(current)) {
    if (required) throw new Error(`Path does not exist: ${start}`);
    return null;
  }
  if (statSync(current).isFile()) current = dirname(current);
  while (true) {
    if (existsSync(join(current, '.vecxy', 'config.json')) || readdirSync(current).some(x => x.endsWith('.csproj'))) return current;
    const parent = dirname(current);
    if (parent === current) break;
    current = parent;
  }
  if (required) throw new Error(`Vecxy project was not found from ${start}`);
  return null;
}

function projectDirectoryFromArgs(values) {
  const separator = values.indexOf('--');
  if (separator >= 0) values = values.slice(0, separator);
  const index = values.indexOf('--project');
  return index >= 0 && values[index + 1] ? values[index + 1] : process.cwd();
}

function engineDirectory(ref) {
  const slug = ref.replace(/[^a-zA-Z0-9._-]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 48) || 'ref';
  const hash = createHash('sha256').update(ref).digest('hex').slice(0, 10);
  return join(home, 'engines', `${slug}-${hash}`);
}

function validateRef(ref) {
  if (!ref || ref.startsWith('-') || /[\0\r\n]/.test(ref)) throw new Error('Engine ref must be a tag, branch, or commit hash.');
}
function isEngine(path) { return existsSync(join(path, 'tools', 'Vecxy.Cli', 'Vecxy.Cli.csproj')); }
function engineCommit(path) {
  if (!isEngine(path)) return 'not installed';
  const result = spawnSync('git', ['-C', path, 'rev-parse', '--short', 'HEAD'], { encoding: 'utf8' });
  return result.status === 0 ? result.stdout.trim() : 'unknown';
}
function resolveGitRef(path, ref) {
  // A clone keeps its local branch at the commit where it was created. Prefer
  // the freshly fetched remote-tracking branch so named branches really update.
  // Tags and commit hashes naturally fall back to the literal ref.
  const candidates = ref === 'latest' ? ['origin/develop'] : [`origin/${ref}`, ref];
  for (const candidate of candidates) {
    const result = spawnSync('git', ['-C', path, 'rev-parse', '--verify', `${candidate}^{commit}`], { encoding: 'utf8' });
    if (result.status === 0) return result.stdout.trim();
  }
  return null;
}
function installedRef(path) {
  const file = join(path, '.vecxy-ref');
  return existsSync(file) ? readFileSync(file, 'utf8').trim() : defaultEngineRef;
}
function takeOption(values, name) {
  const index = values.indexOf(name);
  if (index < 0) return null;
  if (index + 1 >= values.length) throw new Error(`Missing value for ${name}`);
  const value = values[index + 1];
  values.splice(index, 2);
  return value;
}

function takeFlag(values, name) {
  const index = values.indexOf(name);
  if (index < 0) return false;
  values.splice(index, 1);
  return true;
}

function writeIfChanged(file, contents) {
  if (existsSync(file) && readFileSync(file, 'utf8') === contents) return false;
  writeFileSync(file, contents);
  return true;
}

function expandShortOptions(values) {
  const aliases = new Map([
    ['-p', '--project'], ['-o', '--output'], ['-e', '--engine'], ['-t', '--platform'],
    ['-r', '--runtime'], ['-f', '--format'], ['-k', '--keystore'], ['-a', '--alias'],
    ['-y', '--yes'], ['-g', '--global'], ['-h', '--help'], ['-V', '--version']
  ]);
  const separator = values.indexOf('--');
  return values.map((value, index) => separator >= 0 && index > separator ? value : aliases.get(value) || value);
}
