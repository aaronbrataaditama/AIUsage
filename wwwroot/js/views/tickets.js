window.Views = window.Views || {};
window.Views.tickets = (function () {
  // JIRA priority name -> dot colour (falls back to muted grey for unknown/none).
  // Covers both the default scheme (Highest…Lowest) and the common Blocker…Trivial one.
  const PRIORITY_COLORS = {
    blocker: '#b02020', critical: '#d03b3b', highest: '#d03b3b',
    high: '#eb6834', major: '#eb6834',
    medium: '#eda100', moderate: '#eda100',
    low: '#6da7ec', minor: '#6da7ec',
    lowest: '#898781', trivial: '#898781'
  };

  // Tracker id -> display label, for the per-key provider badge and toasts.
  const TRACKER_LABEL = { jira: 'JIRA', clickup: 'ClickUp' };

  // View state (persists across re-renders via the module closure).
  let aiOnly = false;
  // Fetch-more pagination cursor, kept separately per tracker (JIRA pages via an opaque token,
  // ClickUp via a page number — both surfaced uniformly as "cursor" by tickets.fetchMore).
  let cursors = { jira: null, clickup: null };

  const isAiTouched = t => (t.sessionCount || 0) > 0 || (t.manualCount || 0) > 0;

  // Status -> row tint class. Covers the requested Closed/Open/In-Progress plus their
  // common JIRA synonyms so most rows are coloured consistently.
  function statusRowClass(status) {
    const s = (status || '').toLowerCase();
    if (/closed|done|resolved|complete/.test(s)) return 'st-green';
    if (/in progress|in review|in dev/.test(s)) return 'st-orange';
    if (/open|to ?do|backlog|reopen|selected|^new$/.test(s)) return 'st-blue';
    return '';
  }

  function priorityCell(name) {
    if (!name) return '<span class="muted">—</span>';
    const color = PRIORITY_COLORS[name.toLowerCase()] || '#898781';
    return `<span style="display:inline-flex;align-items:center;gap:6px">
      <span style="width:9px;height:9px;border-radius:50%;background:${color};flex-shrink:0"></span>
      ${App.esc(name)}</span>`;
  }

  async function load(el) {
    let tickets, settings;
    try {
      [tickets, settings] = await Promise.all([
        Bridge.call('tickets.list'),
        Bridge.call('settings.get')
      ]);
    } catch (e) {
      el.innerHTML = `<div class="panel empty">Failed to load tickets: ${App.esc(e.message)}</div>`;
      return;
    }

    const aiCount = tickets.filter(isAiTouched).length;
    const shown = aiOnly ? tickets.filter(isAiTouched) : tickets;

    const fetchButtons = [
      settings.jiraEnabled ? `<button id="fetch-more-jira" class="btn" onclick="Views.tickets.fetchMore('jira')">Fetch more from JIRA</button>` : '',
      settings.clickupEnabled ? `<button id="fetch-more-clickup" class="btn" onclick="Views.tickets.fetchMore('clickup')">Fetch more from ClickUp</button>` : ''
    ].join('');

    const controls = `
      <div style="display:flex;gap:8px;align-items:center;margin-bottom:12px;flex-wrap:wrap">
        <button id="sync-all" class="btn btn-primary" onclick="Views.tickets.syncAll()">Sync all</button>
        ${fetchButtons}
        <span style="flex:1"></span>
        <div class="tabs" style="margin:0">
          <button class="btn ${aiOnly ? '' : 'active'}" onclick="Views.tickets.setAiOnly(false)">All</button>
          <button class="btn ${aiOnly ? 'active' : ''}" onclick="Views.tickets.setAiOnly(true)">AI-touched</button>
        </div>
        <button class="btn" onclick="App.exportExcel('export.tickets')">⬇ Export to Excel</button>
      </div>`;

    if (!tickets.length) {
      el.innerHTML = `<h1>Tickets</h1>${controls}
        <div class="panel empty">No tickets yet — they appear when sessions are linked, manual entries are added,
        or you fetch more from JIRA/ClickUp.</div>`;
      return;
    }

    const rows = shown.map(t => `
      <tr class="${statusRowClass(t.status)}">
        <td><span class="badge">${App.esc(t.key)}</span>
            <span class="badge tracker">${App.esc(TRACKER_LABEL[t.provider] || t.provider || '')}</span>
            ${isAiTouched(t) ? `<span class="badge ai" title="AI-assisted — ${t.sessionCount || 0} session(s), ${t.manualCount || 0} manual entry(ies)">✨ AI</span>` : ''}
            ${t.fetchFailed ? `<span class="badge dead" title="Key not found in ${App.esc(TRACKER_LABEL[t.provider] || 'tracker')}">dead key</span>` : ''}</td>
        <td>${App.esc(t.summary || '')}</td>
        <td class="muted">${App.esc(t.project || '')}</td>
        <td class="muted">${App.esc(t.issueType || '')}</td>
        <td>${priorityCell(t.priority)}</td>
        <td class="muted">${App.esc(t.status || '')}</td>
        <td class="muted">${App.esc(t.sprint || '')}</td>
        <td style="text-align:right">${t.sessionCount || 0}</td>
        <td style="text-align:right">${t.manualCount || 0}</td>
        <td class="muted" style="white-space:nowrap">${t.lastSynced ? App.fmtDate(t.lastSynced) : 'never'}</td>
      </tr>`).join('');

    el.innerHTML = `<h1>Tickets</h1>${controls}
      <div class="table-scroll">
        <table>
          <thead><tr>
            <th>Key</th><th>Summary</th><th>Project</th><th>Type</th><th>Priority</th>
            <th>Status</th><th>Sprint</th>
            <th style="text-align:right">Sessions</th><th style="text-align:right">Manual</th><th>Last synced</th>
          </tr></thead>
          <tbody>${rows}</tbody>
        </table>
      </div>
      <div class="footnote">Showing ${shown.length} of ${tickets.length} tickets (${aiCount} AI-touched).
        Project, sprint, and priority come from your tracker — run “Sync all” to populate them, or
        “Fetch more” (per-tracker, above) to import more.</div>`;
  }

  return {
    render: load,
    setAiOnly(v) { aiOnly = v; App.refresh(); },
    async syncAll() {
      const btn = document.getElementById('sync-all');
      btn.disabled = true;
      btn.textContent = 'Syncing…';
      try {
        const r = await Bridge.call('tickets.sync', {}, 0);
        let msg = `Synced ${r.synced}/${r.total} (${r.dead} dead, ${r.failed} failed)`;
        if (r.skipped > 0) msg += `, ${r.skipped} skipped — tracker disabled`;
        App.toast(msg);
        App.refresh();
      } catch (e) {
        App.toast(e.message, true);
        btn.disabled = false;
        btn.textContent = 'Sync all';
      }
    },
    async fetchMore(provider) {
      const label = TRACKER_LABEL[provider] || provider;
      const btn = document.getElementById(`fetch-more-${provider}`);
      if (btn) { btn.disabled = true; btn.textContent = 'Fetching…'; }
      try {
        // no client timeout: a tracker page + upserts can take a while
        const r = await Bridge.call('tickets.fetchMore', { provider, cursor: cursors[provider] }, 0);
        cursors[provider] = r.isLast ? null : r.cursor; // null restarts from the first page
        App.toast(r.isLast
          ? `Imported ${r.imported} from ${label} — all matching tickets fetched`
          : `Imported ${r.imported} from ${label} — click “Fetch more” for the next page`);
        App.refresh();
      } catch (e) {
        App.toast(e.message, true);
        if (btn) { btn.disabled = false; btn.textContent = `Fetch more from ${label}`; }
      }
    }
  };
})();
