import { api, subscribe } from './api.js';
import { jsonView } from './json-view.js';
import { clear, copyButton, el, emptyState, formatTime, toast } from './ui.js';

const MAX_RECORDS = 3000;
const LEVEL_ORDER = ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical'];

/**
 * The live log console.
 *
 * Reading what the packages actually logged is half the point of this app, so the row
 * keeps the formatted message for scanning and the expanded view keeps the message
 * template and the structured state exactly as the package emitted them.
 */
export function createLogConsole() {
  const body = document.getElementById('log-body');
  const countBadge = document.getElementById('log-count');
  const searchInput = document.getElementById('log-search');
  const runFilterChip = document.getElementById('log-run-filter');
  const pauseButton = document.getElementById('log-pause');
  const clearButton = document.getElementById('log-clear');
  const dock = document.getElementById('dock');

  let records = [];
  let paused = false;
  const levels = new Set(['Information', 'Warning', 'Error', 'Critical']);
  let search = '';
  let runId = null;

  function matches(record) {
    if (!levels.has(record.level)) return false;
    if (runId && record.runId !== runId) return false;
    if (search) {
      const haystack = `${record.message} ${record.category} ${record.exception?.message ?? ''}`;
      if (!haystack.toLowerCase().includes(search)) return false;
    }
    return true;
  }

  function atBottom() {
    return body.scrollHeight - body.scrollTop - body.clientHeight < 40;
  }

  function renderAll() {
    const wasAtBottom = atBottom();
    clear(body);

    const visible = records.filter(matches);
    if (visible.length === 0) {
      body.append(
        emptyState(
          records.length === 0 ? 'No logs yet' : 'No records match the filter',
          records.length === 0
            ? 'Run a scenario; the logs it produces stream here live.'
            : 'Try loosening the level chips or the search.',
        ),
      );
    } else {
      const fragment = document.createDocumentFragment();
      for (const record of visible) fragment.append(row(record));
      body.append(fragment);
    }

    countBadge.textContent = runId || search || levels.size < 5 ? `${visible.length}/${records.length}` : `${records.length}`;
    if (wasAtBottom) body.scrollTop = body.scrollHeight;
  }

  function append(record) {
    records.push(record);
    if (records.length > MAX_RECORDS) records = records.slice(-MAX_RECORDS);

    if (paused) return;
    if (!matches(record)) {
      countBadge.textContent = `${records.length}`;
      return;
    }

    const wasAtBottom = atBottom();
    if (body.querySelector('.empty')) clear(body);
    body.append(row(record));

    while (body.childElementCount > MAX_RECORDS) body.firstElementChild.remove();
    countBadge.textContent = runId || search || levels.size < 5
      ? `${body.childElementCount}/${records.length}`
      : `${records.length}`;

    if (wasAtBottom) body.scrollTop = body.scrollHeight;
  }

  function row(record) {
    const node = el('div', {
      class: 'log-row',
      dataset: { level: record.level, expanded: 'false' },
      role: 'button',
      tabindex: '0',
    });

    const message = el('div', { class: 'log-row__message' }, record.message);

    node.append(
      el('div', { class: 'log-row__time' }, formatTime(record.timestamp)),
      el('div', { class: 'log-row__level' }, record.level),
      el('div', { class: 'log-row__category', title: record.category }, record.category),
      message,
    );

    let detail = null;
    const toggle = () => {
      const expanded = node.dataset.expanded === 'true';
      node.dataset.expanded = String(!expanded);
      if (expanded) {
        detail?.remove();
        detail = null;
      } else {
        detail = buildDetail(record);
        node.append(detail);
      }
    };

    node.addEventListener('click', toggle);
    node.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        toggle();
      }
    });

    return node;
  }

  function buildDetail(record) {
    const detail = el('div', { class: 'log-detail' });

    if (record.messageTemplate) {
      detail.append(
        section('message template', el('div', { class: 'json' }, record.messageTemplate)),
      );
    }

    if (record.state && Object.keys(record.state).length > 0) {
      detail.append(section('structured fields', jsonView(record.state, { expandDepth: 3 })));
    }

    if (record.scopes?.length) {
      detail.append(section('scope', jsonView(record.scopes, { expandDepth: 3 })));
    }

    if (record.exception) {
      detail.append(section('exception', jsonView(record.exception, { expandDepth: 2 })));
    }

    const meta = el(
      'div',
      { class: 'row' },
      el('span', { class: 'badge' }, `#${record.id}`),
      record.runId && el('span', { class: 'badge badge--accent' }, record.runId),
      el('span', { class: 'badge' }, `eventId ${record.eventId.id}`),
      copyButton(() => JSON.stringify(record, null, 2)),
    );

    detail.append(section('raw record', meta));
    detail.addEventListener('click', (event) => event.stopPropagation());
    return detail;
  }

  function section(label, content) {
    return el(
      'div',
      { class: 'log-detail__section' },
      el('div', { class: 'log-detail__label' }, label),
      content,
    );
  }

  // ---- filters -------------------------------------------------------------

  document.getElementById('log-filters').addEventListener('click', (event) => {
    const chip = event.target.closest('.chip[data-level]');
    if (!chip) return;
    const level = chip.dataset.level;
    const pressed = chip.getAttribute('aria-pressed') === 'true';
    chip.setAttribute('aria-pressed', String(!pressed));

    if (pressed) {
      levels.delete(level);
      if (level === 'Error') levels.delete('Critical');
    } else {
      levels.add(level);
      if (level === 'Error') levels.add('Critical');
    }

    renderAll();
  });

  let searchTimer;
  searchInput.addEventListener('input', () => {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => {
      search = searchInput.value.trim().toLowerCase();
      renderAll();
    }, 150);
  });

  runFilterChip.addEventListener('click', () => setRunFilter(null));

  pauseButton.addEventListener('click', () => {
    paused = !paused;
    pauseButton.setAttribute('aria-pressed', String(paused));
    pauseButton.textContent = paused ? '▶' : '⏸';
    pauseButton.title = paused ? 'Resume the stream' : 'Pause the stream';
    if (!paused) renderAll();
  });

  clearButton.addEventListener('click', async () => {
    try {
      await api.clearLogs();
      records = [];
      renderAll();
    } catch (error) {
      toast(error.message, 'err');
    }
  });

  function setRunFilter(value) {
    runId = value;
    if (value) {
      runFilterChip.hidden = false;
      runFilterChip.textContent = `run ${value} ✕`;
      runFilterChip.setAttribute('aria-pressed', 'true');
      dock.dataset.collapsed = 'false';
      document.getElementById('dock-caret').textContent = '▾';
      document.getElementById('dock-toggle').setAttribute('aria-expanded', 'true');
    } else {
      runFilterChip.hidden = true;
      runFilterChip.removeAttribute('aria-pressed');
    }
    renderAll();
  }

  // ---- lifecycle -----------------------------------------------------------

  async function start() {
    try {
      const history = await api.logs({ take: 400, level: 'Trace' });
      records = history.entries;
    } catch {
      records = [];
    }

    renderAll();

    subscribe('/api/logs/stream', 'log', append, () =>
      toast('The log stream disconnected. Refresh the page.', 'err'),
    );
  }

  return { start, setRunFilter, levelOrder: LEVEL_ORDER };
}
