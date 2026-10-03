// End-to-end test of the launcher with a fake CurseForge on this PC.
// Everything happens in a scratch folder - the real .minecraft is never
// touched, and the Minecraft Launcher is never opened. Forge and Java are
// really downloaded and installed (into the scratch folder), so the first
// run takes a few minutes.
//   node tools/launcher/test/run-test.js            (FRESH=1 starts over)
const fs = require('fs');
const os = require('os');
const path = require('path');
const http = require('http');
const zlib = require('zlib');
const crypto = require('crypto');
const { spawnSync, spawn } = require('child_process');

const exe = path.join(__dirname, '..', 'dist', 'TheFourthSealLauncher.exe');
const work = path.join(os.tmpdir(), 'fourthseal-launcher-test');
if (process.env.FRESH) fs.rmSync(work, { recursive: true, force: true });
const mc = path.join(work, 'dot-minecraft');
const home = path.join(work, 'home');
const game = path.join(home, 'game');
const downloads = path.join(work, 'Downloads');
[mc, home, downloads].forEach(d => fs.mkdirSync(d, { recursive: true }));

let failed = 0;
function check(label, ok) {
  console.log((ok ? 'ok   ' : 'FAIL ') + label);
  if (!ok) failed++;
}
const sha1 = buf => crypto.createHash('sha1').update(buf).digest('hex');
const KEY = 'test-key';
const FORGE = '47.2.0';
const VERSION_ID = '1.20.1-forge-' + FORGE;

// ---- A tiny zip writer (stored, no compression) ----
function zip(files) {
  const locals = [];
  const centrals = [];
  let offset = 0;
  Object.keys(files).forEach(name => {
    const data = Buffer.from(files[name]);
    const nameBuf = Buffer.from(name);
    const crc = zlib.crc32(data);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0); local.writeUInt16LE(20, 4); local.writeUInt32LE(crc, 14);
    local.writeUInt32LE(data.length, 18); local.writeUInt32LE(data.length, 22); local.writeUInt16LE(nameBuf.length, 26);
    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0); central.writeUInt16LE(20, 4); central.writeUInt16LE(20, 6); central.writeUInt32LE(crc, 16);
    central.writeUInt32LE(data.length, 20); central.writeUInt32LE(data.length, 24); central.writeUInt16LE(nameBuf.length, 28);
    central.writeUInt32LE(offset, 42);
    locals.push(local, nameBuf, data);
    centrals.push(central, nameBuf);
    offset += 30 + nameBuf.length + data.length;
  });
  const cd = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0); end.writeUInt16LE(Object.keys(files).length, 8); end.writeUInt16LE(Object.keys(files).length, 10);
  end.writeUInt32LE(cd.length, 12); end.writeUInt32LE(offset, 16);
  return Buffer.concat(locals.concat([cd, end]));
}

