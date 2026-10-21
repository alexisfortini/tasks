import {mkdirSync,writeFileSync,existsSync,readFileSync,renameSync} from 'node:fs';
import {resolve} from 'node:path';
import {readEnvironment} from '../src/environment.mjs';
const args=process.argv.slice(2);
const root=resolve(import.meta.dirname,'..'),target=resolve(root,'connection/profiles/google-tasks.yaml');
const command=JSON.stringify(process.execPath.replaceAll('\\','/'))+' '+JSON.stringify(resolve(root,'src/server.mjs').replaceAll('\\','/'));
if(args.length===1&&args[0]==='--refresh-paths'){
  const current=readFileSync(target,'utf8');
  const commands=[...current.matchAll(/^([ \t]+command:)[^\r\n]*$/gm)];
  if(commands.length!==1)throw Error('SINGLE_EXISTING_MCP_COMMAND_REQUIRED');
  const replacement=current.replace(/^([ \t]+command:)[^\r\n]*$/m,(_,prefix)=>prefix+' '+JSON.stringify(command));
  writeFileSync(target+'.tmp',replacement,{flag:'wx'});renameSync(target+'.tmp',target);
  console.log('PROFILE_PATHS_REFRESHED_EXISTING_TUNNEL_AND_SETTINGS_PRESERVED');
  process.exit(0);
}
const tunnelId=args.length===0?readEnvironment().GOOGLE_TASKS_TUNNEL_ID:args.length===2&&args[0]==='--tunnel-id'?args[1]:undefined;
if(!/^tunnel_[a-zA-Z0-9_-]+$/.test(tunnelId??''))throw Error('USAGE: --tunnel-id your-own-tunnel-id OR set GOOGLE_TASKS_TUNNEL_ID in .env');
if(existsSync(target))throw Error('PROFILE_ALREADY_EXISTS');
mkdirSync(resolve(root,'connection/profiles'),{recursive:true});
const yaml=`config_version: 1
control_plane:
  base_url: "https://api.openai.com"
  tunnel_id: ${JSON.stringify(tunnelId)}
  api_key: "env:CONTROL_PLANE_API_KEY"
health:
  listen_addr: "127.0.0.1:0"
admin_ui:
  open_browser: false
  log_buffer_events: 1
log:
  level: error
  format: json
  http_raw_unsafe: false
mcp:
  commands:
    - channel: main
      command: ${JSON.stringify(command)}
`;
writeFileSync(target,yaml,{flag:'wx'});
console.log('LOCAL_TUNNEL_PROFILE_CREATED_NO_KEY_STORED_NO_TUNNEL_CREATED_OR_STARTED');
