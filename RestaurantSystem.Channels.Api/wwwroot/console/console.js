'use strict';
const el = id => document.getElementById(id);
const notice = (text, error = false) => { el('notice').textContent = text; el('notice').classList.toggle('error', error); };
let currentOrder = '';
let busy = false;
let loggedIn = false;
async function api(path, body) {
  const response = await fetch('/api/sandbox/' + path, {
    method: body === undefined ? 'GET' : 'POST', credentials: 'same-origin', cache: 'no-store',
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(30000)
  });
  const value = response.status === 204 ? {} : await response.json().catch(() => ({}));
  if (response.status === 401 && path !== 'auth/login') showLogin();
  if (!response.ok) throw new Error(value.message || 'The sandbox request failed. Refresh status before retrying.');
  return value;
}
function showLogin() { loggedIn = false; currentOrder = ''; el('workspace').hidden = true; el('order-panel').hidden = true; el('order-json').textContent = ''; el('login-panel').hidden = false; }
async function work(action) {
  if (busy) return;
  busy = true; document.querySelectorAll('button').forEach(b => b.disabled = true);
  try { await action(); } catch (error) { notice(error.message, true); }
  finally { busy = false; document.querySelectorAll('button').forEach(b => b.disabled = false); }
}
function renderConfig(config) {
  const rows = [['Test store', config.storeId], ['Test client', config.clientId], ['Order delivery', config.enabled ? 'Enabled' : 'Paused'],
    ['Order manager', config.pending ? 'Awaiting Uber promotion' : config.orderManager ? 'Sofra' : 'Not enabled'], ['Tablet acceptance', config.manualAcceptance === null ? 'Not confirmed by Uber' : config.manualAcceptance ? 'Required before notification' : 'Console decision']];
  el('configuration').replaceChildren();
  for (const [label, value] of rows) { const dt = document.createElement('dt'); const dd = document.createElement('dd'); dt.textContent = label; dd.textContent = value; el('configuration').append(dt, dd); }
  el('connection-badge').textContent = config.orderManager && !config.pending ? 'Order manager confirmed' : 'Store connected';
}
async function receipts() {
  const rows = await api('uber/receipts'); el('receipts').replaceChildren(); el('empty-receipts').hidden = rows.length > 0;
  el('empty-receipts').textContent = 'No signed notifications for this store yet.';
  for (const row of rows) {
    const li = document.createElement('li'); const text = document.createElement('span');
    text.textContent = row.EventType + ' · ' + new Date(row.ReceivedAt).toLocaleString(); li.append(text);
    if (row.EventType.startsWith('orders.') && row.ResourceId) { const button = document.createElement('button'); button.className = 'secondary'; button.textContent = 'Review order'; button.addEventListener('click', () => work(() => readOrder(row.ResourceId))); li.append(button); }
    el('receipts').append(li);
  }
}
async function refresh() {
  await receipts();
  try { renderConfig(await api('uber/configuration')); notice('Sandbox status refreshed.'); }
  catch (error) { el('connection-badge').textContent = 'Connect your test store'; el('configuration').replaceChildren(); throw error; }
}
async function openWorkspace() { loggedIn = true; el('login-panel').hidden = true; el('workspace').hidden = false; el('menu-preview').textContent = JSON.stringify(await api('uber/preview'), null, 2); await refresh(); }
function authorize(result) { const url = new URL(result.url); if (url.protocol !== 'https:' || url.hostname !== 'sandbox-login.uber.com') throw new Error('Unexpected authorization destination.'); window.location.assign(url.href); }
async function readOrder(id) { const order = await api('uber/orders/' + encodeURIComponent(id)); currentOrder = id; el('order-panel').hidden = false; el('order-id').textContent = id; el('order-state').textContent = order.current_state || 'Unknown'; el('order-json').textContent = JSON.stringify(order, null, 2); el('reviewed').checked = false; el('reason').value = ''; el('order-panel').scrollIntoView({ behavior: 'smooth', block: 'start' }); notice('Read all order details before choosing a decision.'); }
el('login-form').addEventListener('submit', event => { event.preventDefault(); work(async () => { const key = el('access-key').value; el('access-key').value = ''; await api('auth/login', { accessKey: key }); await openWorkspace(); }); });
el('logout').addEventListener('click', () => work(async () => { await api('auth/logout', {}); showLogin(); notice('Signed out.'); }));
el('refresh').addEventListener('click', () => work(refresh));
el('connect').addEventListener('click', () => work(async () => { authorize(await api('uber/connect', {})); }));
el('publish').addEventListener('click', () => work(async () => { await api('uber/publish', {}); notice('Test menu published and verified against Uber’s readback.'); }));
el('read-menu').addEventListener('click', () => work(async () => { el('menu-result').textContent = JSON.stringify(await api('uber/menu'), null, 2); el('menu-readback').hidden = false; el('menu-readback').open = true; notice('Current menu retrieved from Uber.'); }));
for (const [id, enable] of [['enable', true], ['pause', false]]) el(id).addEventListener('click', () => work(async () => { const result = await api('uber/enable', { enable }); if (result.url) { authorize(result); return; } renderConfig(result); notice('Order testing paused.'); }));
el('refresh-order').addEventListener('click', () => work(() => readOrder(currentOrder)));
for (const action of ['accept', 'deny']) el(action).addEventListener('click', () => work(async () => {
  if (!currentOrder || !el('reviewed').checked || !el('reason').value.trim()) throw new Error('Review the complete order, tick the instruction checkbox and enter a decision reason.');
  const result = await api('uber/orders/' + encodeURIComponent(currentOrder) + '/decision', { action, reason: el('reason').value.trim(), reviewedInstructions: true });
  await readOrder(currentOrder); notice(result.message);
}));
const callbackResult = new URLSearchParams(window.location.search).get('connection');
history.replaceState(null, '', '/console/');
work(async () => { try { await api('auth/session'); await openWorkspace(); } catch (error) { if (!loggedIn) showLogin(); else throw error; }
  if (callbackResult) notice(callbackResult === 'complete' ? 'Test store authorization completed. Continue with menu preparation.' : 'Authorization was not completed. Sign in and start a new connection; check that you used the test merchant account.', callbackResult !== 'complete'); });
setInterval(() => { if (loggedIn && !busy && !document.hidden) work(receipts); }, 30000);
