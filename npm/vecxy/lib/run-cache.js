import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { join, resolve, sep } from 'node:path';

const ignored = new Set(['bin', 'obj', 'build', 'artifacts', '.artifacts', '.git', '.idea', '.vs', '.agents', '.vecxy', 'node_modules', '__pycache__', '.venv', 'venv']);
export function directoryHash(path) {
  return hash(readdirSync(path).filter(name => !ignored.has(name.toLowerCase()) && !/\.(log|user)$/i.test(name) && !/\.blend[^.]*$/i.test(name) && name !== 'vecxy.components.json').sort().join('\n'));
}
const hash = value => createHash('sha256').update(value).digest('hex').toUpperCase();
function trackedEnvironment(name) {
  return /^(VECXY_|Vecxy|NUGET_)/i.test(name) ||
    ['DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_ROOT_ARM64', 'DOTNET_ROOT_X86', 'MSBuildSDKsPath', 'Configuration', 'DefineConstants', 'CI'].includes(name);
}
function currentValue(key, environment) {
  if (key.startsWith('env:')) return hash(environment[key.slice(4)] ?? '');
  if (key.startsWith('dir:'))
    return directoryHash(key.slice(4));
  return existsSync(key) && statSync(key).isFile() ? hash(readFileSync(key)) : '<missing>';
}

// Reuse the validated CLI snapshot, or a snapshot from an earlier game build.
export function cachedRunCli(project, engine, environment) {
  const snapshots = [join(engine, 'tools', 'Vecxy.Cli', 'obj', 'vecxy-tool.json')];
  const directory = project && join(project, 'obj', 'vecxy-run');
  if (directory && existsSync(directory))
    snapshots.push(...readdirSync(directory).filter(name => name.endsWith('.json')).map(name => join(directory, name)));
  for (const path of snapshots) {
    try {
      const snapshot = JSON.parse(readFileSync(path, 'utf8'));
      const assembly = snapshot.ToolAssembly;
      if (typeof assembly !== 'string' || !resolve(assembly).startsWith(resolve(engine, 'tools', 'Vecxy.Cli') + sep) ||
          !assembly.toLowerCase().endsWith('vecxy.cli.dll') || !existsSync(assembly)) continue;
      const inputs = snapshot.ToolInputs, outputs = snapshot.ToolOutputs;
      if (!inputs || !outputs || !Object.keys(outputs).length) continue;
      const expected = Object.keys(inputs).filter(key => key.startsWith('env:')).sort();
      const actual = Object.keys(environment).filter(name => trackedEnvironment(name) && !['VECXY_WAIT_FOR_DEBUGGER', 'VECXY_CLI_BOOTSTRAP'].includes(name))
        .map(name => 'env:' + name).sort();
      if (JSON.stringify(expected) !== JSON.stringify(actual)) continue;
      if ([...Object.entries(inputs), ...Object.entries(outputs)].every(([key, value]) => currentValue(key, environment) === value))
        return assembly;
    } catch { /* Missing/corrupt stamps and changed files fall back to the SDK. */ }
  }
  return null;
}
