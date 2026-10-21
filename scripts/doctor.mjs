import {existsSync,readFileSync} from 'node:fs';
import {resolve,join} from 'node:path';
import {readEnvironment} from '../src/environment.mjs';

// Offline only. Never emit setting values, task data, or decrypted credentials.
const root=resolve(import.meta.dirname,'..');
try{
  const settings=readEnvironment();
  const profile=join(root,'connection/profiles/google-tasks.yaml');
  const profileText=existsSync(profile)?readFileSync(profile,'utf8'):'';
  const rootSlash=root.replaceAll('\\','/');
  console.log(JSON.stringify({
    nodeSupported:Number(process.versions.node.split('.')[0])>=22,
    envFilePresent:existsSync(join(root,'.env')),
    accountPinPresent:!!settings.GOOGLE_TASKS_ACCOUNT_EMAIL||existsSync(join(root,'.private/account.json')),
    tunnelKeyInEnv:!!settings.CONTROL_PLANE_API_KEY,
    encryptedTunnelKeyPresent:existsSync(join(root,'.private/tunnel-runtime/key.dpapi')),
    encryptedGoogleGrantPresent:existsSync(join(root,'.private/google-oauth.dpapi')),
    tunnelBinaryPresent:existsSync(join(root,'tools/tunnel-client-v0.0.15/tunnel-client.exe')),
    backgroundLauncherPresent:existsSync(join(root,'connection/background-tunnel.exe')),
    profilePresent:!!profileText,
    profileUsesCurrentServerPath:profileText.includes(rootSlash+'/src/server.mjs')||profileText.includes((root+'\\src\\server.mjs').replaceAll('\\','\\\\')),
    liveConnectionVerified:false
  }));
}catch{console.error('LOCAL_CONFIGURATION_CHECK_FAILED_NO_VALUES_LOGGED');process.exitCode=1;}
