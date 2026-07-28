import { api } from './api.js';
import { jsonView } from './json-view.js';
import { clear, copyButton, el, emptyState, formatMs, runStatus, toast } from './ui.js';

/**
 * The scenario pane: a form generated from the scenario's declared fields, a run button,
 * and the outcome — steps, artifacts, and a jump into the logs the run produced.
 */
export function createScenarioView({ onStatusChange, onShowLogs }) {
  const host = document.getElementById('main-inner');
  let current = null;
  let running = false;

  function renderWelcome(catalog, infra) {
    clear(host);

    const scenarioCount = catalog.packages.reduce((total, p) => total + p.scenarios.length, 0);
    const availableCount = catalog.packages.reduce(
      (total, p) => total + p.scenarios.filter((s) => s.available).length,
      0,
    );

    const cards = el('div', { class: 'stack' });

    if (!infra.dockerAvailable) {
      cards.append(
        el(
          'div',
          { class: 'banner banner--warn' },
          el('span', { class: 'banner__icon' }, '⚠'),
          el(
            'div',
            {},
            el('div', { class: 'banner__title' }, 'Docker çalışmıyor'),
            el(
              'div',
              { class: 'banner__text' },
              `${infra.dockerError ?? 'Daemon yanıt vermedi'}. Konteyner gerektiren senaryolar bekleyecek; ` +
                `${availableCount} senaryo yine de çalıştırılabilir. Docker'ı başlattıktan sonra üstteki bir servise tıklayıp "Başlat" deyin.`,
            ),
          ),
        ),
      );
    }

    cards.append(
      el(
        'div',
        { class: 'card' },
        el('div', { class: 'card__head' }, el('h2', { class: 'card__title' }, 'Pinqponq Playground')),
        el(
          'div',
          { class: 'card__body stack' },
          el(
            'p',
            { style: 'color:var(--text-muted);max-width:68ch' },
            'Bu konsol reponun 13 paketini gerçek bağımlılıklara karşı çalıştırır ve her ' +
              'çalıştırmanın ürettiği yapılandırılmış log kayıtlarını sonucun yanında gösterir. ' +
              'Soldan bir senaryo seçin; options alanlarını değiştirip davranışın nasıl değiştiğini görün.',
          ),
          el(
            'div',
            { class: 'row' },
            el('span', { class: 'badge badge--accent' }, `${catalog.packages.length} paket`),
            el('span', { class: 'badge' }, `${scenarioCount} senaryo`),
            el('span', { class: 'badge badge--ok' }, `${availableCount} şu an çalıştırılabilir`),
          ),
          el(
            'div',
            { class: 'row' },
            el('span', { style: 'color:var(--text-faint);font-size:var(--fs-xs)' }, 'İpuçları:'),
            el('kbd', {}, 'Ctrl K'),
            el('span', { style: 'color:var(--text-faint);font-size:var(--fs-xs)' }, 'senaryo ara'),
            el('kbd', {}, 'Ctrl ↵'),
            el('span', { style: 'color:var(--text-faint);font-size:var(--fs-xs)' }, 'çalıştır'),
          ),
        ),
      ),
    );

    const groups = new Map();
    for (const pkg of catalog.packages) {
      if (!groups.has(pkg.group)) groups.set(pkg.group, []);
      groups.get(pkg.group).push(pkg);
    }

    for (const [group, packages] of groups) {
      cards.append(
        el(
          'div',
          { class: 'card' },
          el('div', { class: 'card__head' }, el('h3', { class: 'card__title' }, group)),
          el(
            'div',
            { class: 'card__body stack' },
            packages.map((pkg) =>
              el(
                'div',
                {},
                el(
                  'div',
                  { class: 'row', style: 'margin-bottom:2px' },
                  el('strong', { style: 'font-size:var(--fs-sm)' }, pkg.id),
                  el('span', { class: 'badge' }, `${pkg.scenarios.length} senaryo`),
                ),
                el('div', { style: 'color:var(--text-muted);font-size:var(--fs-sm)' }, pkg.summary),
              ),
            ),
          ),
        ),
      );
    }

    host.append(cards);
  }

  function render(scenario) {
    current = scenario;
    clear(host);

    const badges = el('div', { class: 'scenario-head__eyebrow' });
    badges.append(el('span', { class: 'badge badge--accent' }, scenario.packageId));
    if (scenario.negativePath) badges.append(el('span', { class: 'badge badge--warn' }, 'negatif yol'));
    if (scenario.needsInternet) badges.append(el('span', { class: 'badge badge--info' }, 'internet gerekir'));
    for (const service of scenario.requiredServices) {
      const ready = !scenario.blockedBy.includes(service);
      badges.append(
        el('span', { class: `badge ${ready ? 'badge--ok' : 'badge--err'}` }, `${service}${ready ? '' : ' • hazır değil'}`),
      );
    }

    const head = el(
      'header',
      { class: 'scenario-head' },
      badges,
      el('h1', { class: 'scenario-head__title' }, scenario.title),
      el('p', { class: 'scenario-head__summary' }, scenario.summary),
    );

    const form = el('form', { class: 'card', id: 'scenario-form' });
    const fields = el('div', { class: 'form-grid' });

    for (const field of scenario.fields) fields.append(renderField(field));

    const runButton = el(
      'button',
      { class: 'btn btn--primary btn--lg', type: 'submit', disabled: !scenario.available },
      'Çalıştır',
    );

    const runBar = el(
      'div',
      { class: 'run-bar' },
      runButton,
      !scenario.available
        ? el('span', { class: 'badge badge--err' }, `Gereken servis hazır değil: ${scenario.blockedBy.join(', ')}`)
        : null,
      el('span', { class: 'run-bar__hint' }, el('kbd', {}, 'Ctrl ↵')),
    );

    form.append(
      el(
        'div',
        { class: 'card__body' },
        scenario.fields.length > 0
          ? fields
          : el('div', { style: 'color:var(--text-faint);font-size:var(--fs-sm)' }, 'Bu senaryonun ayarlanabilir alanı yok.'),
        runBar,
      ),
    );

    const resultHost = el('div', { id: 'result-host' });

    form.addEventListener('submit', (event) => {
      event.preventDefault();
      void run(scenario, form, runButton, resultHost);
    });

    host.append(head, form, resultHost);
  }

  function renderField(field) {
    const id = `field-${field.name}`;
    const wide = field.kind === 'MultilineText';
    const wrapper = el('div', { class: `field${wide ? ' field--wide' : ''}` });

    wrapper.append(el('label', { class: 'field__label', for: id }, field.label));

    let control;
    switch (field.kind) {
      case 'MultilineText':
        control = el('textarea', { class: 'textarea', id, name: field.name }, field.default ?? '');
        break;
      case 'Enum':
        control = el(
          'select',
          { class: 'select', id, name: field.name },
          (field.choices ?? []).map((choice) =>
            el('option', { value: choice, selected: choice === field.default }, choice),
          ),
        );
        break;
      case 'Bool':
        control = el(
          'label',
          { class: 'switch' },
          el('input', { type: 'checkbox', id, name: field.name, checked: field.default === 'true' }),
          el('span', { class: 'switch__track' }),
        );
        break;
      case 'Number':
      case 'Duration':
        control = el('input', {
          class: 'input input--mono',
          type: 'number',
          id,
          name: field.name,
          value: field.default ?? '',
        });
        break;
      case 'Password':
        control = el('input', {
          class: 'input input--mono',
          type: 'text',
          id,
          name: field.name,
          value: field.default ?? '',
          autocomplete: 'off',
          spellcheck: 'false',
        });
        break;
      default:
        control = el('input', {
          class: 'input',
          type: 'text',
          id,
          name: field.name,
          value: field.default ?? '',
          placeholder: field.required ? 'zorunlu' : '',
          autocomplete: 'off',
        });
    }

    wrapper.append(control);
    if (field.help) wrapper.append(el('div', { class: 'field__help' }, field.help));
    return wrapper;
  }

  function collectInput(form) {
    const input = {};
    for (const element of form.elements) {
      if (!element.name) continue;
      input[element.name] = element.type === 'checkbox' ? String(element.checked) : element.value;
    }
    return input;
  }

  async function run(scenario, form, button, resultHost) {
    if (running) return;
    running = true;
    button.disabled = true;
    button.textContent = 'Çalışıyor…';
    onStatusChange?.(scenario.id, 'running');

    clear(resultHost).append(
      el(
        'div',
        { class: 'card result', style: 'margin-top:var(--s-5)' },
        el('div', { class: 'card__body stack' },
          el('div', { class: 'skeleton', style: 'height:14px;width:40%' }),
          el('div', { class: 'skeleton', style: 'height:14px;width:70%' }),
          el('div', { class: 'skeleton', style: 'height:14px;width:55%' })),
      ),
    );

    try {
      const result = await api.run(scenario.id, collectInput(form));
      renderResult(result, resultHost);
      onStatusChange?.(scenario.id, result.status);
    } catch (error) {
      clear(resultHost).append(
        el(
          'div',
          { class: 'card result', dataset: { status: 'failed' } },
          el('div', { class: 'card__body' }, el('div', { class: 'error-box' }, error.message)),
        ),
      );
      toast(error.message, 'err');
      onStatusChange?.(scenario.id, 'failed');
    } finally {
      running = false;
      button.disabled = !scenario.available;
      button.textContent = 'Çalıştır';
    }
  }

  function renderResult(result, resultHost) {
    const status = runStatus(result.status);

    const card = el('div', { class: 'card result', dataset: { status: result.status } });

    card.append(
      el(
        'div',
        { class: 'card__head' },
        el('span', { class: `badge ${status.badge}` }, status.label),
        el('span', { class: 'card__title' }, 'Sonuç'),
        el(
          'div',
          { class: 'result__meta' },
          el('span', {}, formatMs(result.durationMs)),
          el('span', {}, `${result.steps.length} adım`),
          el(
            'button',
            {
              class: 'btn btn--ghost',
              type: 'button',
              onClick: () => onShowLogs?.(result.runId),
            },
            `${result.logs.length} log →`,
          ),
        ),
      ),
    );

    const bodyStack = el('div', { class: 'card__body stack' });

    if (result.error) {
      bodyStack.append(
        el('div', { class: `error-box${result.status === 'skipped' ? ' error-box--warn' : ''}` }, result.error),
      );
    }

    if (result.steps.length > 0) {
      const steps = el('div', { class: 'steps' });
      for (const step of result.steps) {
        steps.append(
          el(
            'div',
            { class: 'step', dataset: { ok: String(step.ok) } },
            el('span', { class: 'step__icon' }, step.ok ? '✔' : '✕'),
            el('span', {}, step.title),
            step.detail ? el('span', { class: 'step__detail' }, step.detail) : null,
            el('span', { class: 'step__time' }, `${step.elapsedMs} ms`),
          ),
        );
      }
      bodyStack.append(el('div', {}, el('div', { class: 'section-title' }, 'Adımlar'), steps));
    }

    if (result.artifacts.length > 0) {
      const artifacts = el('div', {});
      for (const artifact of result.artifacts) artifacts.append(renderArtifact(artifact));
      bodyStack.append(el('div', {}, el('div', { class: 'section-title' }, 'Çıktılar'), artifacts));
    }

    card.append(bodyStack);
    clear(resultHost).append(card);
    card.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  }

  function renderArtifact(artifact) {
    const body = el('div', { class: 'artifact__body' });

    if (artifact.value === null || artifact.value === undefined) {
      body.append(el('div', { class: 'artifact__text', style: 'color:var(--text-faint)' }, '(boş)'));
    } else if (artifact.kind === 'table' && Array.isArray(artifact.value) && artifact.value.length > 0) {
      body.append(renderTable(artifact.value));
    } else if (artifact.kind === 'text' || artifact.kind === 'token' || artifact.kind === 'uri') {
      body.append(el('div', { class: 'artifact__text' }, String(artifact.value)));
    } else {
      body.append(jsonView(artifact.value, { expandDepth: 2 }));
    }

    return el(
      'div',
      { class: 'artifact' },
      el(
        'div',
        { class: 'artifact__head' },
        artifact.name,
        copyButton(() =>
          typeof artifact.value === 'string' ? artifact.value : JSON.stringify(artifact.value, null, 2),
        ),
      ),
      body,
    );
  }

  // Column keys arrive as camelCase identifiers; split them so headers stay readable.
  function humanize(key) {
    return key.replace(/([a-zçğıöşü])([A-ZÇĞİÖŞÜ])/g, '$1 $2').toLowerCase();
  }

  function renderTable(rows) {
    const columns = [...new Set(rows.flatMap((row) => Object.keys(row)))];
    return el(
      'div',
      { class: 'table-wrap' },
      el(
        'table',
        { class: 'data' },
        el('thead', {}, el('tr', {}, columns.map((column) => el('th', {}, humanize(column))))),
        el(
          'tbody',
          {},
          rows.map((row) =>
            el(
              'tr',
              {},
              columns.map((column) =>
                el('td', {}, row[column] === null || row[column] === undefined ? '—' : String(row[column])),
              ),
            ),
          ),
        ),
      ),
    );
  }

  function renderMissing(id) {
    clear(host).append(emptyState('Senaryo bulunamadı', id));
  }

  return { render, renderWelcome, renderMissing, get current() { return current; } };
}
