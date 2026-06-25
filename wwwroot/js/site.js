// ApproveFlow site.js
document.addEventListener('DOMContentLoaded', () => {
  // Multi-select widgets
  document.querySelectorAll('.ms-wrap').forEach(initMs);

  // Confirm-delete buttons
  document.querySelectorAll('[data-confirm]').forEach(el => {
    el.addEventListener('click', e => { if (!confirm(el.dataset.confirm)) e.preventDefault(); });
  });
});

function initMs(wrap) {
  const trigger = wrap.querySelector('.ms-trigger');
  const drop    = wrap.querySelector('.ms-drop');
  const pills   = wrap.querySelector('.ms-pills');
  const chev    = wrap.querySelector('.ms-chev');
  const cbs     = wrap.querySelectorAll('.ms-opt input[type=checkbox]');
  const name    = wrap.dataset.name;
  const ph      = wrap.dataset.placeholder || 'Select…';
  const txt     = trigger.querySelector('.ms-txt');

  const getSelected = () =>
    [...cbs].filter(c => c.checked).map(c => ({ v: c.value, l: c.dataset.label || c.value }));

  const render = () => {
    const sel = getSelected();
    // Pills
    pills.innerHTML = '';
    sel.forEach(({ v, l }) => {
      const p = document.createElement('span');
      p.className = 'ms-pill';
      p.innerHTML = `${l}<button type="button" class="ms-rm" data-v="${v}">×</button>`;
      pills.appendChild(p);
    });
    pills.querySelectorAll('.ms-rm').forEach(b => {
      b.addEventListener('click', e => {
        e.stopPropagation();
        const cb = [...cbs].find(c => c.value === b.dataset.v);
        if (cb) { cb.checked = false; render(); }
      });
    });
    // Trigger text
    if (txt) txt.textContent = sel.length === 0 ? ph : sel.length === 1 ? sel[0].l : `${sel.length} selected`;
    // Hidden inputs
    wrap.querySelectorAll(`input[type=hidden][name="${name}"]`).forEach(h => h.remove());
    sel.forEach(({ v }) => {
      const h = document.createElement('input');
      h.type = 'hidden'; h.name = name; h.value = v;
      wrap.appendChild(h);
    });
  };

  trigger.addEventListener('click', e => {
    e.stopPropagation();
    drop.classList.toggle('show');
    if (chev) chev.classList.toggle('open', drop.classList.contains('show'));
  });
  document.addEventListener('click', () => {
    drop.classList.remove('show');
    if (chev) chev.classList.remove('open');
  });
  cbs.forEach(cb => cb.addEventListener('change', render));
  render();
}

// Workflow step builder helpers used by Workflows/Form.cshtml
window.removeStep = function(btn) {
  btn.closest('.step-row').remove();
  reindexSteps();
};
window.toggleAutoAssign = function(cb) {
  const sel = cb.closest('.step-row').querySelector('select[data-approver]');
  if (sel) { sel.disabled = cb.checked; if (cb.checked) sel.value = ''; }
};
function reindexSteps() {
  document.querySelectorAll('#stepContainer .step-row').forEach((row, i) => {
    row.querySelector('.step-num-lbl').textContent = i + 1;
    row.querySelectorAll('[name]').forEach(el => {
      el.name = el.name.replace(/Steps\[\d+\]/, `Steps[${i}]`);
    });
    const slInput = row.querySelector('[data-steplevel]');
    if (slInput) slInput.value = i + 1;
  });
}
