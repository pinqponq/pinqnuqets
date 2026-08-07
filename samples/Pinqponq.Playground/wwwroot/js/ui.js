/** Small DOM helpers. Everything is built with createElement so no string ever reaches
 *  innerHTML — scenario output includes exception messages and server values. */

export function el(tag, attrs = {}, ...children) {
  const node = document.createElement(tag);

  for (const [key, value] of Object.entries(attrs)) {
    if (value === null || value === undefined || value === false) continue;
    if (key === 'class') node.className = value;
    else if (key === 'text') node.textContent = value;
    else if (key === 'html') throw new Error('innerHTML is not used');
    else if (key.startsWith('on') && typeof value === 'function') {
      node.addEventListener(key.slice(2).toLowerCase(), value);
    } else if (key === 'dataset') {
      Object.assign(node.dataset, value);
    } else if (value === true) {
      node.setAttribute(key, '');
    } else {
      node.setAttribute(key, value);
    }
  }

  for (const child of children.flat()) {
    if (child === null || child === undefined || child === false) continue;
    node.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }

  return node;
}

export function clear(node) {
  node.replaceChildren();
  return node;
}

export function formatMs(value) {
  if (value === null || value === undefined) return '—';
  if (value < 1000) return `${Math.round(value)} ms`;
  return `${(value / 1000).toFixed(value < 10000 ? 2 : 1)} s`;
}

export function formatTime(iso) {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '--:--:--';
  const pad = (value, size = 2) => String(value).padStart(size, '0');
  return `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}.${pad(
    date.getMilliseconds(),
    3,
  )}`;
}

const SERVICE_STATE_LABELS = {
  Stopped: 'stopped',
  Starting: 'starting',
  Ready: 'ready',
  Failed: 'failed',
  DockerUnavailable: 'Docker unavailable',
  External: 'external',
};

export function serviceStateLabel(state) {
  return SERVICE_STATE_LABELS[state] ?? state;
}

const RUN_STATUS = {
  passed: { label: 'passed', badge: 'badge--ok' },
  failed: { label: 'failed', badge: 'badge--err' },
  skipped: { label: 'skipped', badge: 'badge--warn' },
};

export function runStatus(status) {
  return RUN_STATUS[status] ?? { label: status, badge: '' };
}

export function toast(message, variant = '') {
  const host = document.getElementById('toasts');
  const node = el('div', { class: `toast ${variant ? `toast--${variant}` : ''}` }, message);
  host.append(node);
  setTimeout(() => {
    node.style.opacity = '0';
    setTimeout(() => node.remove(), 200);
  }, 4200);
}

export async function copyToClipboard(value, label = 'Copied') {
  try {
    await navigator.clipboard.writeText(value);
    toast(label, 'ok');
  } catch {
    // Clipboard access needs a secure context; fall back to a selectable prompt.
    toast('Could not copy to clipboard — select the text manually.', 'err');
  }
}

export function copyButton(getValue) {
  return el(
    'button',
    {
      class: 'copy-btn',
      type: 'button',
      title: 'Copy to clipboard',
      onClick: () => copyToClipboard(getValue()),
    },
    'copy',
  );
}

export function emptyState(title, detail) {
  return el('div', { class: 'empty' }, el('div', { class: 'empty__title' }, title), detail && el('div', {}, detail));
}
