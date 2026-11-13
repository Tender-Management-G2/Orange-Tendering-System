// Local-only SMTP inbox for Development. Messages stay in memory and are never logged.
const net = require('node:net');
const http = require('node:http');

const messages = [];
const escapeHtml = value => value.replace(/[&<>"']/g, char => ({
  '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
})[char]);

net.createServer(socket => {
  socket.setEncoding('utf8');
  socket.on('error', () => {});
  socket.write('220 Local development mail inbox\r\n');
  let buffer = '';
  let body = '';
  let inData = false;
  let recipient = '';
  socket.on('data', chunk => {
    buffer += chunk;
    if (buffer.length > 2_000_000) return socket.destroy();
    let newline;
    while ((newline = buffer.indexOf('\r\n')) >= 0) {
      const line = buffer.slice(0, newline);
      buffer = buffer.slice(newline + 2);
      if (inData) {
        if (line === '.') {
          messages.unshift({ to: recipient, received: new Date().toISOString(), raw: body });
          messages.length = Math.min(messages.length, 25);
          body = '';
          inData = false;
          socket.write('250 Message accepted\r\n');
        } else {
          body += (line.startsWith('..') ? line.slice(1) : line) + '\r\n';
          if (body.length > 1_000_000) socket.destroy();
        }
        continue;
      }
      const command = line.split(' ', 1)[0].toUpperCase();
      if (command === 'EHLO') socket.write('250-localhost\r\n250 SIZE 1000000\r\n');
      else if (command === 'HELO') socket.write('250 localhost\r\n');
      else if (command === 'MAIL') socket.write('250 Sender accepted\r\n');
      else if (command === 'RCPT') {
        recipient = line.slice(8);
        socket.write('250 Recipient accepted\r\n');
      } else if (command === 'DATA') {
        inData = true;
        socket.write('354 End data with <CRLF>.<CRLF>\r\n');
      } else if (command === 'RSET') {
        body = '';
        recipient = '';
        socket.write('250 Reset\r\n');
      } else if (command === 'QUIT') {
        socket.end('221 Bye\r\n');
      } else socket.write('250 OK\r\n');
    }
  });
}).listen(1025, '127.0.0.1');

http.createServer((request, response) => {
  if (request.url === '/api/messages') {
    response.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' });
    response.end(JSON.stringify(messages));
    return;
  }
  if (request.url !== '/') {
    response.writeHead(404);
    response.end();
    return;
  }
  const items = messages.map(message => `<details><summary>${escapeHtml(message.received)} — ${escapeHtml(message.to)}</summary><pre>${escapeHtml(message.raw)}</pre></details>`).join('');
  response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' });
  response.end(`<!doctype html><html lang="en"><meta charset="utf-8"><meta http-equiv="refresh" content="10"><title>Development mail inbox</title><style>body{font:16px system-ui;max-width:900px;margin:40px auto;padding:0 16px;background:#fafafa;color:#222}details{background:white;border:1px solid #ddd;border-radius:8px;margin:12px 0;padding:16px}pre{white-space:pre-wrap;overflow-wrap:anywhere}</style><h1>Development mail inbox</h1><p>Local testing only. Emails are kept in memory until this process stops.</p>${items || '<p>No messages yet. Submit a supplier registration, then refresh this page.</p>'}</html>`);
}).listen(8025, '127.0.0.1', () => {
  process.stdout.write('Development mail inbox: http://127.0.0.1:8025\n');
});
