import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, dirname } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const cli = fileURLToPath(new URL('../bin/vecxy.js', import.meta.url));
function git(cwd, ...args) {
  const r = spawnSync('git', ['-c','user.name=Vecxy Test','-c','user.email=test@example.invalid',...args], {cwd, encoding:'utf8'});
  assert.equal(r.status,0,r.stderr); return r.stdout.trim();
}
function fixture(t) {
  const root=mkdtempSync(join(tmpdir(),'vecxy-local-')); t.after(()=>rmSync(root,{recursive:true,force:true}));
  const remote=join(root,'upstream.git'), seed=join(root,'seed'), engine=join(root,'Vendors','Vecxy'), project=join(root,'Game'), home=join(root,'home');
  mkdirSync(seed); mkdirSync(project); mkdirSync(home);
  git(root,'init','--bare',remote); git(seed,'init','-b','develop');
  mkdirSync(join(seed,'tools','Vecxy.Cli'),{recursive:true}); writeFileSync(join(seed,'tools','Vecxy.Cli','Vecxy.Cli.csproj'),'<Project />');
  git(seed,'add','.'); git(seed,'commit','-m','initial'); git(seed,'remote','add','origin',remote); git(seed,'push','-u','origin','develop');
  git(root,'clone','--branch','develop',remote,engine);
  writeFileSync(join(project,'Game.csproj'),'<Project Sdk="Microsoft.NET.Sdk"></Project>');
  const call=(...args)=>spawnSync(process.execPath,[cli,...args],{cwd:project,encoding:'utf8',env:{...process.env,VECXY_HOME:home,VECXY_ENGINE_PATH:''}});
  assert.equal(call('engine','use','--local','../Vendors/Vecxy').status,0);
  const advance=()=>{writeFileSync(join(seed,'new.txt'),'upstream');git(seed,'add','.');git(seed,'commit','-m','upstream');git(seed,'push');};
  return {root,remote,seed,engine,project,home,call,advance};
}
test('local checkout uses a portable path and resolves without engine cache',t=>{
 const f=fixture(t), config=JSON.parse(readFileSync(join(f.project,'.vecxy/config.json')));
 assert.equal(config.engine.path,'../Vendors/Vecxy');
 const r=f.call('engine','current');assert.equal(r.status,0);assert.ok(r.stdout.includes(f.engine));
});
test('missing local engine reports the submodule instead of falling back globally',t=>{
 const f=fixture(t);rmSync(f.engine,{recursive:true,force:true});const r=f.call('engine','current');
 assert.notEqual(r.status,0);assert.match(r.stderr,/Initialize the submodule/);
});
test('update refuses uncommitted files and preserves their contents',t=>{
 const f=fixture(t), file=join(f.engine,'custom.txt');writeFileSync(file,'my changes');
 const before=git(f.engine,'rev-parse','HEAD'),r=f.call('update');
 assert.notEqual(r.status,0);assert.match(r.stderr,/uncommitted changes/);assert.equal(readFileSync(file,'utf8'),'my changes');assert.equal(git(f.engine,'rev-parse','HEAD'),before);
});
test('check fetches upstream without changing HEAD, even with local edits',t=>{
 const f=fixture(t);f.advance();writeFileSync(join(f.engine,'custom.txt'),'mine');
 const before=git(f.engine,'rev-parse','HEAD'),r=f.call('update','--check');
 assert.equal(r.status,0,r.stderr);assert.match(r.stdout,/1 behind/);assert.equal(git(f.engine,'rev-parse','HEAD'),before);
});
test('update fast-forwards to upstream',t=>{
 const f=fixture(t);f.advance();const r=f.call('engine','update');
 assert.equal(r.status,0,r.stderr);assert.equal(git(f.engine,'rev-parse','HEAD'),git(f.seed,'rev-parse','HEAD'));
});
test('diverged history and detached HEAD are never overwritten',t=>{
 const f=fixture(t);f.advance();writeFileSync(join(f.engine,'local.txt'),'local');git(f.engine,'add','.');git(f.engine,'commit','-m','local');
 const before=git(f.engine,'rev-parse','HEAD');let r=f.call('update');assert.notEqual(r.status,0);assert.match(r.stderr,/diverged/);assert.equal(git(f.engine,'rev-parse','HEAD'),before);
 git(f.engine,'checkout','--detach');r=f.call('update');assert.notEqual(r.status,0);assert.match(r.stderr,/detached HEAD/);
});
test('local and global modes cannot be combined',t=>{
 const f=fixture(t);assert.notEqual(f.call('engine','use','--local','../Vendors/Vecxy','--global').status,0);
});
test('forwarding propagates the selected SDK to nested dotnet commands',t=>{
 const f=fixture(t), sdk=join(f.home,'dotnet'), captured=join(f.root,'env.json');mkdirSync(sdk);
 const fake=join(sdk,'dotnet');writeFileSync(fake,`#!${process.execPath}\nimport('node:fs').then(fs=>{if(process.argv.includes('--list-sdks'))console.log('10.0.110 [fake]');else fs.writeFileSync(${JSON.stringify(captured)},JSON.stringify({path:process.env.PATH,root:process.env.DOTNET_ROOT,engine:process.env.VECXY_ENGINE_PATH}));});\n`,{mode:0o755});
 const r=f.call('assets','validate');assert.equal(r.status,0,r.stderr);const env=JSON.parse(readFileSync(captured));
 assert.equal(env.root,sdk);assert.ok(env.path.startsWith(sdk));assert.equal(env.engine,f.engine);
});
