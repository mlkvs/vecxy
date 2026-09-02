#!/usr/bin/env node
import { spawn, spawnSync } from 'node:child_process';
import { createInterface } from 'node:readline/promises';
import { stdin, stdout } from 'node:process';
import { existsSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { homedir, platform } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const VERSION = '0.1.2';
const DOTNET_VERSION = '10.0.110';
const ENGINE_REPOSITORY = 'https://github.com/mlkvs/vecxy.git';
const home = process.env.VECXY_HOME || join(homedir(), '.vecxy');
const defaultEngineRef = 'develop';
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
if (args[0] === 'engine') {
  engineCommand(args.slice(1)).then(code => process.exit(code), fail);
} else if (args[0] === 'setup') {
  setup(args.slice(1)).then(code => process.exit(code), fail);
} else {
  forward(args);
}

function printHelp() {
  console.log(`Vecxy CLI ${VERSION}

Usage:
  vecxy setup [--yes] [--no-android] [--dry-run] [--engine <ref>]
  vecxy doctor [--no-android]
  vecxy new <name> [--output <directory>]
  vecxy engine install <tag|branch|commit>
  vecxy engine use <tag|branch|commit> [--project <directory>]
  vecxy engine current [--project <directory>]
  vecxy engine list
  vecxy build [dev|release] --project <path> --platform <linux|windows|android>
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
    console.log(`Would install Vecxy Engine ref '${engineRef}' in ${engineDirectory(engineRef)}`);
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
  const installedEngine = await installEngine(engineRef);
  if (projectConfig) configureProject(project, engineRef, installedEngine);
  if (android) await installAndroid(installedEngine);
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

async function installEngine(ref = defaultEngineRef) {
  if (!commandExists('git')) throw new Error('Git is required. Install Git and run vecxy setup again.');
  validateRef(ref);
  const engine = engineDirectory(ref);
  const gitOptions = ['-c', 'credential.helper=', '-c', 'core.askPass='];
  const validCheckout = existsSync(join(engine, '.git')) && isEngine(engine);
  if (validCheckout) {
    console.log(`Updating Vecxy Engine '${ref}' in ${engine}...`);
  } else {
    if (existsSync(engine)) rmSync(engine, { recursive: true, force: true });
    mkdirSync(dirname(engine), { recursive: true });
    run('git', [...gitOptions, 'clone', '--filter=blob:none', '--no-checkout', ENGINE_REPOSITORY, engine], { GIT_TERMINAL_PROMPT: '0' });
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
  const engine = resolveEngine(projectDirectoryFromArgs(values));
  const project = join(engine, 'tools', 'Vecxy.Cli', 'Vecxy.Cli.csproj');
  if (!existsSync(project)) fail(new Error(`Vecxy Engine is missing at ${engine}. Run: vecxy setup`));
  const forwarded = [...values];
  if (values[0] === 'new' && !values.includes('--engine')) forwarded.push('--engine', engine);
  const child = spawn(dotnet, ['run', '--project', project, '--', ...forwarded], {
    stdio: 'inherit', env: { ...process.env, VecxyEnginePath: engine, VECXY_ENGINE_PATH: engine, VECXY_ENGINE_REF: installedRef(engine) }
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
  if (values.length === 2 && values[0] === 'install') {
    await installEngine(values[1]);
    return 0;
  }
  if (values.length === 2 && values[0] === 'use') {
    const ref = values[1];
    const installed = await installEngine(ref);
    const project = findProjectRoot(projectOption || process.cwd());
    configureProject(project, ref, installed);
    console.log(`Project ${project}\nuses Vecxy ${ref} (${engineCommit(installed)})`);
    return 0;
  }
  if (values.length === 1 && values[0] === 'current') {
    const project = findProjectRoot(projectOption || process.cwd(), false);
    const config = project && readProjectConfig(project);
    const selected = resolveEngine(project || undefined, false);
    console.log(config ? `${config.engine.ref}\n${selected}\n${engineCommit(selected)}` : `default (${defaultEngineRef})\n${selected}\n${engineCommit(selected)}`);
    return 0;
  }
  if (values.length === 1 && values[0] === 'list' && !projectOption) {
    const root = join(home, 'engines');
    if (!existsSync(root)) { console.log('No engine versions installed.'); return 0; }
    for (const directory of readdirSync(root, { withFileTypes: true }).filter(x => x.isDirectory())) {
      const path = join(root, directory.name);
      if (!isEngine(path)) continue;
      const refFile = join(path, '.vecxy-ref');
      const ref = existsSync(refFile) ? readFileSync(refFile, 'utf8').trim() : directory.name;
      console.log(`${ref}\t${engineCommit(path)}\t${path}`);
    }
    return 0;
  }
  throw new Error('Usage: vecxy engine <install|use|current|list> [ref] [--project <directory>]');
}

function configureProject(project, ref, engine) {
  const settings = join(project, '.vecxy');
  mkdirSync(settings, { recursive: true });
  writeFileSync(join(settings, 'config.json'), `${JSON.stringify({ engine: { repository: ENGINE_REPOSITORY, ref } }, null, 2)}\n`);
  writeEngineProps(project, engine);
  for (const file of readdirSync(project).filter(x => x.endsWith('.csproj'))) migrateProjectFile(join(project, file));
  const ignoreFile = join(project, '.gitignore');
  const ignore = existsSync(ignoreFile) ? readFileSync(ignoreFile, 'utf8') : '';
  if (!ignore.split(/\r?\n/).includes('.vecxy/Engine.props')) writeFileSync(ignoreFile, `${ignore}${ignore.endsWith('\n') || !ignore ? '' : '\n'}.vecxy/Engine.props\n`);
}

function writeEngineProps(project, engine) {
  const slash = value => value.replaceAll('\\', '/');
  const escape = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
  const root = escape(slash(engine));
  writeFileSync(join(project, '.vecxy', 'Engine.props'), `<Project>\n  <PropertyGroup>\n    <VecxyEnginePath>${root}</VecxyEnginePath>\n  </PropertyGroup>\n  <Import Project="$(VecxyEnginePath)/Code/Vecxy.Platforms/build/Vecxy.Platforms.props" />\n  <ItemGroup>\n    <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Engine/Vecxy.Engine.csproj" />\n    <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Assets/Vecxy.Assets.csproj" />\n    <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Kernel/Vecxy.Kernel.csproj" />\n  </ItemGroup>\n</Project>\n`);
}

function migrateProjectFile(file) {
  let xml = readFileSync(file, 'utf8');
  const importPattern = /^\s*<Import\s+Project="[^"]*Vecxy\.Platforms[\\/]build[\\/]Vecxy\.Platforms\.props"\s*\/>\s*$/m;
  if (importPattern.test(xml)) xml = xml.replace(importPattern, '  <Import Project=".vecxy/Engine.props" />');
  else if (!xml.includes('.vecxy/Engine.props')) xml = xml.replace(/<\/Project>\s*$/, '  <Import Project=".vecxy/Engine.props" />\n</Project>\n');
  xml = xml.replace(/^[ \t]*<ProjectReference\s+Include="[^"]*Vecxy\.(?:Engine|Assets|Kernel)[\\/][^"]+\.csproj"\s*\/>[ \t]*\r?\n?/gm, '');
  writeFileSync(file, xml);
}

function resolveEngine(project, syncProject = true) {
  if (process.env.VECXY_ENGINE_PATH) return resolve(process.env.VECXY_ENGINE_PATH);
  const root = project ? findProjectRoot(project, false) : findProjectRoot(process.cwd(), false);
  const config = root && readProjectConfig(root);
  if (config) {
    const selected = engineDirectory(config.engine.ref);
    if (!isEngine(selected)) throw new Error(`Vecxy Engine '${config.engine.ref}' is not installed. Run: vecxy engine install ${config.engine.ref}`);
    if (syncProject) writeEngineProps(root, selected);
    return selected;
  }
  const versioned = engineDirectory(defaultEngineRef);
  const legacy = join(home, 'engine');
  return isEngine(versioned) ? versioned : legacy;
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
  for (const candidate of [ref, `origin/${ref}`]) {
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