// ---- The fake CurseForge ----
// Mods (project -> class, name); files (id -> mod, name, content, allowed).
const PACK_ID = 900;
const mods = {
  1: { classId: 6, name: 'Alpha Mod' }, 2: { classId: 6, name: 'Beta Mod' },
  3: { classId: 6, name: 'Gamma Mod (website only)' }, 4: { classId: 12, name: 'Pretty Textures' },
  5: { classId: 6, name: 'Delta Mod' }
};
const files = {
  101: { mod: 1, name: 'alpha-1.0.jar', data: 'alpha jar' },
  201: { mod: 2, name: 'beta-2.0.jar', data: 'beta jar' },
  301: { mod: 3, name: 'gamma-3.0.jar', data: 'gamma jar', manual: true },
  401: { mod: 4, name: 'pretty-textures.zip', data: 'textures' },
  501: { mod: 5, name: 'delta-1.0.jar', data: 'delta jar' },
  202: { mod: 2, name: 'beta-2.1.jar', data: 'beta jar v2.1' }
};
function packZip(version) {
  const v1 = version === 1;
  const manifest = {
    minecraft: { version: '1.20.1', modLoaders: [{ id: 'forge-' + FORGE, primary: true }] },
    manifestType: 'minecraftModpack', name: 'Test Pack', version: v1 ? '1.0' : '1.1', overrides: 'overrides',
    files: v1
      ? [{ projectID: 1, fileID: 101, required: true }, { projectID: 2, fileID: 201, required: true }, { projectID: 3, fileID: 301, required: true }, { projectID: 4, fileID: 401, required: true }, { projectID: 5, fileID: 501, required: false }]
      : [{ projectID: 2, fileID: 202, required: true }, { projectID: 3, fileID: 301, required: true }, { projectID: 4, fileID: 401, required: true }, { projectID: 5, fileID: 501, required: true }]
  };
  return zip({
    'manifest.json': JSON.stringify(manifest),
    'overrides/config/pack.toml': v1 ? 'pack = 1' : 'pack = 2',
    'overrides/options.txt': 'pack options',
    ['overrides/mods/bundled-' + (v1 ? 'a' : 'b') + '.jar']: 'bundled jar',
    'overrides/kubejs/client_scripts/pack.js': '// pack script'
  });
}
const packs = { 9001: packZip(1), 9002: packZip(2) };
let port = 0;
function fileJson(id) {
  if (packs[id]) {
    return { id: Number(id), modId: PACK_ID, displayName: 'Test Pack ' + (id === '9001' || id === 9001 ? '1.0' : '1.1'), fileName: 'pack-' + id + '.zip', fileDate: '2026-0' + (Number(id) - 9000) + '-01T00:00:00Z',
      fileLength: packs[id].length, hashes: [{ value: sha1(packs[id]), algo: 1 }], isServerPack: false, downloadUrl: `http://127.0.0.1:${port}/dl/${id}` };
  }
  const f = files[id];
  if (!f) return null;
  return { id: Number(id), modId: f.mod, displayName: f.name, fileName: f.name, fileLength: Buffer.byteLength(f.data),
    hashes: [{ value: sha1(Buffer.from(f.data)), algo: 1 }, { value: 'md5ignored', algo: 2 }], downloadUrl: f.manual ? null : `http://127.0.0.1:${port}/dl/${id}` };
}
const requests = [];
const server = http.createServer((req, res) => {
  let body = '';
  req.on('data', c => { body += c; });
  req.on('end', () => {
    requests.push(req.method + ' ' + req.url);
    const json = o => { res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify(o)); };
    let m;
    if ((m = req.url.match(/^\/dl\/(\d+)$/))) {
      const data = packs[m[1]] || Buffer.from(files[m[1]].data);
      res.writeHead(200); res.end(data); return;
    }
    if (req.url === '/settings.json') return json({ serverAddress: 'moved.fourthseal.test', packFileId: 9002, message: 'Hello from the hosted settings' });
    if (req.url === '/broken-settings.json') { res.writeHead(200); res.end('{ not json'); return; }
    if (req.headers['x-api-key'] !== KEY) { res.writeHead(403); res.end(); return; }
    if (req.url.indexOf('/v1/mods/search') === 0) return json({ data: [{ id: PACK_ID, slug: 'all-the-mods-9-to-the-sky', classId: 4471 }] });
    if ((m = req.url.match(/^\/v1\/mods\/(\d+)\/files\/(\d+)$/))) return json({ data: fileJson(m[2]) });
    if ((m = req.url.match(/^\/v1\/mods\/(\d+)\/files\?/))) return json({ data: [fileJson(9002), fileJson(9001)], pagination: { index: 0, pageSize: 50, resultCount: 2, totalCount: 2 } });
    if (req.method === 'POST' && req.url === '/v1/mods/files') return json({ data: JSON.parse(body).fileIds.map(fileJson).filter(Boolean) });
    if (req.method === 'POST' && req.url === '/v1/mods') {
      return json({ data: JSON.parse(body).modIds.map(id => ({ id, name: mods[id].name, classId: mods[id].classId, links: { websiteUrl: 'https://www.curseforge.com/minecraft/mc-mods/mod-' + id } })) });
    }
    res.writeHead(404); res.end();
  });
});

// ---- servers.dat with someone's existing server (uncompressed NBT) ----
function nbtString(s) { const b = Buffer.from(s, 'utf8'); const l = Buffer.alloc(2); l.writeUInt16BE(b.length); return Buffer.concat([l, b]); }
function serversDat(entries) {
  const parts = [Buffer.from([10]), nbtString(''), Buffer.from([9]), nbtString('servers'), Buffer.from([10])];
  const n = Buffer.alloc(4); n.writeInt32BE(entries.length); parts.push(n);
  entries.forEach(e => {
    parts.push(Buffer.from([8]), nbtString('name'), nbtString(e.name), Buffer.from([8]), nbtString('ip'), nbtString(e.ip), Buffer.from([1]), nbtString('acceptTextures'), Buffer.from([1]), Buffer.from([0]));
  });
  parts.push(Buffer.from([0]));
  return Buffer.concat(parts);
}
function readServers(file) {
  // Just the name/ip strings in order (string tags named "name" or "ip").
  const buf = fs.readFileSync(file);
  const out = [];
  for (let i = 0; i < buf.length - 3; i++) {
    if (buf[i] !== 8) continue;
    const keyLen = buf.readUInt16BE(i + 1);
    const key = buf.toString('utf8', i + 3, i + 3 + keyLen);
    if (key !== 'name' && key !== 'ip') continue;
    const len = buf.readUInt16BE(i + 3 + keyLen);
    out.push(key + '=' + buf.toString('utf8', i + 5 + keyLen, i + 5 + keyLen + len));
    i += 4 + keyLen + len;
  }
  return out;
}

