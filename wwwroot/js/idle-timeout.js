// ApproveFlow idle-session-timeout
// Loaded from _Layout.cshtml, so it runs on authenticated pages only
// (the Login page uses Layout = null and never includes this file).
//
// Logs the user out after 5 minutes of no activity, resetting the timer on
// any mouse/keyboard/scroll/touch input. Also intercepts AJAX 401/403
// responses and synchronises timeout/logout across browser tabs.
(function () {
  'use strict';

  var IDLE_LIMIT   = 5 * 60 * 1000;                 // 5 minutes
  var ACTIVITY_KEY = 'af_lastActivity';             // shared across tabs
  var LOGOUT_KEY   = 'af_sessionLoggedOut';         // broadcast on logout
  var EXPIRED_MSG  = 'Your session has expired due to inactivity. Please login again.';
  var LOGIN_URL    = '/login';
  var LOGOUT_URL   = '/logout';                      // existing Auth/Logout action

  var timer = null;
  var lastWrite = 0;        // throttle localStorage writes on high-frequency events
  var loggedOut = false;

  function now() { return Date.now(); }

  function readLastActivity() {
    var v = parseInt(localStorage.getItem(ACTIVITY_KEY) || '0', 10);
    return isNaN(v) ? 0 : v;
  }

  // Session expired in THIS tab: notify other tabs, inform the user, sign out.
  function expireSession() {
    if (loggedOut) return;
    loggedOut = true;
    if (timer) { clearTimeout(timer); timer = null; }
    try { localStorage.setItem(LOGOUT_KEY, String(now())); } catch (e) { /* ignore */ }
    alert(EXPIRED_MSG);
    window.location.href = LOGOUT_URL;   // clears the auth cookie, then redirects to Login
  }

  // (Re)arm the timer against the shared last-activity timestamp so that
  // activity in any tab keeps every tab alive.
  function schedule() {
    if (loggedOut) return;
    if (timer) clearTimeout(timer);
    var remaining = IDLE_LIMIT - (now() - readLastActivity());
    if (remaining <= 0) { expireSession(); return; }
    timer = setTimeout(function () {
      if (now() - readLastActivity() >= IDLE_LIMIT) expireSession();
      else schedule();   // another tab was active in the meantime
    }, remaining);
  }

  function onActivity() {
    if (loggedOut) return;
    var t = now();
    if (t - lastWrite > 1000) {          // write at most once per second
      lastWrite = t;
      try { localStorage.setItem(ACTIVITY_KEY, String(t)); } catch (e) { /* ignore */ }
    }
    schedule();
  }

  var events = ['mousemove', 'mousedown', 'click', 'keydown', 'scroll', 'touchstart'];
  events.forEach(function (ev) {
    window.addEventListener(ev, onActivity, { passive: true });
  });

  // Cross-tab coordination.
  window.addEventListener('storage', function (e) {
    if (e.key === LOGOUT_KEY) {
      loggedOut = true;
      if (timer) { clearTimeout(timer); timer = null; }
      window.location.href = LOGIN_URL;  // another tab timed out / logged out
    } else if (e.key === ACTIVITY_KEY) {
      schedule();                        // another tab registered activity
    }
  });

  // Redirect to Login when a background AJAX call finds the session gone,
  // instead of surfacing raw 401/403 JSON or the login HTML.
  function handleUnauthorized(status) {
    if (loggedOut) return;
    if (status === 401 || status === 403) {
      loggedOut = true;
      window.location.href = LOGIN_URL;
    }
  }

  if (window.fetch) {
    var nativeFetch = window.fetch;
    window.fetch = function () {
      return nativeFetch.apply(this, arguments).then(function (resp) {
        if (resp) handleUnauthorized(resp.status);
        return resp;
      });
    };
  }

  var nativeOpen = XMLHttpRequest.prototype.open;
  XMLHttpRequest.prototype.open = function () {
    this.addEventListener('load', function () { handleUnauthorized(this.status); });
    return nativeOpen.apply(this, arguments);
  };

  // Initialise. Loading an authenticated page IS user activity (they just logged
  // in or navigated) and the server already validated the session to render it,
  // so stamp "now" as the last activity. This must be unconditional: a stale
  // timestamp left in localStorage by a previous session would otherwise make the
  // very next page look "idle > 5 min" and fire a false expiry right after login.
  lastWrite = now();
  try { localStorage.setItem(ACTIVITY_KEY, String(lastWrite)); } catch (e) { /* ignore */ }
  schedule();
})();
