// Multi-select filter dropdowns on the DWO grids (.msf, rendered by Register.cshtml).
// Pure UI: the checkboxes already carry the form field name, so the existing Filter
// button submits whatever is ticked. This only opens/closes the panel, keeps the count
// badge and the "All / N selected" label current, and wires Clear / Done.
(function () {
  const boxes = Array.from(document.querySelectorAll('[data-msf]'));
  if (!boxes.length) return;

  function refresh(box) {
    const n = box.querySelectorAll('input[type=checkbox]:checked').length;
    const btn = box.querySelector('.msf-btn');
    btn.classList.toggle('has', n > 0);
    btn.querySelector('.msf-n').textContent = n;
    btn.querySelector('span').textContent = n === 0 ? 'All' : n + ' selected';
  }
  function close(box) { box.querySelector('.msf-panel').hidden = true; }
  function closeAll(except) { boxes.forEach(b => { if (b !== except) close(b); }); }

  boxes.forEach(box => {
    const panel = box.querySelector('.msf-panel');
    box.querySelector('.msf-btn').addEventListener('click', e => {
      e.stopPropagation();
      const open = panel.hidden;
      closeAll(box);
      panel.hidden = !open;
    });
    panel.addEventListener('click', e => e.stopPropagation());
    panel.addEventListener('change', () => refresh(box));
    box.querySelector('[data-msf-clear]').addEventListener('click', () => {
      panel.querySelectorAll('input[type=checkbox]').forEach(c => { c.checked = false; });
      refresh(box);
    });
    box.querySelector('[data-msf-done]').addEventListener('click', () => close(box));
    refresh(box);
  });
  document.addEventListener('click', () => closeAll(null));
  document.addEventListener('keydown', e => { if (e.key === 'Escape') closeAll(null); });
})();
