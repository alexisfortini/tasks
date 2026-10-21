import {readFileSync} from 'node:fs';
import {parseEnv} from 'node:util';

export const ENV_KEYS=['GOOGLE_TASKS_ACCOUNT_EMAIL','GOOGLE_TASKS_TUNNEL_ID','CONTROL_PLANE_API_KEY'];
export const envPath=new URL('../.env',import.meta.url);

// Read only supported application settings. Never load arbitrary NODE_OPTIONS,
// logging flags, or commands from this file into the process environment.
export function readEnvironment(target=envPath,environment=process.env){
  let parsed={};
  try{
    const content=readFileSync(target,'utf8');
    if(content.length>65536)throw Error();
    parsed=parseEnv(content);
  }catch(error){if(error.code!=='ENOENT')throw Error('LOCAL_ENV_READ_FAILED');}
  return Object.fromEntries(ENV_KEYS.flatMap(key=>{
    const value=Object.hasOwn(environment,key)?environment[key]:parsed[key];
    return typeof value==='string'&&value.length?[ [key,value] ]:[];
  }));
}
