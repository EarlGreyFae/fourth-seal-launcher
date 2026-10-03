// Asks a Minecraft server for its status (what the multiplayer list shows):
// version, players, description, and Forge's mod info if it sends any.
//   node tools/launcher/test/ping-server.js <host> [port]
const net = require('net');
const host = process.argv[2];
const port = Number(process.argv[3] || 25565);

function varint(n) {
  const out = [];
  do { let b = n & 0x7f; n >>>= 7; if (n) b |= 0x80; out.push(b); } while (n);
  return Buffer.from(out);
}
function packet(id, body) {
  const data = Buffer.concat([varint(id), body]);
  return Buffer.concat([varint(data.length), data]);
}
function str(s) { const b = Buffer.from(s, 'utf8'); return Buffer.concat([varint(b.length), b]); }

const sock = net.connect(port, host, () => {
  const portBuf = Buffer.alloc(2); portBuf.writeUInt16BE(port);
  sock.write(packet(0, Buffer.concat([varint(763), str(host), portBuf, varint(1)])));
  sock.write(packet(0, Buffer.alloc(0)));
});
let buf = Buffer.alloc(0);
sock.setTimeout(8000, () => { console.log('No answer within 8 seconds.'); sock.destroy(); });
sock.on('error', e => { console.log('Could not connect: ' + e.message); });
sock.on('data', d => {
  buf = Buffer.concat([buf, d]);
  let i = 0, len = 0, shift = 0, b;
  do { b = buf[i++]; len |= (b & 0x7f) << shift; shift += 7; } while (b & 0x80);
  if (buf.length < i + len) return;
  let j = i; do { b = buf[j++]; } while (b & 0x80); // packet id
  let slen = 0; shift = 0; do { b = buf[j++]; slen |= (b & 0x7f) << shift; shift += 7; } while (b & 0x80);
  const status = JSON.parse(buf.toString('utf8', j, j + slen));
  const desc = typeof status.description === 'string' ? status.description : JSON.stringify(status.description);
  console.log('Version: ' + (status.version && status.version.name) + ' (protocol ' + (status.version && status.version.protocol) + ')');
  console.log('Players: ' + (status.players && status.players.online) + '/' + (status.players && status.players.max));
  console.log('Description: ' + desc);
  const forge = status.forgeData || status.modinfo;
  if (forge) console.log('Forge data: ' + JSON.stringify(forge).slice(0, 400));
  sock.destroy();
});
