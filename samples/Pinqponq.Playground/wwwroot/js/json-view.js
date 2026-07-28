import { el } from './ui.js';

/**
 * Renders a value as collapsible, syntax-coloured JSON.
 *
 * The point of this console is reading raw structured output — log state bags, error
 * bodies, stored token records — so values are shown with their types visible rather than
 * flattened into text.
 */
export function jsonView(value, { expandDepth = 2 } = {}) {
  const root = el('div', { class: 'json' });
  root.append(render(value, expandDepth, 0));
  return root;
}

function render(value, expandDepth, depth) {
  if (value === null) return el('span', { class: 'json__null' }, 'null');
  if (value === undefined) return el('span', { class: 'json__null' }, 'undefined');

  const type = typeof value;
  if (type === 'string') return el('span', { class: 'json__string' }, JSON.stringify(value));
  if (type === 'number') return el('span', { class: 'json__number' }, String(value));
  if (type === 'boolean') return el('span', { class: 'json__bool' }, String(value));

  if (Array.isArray(value)) {
    return renderContainer(value.map((item, index) => [index, item]), '[', ']', expandDepth, depth, true);
  }

  return renderContainer(Object.entries(value), '{', '}', expandDepth, depth, false);
}

function renderContainer(entries, open, close, expandDepth, depth, isArray) {
  const wrapper = el('span', {});

  if (entries.length === 0) {
    wrapper.append(el('span', { class: 'json__punct' }, `${open}${close}`));
    return wrapper;
  }

  const body = el('div', { style: `padding-left:${depth === 0 ? 0 : 14}px` });
  const expanded = depth < expandDepth;

  const toggle = el(
    'button',
    {
      class: 'json__toggle',
      type: 'button',
      'aria-expanded': String(expanded),
      title: 'Aç/kapat',
    },
    expanded ? '▾' : '▸',
  );

  const summary = el(
    'span',
    { class: 'json__punct' },
    ` ${open} ${entries.length} ${isArray ? 'öğe' : 'alan'} ${close}`,
  );
  summary.hidden = expanded;

  toggle.addEventListener('click', () => {
    const nowExpanded = toggle.getAttribute('aria-expanded') !== 'true';
    toggle.setAttribute('aria-expanded', String(nowExpanded));
    toggle.textContent = nowExpanded ? '▾' : '▸';
    body.hidden = !nowExpanded;
    summary.hidden = nowExpanded;
  });

  for (const [key, item] of entries) {
    const row = el('span', { class: 'json__row' });
    row.append(
      el('span', { class: 'json__key' }, isArray ? `${key}` : `${key}`),
      el('span', { class: 'json__punct' }, ': '),
      render(item, expandDepth, depth + 1),
    );
    body.append(row);
  }

  body.hidden = !expanded;
  wrapper.append(toggle, summary, body);
  return wrapper;
}