function runLauncher(extraEnv) {
  return new Promise(resolve => {
    const env = Object.assign({}, process.env, {
      FS_MINECRAFT_DIR: mc, FS_HOME: home, FS_API_BASE: `http://127.0.0.1:${port}`, FS_API_KEY: KEY,
      FS_SERVER_ADDRESS: 'play.fourthseal.test', FS_NO_LAUNCH: '1', FS_DOWNLOADS_DIR: downloads, FS_SKIP_SYSTEM_JAVA: '1',
      FS_CONFIG_URL: 'none'
    }, extraEnv);
    // Output goes to launcher.log; an unread pipe would fill up and stall it.
    const child = spawn(exe, ['--selftest'], { env, stdio: 'ignore' });
    child.on('exit', code => resolve({ code, log: fs.readFileSync(path.join(home, 'launcher.log'), 'utf8') }));
  });
}

server.listen(0, '127.0.0.1', async () => {
  port = server.address().port;
  const profilesPath = path.join(mc, 'launcher_profiles.json');
  if (!fs.existsSync(profilesPath)) {
    fs.writeFileSync(profilesPath, JSON.stringify({ profiles: { abc: { name: 'My Vanilla', type: 'custom', lastVersionId: '1.21' } }, settings: { profileSorting: 'ByLastPlayed' }, version: 3 }, null, 2));
  }
  fs.mkdirSync(game, { recursive: true });
  fs.writeFileSync(path.join(game, 'servers.dat'), serversDat([{ name: 'Friend’s Server ✦', ip: 'friend.example.com' }]));
  fs.writeFileSync(path.join(game, 'options.txt'), 'my own options');
  fs.rmSync(path.join(downloads, 'gamma-3.0.jar'), { force: true });
  fs.rmSync(path.join(home, 'state.json'), { force: true });
  fs.rmSync(path.join(game, 'mods'), { recursive: true, force: true });
  fs.rmSync(path.join(game, 'config'), { recursive: true, force: true });

  // Wrong API key: a clear message.
  let r = await runLauncher({ FS_API_KEY: 'wrong', FS_PACK_FILE_ID: '9001' });
  check('a wrong API key stops with a clear message', r.code === 1 && r.log.indexOf('API key is missing or not approved') >= 0);

  // Run 1: the website-only mod isn't in Downloads yet.
  console.log('(installing Forge for real - this can take a few minutes the first time)');
  r = await runLauncher({ FS_PACK_FILE_ID: '9001' });
  check('run 1 stops and lists the website-only mod with its page', r.code === 1 && r.log.indexOf('[manual] gamma-3.0.jar <- https://www.curseforge.com/minecraft/mc-mods/mod-3/files/301') >= 0);
  check('Java was downloaded (none on this "PC") and Forge installed', fs.existsSync(path.join(mc, 'versions', VERSION_ID, VERSION_ID + '.json')) || r.log.indexOf('[error]') >= 0 && console.log(r.log.split('\n').slice(-40).join('\n')));
  check('the downloadable mods are there', fs.readFileSync(path.join(game, 'mods', 'alpha-1.0.jar'), 'utf8') === 'alpha jar' && fs.existsSync(path.join(game, 'mods', 'beta-2.0.jar')));
  check('a resource pack goes in resourcepacks', fs.existsSync(path.join(game, 'resourcepacks', 'pretty-textures.zip')));
  check('optional files are left out', !fs.existsSync(path.join(game, 'mods', 'delta-1.0.jar')));

  // Run 2: the player downloaded it.
  fs.writeFileSync(path.join(downloads, 'gamma-3.0.jar'), 'gamma jar');
  fs.writeFileSync(path.join(game, 'mods', 'my-own-minimap.jar'), 'player added this');
  r = await runLauncher({ FS_PACK_FILE_ID: '9001' });
  check('run 2 finishes', r.code === 0 && r.log.indexOf('[done] ' + VERSION_ID) >= 0);
  if (r.code !== 0) console.log(r.log.split('\n').slice(-30).join('\n'));
  check('Forge isn\'t installed twice', r.log.indexOf('already installed') >= 0);
  check('the website-only mod was picked up from Downloads', fs.readFileSync(path.join(game, 'mods', 'gamma-3.0.jar'), 'utf8') === 'gamma jar');
  check('the pack\'s configs, scripts and bundled jars were copied', fs.readFileSync(path.join(game, 'config', 'pack.toml'), 'utf8') === 'pack = 1' && fs.existsSync(path.join(game, 'kubejs', 'client_scripts', 'pack.js')) && fs.existsSync(path.join(game, 'mods', 'bundled-a.jar')));
  check('the player\'s own options are kept', fs.readFileSync(path.join(game, 'options.txt'), 'utf8') === 'my own options');
  check('the open-the-server-list script is there', fs.readFileSync(path.join(game, 'kubejs', 'client_scripts', 'fourthseal_launcher.js'), 'utf8').indexOf('JoinMultiplayerScreen') >= 0);
  const list = readServers(path.join(game, 'servers.dat'));
  check('The Fourth Seal is first in the server list, the friend\'s server kept after it (emoji intact)', list.join('|') === 'name=The Fourth Seal Skyblock|ip=play.fourthseal.test|name=Friend’s Server ✦|ip=friend.example.com');
  const profiles = JSON.parse(fs.readFileSync(profilesPath, 'utf8'));
  const p = profiles.profiles.fourthseal;
  check('a Minecraft Launcher profile uses the Forge version and its own game folder', p && p.lastVersionId === VERSION_ID && p.gameDir === game && p.name === 'The Fourth Seal Skyblock' && /^-Xmx\d+G /.test(p.javaArgs) && p.icon.indexOf('data:image/png;base64,') === 0);
  check('other profiles and settings are kept, with a backup of the file', profiles.profiles.abc && profiles.profiles.abc.name === 'My Vanilla' && profiles.settings.profileSorting === 'ByLastPlayed' && fs.existsSync(profilesPath + '.fourthseal-backup'));

  // Run 3: the server moves to pack 1.1 (picked by version text).
  r = await runLauncher({ FS_PACK_FILE_ID: '0', FS_PACK_VERSION: '1.1' });
  check('with no file id set it finds the pack by its version text', r.code === 0 && r.log.indexOf('Test Pack 1.1') >= 0);
  check('mods the new version dropped are removed', !fs.existsSync(path.join(game, 'mods', 'alpha-1.0.jar')) && !fs.existsSync(path.join(game, 'mods', 'beta-2.0.jar')) && !fs.existsSync(path.join(game, 'mods', 'bundled-a.jar')));
  check('new and updated ones are there', fs.existsSync(path.join(game, 'mods', 'beta-2.1.jar')) && fs.existsSync(path.join(game, 'mods', 'delta-1.0.jar')) && fs.existsSync(path.join(game, 'mods', 'bundled-b.jar')));
  check('the player\'s own mod is never touched', fs.readFileSync(path.join(game, 'mods', 'my-own-minimap.jar'), 'utf8') === 'player added this');
  check('the configs follow the new version', fs.readFileSync(path.join(game, 'config', 'pack.toml'), 'utf8') === 'pack = 2');
  check('the server list isn\'t rewritten when nothing changed', r.log.indexOf('Added The Fourth Seal') < 0);

  // The hosted settings file moves the server and the pack.
  r = await runLauncher({ FS_PACK_FILE_ID: '9001', FS_CONFIG_URL: `http://127.0.0.1:${port}/settings.json` });
  check('the hosted settings file changes the address and pack, and shows its message', r.code === 0 && r.log.indexOf('Hello from the hosted settings') >= 0 && r.log.indexOf('Test Pack 1.1') >= 0 && readServers(path.join(game, 'servers.dat'))[1] === 'ip=moved.fourthseal.test');
  r = await runLauncher({ FS_PACK_FILE_ID: '9002', FS_CONFIG_URL: `http://127.0.0.1:${port}/broken-settings.json` });
  check('a broken hosted file is ignored (built-in settings used)', r.code === 0 && r.log.indexOf('using the built-in ones') >= 0);

  // Not set up: a clear message.
  r = await runLauncher({ FS_SERVER_ADDRESS: 'SERVER-ADDRESS-HERE' });
  check('an unset server address says so', r.code === 1 && r.log.indexOf('server address is missing') >= 0);

  server.close();
  console.log(failed === 0 ? 'All passed.' : failed + ' failed.');
  process.exitCode = failed === 0 ? 0 : 1;
});
