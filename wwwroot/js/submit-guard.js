// ApproveFlow submit-guard.js
// ─────────────────────────────────────────────────────────────────────────────
// Reusable double-submit guard + progress overlay for every workflow submit action
// (New Request, Submit for Approval, Approve / Reject / Clarification, Submit
// Clarification, Revoke). Opt in from markup — no per-page JavaScript:
//
//   <form asp-action="Save" method="post" data-submit-guard>
//
// Optional attributes:
//   data-guard-title="Submitting request"   heading shown in the overlay
//   data-guard-idempotent="false"           skip the hidden IdempotencyKey field
//
// What it does, in order, on the first submit:
//   1. blocks every later submit of the same form (clicks, Enter, programmatic)
//   2. disables all submit buttons — and posts the clicked button's name/value as a
//      hidden field first, because a disabled button is NOT included in form data
//   3. shows a full-screen overlay with a staged progress bar
//   4. after 3s, adds the "do not refresh" warning
//
// The guard is deliberately NOT released on success: these forms do a full-page POST
// and redirect, so the fresh page is the reset. It IS released when the browser
// restores the page from its back/forward cache, which would otherwise leave the user
// staring at a permanently disabled form.
(function () {
  'use strict';

  // Stages from the spec. Each is applied on a timer; they describe what the server
  // is doing, and the bar deliberately stops at 80% until the response actually
  // arrives, so it never claims success the server has not confirmed.
  var STAGES = [
    { at: 0,    pct: 10,  msg: 'Validating request…' },
    { at: 400,  pct: 30,  msg: 'Saving request…' },
    { at: 1100, pct: 55,  msg: 'Creating approval workflow…' },
    { at: 1900, pct: 80,  msg: 'Finalizing transaction…' }
  ];
  var SLOW_AFTER_MS = 3000;
  var SLOW_TEXT = 'Processing your request. Please wait… Do not refresh or close this page.';

  function uuid() {
    if (window.crypto && typeof window.crypto.randomUUID === 'function')
      return window.crypto.randomUUID();
    // Fallback for older browsers / non-secure origins where randomUUID is absent.
    if (window.crypto && window.crypto.getRandomValues) {
      var b = new Uint8Array(16);
      window.crypto.getRandomValues(b);
      b[6] = (b[6] & 0x0f) | 0x40;
      b[8] = (b[8] & 0x3f) | 0x80;
      var h = [];
      for (var i = 0; i < 16; i++) h.push(('0' + b[i].toString(16)).slice(-2));
      return h.slice(0, 4).join('') + '-' + h.slice(4, 6).join('') + '-' +
             h.slice(6, 8).join('') + '-' + h.slice(8, 10).join('') + '-' + h.slice(10).join('');
    }
    return 'k' + Date.now().toString(16) + Math.random().toString(16).slice(2, 10);
  }

  // One overlay is built lazily and reused by every form on the page.
  var overlay = null;
  function getOverlay() {
    if (overlay) return overlay;
    overlay = document.createElement('div');
    overlay.className = 'sg-overlay';
    overlay.setAttribute('role', 'status');
    overlay.setAttribute('aria-live', 'polite');
    overlay.innerHTML =
      '<div class="sg-panel">' +
        '<div class="sg-title"></div>' +
        '<div class="sg-bar"><div class="sg-fill"></div></div>' +
        '<div class="sg-msg"></div>' +
        '<div class="sg-slow" hidden></div>' +
      '</div>';
    document.body.appendChild(overlay);
    return overlay;
  }

  function showOverlay(title) {
    var o = getOverlay();
    o.querySelector('.sg-title').textContent = title;
    o.querySelector('.sg-fill').style.width = '0%';
    o.querySelector('.sg-msg').textContent = '';
    o.querySelector('.sg-slow').hidden = true;
    o.classList.add('show');
    return o;
  }

  function hideOverlay() {
    if (overlay) overlay.classList.remove('show');
  }

  function initForm(form) {
    // Idempotency key — one per page load, i.e. one per user submit attempt. Every
    // retry of that attempt (double-click, browser resend, retry after a timeout)
    // posts the SAME key, so the server collapses them into one request.
    if (form.dataset.guardIdempotent !== 'false' &&
        !form.querySelector('input[name="IdempotencyKey"]')) {
      var k = document.createElement('input');
      k.type = 'hidden';
      k.name = 'IdempotencyKey';
      k.value = uuid();
      form.appendChild(k);
    }

    var timers = [];
    function clearTimers() {
      timers.forEach(clearTimeout);
      timers = [];
    }

    // Enter anywhere in the form re-submits it. Once we are processing, swallow it.
    form.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' && form.dataset.sgBusy === '1') {
        e.preventDefault();
        e.stopPropagation();
      }
    }, true);

    // Capture phase, so a second click is stopped before it reaches any other handler.
    form.addEventListener('click', function (e) {
      if (form.dataset.sgBusy !== '1') return;
      var b = e.target.closest('button, input[type=submit]');
      if (b && form.contains(b)) { e.preventDefault(); e.stopPropagation(); }
    }, true);

    form.addEventListener('submit', function (e) {
      // Second and later submits of the same attempt: ignore completely.
      if (form.dataset.sgBusy === '1') { e.preventDefault(); return; }

      // Let client-side validation fail normally without arming the guard —
      // otherwise an invalid form would lock its own submit button forever.
      if (typeof form.checkValidity === 'function' && !form.checkValidity()) return;

      var submitter = e.submitter || document.activeElement;

      // A disabled button is not posted, so preserve the chosen action first. This is
      // what distinguishes "Save as Draft" from "Save & Submit for Approval", and
      // Approve from Reject.
      if (submitter && submitter.name && form.contains(submitter) &&
          !form.querySelector('input[type=hidden][data-sg-submitter]')) {
        var hid = document.createElement('input');
        hid.type = 'hidden';
        hid.name = submitter.name;
        hid.value = submitter.value;
        hid.setAttribute('data-sg-submitter', '');
        form.appendChild(hid);
      }

      form.dataset.sgBusy = '1';
      form.querySelectorAll('button[type=submit], input[type=submit]').forEach(function (b) {
        b.disabled = true;
      });

      var o = showOverlay(form.dataset.guardTitle || 'Submitting');
      var fill = o.querySelector('.sg-fill');
      var msg = o.querySelector('.sg-msg');
      var slow = o.querySelector('.sg-slow');

      STAGES.forEach(function (s) {
        timers.push(setTimeout(function () {
          fill.style.width = s.pct + '%';
          msg.textContent = s.msg;
        }, s.at));
      });
      timers.push(setTimeout(function () {
        slow.textContent = SLOW_TEXT;
        slow.hidden = false;
      }, SLOW_AFTER_MS));
    });

    // Released ONLY on a real failure to navigate: the browser restoring this page
    // from the back/forward cache. A successful submit redirects to a new page, and
    // a server-side validation error re-renders the form fresh, so neither needs it.
    window.addEventListener('pageshow', function (ev) {
      if (!ev.persisted) return;
      clearTimers();
      hideOverlay();
      form.dataset.sgBusy = '';
      form.querySelectorAll('[data-sg-submitter]').forEach(function (h) { h.remove(); });
      form.querySelectorAll('button[type=submit], input[type=submit]').forEach(function (b) {
        // Re-enable only what the guard disabled — never a button the server rendered
        // disabled (e.g. New Request with no mapped request types).
        if (!b.hasAttribute('data-sg-keep-disabled')) b.disabled = false;
      });
    });
  }

  document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('form[data-submit-guard]').forEach(function (form) {
      // Remember which buttons the SERVER rendered disabled, so the bfcache reset
      // above does not accidentally enable them.
      form.querySelectorAll('button[type=submit][disabled], input[type=submit][disabled]')
        .forEach(function (b) { b.setAttribute('data-sg-keep-disabled', ''); });
      initForm(form);
    });
  });
})();
