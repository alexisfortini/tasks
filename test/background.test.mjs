import test from 'node:test';
import assert from 'node:assert/strict';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {fileURLToPath} from 'node:url';

test('Windows background launcher validates .env and encrypted synthetic keys, restricts ACLs, rejects duplicates and isolates child credentials', {skip:process.platform!=='win32'}, async()=>{
  await promisify(execFile)(fileURLToPath(new URL('../connection/background-tunnel.exe',import.meta.url)),['--self-test'],{
    windowsHide:true,timeout:15000,env:{...process.env,OPENAI_API_KEY:'NONSECRET_UNRELATED',OPENAI_ADMIN_KEY:'NONSECRET_UNRELATED'}
  });
  const foreground=await promisify(execFile)(fileURLToPath(new URL('../connection/foreground-tunnel.exe',import.meta.url)),['--self-test'],{windowsHide:true,timeout:15000});
  assert.match(foreground.stdout,/OFFLINE_MASKED_LAUNCHER_CHILD_ENV_PASS_NO_NETWORK/);
});
