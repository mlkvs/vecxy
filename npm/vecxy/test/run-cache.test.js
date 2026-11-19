import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { cachedRunCli, directoryHash } from '../lib/run-cache.js';
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
