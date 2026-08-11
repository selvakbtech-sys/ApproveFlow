# ApproveFlow — Server Deployment Checklist

Practical checklist for hosting ApproveFlow on IIS (server: BNRSVR02). The focus is on
making the **WhatsApp pending-approval reminders** (08:59 / 13:00 / 18:00 IST) fire reliably.

Dev and the server are **separate deployments with separate databases**, sharing one WhatsApp
number and one Meta callback. Apply every server-side step below on the server itself.

---

## 1. Keep the app pool alive (most important)

The reminder scheduler runs as a hosted `BackgroundService`. A hosted service **dies with its
IIS app pool**, so if the pool idles or recycles between two slots and only wakes after the later
slot's time, that slot can be lost. Keep the pool always running:

**IIS Manager (GUI)** — Application Pools → *ApproveFlow pool* → Advanced Settings:

- **Start Mode** = `AlwaysRunning`
- **(Process Model) Idle Time-out (minutes)** = `0`
- **(Recycling) Regular Time Interval (minutes)** = `0`
  (optional but recommended: instead add a **Specific Time** recycle at a quiet hour, e.g. `03:00`,
  so a recycle never lands on a reminder slot)

Then the **site** → Advanced Settings → **Preload Enabled** = `True`.

Recycle the app pool once so the settings take effect.

**Or command line (run as admin; replace pool/site names):**

```bash
%windir%\system32\inetsrv\appcmd set apppool "ApproveFlowPool" /startMode:AlwaysRunning
```
```bash
%windir%\system32\inetsrv\appcmd set apppool "ApproveFlowPool" /processModel.idleTimeout:00:00:00
```
```bash
%windir%\system32\inetsrv\appcmd set apppool "ApproveFlowPool" /recycling.periodicRestart.time:00:00:00
```
```bash
%windir%\system32\inetsrv\appcmd set app "ApproveFlowSite/" /preloadEnabled:true
```

---

## 2. Build and publish (do NOT build from the CLI)

A CLI `dotnet build` / `dotnet run` on this environment poisons the DLL
(WDAC `0x800711C7` → IIS `500.30`). **Build and publish only from Visual Studio.**

- [ ] Rebuild in Visual Studio (Release).
- [ ] Publish to the server target from Visual Studio.
- [ ] Confirm the deployed build includes the reminder **catch-up fix** in
      `Services/AgingReminderBackgroundService.cs` (fires a slot that is already due today but
      hasn't gone out yet — recovers a slot missed while the pool was asleep).

---

## 3. Reminder configuration (appsettings on the server)

Confirm the server's `appsettings.json` (or environment overrides):

- [ ] `WhatsApp:PendingReminderTimes` = `[ "08:59", "13:00", "18:00" ]` (IST, `HH:mm`)
- [ ] `WhatsApp:PendingReminderTemplate` = `pending_approval_summary` (must be an **approved** Meta template)
- [ ] `WhatsApp:ReminderRetryAttempts` / `WhatsApp:ReminderRetryDelaySeconds` set as intended
- [ ] Times are interpreted in **India Standard Time** in code (converted from UTC), so they are
      correct regardless of the server clock's timezone — no server-clock change needed.

---

## 4. Network to Meta (transport)

BNRSVR02 is known to reset outbound TLS to `graph.facebook.com`. Effects and mitigations:

- A reminder that fires may still fail at transport and be handed to the **WhatsAppOutbox**, which
  retries in the background — so it can arrive **late** rather than never. "Fired" ≠ "delivered instantly".
- Real fix is on the **IT firewall** (allow stable outbound TLS to `graph.facebook.com`).
- [ ] Confirm the outbox background service is running and draining (see verification below).

---

## 5. Post-deploy verification

- [ ] App pool shows **Started** and stays started (no idle shutdown).
- [ ] App logs show `AgingReminderBackgroundService started. Schedule (IST): 08:59, 13:00, 18:00`.
- [ ] After a slot, check `WhatsAppMessageHistory` for the outbound
      `template:pending_approval_summary` rows for that slot.
- [ ] Check `WhatsAppOutbox` for stuck/`Abandoned` rows and their `LastError`.
- [ ] Reminders only go to approvers with a **current pending count** — an empty slot with no
      pending approvals sending nothing is **by design**, not a failure.

---

## Why 18:00 is the most exposed slot

18:00 is the **last** slot of the day. The catch-up recovery is deliberately bounded to *today*
(so it never resends a stale reminder), and evenings/overnight are exactly when the server gets no
traffic. If the pool is asleep at 18:00 and the next request only arrives the following day, that
18:00 slot is lost. **Step 1 (AlwaysRunning + Idle Time-out 0) is what prevents this** — treat it
as required, not optional.
