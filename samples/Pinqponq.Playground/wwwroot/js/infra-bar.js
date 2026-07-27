import { api, subscribe } from './api.js';
import { clear, el, formatMs, serviceStateLabel, toast } from './ui.js';

/**
 * The service strip in the header.
 *
 * Nothing is provisioned automatically, so this is the control surface: each pill shows a
 * service's state and opens a popover to start, stop or restart it. Stopping a container
 * on purpose is how the retry and health-check scenarios are demonstrated.
 */
export function createInfraBar({ onChange }) {
  const bar = document.getElementById('infra-bar');
  let services = [];
  let dockerAvailable = false;
  let dockerError = null;
  let popover = null;

  function render() {
    clear(bar);

    for (const service of services) {
      const pill = el(
        'button',
        {
          class: 'pill',
          type: 'button',
          dataset: { state: service.state, id: service.id },
          'aria-label': `${service.displayName}: ${serviceStateLabel(service.state)}`,
          onClick: (event) => openPopover(event.currentTarget, service),
        },
        el('span', { class: 'pill__dot' }),
        service.displayName,
        service.state === 'Ready' && service.startupMs
          ? el('span', { class: 'pill__meta' }, formatMs(service.startupMs))
          : null,
        service.state !== 'Ready' && service.state !== 'External'
          ? el('span', { class: 'pill__meta' }, serviceStateLabel(service.state))
          : null,
      );
      bar.append(pill);
    }
  }

  function closePopover() {
    popover?.remove();
    popover = null;
    document.removeEventListener('click', onDocumentClick, true);
    document.removeEventListener('keydown', onEscape);
  }

  function onDocumentClick(event) {
    if (popover && !popover.contains(event.target) && !event.target.closest('.pill')) closePopover();
  }

  function onEscape(event) {
    if (event.key === 'Escape') closePopover();
  }

  function openPopover(anchor, service) {
    const alreadyOpen = popover?.dataset.id === service.id;
    closePopover();
    if (alreadyOpen) return;

    const busy = service.state === 'Starting';
    const running = service.state === 'Ready';

    const actions = el('div', { class: 'popover__actions' });

    if (service.state === 'External') {
      actions.append(el('span', { class: 'badge' }, 'Yapılandırmadan geliyor, yönetilmiyor'));
    } else {
      actions.append(
        el(
          'button',
          {
            class: 'btn btn--primary',
            type: 'button',
            disabled: busy || running,
            onClick: () => act(service.id, api.startService, 'başlatılıyor'),
          },
          'Başlat',
        ),
        el(
          'button',
          {
            class: 'btn',
            type: 'button',
            disabled: !running,
            onClick: () => act(service.id, api.stopService, 'durduruluyor'),
          },
          'Durdur',
        ),
        el(
          'button',
          {
            class: 'btn',
            type: 'button',
            disabled: !running,
            onClick: () => act(service.id, api.restartService, 'yeniden başlatılıyor'),
          },
          'Yeniden başlat',
        ),
      );
    }

    popover = el(
      'div',
      { class: 'popover', dataset: { id: service.id }, role: 'dialog', 'aria-label': service.displayName },
      el(
        'div',
        { class: 'popover__title' },
        el('span', { class: 'pill__dot', style: 'width:8px;height:8px' }),
        service.displayName,
        el('span', { class: 'badge' }, serviceStateLabel(service.state)),
      ),
      el('div', { class: 'popover__meta' }, service.description),
      el('div', { class: 'popover__meta' }, service.image),
      service.heavy
        ? el('div', { class: 'badge badge--warn' }, 'Ağır imaj (~1,5 GB), ARM64 desteklenmiyor')
        : null,
      service.connectionString
        ? el('div', { class: 'popover__meta' }, service.connectionString)
        : null,
      service.host && !service.connectionString
        ? el('div', { class: 'popover__meta' }, `${service.host}:${service.port}`)
        : null,
      service.lastError ? el('div', { class: 'error-box' }, service.lastError) : null,
      !dockerAvailable && service.state !== 'External'
        ? el('div', { class: 'error-box error-box--warn' }, `Docker kullanılamıyor: ${dockerError ?? 'bilinmiyor'}`)
        : null,
      actions,
    );

    document.body.append(popover);

    const rect = anchor.getBoundingClientRect();
    popover.style.top = `${rect.bottom + 8}px`;
    popover.style.left = `${Math.max(8, Math.min(rect.left, window.innerWidth - popover.offsetWidth - 8))}px`;

    document.addEventListener('click', onDocumentClick, true);
    document.addEventListener('keydown', onEscape);
  }

  async function act(id, call, verb) {
    closePopover();
    try {
      await call(id);
      toast(`${id} ${verb}…`);
    } catch (error) {
      toast(error.message, 'err');
    }
  }

  function applyStatus(status) {
    const index = services.findIndex((service) => service.id === status.id);
    if (index >= 0) services[index] = status;
    else services.push(status);
    render();
    onChange?.(services, { dockerAvailable, dockerError });
  }

  async function refresh() {
    const payload = await api.infra();
    services = payload.services;
    dockerAvailable = payload.dockerAvailable;
    dockerError = payload.dockerError;
    render();
    onChange?.(services, { dockerAvailable, dockerError });
  }

  async function start() {
    await refresh();
    subscribe('/api/infra/stream', 'service', applyStatus);
  }

  return { start, refresh, get services() { return services; } };
}
