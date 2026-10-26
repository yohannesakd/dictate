const indicator = document.querySelector('.indicator');
const bars = [...document.querySelectorAll('#wave i')];
let currentPhase;
function phase(value) {
  currentPhase = value;
  indicator.dataset.phase = value;
  indicator.setAttribute('aria-label', value === 'processing' ? 'Processing dictation' : value === 'delivering' ? 'Inserting dictation' : 'Listening');
  bars.forEach((bar, index) => { bar.style.height = `${4 + (1 - Math.abs(index - 4) / 4) * 14}px`; });
}
window.dictate.subscribe(event => {
  if (event.type === 'phase') phase(event.phase);
  if (event.type === 'level' && currentPhase === 'listening') {
    bars.forEach((bar, index) => { bar.style.height = `${3 + Math.min(event.level * 150, 15) * (1 - Math.abs(index - 4) / 6)}px`; });
  }
});
window.dictate.state().then(state => phase(state.phase));
