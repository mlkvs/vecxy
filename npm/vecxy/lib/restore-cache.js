import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join } from 'node:path';
import { isDeepStrictEqual } from 'node:util';

function readJson(path) {
  return JSON.parse(readFileSync(path, 'utf8'));
}

export function restoredGraphMatches(graph, restored) {
  // Audit reports are network metadata. They don't change which packages MSBuild consumes.
  const dependencies = spec => Object.fromEntries(Object.entries(spec.projects).map(([path, project]) => {
    const { restoreAuditProperties, ...restore } = project.restore;
    return [path, { ...project, restore }];
  }));
  // The evaluation lists every project in "restore"; the saved dgspec lists only its root.
  return graph.format === restored.format && isDeepStrictEqual(dependencies(graph), dependencies(restored));
}

export function canReuseCliRestore(dotnet, project, environment) {
  const obj = join(dirname(project), 'obj');
  const graphName = basename(project) + '.nuget.dgspec.json';
  if (![join(obj, 'Desktop', graphName), join(obj, graphName)].some(existsSync))
    return false;
  let temporary;
  try {
    temporary = mkdtempSync(join(tmpdir(), 'vecxy-restore-graph-'));
    const file = join(temporary, 'graph.json');
    const evaluation = spawnSync(dotnet, ['msbuild', project, '-nologo', '-t:GenerateRestoreGraphFile',
      `-p:RestoreGraphOutputPath=${file}`], { stdio: 'ignore', env: environment });
    if (evaluation.status !== 0) return false;

    const current = readJson(file);
    const entry = Object.values(current.projects).find(value => value.restore.projectPath === project);
    if (!entry) return false;
    const previous = readJson(join(entry.restore.outputPath, graphName));
    if (!restoredGraphMatches(current, previous)) return false;
    return Object.values(current.projects).every(({ restore }) => {
      const cache = readJson(join(restore.outputPath, 'project.nuget.cache'));
      return cache.success && cache.projectFilePath === restore.projectPath &&
        Array.isArray(cache.expectedPackageFiles) && cache.expectedPackageFiles.every(existsSync) &&
        ['project.assets.json', basename(restore.projectPath) + '.nuget.g.props',
          basename(restore.projectPath) + '.nuget.g.targets'].every(name => existsSync(join(restore.outputPath, name)));
    });
  } catch {
    return false;
  } finally {
    if (temporary) rmSync(temporary, { recursive: true, force: true });
  }
}
