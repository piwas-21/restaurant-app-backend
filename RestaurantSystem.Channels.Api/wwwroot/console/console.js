'use strict';
const el = id => document.getElementById(id);
const notice = (text, error = false) => {
  el('notice').textContent = text;
  el('notice').classList.toggle('error', error);
};
let currentOrder = '';
let importsCursor = '';
let nextImportsCursor = '';
let busy = false;
let loggedIn = false;
async function api(path, body) {
  const response = await fetch('/api/sandbox/' + path, {
    method: body === undefined ? 'GET' : 'POST', credentials: 'same-origin', cache: 'no-store',
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(30000)
  });
  const value = response.status === 204 ? {} : await response.json().catch(() => ({}));
  if (response.status === 401 && path !== 'auth/login') { showLogin(); }
  if (!response.ok) { throw new Error(value.message || 'The sandbox request failed. Refresh status before retrying.'); }
  return value;
}
function showLogin() {
  loggedIn = false; currentOrder = ''; importsCursor = ''; nextImportsCursor = '';
  el('imports').replaceChildren(); el('open-staff-orders').hidden = true;
  el('workspace').hidden = true; el('order-panel').hidden = true;
  el('order-json').value = ''; el('login-panel').hidden = false;
}
async function work(action) {
  if (busy) { return; }
  busy = true; document.querySelectorAll('button').forEach(b => b.disabled = true);
  try { await action(); } catch (error) { notice(error.message, true); }
  finally { busy = false; document.querySelectorAll('button').forEach(b => b.disabled = false); }
}
function bind(id, action) { el(id).addEventListener('click', () => { void work(action); }); }
function managerLabel(config) {
  if (config.pending) { return 'Awaiting Uber promotion'; }
  return config.orderManager ? 'Sofra' : 'Not enabled';
}
function acceptanceLabel(config) {
  if (config.manualAcceptance === null) { return 'Not confirmed by Uber'; }
  return config.manualAcceptance ? 'Required before notification' : 'Console decision';
}
function renderConfig(config) {
  const rows = [['Test store', config.storeId], ['Test client', config.clientId], ['Order delivery', config.enabled ? 'Enabled' : 'Paused'],
    ['Order manager', managerLabel(config)], ['Tablet acceptance', acceptanceLabel(config)]];
  el('configuration').replaceChildren();
  for (const [label, value] of rows) {
    const dt = document.createElement('dt'); const dd = document.createElement('dd');
    dt.textContent = label; dd.textContent = value; el('configuration').append(dt, dd);
  }
  el('connection-badge').textContent = config.orderManager && !config.pending ? 'Order manager confirmed' : 'Store connected';
}
async function receipts() {
  const rows = await api('uber/receipts');
  el('receipts').replaceChildren(); el('empty-receipts').hidden = rows.length > 0;
  el('empty-receipts').textContent = 'No signed notifications for this store yet.';
  for (const row of rows) {
    const li = document.createElement('li'); const text = document.createElement('span');
    text.textContent = row.EventType + ' · ' + new Date(row.ReceivedAt).toLocaleString(); li.append(text);
    if (row.EventType.startsWith('orders.') && row.ResourceId) {
      const button = document.createElement('button'); button.className = 'secondary'; button.textContent = 'Review order';
      button.addEventListener('click', () => { void work(() => readOrder(row.ResourceId)); }); li.append(button);
    }
    el('receipts').append(li);
  }
}
async function stock() {
  const status = await api('uber/availability');
  let message = 'Stock synchronization is not enabled.';
  if (status.enabled) { message = 'Stock synchronization enabled.'; }
  else if (status.paused) { message = 'Stock synchronization paused.'; }
  el('stock-status').textContent = message;
  el('stock-items').replaceChildren();
  for (const row of status.items) {
    const li = document.createElement('li');
    const checked = row.verifiedAt ? new Date(row.verifiedAt).toLocaleString() : 'Not verified';
    const state = row.state === 'Verified' && !row.fresh ? 'Verification is stale' : row.state;
    li.textContent = row.itemId + ' · Sofra: ' + (row.desiredAvailable ? 'Available' : 'Unavailable') +
      ' · ' + state + ' · Last verified: ' + checked + ' · ' + row.sourceReason;
    el('stock-items').append(li);
  }
  if (!status.items.length) {
    const li = document.createElement('li'); li.textContent = 'No availability observations yet.'; el('stock-items').append(li);
  }
}
async function imports(cursor = '') {
  const status = await api('uber/imports' + (cursor ? '?cursor=' + encodeURIComponent(cursor) : ''));
  importsCursor = cursor; nextImportsCursor = status.nextCursor || '';
  el('open-staff-orders').hidden = true;
  if (status.enabled && status.tenantUrl) {
    const origin = new URL(status.tenantUrl);
    if (origin.protocol !== 'https:' || origin.username || origin.password || origin.pathname !== '/') {
      throw new Error('The staff destination is not a reviewed tenant origin.');
    }
    el('open-staff-orders').href = new URL('/admin/orders-management', origin).href;
    el('open-staff-orders').hidden = false;
  }
  el('more-imports').hidden = !nextImportsCursor;
  let message = 'Import status refreshed. Refresh to see new or changed jobs.';
  if (!status.enabled) { message = 'Forwarding is not enabled.'; }
  else if (status.paused) { message = 'Forwarding is paused; retained jobs remain visible.'; }
  el('imports-status').textContent = message;
  el('imports').replaceChildren();
  for (const row of status.items) {
    const li = document.createElement('li'); const text = document.createElement('span');
    let state = row.state;
    if (row.reviewRequired) { state = 'Delivery unconfirmed — requires review'; } else if (row.retrying) { state = 'Retrying'; }
    text.textContent = row.orderId + ' · ' + state +
      ' · Attempts: ' + row.attempts + (row.code ? ' · ' + row.code : '') +
      ' · Updated: ' + new Date(row.updatedAt).toLocaleString() + (row.tenantOrderId ? ' · Sofra order: ' + row.tenantOrderId : '');
    li.append(text);
    if (row.reviewRequired) {
      const button = document.createElement('button'); button.className = 'secondary'; button.textContent = 'Review provider order';
      button.addEventListener('click', () => { void work(async () => {
        await readOrder(row.orderId); notice('Delivery to Sofra is unconfirmed. Check existing Sofra and Uber handling before any fallback decision to avoid duplicate preparation. Review all customer instructions.');
      }); }); li.append(button);
    }
    el('imports').append(li);
  }
  if (!status.items.length) {
    const li = document.createElement('li'); li.textContent = 'No import jobs on this review page.'; el('imports').append(li);
  }
}
async function refresh() {
  await receipts(); await stock(); await imports();
  try { renderConfig(await api('uber/configuration')); notice('Sandbox status refreshed.'); }
  catch (error) {
    el('connection-badge').textContent = 'Connect your test store'; el('configuration').replaceChildren(); throw error;
  }
}
async function openWorkspace() {
  loggedIn = true; el('login-panel').hidden = true; el('workspace').hidden = false;
  el('menu-preview').textContent = JSON.stringify(await api('uber/preview'), null, 2);
  await refresh();
}
function authorize(result) {
  const url = new URL(result.url);
  if (url.protocol !== 'https:' || url.hostname !== 'sandbox-login.uber.com') { throw new Error('Unexpected authorization destination.'); }
  window.location.assign(url.href);
}
async function readOrder(id) {
  const order = await api('uber/orders/' + encodeURIComponent(id));
  currentOrder = id; el('order-panel').hidden = false; el('order-id').textContent = id;
  el('order-state').textContent = order.current_state || 'Unknown'; el('order-json').value = JSON.stringify(order, null, 2);
  el('reviewed').checked = false; el('reason').value = '';
  el('order-panel').scrollIntoView({ behavior: 'smooth', block: 'start' });
  notice('Read all order details before choosing a decision.');
}
el('login-form').addEventListener('submit', event => {
  event.preventDefault();
  void work(async () => {
    const key = el('access-key').value; el('access-key').value = '';
    await api('auth/login', { accessKey: key }); await openWorkspace();
  });
});
bind('logout', async () => { await api('auth/logout', {}); showLogin(); notice('Signed out.'); });
bind('refresh', refresh);
bind('refresh-stock', stock);
bind('refresh-imports', () => imports());
bind('more-imports', () => imports(nextImportsCursor));
bind('connect', async () => { authorize(await api('uber/connect', {})); });
bind('publish', async () => { await api('uber/publish', {}); notice('Test menu published and verified against Uber’s readback.'); });
bind('verify-menu', async () => { await api('uber/verification'); notice('Current test menu verified against Uber’s readback.'); });
bind('read-menu', async () => {
  el('menu-result').textContent = JSON.stringify(await api('uber/menu'), null, 2);
  el('menu-readback').hidden = false; el('menu-readback').open = true; notice('Current menu retrieved from Uber.');
});
for (const [id, enable] of [['enable', true], ['pause', false]]) {
  bind(id, async () => {
    const result = await api('uber/enable', { enable });
    if (result.url) { authorize(result); return; }
    renderConfig(result); notice('Order testing paused.');
  });
}
bind('refresh-order', () => readOrder(currentOrder));
for (const action of ['accept', 'deny']) {
  bind(action, async () => {
    if (!currentOrder || !el('reviewed').checked || !el('reason').value.trim()) {
      throw new Error('Review the complete order, tick the instruction checkbox and enter a decision reason.');
    }
    const result = await api('uber/orders/' + encodeURIComponent(currentOrder) + '/decision', {
      action, reason: el('reason').value.trim(), reviewedInstructions: true
    });
    await readOrder(currentOrder); notice(result.message);
  });
}
const callbackResult = new URLSearchParams(window.location.search).get('connection');
history.replaceState(null, '', '/console/');
await work(async () => {
  try { await api('auth/session'); await openWorkspace(); }
  catch (error) { if (!loggedIn) { showLogin(); } else { throw error; } }
  if (callbackResult) {
    notice(callbackResult === 'complete' ? 'Test store authorization completed. Check status before continuing.' :
      'Authorization was not completed. Sign in and start a new connection; check that you used the test merchant account.', callbackResult !== 'complete');
  }
});
setInterval(() => { if (loggedIn && !busy && !document.hidden) { void work(async () => { await receipts(); await stock(); await imports(importsCursor); }); } }, 30000);
