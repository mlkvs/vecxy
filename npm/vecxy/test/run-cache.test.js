import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { cachedRunCli, directoryHash } from '../lib/run-cache.js';
import { restoredGraphMatches } from '../lib/restore-cache.js';
const hash = value => createHash('sha256').update(value).digest('hex').toUpperCase();
function fixture(t) {
 const root=mkdtempSync(join(tmpdir(),'vecxy-tool-cache-'));t.after(()=>rmSync(root,{recursive:true,force:true}));
 const project=join(root,'Game'),engine=join(root,'Engine'),tool=join(engine,'tools/Vecxy.Cli'),bin=join(tool,'bin/Debug/net10.0');
 const directory=join(project,'obj/vecxy-run');mkdirSync(bin,{recursive:true});mkdirSync(directory,{recursive:true});
 const source=join(tool,'Program.cs'),assembly=join(bin,'Vecxy.Cli.dll');writeFileSync(source,'original');writeFileSync(assembly,'compiled');
 const environment={VECXY_ENGINE_PATH:engine};const inputs={[source]:hash('original'),['env:VECXY_ENGINE_PATH']:hash(engine),['dir:'+tool]:directoryHash(tool)};
 const snapshot={ToolAssembly:assembly,ToolInputs:inputs,ToolOutputs:{[assembly]:hash('compiled')}};
 const stamp=join(directory,'stamp.json');writeFileSync(stamp,JSON.stringify(snapshot));
 return {project,engine,tool,source,assembly,environment,stamp};
}
test('an unchanged compiled CLI is reused',t=>{const f=fixture(t);assert.equal(cachedRunCli(f.project,f.engine,f.environment),f.assembly);});
test('CLI source edits and added source files require compilation',t=>{
 const f=fixture(t);writeFileSync(f.source,'modified');assert.equal(cachedRunCli(f.project,f.engine,f.environment),null);
 writeFileSync(f.source,'original');writeFileSync(join(f.tool,'New.cs'),'new');assert.equal(cachedRunCli(f.project,f.engine,f.environment),null);
});
test('changed binaries, build environment and corrupt stamps fall back to the SDK',t=>{
 const f=fixture(t);assert.equal(cachedRunCli(f.project,f.engine,{...f.environment,DefineConstants:'TEST'}),null);
 writeFileSync(f.assembly,'changed');assert.equal(cachedRunCli(f.project,f.engine,f.environment),null);
 writeFileSync(f.stamp,'broken');assert.equal(cachedRunCli(f.project,f.engine,f.environment),null);
});
test('a cache from another engine cannot select the CLI',t=>{const f=fixture(t);assert.equal(cachedRunCli(f.project,join(f.engine,'other'),f.environment),null);});

test('ignored IDE/build entries do not repeatedly invalidate the compiled CLI',t=>{
 const f=fixture(t);mkdirSync(join(f.tool,'.idea'));writeFileSync(join(f.tool,'settings.user'),'local');
 assert.equal(cachedRunCli(f.project,f.engine,f.environment),f.assembly);
});

test('the CLI can be reused before any game build completes', t => {
 const f = fixture(t);
 const obj = join(f.tool, 'obj');
 mkdirSync(obj);
 writeFileSync(join(obj, 'vecxy-tool.json'), readFileSync(f.stamp));
 rmSync(f.stamp);
 assert.equal(cachedRunCli(null, f.engine, {...f.environment, VECXY_CLI_BOOTSTRAP: '123'}), f.assembly);
});

test('restore graph reuse ignores audit metadata and keeps package and runtime checks', () => {
 const spec = (version, runtime, enableAudit) => ({
  format: 1,
  restore: {},
  projects: {
   'Vecxy.Cli.csproj': {
    restore: {restoreAuditProperties: {enableAudit}},
    frameworks: {net10: {dependencies: {Package: {version}}}},
    runtimes: {[runtime]: {}}
   }
  }
 });
 const restored = spec('1.0', 'linux-x64', 'false');
 const current = spec('1.0', 'linux-x64', 'true');
 current.restore['Vecxy.Cli.csproj'] = {};
 assert.equal(restoredGraphMatches(current, restored), true);
 assert.equal(restoredGraphMatches(spec('2.0', 'linux-x64', 'true'), restored), false);
 assert.equal(restoredGraphMatches(spec('1.0', 'linux-arm64', 'true'), restored), false);
});
