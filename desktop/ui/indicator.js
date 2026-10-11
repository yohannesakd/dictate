function phase(value) {
  document.getElementById('wave').hidden = value !== 'listening';
  document.getElementById('phase').textContent = value === 'processing' ? 'Processing…' : value === 'delivering' ? 'Inserting…' : 'Listening';
  document.getElementById('cancel').hidden = value !== 'listening';
}
window.dictate.subscribe(event => {
  if (event.type === 'phase') phase(event.phase);
  if (event.type === 'level') {
    document.querySelectorAll('#wave i').forEach((bar, index) => { bar.style.height = `${4 + Math.min(event.level * 150, 22) * (1 - Math.abs(index - 4) / 6)}px`; });
  }
});
window.dictate.state().then(state => phase(state.phase));
