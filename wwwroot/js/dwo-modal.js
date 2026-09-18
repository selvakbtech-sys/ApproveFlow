// Shared modal shell for the DWO module.
//
// The main DWO screens — Discussion Register, Create/Edit and Discussion Details — are
// ordinary full pages. What remains here serves the two that are still popups (the Set
// screen and Build Set), plus the small helpers the full pages share: the action panes and
// the repeatable reference-link rows. Nothing here is used by any non-DWO screen, and the
// shell markup is injected on first use so no layout change is needed.
(function () {
  'use strict';

  function shell() {
    var bg = document.getElementById('dwoModalBg');
    if (bg) return bg;
    bg = document.createElement('div');
    bg.className = 'modal-bg';
    bg.id = 'dwoModalBg';
    bg.innerHTML = '<div class="modal" id="dwoModalBody"></div>';
    document.body.appendChild(bg);
    // Click the backdrop (never the panel) to dismiss.
    bg.addEventListener('click', function (e) { if (e.target === bg) closeModal(); });
    return bg;
  }

  function openModal(html, wide) {
    var bg = shell();
    var body = document.getElementById('dwoModalBody');
    body.className = wide ? 'modal lg' : 'modal';
    body.innerHTML = html;
    bg.classList.add('open');
    document.body.style.overflow = 'hidden';
    // Lets the print stylesheet hide the page behind the modal (see site.css).
    document.body.classList.add('dwo-modal-open');
    guardForms(body);
    // Every modal form carries a returnUrl so the post lands back on the page the user was
    // on — same filters, same chip, same page number.
    var here = location.pathname + location.search;
    body.querySelectorAll('input[name="returnUrl"]').forEach(function (i) { i.value = here; });
    var first = body.querySelector('input:not([type=hidden]),textarea,select');
    if (first) first.focus();
  }

  // The shared submit-guard binds on DOMContentLoaded, so it never sees a form injected
  // into the modal. This is the local equivalent: one submit only, and the button says so
  // while the post is in flight.
  function guardForms(root) {
    root.querySelectorAll('form').forEach(function (form) {
      form.addEventListener('submit', function (e) {
        if (form.dataset.dwoSubmitting === '1') { e.preventDefault(); return; }
        form.dataset.dwoSubmitting = '1';
        form.querySelectorAll('button[type=submit]').forEach(function (b) {
          b.disabled = true;
          if (!b.dataset.dwoLabel) b.dataset.dwoLabel = b.textContent;
          b.textContent = 'Working…';
        });
      });
    });
  }

  function closeModal() {
    var bg = document.getElementById('dwoModalBg');
    if (!bg) return;
    bg.classList.remove('open');
    document.body.style.overflow = '';
    document.body.classList.remove('dwo-modal-open');
  }

  // Fetches a modal partial. `wide` renders the large panel (details, Set, Build Set).
  function loadModal(url, wide) {
    openModal('<div class="modal-loading">Loading…</div>', wide);
    fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
      .then(function (r) {
        if (!r.ok) throw new Error(r.status === 403 ? 'You do not have permission to open that.' : 'Could not load that.');
        return r.text();
      })
      .then(function (html) { openModal(html, wide); })
      .catch(function (err) {
        openModal('<div class="modal-body"><div class="alert alert-err" style="margin:0">' +
          (err.message || 'Could not load that.') +
          '</div></div><div class="modal-actions"><button type="button" class="btn btn-sec" onclick="dwoCloseModal()">Close</button></div>', wide);
      });
  }

  document.addEventListener('keydown', function (e) { if (e.key === 'Escape') closeModal(); });

  // Shows one action pane (send back, reject, close, follow-up, hold, reassign, complete
  // follow-up) and hides the rest. Scoped to the document, so it works both for panes on
  // the Discussion Details page and for those inside a modal such as the Set screen.
  window.dwoShowPane = function (id) {
    document.querySelectorAll('[data-dwo-pane]').forEach(function (p) { p.hidden = p.id !== id; });
    var pane = document.getElementById(id);
    if (pane) {
      var f = pane.querySelector('input:not([type=hidden]),textarea,select');
      if (f) f.focus();
      pane.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }
  };

  window.dwoHidePanes = function () {
    document.querySelectorAll('[data-dwo-pane]').forEach(function (p) { p.hidden = true; });
  };

  window.dwoOpenModal = openModal;
  window.dwoCloseModal = closeModal;
  window.dwoLoadModal = loadModal;

  // ── Register grid: Set builder ────────────────────────────
  // Ticking rows enables "Build Set & Submit to Management"; the ids ride to the modal.
  window.dwoSelectedIds = function () {
    return Array.prototype.slice
      .call(document.querySelectorAll('.dwo-setpick:checked'))
      .map(function (c) { return c.value; });
  };

  window.dwoSyncSetBar = function () {
    var ids = window.dwoSelectedIds();
    var bar = document.getElementById('dwoSetBar');
    if (!bar) return;
    var count = document.getElementById('dwoSetCount');
    if (count) count.textContent = ids.length;
    document.querySelectorAll('.dwo-setact').forEach(function (b) { b.disabled = ids.length === 0; });
  };

  window.dwoOpenBuildSet = function () {
    var ids = window.dwoSelectedIds();
    if (ids.length === 0) return;
    loadModal('/Dwo/BuildSetModal?ids=' + encodeURIComponent(ids.join(',')), true);
  };

  // "Add to Draft Set": picker of the Draft Sets the ticked points may join (large panel).
  // The picker's search/filter/pager call dwoReloadDraftPicker, which re-fetches the same
  // partial with the form's current values (the point ids ride along as hidden inputs).
  window.dwoOpenDraftPicker = function () {
    var ids = window.dwoSelectedIds();
    if (ids.length === 0) return;
    loadModal('/Dwo/DraftSetPicker?ids=' + encodeURIComponent(ids.join(',')), true);
  };

  window.dwoReloadDraftPicker = function (page) {
    var form = document.getElementById('dwoDraftPickerFilter');
    if (!form) return;
    var params = new URLSearchParams(new FormData(form));
    if (page) params.set('Page', page);
    loadModal('/Dwo/DraftSetPicker?' + params.toString(), true);
  };

  // The Add button wakes up once a Set is chosen.
  window.dwoDraftPicked = function () {
    var btn = document.getElementById('dwoDraftAddBtn');
    if (btn) btn.disabled = !document.querySelector('input[name="setId"]:checked');
  };

  // ── Create/Edit form: repeatable reference-link rows ──────
  window.dwoAddLinkRow = function () {
    var wrap = document.getElementById('dwoLinks');
    if (!wrap) return;
    var row = document.createElement('div');
    row.className = 'dwo-linkrow';
    row.innerHTML =
      '<input type="url" name="ReferenceLinks" class="inp" placeholder="https://…">' +
      '<button type="button" class="btn btn-sec btn-sm" onclick="this.parentNode.remove()">Remove</button>';
    wrap.appendChild(row);
    row.querySelector('input').focus();
  };
})();
