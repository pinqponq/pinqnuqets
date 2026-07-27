import { api } from './api.js';
import { createInfraBar } from './infra-bar.js';
import { createLogConsole } from './log-console.js';
import { createScenarioView } from './scenario-view.js';
import { clear, el, emptyState, toast } from './ui.js';

const appNode = document.getElementById('app');
const sidebarList = document.getElementById('sidebar-list');
const sidebarFilter = document.getElementById('sidebar-filter');

let catalog = { packages: [] };
let infraState = { dockerAvailable: false, dockerError: null };
const runStatuses = new Map();

// ---------------------------------------------------------------- theme

const THEME_KEY = 'pinqponq.playground.theme';

function applyTheme(theme) {
  document.documentElement.dataset.theme = theme;
  localStorage.setItem(THEME_KEY, theme);
}

applyTheme(
  localStorage.getItem(THEME_KEY) ??
    (window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark'),
);

document.getElementById('theme-toggle').addEventListener('click', () => {
  applyTheme(document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark');
});

// ---------------------------------------------------------------- platform key labels

const isMac = navigator.platform.toLowerCase().includes('mac');
if (isMac) {
  document.getElementById('palette-kbd').textContent = '⌘ K';
}

// ---------------------------------------------------------------- components

const logConsole = createLogConsole();

const scenarioView = createScenarioView({
  onStatusChange(id, status) {
    // A pinned run filter from an earlier run would hide the logs of the one just started.
    if (status === 'running') logConsole.setRunFilter(null);
    runStatuses.set(id, status);
    const dot = sidebarList.querySelector(`.scenario-link[data-id="${CSS.escape(id)}"] .scenario-link__status`);
    if (dot) dot.dataset.status = status;
  },
  onShowLogs(runId) {
    logConsole.setRunFilter(runId);
  },
});

const infraBar = createInfraBar({
  onChange(services, state) {
    infraState = state;
    // Availability depends on live service state, so the catalog is re-read rather than
    // recomputed here — the server owns the "blocked by" reasoning. Status events arrive
    // in bursts while a container starts, hence the coalescing.
    scheduleCatalogRefresh();
  },
});

let catalogRefreshTimer;
function scheduleCatalogRefresh() {
  clearTimeout(catalogRefreshTimer);
  catalogRefreshTimer = setTimeout(() => void refreshCatalog(), 250);
}

// ---------------------------------------------------------------- sidebar

function allScenarios() {
  return catalog.packages.flatMap((pkg) => pkg.scenarios.map((scenario) => ({ ...scenario, pkg })));
}

function renderSidebar(filterText = '') {
  const filter = filterText.trim().toLowerCase();
  clear(sidebarList);

  const groups = new Map();
  for (const pkg of catalog.packages) {
    const scenarios = filter
      ? pkg.scenarios.filter(
          (scenario) =>
            scenario.title.toLowerCase().includes(filter) ||
            scenario.id.toLowerCase().includes(filter) ||
            pkg.id.toLowerCase().includes(filter),
        )
      : pkg.scenarios;

    if (scenarios.length === 0) continue;
    if (!groups.has(pkg.group)) groups.set(pkg.group, []);
    groups.get(pkg.group).push({ pkg, scenarios });
  }

  if (groups.size === 0) {
    sidebarList.append(emptyState('Eşleşen senaryo yok', filterText));
    return;
  }

  const activeId = currentScenarioId();

  for (const [group, entries] of groups) {
    sidebarList.append(el('div', { class: 'sidebar__group' }, group));

    for (const { pkg, scenarios } of entries) {
      const containsActive = scenarios.some((scenario) => scenario.id === activeId);
      const open = Boolean(filter) || containsActive;

      const list = el('div', { class: 'pkg__list' });
      for (const scenario of scenarios) {
        list.append(
          el(
            'button',
            {
              class: 'scenario-link',
              type: 'button',
              dataset: { id: scenario.id, available: String(scenario.available) },
              'aria-current': String(scenario.id === activeId),
              title: scenario.available ? scenario.title : `Gereken servis hazır değil: ${scenario.blockedBy.join(', ')}`,
              onClick: () => navigate(scenario.id),
            },
            el('span', { class: 'scenario-link__title' }, scenario.title),
            !scenario.available ? el('span', { class: 'badge badge--err' }, '!') : null,
            el('span', {
              class: 'scenario-link__status',
              dataset: { status: runStatuses.get(scenario.id) ?? '' },
            }),
          ),
        );
      }

      const node = el(
        'div',
        { class: 'pkg', dataset: { open: String(open) } },
        el(
          'button',
          {
            class: 'pkg__head',
            type: 'button',
            'aria-expanded': String(open),
            onClick: (event) => {
              const host = event.currentTarget.parentElement;
              const nowOpen = host.dataset.open !== 'true';
              host.dataset.open = String(nowOpen);
              event.currentTarget.setAttribute('aria-expanded', String(nowOpen));
            },
          },
          el('span', { class: 'pkg__caret' }, '▶'),
          pkg.title,
          el('span', { class: 'pkg__count' }, scenarios.length),
        ),
        list,
      );

      sidebarList.append(node);
    }
  }
}

sidebarFilter.addEventListener('input', () => renderSidebar(sidebarFilter.value));

// ---------------------------------------------------------------- routing

function currentScenarioId() {
  const match = window.location.hash.match(/^#\/scenario\/(.+)$/);
  return match ? decodeURIComponent(match[1]) : null;
}

function navigate(id) {
  window.location.hash = `#/scenario/${encodeURIComponent(id)}`;
  if (window.matchMedia('(width <= 1024px)').matches) setNav(false);
}

function route() {
  const id = currentScenarioId();
  if (!id) {
    scenarioView.renderWelcome(catalog, infraState);
  } else {
    const scenario = allScenarios().find((item) => item.id === id);
    if (scenario) scenarioView.render(scenario);
    else scenarioView.renderMissing(id);
  }
  renderSidebar(sidebarFilter.value);
}

window.addEventListener('hashchange', route);

// ---------------------------------------------------------------- command palette

const palette = document.getElementById('palette');
const paletteInput = document.getElementById('palette-input');
const paletteList = document.getElementById('palette-list');
let paletteIndex = 0;
let paletteMatches = [];

function openPalette() {
  palette.hidden = false;
  paletteInput.value = '';
  renderPalette('');
  paletteInput.focus();
}

function closePalette() {
  palette.hidden = true;
}

function renderPalette(query) {
  const filter = query.trim().toLowerCase();
  paletteMatches = allScenarios()
    .filter(
      (scenario) =>
        !filter ||
        scenario.title.toLowerCase().includes(filter) ||
        scenario.id.toLowerCase().includes(filter) ||
        scenario.packageId.toLowerCase().includes(filter),
    )
    .slice(0, 40);

  paletteIndex = 0;
  clear(paletteList);

  if (paletteMatches.length === 0) {
    paletteList.append(emptyState('Eşleşme yok'));
    return;
  }

  paletteMatches.forEach((scenario, index) => {
    paletteList.append(
      el(
        'button',
        {
          class: 'palette__item',
          type: 'button',
          role: 'option',
          'aria-selected': String(index === paletteIndex),
          onClick: () => {
            closePalette();
            navigate(scenario.id);
          },
        },
        el('span', { class: 'badge badge--accent' }, scenario.pkg.title),
        scenario.title,
        el('small', {}, scenario.id),
      ),
    );
  });
}

function movePalette(delta) {
  if (paletteMatches.length === 0) return;
  paletteIndex = (paletteIndex + delta + paletteMatches.length) % paletteMatches.length;
  [...paletteList.children].forEach((child, index) => {
    child.setAttribute?.('aria-selected', String(index === paletteIndex));
    if (index === paletteIndex) child.scrollIntoView({ block: 'nearest' });
  });
}

paletteInput.addEventListener('input', () => renderPalette(paletteInput.value));

paletteInput.addEventListener('keydown', (event) => {
  if (event.key === 'ArrowDown') {
    event.preventDefault();
    movePalette(1);
  } else if (event.key === 'ArrowUp') {
    event.preventDefault();
    movePalette(-1);
  } else if (event.key === 'Enter') {
    event.preventDefault();
    const scenario = paletteMatches[paletteIndex];
    if (scenario) {
      closePalette();
      navigate(scenario.id);
    }
  }
});

palette.addEventListener('click', (event) => {
  if (event.target === palette) closePalette();
});

document.getElementById('palette-open').addEventListener('click', openPalette);

document.addEventListener('keydown', (event) => {
  const inField = ['INPUT', 'TEXTAREA', 'SELECT'].includes(event.target.tagName);

  if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
    event.preventDefault();
    palette.hidden ? openPalette() : closePalette();
    return;
  }

  if (event.key === 'Escape' && !palette.hidden) {
    closePalette();
    return;
  }

  if ((event.metaKey || event.ctrlKey) && event.key === 'Enter') {
    const form = document.getElementById('scenario-form');
    if (form) {
      event.preventDefault();
      form.requestSubmit();
    }
    return;
  }

  if (event.key === '/' && !inField && palette.hidden) {
    event.preventDefault();
    sidebarFilter.focus();
  }
});

// ---------------------------------------------------------------- log dock

const dock = document.getElementById('dock');
const dockToggle = document.getElementById('dock-toggle');
const dockCaret = document.getElementById('dock-caret');
const dockGrip = document.getElementById('dock-grip');

dockToggle.addEventListener('click', () => {
  const collapsed = dock.dataset.collapsed === 'true';
  dock.dataset.collapsed = String(!collapsed);
  dockCaret.textContent = collapsed ? '▾' : '▸';
  dockToggle.setAttribute('aria-expanded', String(collapsed));
});

let dragging = false;
dockGrip.addEventListener('pointerdown', (event) => {
  dragging = true;
  dockGrip.setPointerCapture(event.pointerId);
});

dockGrip.addEventListener('pointermove', (event) => {
  if (!dragging) return;
  const height = Math.min(Math.max(window.innerHeight - event.clientY, 80), window.innerHeight * 0.8);
  dock.style.height = `${height}px`;
});

dockGrip.addEventListener('pointerup', (event) => {
  dragging = false;
  dockGrip.releasePointerCapture(event.pointerId);
  localStorage.setItem('pinqponq.playground.dock', dock.style.height);
});

dockGrip.addEventListener('keydown', (event) => {
  const step = event.shiftKey ? 60 : 20;
  if (event.key !== 'ArrowUp' && event.key !== 'ArrowDown') return;
  event.preventDefault();
  const current = dock.getBoundingClientRect().height;
  const next = event.key === 'ArrowUp' ? current + step : current - step;
  dock.style.height = `${Math.min(Math.max(next, 80), window.innerHeight * 0.8)}px`;
});

const savedDockHeight = localStorage.getItem('pinqponq.playground.dock');
if (savedDockHeight) dock.style.height = savedDockHeight;

// ---------------------------------------------------------------- mobile nav

function setNav(open) {
  appNode.dataset.nav = open ? 'open' : 'closed';
  document.getElementById('nav-toggle').setAttribute('aria-expanded', String(open));
}

document.getElementById('nav-toggle').addEventListener('click', () => {
  setNav(appNode.dataset.nav !== 'open');
});

// ---------------------------------------------------------------- boot

async function refreshCatalog() {
  try {
    catalog = await api.catalog();
    route();
  } catch (error) {
    toast(error.message, 'err');
  }
}

async function boot() {
  await refreshCatalog();
  await infraBar.start();
  await logConsole.start();
}

boot().catch((error) => {
  clear(document.getElementById('main-inner')).append(
    emptyState('Konsol başlatılamadı', error.message),
  );
});
