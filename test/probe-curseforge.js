// Checks the CurseForge API key in src/ApiKey.cs against the real API
// (never prints the key): finds ATM9-TTS and the pinned pack file.
//   node tools/launcher/test/probe-curseforge.js
const fs = require('fs');
const path = require('path');
const settings = fs.readFileSync(path.join(__dirname, '..', 'src', 'Settings.cs'), 'utf8');
const keyFile = path.join(__dirname, '..', 'src', 'ApiKey.cs');
const key = fs.existsSync(keyFile) ? ((fs.readFileSync(keyFile, 'utf8').match(/Value = "([^"]*)"/) || [])[1] || '') : '';
const fileId = Number((settings.match(/PackFileId = (\d+)/) || [])[1] || 0);
console.log('Key in src/ApiKey.cs: ' + (key ? key.length + ' characters' : 'MISSING'));

async function get(url, body) {
  const r = await fetch('https://api.curseforge.com' + url, {
    method: body ? 'POST' : 'GET',
    headers: Object.assign({ 'x-api-key': key, accept: 'application/json' }, body ? { 'content-type': 'application/json' } : {}),
    body: body ? JSON.stringify(body) : undefined
  });
  const text = await r.text();
  if (!r.ok) throw new Error(r.status + ' ' + r.statusText + ' for ' + url);
  return JSON.parse(text);
}

(async () => {
  const search = await get('/v1/mods/search?gameId=432&classId=4471&slug=all-the-mods-9-to-the-sky');
  const pack = search.data.find(m => m.slug === 'all-the-mods-9-to-the-sky');
  console.log('Modpack: ' + pack.id + ' ' + pack.name);
  const file = (await get('/v1/mods/' + pack.id + '/files/' + fileId)).data;
  console.log('Pinned file ' + fileId + ': ' + file.displayName + ' (' + (file.isServerPack ? 'SERVER' : 'client') + ' pack, ' + Math.round(file.fileLength / 1048576) + ' MB, download ' + (file.downloadUrl ? 'allowed' : 'NOT allowed') + ')');
})().catch(e => { console.log('Failed: ' + e.message); process.exitCode = 1; });
