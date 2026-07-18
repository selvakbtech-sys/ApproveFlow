// ApproveFlow reusable data grid
// Progressive-enhancement for Master list tables. Add `data-grid` to any
// <table class="tbl"> and this adds a search box, page-size selector,
// column sorting, pagination, a record count, and an empty state — with
// per-page state (search / page / size / sort) remembered in sessionStorage
// so it survives navigating to Edit and back.
//
// Pure client-side: it only reorders/hides the rows the server already
// rendered. No dependencies, no markup changes to rows, so Edit/Delete/Toggle
// forms and permissions keep working exactly as before.
(function () {
  'use strict';

  var PAGE_SIZES = [10, 25, 50, 100];

  function ready(fn) {
    if (document.readyState !== 'loading') fn();
    else document.addEventListener('DOMContentLoaded', fn);
  }

  ready(function () {
    var tables = document.querySelectorAll('table.tbl[data-grid]');
    Array.prototype.forEach.call(tables, function (t, i) { initGrid(t, i); });
  });

  function el(tag, cls) { var e = document.createElement(tag); if (cls) e.className = cls; return e; }

  // Sort comparison with light type detection: numbers, then dates, then text.
  function toNum(s) { if (s === '') return null; var n = Number(s.replace(/[,₹%\s]/g, '')); return isNaN(n) ? null : n; }
  function toDate(s) { if (!/\d{4}/.test(s)) return null; var t = Date.parse(s); return isNaN(t) ? null : t; }
  function cmp(a, b) {
    var na = toNum(a), nb = toNum(b);
    if (na !== null && nb !== null) return na - nb;
    var da = toDate(a), db = toDate(b);
    if (da !== null && db !== null) return da - db;
    return a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' });
  }

  function initGrid(table, gridIndex) {
    var tbody = table.tBodies[0];
    if (!tbody) return;

    // Split the server-rendered rows into real data rows and an optional
    // "empty" placeholder row (the friendly "No X yet…" message + add link).
    var initialRows = Array.prototype.slice.call(tbody.rows);
    var placeholder = null;
    var dataRows = [];
    initialRows.forEach(function (r) {
      if (r.querySelector('.tbl-empty')) placeholder = r;
      else dataRows.push(r);
    });
    dataRows.forEach(function (r) { r._dgText = r.textContent.toLowerCase(); });

    var headerCells = table.tHead ? Array.prototype.slice.call(table.tHead.rows[0].cells) : [];
    var colCount = headerCells.length || (dataRows[0] ? dataRows[0].cells.length : 1);

    var state = loadState(gridIndex);
    if (PAGE_SIZES.indexOf(state.pageSize) === -1) state.pageSize = 10;

    // ── Build toolbar (search + page size) and footer (count + pager) ──
    var card = table.closest('.card') || table.parentNode;
    var wrap = table.closest('.tbl-wrap') || table;

    var toolbar = el('div', 'dg-toolbar');
    var search = el('input', 'dg-search');
    search.type = 'search';
    search.setAttribute('aria-label', 'Search');
    search.placeholder = 'Search…';
    search.value = state.search || '';
    toolbar.appendChild(search);

    var size = el('label', 'dg-size');
    size.appendChild(document.createTextNode('Show '));
    var sizeSel = el('select', 'dg-size-sel');
    PAGE_SIZES.forEach(function (n) {
      var o = el('option'); o.value = n; o.textContent = n;
      if (n === state.pageSize) o.selected = true;
      sizeSel.appendChild(o);
    });
    size.appendChild(sizeSel);
    size.appendChild(document.createTextNode(' entries'));
    toolbar.appendChild(size);
    card.insertBefore(toolbar, wrap);

    var footer = el('div', 'dg-footer');
    var info = el('div', 'dg-info');
    var pager = el('div', 'dg-pager');
    footer.appendChild(info);
    footer.appendChild(pager);
    card.insertBefore(footer, wrap.nextSibling);

    // ── Sorting: make every header sortable except Actions (.r) / [data-nosort] ──
    headerCells.forEach(function (th, i) {
      if (th.hasAttribute('data-nosort') || th.classList.contains('r')) return;
      th.classList.add('dg-sortable');
      th.setAttribute('role', 'button');
      th.setAttribute('tabindex', '0');
      var arrow = el('span', 'dg-arrow');
      th.appendChild(arrow);
      th._dgArrow = arrow;
      th.addEventListener('click', function () { sortBy(i); });
      th.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); sortBy(i); }
      });
    });

    function cellText(row, col) {
      var c = row.cells[col];
      return c ? c.textContent.trim() : '';
    }

    var filtered = dataRows.slice();

    function update() {
      var q = (state.search || '').trim().toLowerCase();
      filtered = q ? dataRows.filter(function (r) { return r._dgText.indexOf(q) !== -1; }) : dataRows.slice();

      if (state.sortCol != null && headerCells[state.sortCol]) {
        var dir = state.sortDir === 'desc' ? -1 : 1;
        filtered.sort(function (a, b) { return dir * cmp(cellText(a, state.sortCol), cellText(b, state.sortCol)); });
      }

      var totalPages = Math.max(1, Math.ceil(filtered.length / state.pageSize));
      if (state.page > totalPages) state.page = totalPages;
      if (state.page < 1) state.page = 1;

      render(totalPages);
      saveState(gridIndex, state);
    }

    function render(totalPages) {
      // Detach current rows, then re-append the current page in order.
      dataRows.forEach(function (r) { if (r.parentNode) r.parentNode.removeChild(r); });
      if (placeholder && placeholder.parentNode) placeholder.parentNode.removeChild(placeholder);

      if (filtered.length === 0) {
        var q = (state.search || '').trim();
        if (!q && placeholder) tbody.appendChild(placeholder); // truly empty → keep friendly message
        else tbody.appendChild(noResultsRow());                // search matched nothing
        info.textContent = 'Showing 0 to 0 of 0 entries';
        pager.innerHTML = '';
      } else {
        var start = (state.page - 1) * state.pageSize;
        var pageRows = filtered.slice(start, start + state.pageSize);
        pageRows.forEach(function (r) { tbody.appendChild(r); });
        info.textContent = 'Showing ' + (start + 1) + ' to ' + (start + pageRows.length) + ' of ' + filtered.length + ' entries';
        buildPager(totalPages);
      }
      updateArrows();
    }

    function noResultsRow() {
      var tr = el('tr'); var td = el('td', 'tbl-empty');
      td.colSpan = colCount; td.textContent = 'No records found.';
      tr.appendChild(td); return tr;
    }

    function updateArrows() {
      headerCells.forEach(function (th, i) {
        if (!th._dgArrow) return;
        th._dgArrow.textContent = (i === state.sortCol) ? (state.sortDir === 'desc' ? '▼' : '▲') : '';
      });
    }

    function buildPager(totalPages) {
      pager.innerHTML = '';
      if (totalPages <= 1) return;
      addBtn('‹', 'Previous', state.page > 1, function () { goto(state.page - 1); });
      var win = 2, start = Math.max(1, state.page - win), end = Math.min(totalPages, state.page + win);
      if (start > 1) { addNum(1); if (start > 2) addGap(); }
      for (var p = start; p <= end; p++) addNum(p);
      if (end < totalPages) { if (end < totalPages - 1) addGap(); addNum(totalPages); }
      addBtn('›', 'Next', state.page < totalPages, function () { goto(state.page + 1); });
    }
    function addBtn(label, title, enabled, fn) {
      var b = el('button', 'dg-page-btn'); b.type = 'button'; b.textContent = label; b.title = title;
      b.disabled = !enabled; if (enabled) b.addEventListener('click', fn); pager.appendChild(b);
    }
    function addNum(p) {
      var b = el('button', 'dg-page-btn' + (p === state.page ? ' active' : '')); b.type = 'button';
      b.textContent = p; b.addEventListener('click', function () { goto(p); }); pager.appendChild(b);
    }
    function addGap() { var s = el('span', 'dg-gap'); s.textContent = '…'; pager.appendChild(s); }

    function goto(p) { state.page = p; update(); }
    function sortBy(i) {
      if (state.sortCol === i) state.sortDir = state.sortDir === 'asc' ? 'desc' : 'asc';
      else { state.sortCol = i; state.sortDir = 'asc'; }
      state.page = 1; update();
    }

    var typing;
    search.addEventListener('input', function () {
      clearTimeout(typing);
      typing = setTimeout(function () { state.search = search.value; state.page = 1; update(); }, 200);
    });
    sizeSel.addEventListener('change', function () {
      state.pageSize = parseInt(sizeSel.value, 10) || 10; state.page = 1; update();
    });

    update();
  }

  // ── State persistence (per page + grid, for the current tab session) ──
  function stateKey(i) { return 'dg:' + location.pathname + location.search + '#' + i; }
  function loadState(i) {
    var def = { page: 1, pageSize: 10, search: '', sortCol: null, sortDir: 'asc' };
    try { return Object.assign(def, JSON.parse(sessionStorage.getItem(stateKey(i)) || '{}')); }
    catch (e) { return def; }
  }
  function saveState(i, s) {
    try {
      sessionStorage.setItem(stateKey(i), JSON.stringify({
        page: s.page, pageSize: s.pageSize, search: s.search, sortCol: s.sortCol, sortDir: s.sortDir
      }));
    } catch (e) { /* storage unavailable — non-fatal */ }
  }
})();
