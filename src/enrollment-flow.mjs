import { PERSONAL_EMAIL, READ_SCOPES, WRITE_SCOPES, verifyPersonal, verifyEnrollmentScopes, verifyScopes } from './identity.mjs';

export function renewalProfile(saved) {
  if (!PERSONAL_EMAIL || saved?.email !== PERSONAL_EMAIL || !saved.clientId || !saved.clientSecret || !saved.sub) throw new Error('PERSONAL_ACCOUNT_REQUIRED');
  const requiredAccess = verifyScopes(saved.tokens?.scope);
  return {enrollment:{requiredAccess,scopes:requiredAccess === 'write' ? WRITE_SCOPES : READ_SCOPES},client:{client_id:saved.clientId,client_secret:saved.clientSecret},expectedSubject:saved.sub};
}

const stages = new Set(['arguments','client_file','client_type','loopback','browser_consent','token_exchange','identity','scope','encrypted_storage']);

const codes = new Set(['ACCOUNT_CONFIGURATION_REQUIRED','EXPLICIT_APPROVAL_REQUIRED','DESKTOP_CLIENT_REQUIRED','OAUTH_TIMEOUT','OAUTH_DENIED','BROWSER_OPEN_FAILED','OFFLINE_GRANT_REQUIRED','PERSONAL_ACCOUNT_REQUIRED','GRANT_MISMATCH','VAULT_OPERATION_FAILED','ENOENT','EACCES','EADDRINUSE','invalid_grant','invalid_client','access_denied']);

export function safeEnrollmentDiagnostic(stage, error) {

  const candidate = [error?.code, error?.message, error?.response?.data?.error].find(value => typeof value === 'string' && codes.has(value));

  const code = candidate ?? (error instanceof SyntaxError ? 'INVALID_JSON' : 'FAILED');

  return `ENROLLMENT_FAILED stage=${stages.has(stage) ? stage : 'unknown'} code=${code}`;

}

export async function finishEnrollment({auth,tokens,client,nonce,expectedSubject,requiredAccess,save,stage}) {

  stage('identity');

  if (!tokens.id_token || !tokens.refresh_token) throw new Error('OFFLINE_GRANT_REQUIRED');

  const ticket = await auth.verifyIdToken({idToken:tokens.id_token,audience:client.client_id});

  const claims = ticket.getPayload();

  // Google revocation can invalidate the entire project's grant, including
  // older tokens. On a validation failure preserve the existing vault and
  // leave any grant removal to a separately reviewed user action.
  verifyPersonal(claims);

  if (expectedSubject !== undefined && claims.sub !== expectedSubject) throw new Error('PERSONAL_ACCOUNT_REQUIRED');

  if (claims.nonce !== nonce) throw new Error('GRANT_MISMATCH');

  stage('scope');

  verifyEnrollmentScopes(tokens.scope,requiredAccess);

  stage('encrypted_storage');

  await save({clientId:client.client_id,clientSecret:client.client_secret,email:claims.email.toLowerCase(),sub:claims.sub,tokens});

}

