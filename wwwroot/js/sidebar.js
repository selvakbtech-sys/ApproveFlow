// Sidebar group collapse — persistence only.
//
// The groups are native <details> elements, so opening and closing already works with
// JavaScript disabled. This file adds two things on top:
//   1. the open/closed state of each group is remembered per browser, and
//   2. whichever group holds the active link is always opened on load, so navigating can
//      never land you on a page whose nav entry is hidden.
(function () {
  'use strict';

  var KEY = 'af.sidebar.groups';

  // localStorage throws in some locked-down browser modes; the sidebar must still work.
  function readState() {
    try { return JSON.parse(localStorage.getItem(KEY)) || {}; }
    catch (e) { return {}; }
  }

  function writeState(state) {
    try { localStorage.setItem(KEY, JSON.stringify(state)); }
    catch (e) { /* private mode / site data blocked — collapse still works, just not sticky */ }
  }

  document.addEventListener('DOMContentLoaded', function () {
    // Both top-level sections (.sb-group) and nested parent menus (.sb-sub) carry
    // data-sb-group, so one selector covers the whole tree.
    var groups = document.querySelectorAll('[data-sb-group]');
    if (!groups.length) return;
    var state = readState();

    groups.forEach(function (g) {
      var key = g.getAttribute('data-sb-group');
      var hasActive = !!g.querySelector('.nav-link.active');

      // The active group always opens, whatever was stored. Otherwise honour the stored
      // choice, falling back to the `open` attribute the server rendered.
      if (hasActive) g.open = true;
      else if (Object.prototype.hasOwnProperty.call(state, key)) g.open = !!state[key];

      g.addEventListener('toggle', function () {
        var s = readState();
        s[key] = g.open;
        writeState(s);
      });
    });
  });
})();
