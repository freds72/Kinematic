var net = require('net');

var server = net.createServer((socket) => {    
    console.log('client connected');
    socket.on('data', (data) => {
        console.log('got: ' + data.toString());
    });
}).on('error', (err) => {
    throw err;
});

// grab a random port.
server.listen(13000, '127.0.0.1',() => {
    console.log('opened server on', server.address());    
});

