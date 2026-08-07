/** Thin wrapper over the console's own API. Every failure surfaces as an Error with a
 *  message worth showing the user — the backend answers with the standard error body
 *  produced by Pinqponq.ErrorHandling. */

async function request(path, options = {}) {
  let response;
  try {
    response = await fetch(path, options);
  } catch (cause) {
    throw new Error('Could not reach the server. Is the app still running?', { cause });
  }

  if (response.status === 204) return null;

  const text = await response.text();
  let payload = null;
  if (text) {
    try {
      payload = JSON.parse(text);
    } catch {
      payload = text;
    }
  }

  if (!response.ok) {
    const message =
      (payload && (payload.message || payload.reason)) || `Request failed (${response.status})`;
    const error = new Error(message);
    error.status = response.status;
    error.payload = payload;
    throw error;
  }

  return payload;
}

export const api = {
  catalog: () => request('/api/catalog'),
  infra: () => request('/api/infra'),
  probeDocker: () => request('/api/infra/probe', { method: 'POST' }),
  startService: (id) => request(`/api/infra/${id}/start`, { method: 'POST' }),
  stopService: (id) => request(`/api/infra/${id}/stop`, { method: 'POST' }),
  restartService: (id) => request(`/api/infra/${id}/restart`, { method: 'POST' }),

  run: (id, input) =>
    request(`/api/scenarios/${id}/run`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ input }),
    }),

  logs: (params = {}) => {
    const query = new URLSearchParams(
      Object.entries(params).filter(([, value]) => value !== undefined && value !== ''),
    );
    return request(`/api/logs?${query}`);
  },
  clearLogs: () => request('/api/logs', { method: 'DELETE' }),

  mail: () => request('/api/mail'),
  sms: () => request('/api/sms'),
};

/**
 * Subscribes to one of the server-sent event streams.
 *
 * EventSource is used rather than a WebSocket: the data only ever flows server to client,
 * it reconnects on its own, and it needs no library — which keeps this page buildless.
 */
export function subscribe(path, eventName, onMessage, onError) {
  const source = new EventSource(path);

  source.addEventListener(eventName, (event) => {
    try {
      onMessage(JSON.parse(event.data));
    } catch {
      // A partially delivered frame is not worth surfacing; the next one will arrive.
    }
  });

  source.addEventListener('error', () => {
    // EventSource retries by itself; only report once it is actually closed.
    if (source.readyState === EventSource.CLOSED && onError) onError();
  });

  return () => source.close();
}
