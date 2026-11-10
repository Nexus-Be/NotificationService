/** Gateway collection URL, e.g. https://api.example/api/v1/notifications. */
export function createNotificationClient({ baseUrl, getAccessToken, fetchImpl = globalThis.fetch }) {
  const base = new URL(baseUrl);
  if (!['http:', 'https:'].includes(base.protocol) || base.username || base.password || base.search || base.hash)
    throw new TypeError('Notification base URL must be an HTTP collection URL without credentials or query.');
  if (typeof getAccessToken !== 'function' || typeof fetchImpl !== 'function') throw new TypeError('Session token provider and fetch are required.');
  const root = base.href.replace(/\/$/, '');
  async function request(path = '', { method = 'GET', body, signal, query } = {}) {
    const token = await getAccessToken();
    if (typeof token !== 'string' || !token.trim()) throw new NotificationApiError(401, 'UNAUTHORIZED', 'An authenticated session is required.');
    const url = new URL(root + path);
    for (const [key, value] of Object.entries(query ?? {})) if (value !== undefined && value !== null) url.searchParams.set(key, String(value));
    const headers = { Authorization: `Bearer ${token}` };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetchImpl(url.href, { method, headers, credentials: 'omit', cache: 'no-store', signal, ...(body !== undefined ? { body: JSON.stringify(body) } : {}) });
    if (response.status === 204) return;
    let payload;
    try { payload = await response.json(); }
    catch { throw new NotificationApiError(response.ok ? 502 : response.status, 'INVALID_RESPONSE', 'Notification API returned an invalid JSON response.'); }
    if (!response.ok || payload?.success === false) {
      const status = response.ok ? (payload.statusCode >= 400 ? payload.statusCode : 502) : response.status;
      throw new NotificationApiError(status, payload?.errorCode ?? payload?.errors?.[0]?.code ?? 'NOTIFICATION_REQUEST_FAILED', payload?.detail ?? payload?.message ?? 'Notification request failed.');
    }
    return payload && typeof payload.success === 'boolean' && Object.hasOwn(payload, 'data') ? payload.data : payload;
  }
  function segment(value) {
    if (typeof value !== 'string' || !value.trim()) throw new TypeError('A resource identifier is required.');
    return encodeURIComponent(value);
  }
  function list({ page = 1, pageSize = 20, isRead, notificationType, signal } = {}) {
    if (!Number.isInteger(page) || page < 1 || !Number.isInteger(pageSize) || pageSize < 1 || pageSize > 100) throw new RangeError('Invalid notification pagination.');
    if (isRead !== undefined && typeof isRead !== 'boolean') throw new TypeError('Read state must be boolean.');
    return request('/me', { signal, query: { page, pageSize, isRead, notificationType } });
  }
  return Object.freeze({
    list,
    async unreadCount({ signal } = {}) { return (await list({ page: 1, pageSize: 1, isRead: false, signal })).pagination.totalCount; },
    detail: (id, { signal } = {}) => request('/' + segment(id), { signal }),
    registerDevice: ({ token, previousToken }, { signal } = {}) => request('/devices', { method: 'POST', body: { token, platform: 'Web', ...(previousToken ? { previousToken } : {}) }, signal }),
    revokeDevice: (token, { signal } = {}) => request('/devices/' + segment(token), { method: 'DELETE', signal }),
    setRead: (id, isRead, { signal } = {}) => {
      if (typeof isRead !== 'boolean') throw new TypeError('Read state must be boolean.');
      return request('/' + segment(id) + '/read-states', { method: 'PUT', body: { isRead }, signal });
    },
    markAllRead: ({ signal } = {}) => request('/me/read', { method: 'PATCH', body: {}, signal }),
    preferences: ({ signal } = {}) => request('/preferences', { signal }),
    updatePreferences: (preferences, { signal } = {}) => request('/preferences', { method: 'PUT', body: { preferences }, signal })
  });
}

export class NotificationApiError extends Error {
  constructor(status, code, message) { super(message); this.name = 'NotificationApiError'; this.status = status; this.code = code; }
}
