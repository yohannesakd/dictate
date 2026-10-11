const readline = require('node:readline');
process.stdout.write('{"event":"state","data":');
setTimeout(() => process.stdout.write('{"sequence":1}}\n'), 5);
readline.createInterface({ input: process.stdin }).on('line', line => {
  const request = JSON.parse(line);
  if (request.command === 'exit') { setTimeout(() => process.exit(1), 20); return; }
  setTimeout(() => process.stdout.write(JSON.stringify({ id: request.id, ok: request.command !== 'failed', error: 'refused', result: request.args.value }) + '\n'), request.command === 'slow' ? 60 : 15);
});
