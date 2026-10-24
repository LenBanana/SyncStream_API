'use strict';
// A TCP proxy in front of the API whose connections can be cut on demand, to simulate a network blip.
// Listens on 5001 → 127.0.0.1:5000. POST http://127.0.0.1:5002/cut drops every open connection.
const net = require('net');
const http = require('http');

const sockets = new Set();

net.createServer(client => {
  const upstream = net.connect(5000, '127.0.0.1');
  sockets.add(client);
  sockets.add(upstream);
  const forget = () => { sockets.delete(client); sockets.delete(upstream); };
  client.on('close', forget);
  upstream.on('close', forget);
  client.on('error', () => upstream.destroy());
  upstream.on('error', () => client.destroy());
  client.pipe(upstream);
  upstream.pipe(client);
}).listen(5001, '127.0.0.1');

http.createServer((req, res) => {
  if (req.method === 'POST' && req.url === '/cut') {
    const count = sockets.size;
    sockets.forEach(socket => socket.destroy());
    res.end(String(count));
    return;
  }
  res.statusCode = 404;
  res.end();
}).listen(5002, '127.0.0.1');

console.log('tcp proxy 5001 -> 5000, control on 5002');
