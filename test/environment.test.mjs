import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,writeFileSync,rmSync,readFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join,dirname,resolve} from 'node:path';
import {readEnvironment} from '../src/environment.mjs';
import {inspectText} from '../scripts/audit-share.mjs';

test('local .env reads supported settings without exporting arbitrary execution flags or secrets',()=>{
  const directory=mkdtempSync(join(tmpdir(),'tasks-env-'));
  try{
    const file=join(directory,'.env');
    writeFileSync(file,'GOOGLE_TASKS_ACCOUNT_EMAIL="personal@example.com"\nGOOGLE_TASKS_TUNNEL_ID=tunnel_example\nCONTROL_PLANE_API_KEY="mock-value"\nNODE_OPTIONS=untrusted\nLOG_HTTP_RAW_UNSAFE=true\n');
    assert.deepEqual(readEnvironment(file,{}),{GOOGLE_TASKS_ACCOUNT_EMAIL:'personal@example.com',GOOGLE_TASKS_TUNNEL_ID:'tunnel_example',CONTROL_PLANE_API_KEY:'mock-value'});
    assert.equal(readEnvironment(file,{GOOGLE_TASKS_ACCOUNT_EMAIL:'override@example.com'}).GOOGLE_TASKS_ACCOUNT_EMAIL,'override@example.com');
    assert.deepEqual(readEnvironment(join(directory,'absent'),{}),{});
    writeFileSync(file,'CONTROL_PLANE_API_KEY=\nGOOGLE_TASKS_ACCOUNT_EMAIL=\n');
    assert.deepEqual(readEnvironment(file,{}),{});
    writeFileSync(file,'X'.repeat(65537));
    assert.throws(()=>readEnvironment(file,{}),/^Error: LOCAL_ENV_READ_FAILED$/);
  }finally{assert.equal(dirname(directory),resolve(tmpdir()));rmSync(directory,{recursive:true,force:true});}
});

test('only the placeholder-only root .env.example is shareable; populated or nested env files fail audit',()=>{
  const template=readFileSync(new URL('../.env.example',import.meta.url),'utf8');
  assert.deepEqual(inspectText('.env.example',template),[]);
  assert.ok(inspectText('.env.example','CONTROL_PLANE_API_KEY=mock-value').includes('nonempty-or-unsupported-env-template'));
  assert.ok(inspectText('.env.example','NODE_OPTIONS=').includes('nonempty-or-unsupported-env-template'));
  assert.ok(inspectText('.env','').includes('generated-or-private-path'));
  assert.ok(inspectText('nested/.env.example','').includes('generated-or-private-path'));
});
