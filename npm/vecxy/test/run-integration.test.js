import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, statSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
const engine=process.env.VECXY_INTEGRATION_ENGINE;
const cli=fileURLToPath(new URL('../bin/vecxy.js',import.meta.url));
test('run reuses builds and refreshes existing asset contents without compiling code',{
 skip:!engine || !['linux','win32'].includes(process.platform),timeout:180000
},t=>{
 const root=mkdtempSync(join(process.env.VECXY_TEST_TEMP || tmpdir(),'vecxy-run-integration-'));t.after(()=>rmSync(root,{recursive:true,force:true}));
 const game=join(root,'Game');mkdirSync(join(game,'Assets'),{recursive:true});mkdirSync(join(game,'.vecxy'));
 writeFileSync(join(root,'Directory.Build.props'),'<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><BaseIntermediateOutputPath>obj/Desktop/</BaseIntermediateOutputPath></PropertyGroup></Project>');
 writeFileSync(join(game,'.vecxy/config.json'),JSON.stringify({engine:{ref:'develop',path:resolve(engine)}}));
 writeFileSync(join(game,'Game.csproj'),'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><VecxyGenerateAuthoring>false</VecxyGenerateAuthoring></PropertyGroup><Import Project=".vecxy/Engine.props"/><ItemGroup><None Update="Assets.manifest" CopyToPublishDirectory="PreserveNewest"/></ItemGroup></Project>');
 const source=join(game,'Program.cs');writeFileSync(source,'Console.WriteLine("RUN_FIXTURE_ONE");');
 const asset=join(game,'Assets/settings.yaml');writeFileSync(asset,'speed: 2.5\n');
 const invoke=(...args)=>{
  const r=spawnSync(process.execPath,[cli,...args],{cwd:game,encoding:'utf8',timeout:120000,env:{...process.env,VECXY_ENGINE_PATH:resolve(engine)}});
  assert.equal(r.status,0,(r.stdout||'')+'\n'+(r.stderr||''));return r.stdout;
 };
 assert.match(invoke('run'),/Desktop build:/);
 assert.match(invoke('run'),/Run build is up to date/);
 const output=join(game,'artifacts',process.platform==='win32'?'windows':'linux','dev');
 const dll=join(output,'Game.dll'),generated=join(game,'Generated/Assets.g.cs');
 const original=readFileSync(dll),mtime=statSync(dll).mtimeMs,generatedTime=statSync(generated).mtimeMs;
 writeFileSync(asset,'speed: 3.0\n');const refreshed=invoke('run');
 assert.match(refreshed,/Runtime assets refreshed/);assert.doesNotMatch(refreshed,/Desktop build:|MSBuild version/);
 assert.deepEqual(readFileSync(dll),original);assert.equal(statSync(dll).mtimeMs,mtime);assert.equal(statSync(generated).mtimeMs,generatedTime);
 assert.match(invoke('run'),/Run build is up to date/);
 writeFileSync(join(game,'Assets/new.txt'),'new handle');assert.match(invoke('run'),/Desktop build:/);
 assert.match(invoke('run'),/Run build is up to date/);
 writeFileSync(source,'Console.WriteLine("RUN_FIXTURE_TWO");');const rebuilt=invoke('run');
 assert.match(rebuilt,/Desktop build:/);assert.match(rebuilt,/RUN_FIXTURE_TWO/);
 assert.match(invoke('run'),/Run build is up to date/);
});
