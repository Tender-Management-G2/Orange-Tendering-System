// ORANGE INTELLIGENCE - shared code of the Overview, Suppliers and Tenders tabs (Views/Intelligence/Index.cshtml).
// Read this file first. It contains:
//   1. page variables and small helpers (translation, escaping, number and date formatting)
//   2. getJson / postJson: every request to IntelligenceController goes through these two functions
//   3. the context bar (scope, date period, status line) and the toast message
//   4. the tabs: showTab() shows one panel and asks that tab's file to load its data
//   5. chart and KPI helpers used by the three tab files
//   6. the bridge to AI Studio (window.OrangeRadar) and the page start (startIntelligencePage)
// The tab files are intelligence-overview.js, intelligence-suppliers.js and intelligence-tenders.js.
// AI Studio has its own files (intelligence-ai.js, intelligence-audio.js) and is not changed by this code.

// ======================================================================
// 1. Page variables and small helpers
// ======================================================================

var page = document.getElementById('radar');
var pageIsArabic = page.dataset.uiLanguage === 'ar';
var PAGE_TABS = ['overview', 'suppliers', 'tenders', 'studio'];
var currentTab = 'overview';

// tr('Suppliers', 'الموردون') returns the text in the page language.
function tr(english, arabic) {
  if (pageIsArabic) {
    return arabic;
  }
  return english;
}

// Text from the database is always escaped before it is put inside innerHTML (protects against HTML injection).
function escapeHtml(value) {
  var text = value === null || value === undefined ? '' : String(value);
  text = text.replace(/&/g, '&amp;');
  text = text.replace(/</g, '&lt;');
  text = text.replace(/>/g, '&gt;');
  text = text.replace(/"/g, '&quot;');
  text = text.replace(/'/g, '&#39;');
  return text;
}

// 1234.5 -> "1,234.5"   (null -> "—")
function formatNumber(value, digits) {
  if (value === null || value === undefined) {
    return '—';
  }
  if (digits === undefined) {
    digits = 2;
  }
  return Number(value).toLocaleString('en-US', { maximumFractionDigits: digits });
}

function formatInteger(value) {
  return formatNumber(value, 0);
}

// 0.834 -> "83%"
function formatPercent(ratio, digits) {
  if (ratio === null || ratio === undefined) {
    return '—';
  }
  return (ratio * 100).toFixed(digits || 0) + '%';
}

// 0.05 -> "+5.0%", -0.1 -> "-10.0%"
function formatSignedPercent(ratio, digits) {
  if (ratio === null || ratio === undefined) {
    return '—';
  }
  var sign = ratio >= 0 ? '+' : '';
  return sign + (ratio * 100).toFixed(digits === undefined ? 1 : digits) + '%';
}

// ("JOD", 12500) -> "JOD 12,500"
function formatMoney(currency, value) {
  return currency + ' ' + Number(value).toLocaleString('en-US', { maximumFractionDigits: 0 });
}

// "2026-03-05T10:00:00" -> "05 Mar 2026"
function formatDate(value) {
  if (!value) {
    return '—';
  }
  return new Date(value).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
}

function formatDateTime(value) {
  return new Date(value).toLocaleString('en-GB', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

// Cycle 1 is the initial bidding; later cycles are re-tendering cycles.
function cycleLabel(roundNumber) {
  if (roundNumber === 1) {
    return tr('Initial Bidding', 'العطاء الأولي');
  }
  return tr('Re-tendering — Cycle ' + roundNumber, 'إعادة الطرح — الدورة ' + roundNumber);
}

// ======================================================================
// 2. Requests to IntelligenceController
// ======================================================================

// buildUrl('/Intelligence/Suppliers', { from: '2026-01-01', category: 'IT' }) -> '/Intelligence/Suppliers?from=2026-01-01&category=IT'
function buildUrl(url, params) {
  var query = new URLSearchParams();
  for (var key in params) {
    if (params[key] !== null && params[key] !== undefined) {
      query.append(key, params[key]);
    }
  }
  return url + '?' + query.toString();
}

// The error message sent by the controller ({ error: "..." }), or a general message.
async function readErrorMessage(response) {
  var message = tr('Request failed', 'تعذر تنفيذ الطلب') + ' (' + response.status + ')';
  try {
    var body = await response.json();
    if (body && body.error) {
      message = body.error;
    }
  } catch (error) {
    // The answer was not JSON (for example an HTML error page): keep the general message.
  }
  return message;
}

// GET request that returns JSON. Throws an Error with a readable message when the server answers with an error.
async function getJson(url, params) {
  var response = await fetch(buildUrl(url, params), { credentials: 'same-origin' });
  if (!response.ok || response.redirected) {
    throw new Error(await readErrorMessage(response));
  }
  return await response.json();
}

// POST request with a JSON body. The RequestVerificationToken header is the anti-forgery token
// required by [AutoValidateAntiforgeryToken] on IntelligenceController.
async function postRequest(url, body) {
  var response = await fetch(url, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': page.dataset.csrf },
    body: JSON.stringify(body)
  });
  if (!response.ok) {
    throw new Error(await readErrorMessage(response));
  }
  return response;
}

async function postJson(url, body) {
  var response = await postRequest(url, body);
  return await response.json();
}

// Asks the server for an Excel/PDF file and saves it (used by the Tenders tab).
async function downloadFile(request) {
  try {
    var response = await postRequest(page.dataset.urlExport, request);
    var blob = await response.blob();
    var fileName = 'Orange-Intelligence.' + request.format;
    var disposition = response.headers.get('content-disposition') || '';
    var match = disposition.match(/filename="?([^";]+)/);
    if (match) {
      fileName = match[1];
    }
    var link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(function () { URL.revokeObjectURL(link.href); }, 4000);
    showToast(request.format.toUpperCase() + ' ' + tr('file downloaded.', 'تم تنزيل الملف.'));
  } catch (error) {
    showStatus(tr('Export failed: ', 'فشل التصدير: ') + error.message, true);
  }
}

// ======================================================================
// 3. Context bar (scope, period, status) and toast
// ======================================================================

// The date inputs of the context bar. Empty = no limit.
function periodParams() {
  return {
    from: document.getElementById('rd-from').value,
    to: document.getElementById('rd-to').value
  };
}

function showStatus(text, isError) {
  var status = document.getElementById('ctx-status');
  status.textContent = text || '';
  status.classList.toggle('is-error', isError === true);
}

// Every tab answer contains a "context" object (PageContext in IntelligenceModels.cs).
function showContext(context) {
  document.getElementById('ctx-scope').textContent = context.scope || tr('Authorized scope', 'النطاق المصرح');
  document.getElementById('ctx-demo').hidden = !context.isDemo;
  if (context.tenderCount === 0 && !context.isWholeMarket) {
    showStatus(tr('No authorized tenders in this scope. Data appears when a tender of your department or committee falls inside the period.',
      'لا توجد عطاءات مصرح بها في هذا النطاق. تظهر البيانات عندما يقع عطاء لجهتك أو لجنتك ضمن الفترة.'));
  } else if (context.analyzedRounds === 0) {
    showStatus(tr('No closed bidding cycle falls inside this period - every supplier in scope is still shown. Widen the period for patterns.',
      'لا توجد دورة عطاء مغلقة ضمن هذه الفترة - يبقى كل الموردين ظاهرين. وسّع الفترة لرؤية الأنماط.'));
  } else {
    showStatus('');
  }
}

// The chip "2026-01-01 → 2026-06-30 ×" under the period inputs.
function renderPeriodChip() {
  var period = periodParams();
  var chips = document.getElementById('ctx-chips');
  if (!period.from && !period.to) {
    chips.innerHTML = '';
    return;
  }
  var text = (period.from || '…') + ' → ' + (period.to || '…');
  chips.innerHTML = '<span class="oi-chip">' + escapeHtml(text) +
    '<button type="button" id="ctx-clear-period" aria-label="' + tr('Remove period filter', 'إزالة مرشح الفترة') + '">×</button></span>';
  document.getElementById('ctx-clear-period').addEventListener('click', function () {
    document.getElementById('rd-from').value = '';
    document.getElementById('rd-to').value = '';
    onPeriodChanged(false);
  });
}

// A new period makes every loaded tab out of date: forget their data and reload the visible tab.
function onPeriodChanged(refresh) {
  renderPeriodChip();
  forgetOverviewData();
  forgetSuppliersData();
  forgetTendersData();
  loadCurrentTab(refresh);
}

function loadCurrentTab(refresh) {
  if (currentTab === 'overview') {
    loadOverview(refresh);
  } else if (currentTab === 'suppliers') {
    loadSuppliers(refresh);
  } else if (currentTab === 'tenders') {
    loadTenders(refresh);
  }
}

var toastTimer = 0;
function showToast(text) {
  var toast = document.getElementById('page-toast');
  toast.textContent = text;
  toast.hidden = false;
  requestAnimationFrame(function () { toast.classList.add('is-on'); });
  clearTimeout(toastTimer);
  toastTimer = setTimeout(function () {
    toast.classList.remove('is-on');
    setTimeout(function () { toast.hidden = true; }, 250);
  }, 4200);
}

// ======================================================================
// 4. Tabs
// ======================================================================

// Shows one tab panel and hides the three others, then lets the tab file load or redraw its data.
function showTab(tab) {
  if (PAGE_TABS.indexOf(tab) < 0) {
    tab = 'overview';
  }
  currentTab = tab;
  page.dataset.tab = tab;

  var buttons = document.querySelectorAll('[data-tab-link]');
  for (var i = 0; i < buttons.length; i++) {
    var selected = buttons[i].dataset.tabLink === tab;
    buttons[i].setAttribute('aria-selected', selected ? 'true' : 'false');
    buttons[i].tabIndex = selected ? 0 : -1;
  }
  var panels = document.querySelectorAll('[data-tab-panel]');
  for (var j = 0; j < panels.length; j++) {
    panels[j].hidden = panels[j].dataset.tabPanel !== tab;
  }
  history.replaceState(null, '', '#' + tab);

  if (tab === 'overview') {
    openOverviewTab();
  } else if (tab === 'suppliers') {
    openSuppliersTab();
  } else if (tab === 'tenders') {
    openTendersTab();
  }
  // tab === 'studio': AI Studio (intelligence-ai.js) manages its own panel.
}

// '/Intelligence#tenders' -> 'tenders'. No hash (or an unknown one) opens the Overview.
function readTabFromHash() {
  var hash = location.hash.replace('#', '');
  if (PAGE_TABS.indexOf(hash) >= 0) {
    return hash;
  }
  return 'overview';
}

// Arrow keys move between the tabs (accessibility pattern for tab lists). In Arabic (RTL) the arrows are mirrored.
function onTabKeyDown(event) {
  var index = PAGE_TABS.indexOf(currentTab);
  var step = document.documentElement.dir === 'rtl' ? -1 : 1;
  var next = null;
  if (event.key === 'ArrowRight') {
    next = PAGE_TABS[(index + step + PAGE_TABS.length) % PAGE_TABS.length];
  } else if (event.key === 'ArrowLeft') {
    next = PAGE_TABS[(index - step + PAGE_TABS.length) % PAGE_TABS.length];
  } else if (event.key === 'Home') {
    next = PAGE_TABS[0];
  } else if (event.key === 'End') {
    next = PAGE_TABS[PAGE_TABS.length - 1];
  }
  if (next) {
    event.preventDefault();
    showTab(next);
    document.getElementById('oi-tab-' + next).focus();
  }
}

// ======================================================================
// 5. Charts (ECharts) and KPI tiles
// ======================================================================

var charts = {};

// Returns the ECharts object of a chart <div>, creating it the first time.
function getChart(elementId) {
  var element = document.getElementById(elementId);
  if (!element || !window.echarts) {
    return null;
  }
  if (!charts[elementId]) {
    charts[elementId] = echarts.init(element, null, { renderer: 'canvas' });
    // Redraw when the <div> changes size (window resize, tab shown, sidebar collapsed).
    new ResizeObserver(function () { charts[elementId].resize(); }).observe(element);
  }
  return charts[elementId];
}

// Colours come from the CSS variables, so the charts follow the light / dark theme.
function cssValue(name) {
  return getComputedStyle(page).getPropertyValue(name).trim();
}

// Category colour number from the server (0..7) -> --c1..--c8. -1 = grey.
function categoryColor(index) {
  if (index === null || index === undefined || index < 0) {
    return cssValue('--rd-neutral');
  }
  return cssValue('--c' + ((index % 8) + 1));
}

// Supplier colour number from the server (0..7) -> --s1..--s8. -1 = grey.
function supplierColor(index) {
  if (index === null || index === undefined || index < 0) {
    return cssValue('--rd-neutral');
  }
  return cssValue('--s' + (index + 1));
}

// '#ff7900' + 0.3 -> 'rgba(255,121,0,0.3)'
function withAlpha(hex, alpha) {
  var value = String(hex).replace('#', '');
  if (value.length === 3) {
    value = value[0] + value[0] + value[1] + value[1] + value[2] + value[2];
  }
  var number = parseInt(value.slice(0, 6), 16);
  var red = (number >> 16) & 255;
  var green = (number >> 8) & 255;
  var blue = number & 255;
  return 'rgba(' + red + ',' + green + ',' + blue + ',' + alpha + ')';
}

// A colour gradient for bars and areas. vertical = top to bottom, otherwise left to right.
function gradient(vertical, fromColor, toColor) {
  return new echarts.graphic.LinearGradient(0, 0, vertical ? 0 : 1, vertical ? 1 : 0, [
    { offset: 0, color: fromColor },
    { offset: 1, color: toColor }
  ]);
}

// Settings shared by every chart: transparent background, page font, tooltip style.
function chartBase() {
  var reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  return {
    backgroundColor: 'transparent',
    textStyle: { color: cssValue('--rd-ink-2'), fontFamily: getComputedStyle(page).fontFamily, fontSize: 14 },
    animationDuration: reduceMotion ? 0 : 700,
    animationDurationUpdate: reduceMotion ? 0 : 420,
    tooltip: {
      backgroundColor: cssValue('--rd-surface'),
      borderColor: cssValue('--rd-line'),
      textStyle: { color: cssValue('--rd-ink'), fontSize: 14, lineHeight: 21 },
      confine: true,
      extraCssText: 'border-radius:14px;box-shadow:0 18px 44px rgba(0,0,0,.24);padding:12px 14px;'
    },
    grid: { left: 14, right: 22, top: 34, bottom: 14, containLabel: true }
  };
}

// Axis style shared by every chart.
function chartAxis() {
  return {
    axisLine: { lineStyle: { color: cssValue('--rd-line') } },
    axisTick: { show: false },
    axisLabel: { color: cssValue('--rd-muted'), fontSize: 14 },
    splitLine: { lineStyle: { color: cssValue('--rd-line-soft'), type: [4, 6] } },
    nameTextStyle: { color: cssValue('--rd-muted'), fontSize: 13.5, fontWeight: 600 }
  };
}

function chartLegend() {
  return { textStyle: { color: cssValue('--rd-ink-2'), fontSize: 14 }, itemWidth: 12, itemHeight: 12, itemGap: 18, icon: 'circle' };
}

// Replaces a chart with a centred message (empty data).
function showChartMessage(chart, text) {
  chart.setOption({
    title: { text: text, left: 'center', top: 'middle', textStyle: { color: cssValue('--rd-muted'), fontSize: 15, fontWeight: 500, width: 360, overflow: 'break', lineHeight: 23 } },
    xAxis: { show: false }, yAxis: { show: false }, series: [], legend: { show: false }
  }, true);
}

// The "Table" link of a chart card: adds a table with the same numbers under the chart, or removes it on the next click.
function toggleChartTable(button, html) {
  var card = button.closest('.rd-card');
  var existing = card.querySelector('.rd-table-wrap');
  if (existing) {
    existing.remove();
    return;
  }
  var wrap = document.createElement('div');
  wrap.className = 'rd-table-wrap';
  wrap.innerHTML = html;
  card.appendChild(wrap);
}


// Icons of the KPI tiles and attention items (SVG paths).
var ICONS = {
  users: '<circle cx="9" cy="8" r="3.5"/><path d="M2.5 20c.6-4 3.2-6 6.5-6s5.9 2 6.5 6M16 4.8a3.3 3.3 0 0 1 0 6.4M18.5 14.4c1.8.8 2.8 2.7 3 5.6"/>',
  check: '<circle cx="12" cy="12" r="9"/><path d="m8 12 3 3 5-6"/>',
  layers: '<path d="M3 7l9-4 9 4-9 4zM3 12l9 4 9-4M3 17l9 4 9-4"/>',
  chart: '<path d="M4 20h16M7 17v-5M12 17V8M17 17v-9"/>',
  doc: '<path d="M7 3h7l4 4v14H7zM14 3v4h4M10 12h5M10 16h5"/>',
  send: '<path d="M22 2 11 13M22 2l-7 20-4-9-9-4z"/>',
  clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
  award: '<path d="M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0z"/>',
  mail: '<rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/>',
  spark: '<path d="M12 3l1.8 4.7 4.7 1.8-4.7 1.8L12 16l-1.8-4.7-4.7-1.8 4.7-1.8z"/>'
};

// Draws KPI tiles. Each tile: { label, value, unit, sub, help, icon, tone, bar }.
// value can be a number (formatted) or a text; bar (0..1) draws the thin progress line at the bottom.
function renderKpiTiles(containerId, tiles) {
  var html = '';
  for (var i = 0; i < tiles.length; i++) {
    var tile = tiles[i];
    var isNumber = typeof tile.value === 'number';
    var valueText = isNumber ? formatInteger(tile.value) : escapeHtml(tile.value === null || tile.value === undefined ? '—' : tile.value);
    var unit = tile.unit ? '<em>' + escapeHtml(tile.unit) + '</em>' : '';
    var bar = '';
    if (tile.bar !== undefined && tile.bar !== null) {
      var width = Math.max(0, Math.min(1, tile.bar)) * 100;
      bar = '<i class="oi-kpi-bar" aria-hidden="true"><u style="--w:' + width + '%"></u></i>';
    }
    html += '<div class="oi-kpi is-' + (tile.tone || 'orange') + '" title="' + escapeHtml(tile.help || '') + '">' +
      '<span class="oi-kpi-icon" aria-hidden="true"><svg viewBox="0 0 24 24">' + (ICONS[tile.icon] || '') + '</svg></span>' +
      '<div class="oi-kpi-text"><span class="oi-kpi-label">' + escapeHtml(tile.label) + '</span>' +
      '<strong class="' + (isNumber ? '' : 'is-text') + '">' + valueText + unit + '</strong>' +
      '<small>' + escapeHtml(tile.sub || '') + '</small></div>' + bar + '</div>';
  }
  document.getElementById(containerId).innerHTML = html;
}

// ======================================================================
// 6. AI Studio bridge and page start
// ======================================================================

// intelligence-ai.js (AI Studio, unchanged) expects:
//   window.OrangeRadar.data.suppliers  - the authorized suppliers for its drop-down list
//   window.OrangeRadar.setTab('studio') - to open its tab
//   a "radar:data" event whenever the supplier list is (re)loaded
window.OrangeRadar = {
  data: null,
  setTab: function (tab) { showTab(tab); }
};

function shareSuppliersWithAiStudio(suppliers) {
  window.OrangeRadar.data = { suppliers: suppliers };
  window.dispatchEvent(new CustomEvent('radar:data', { detail: { suppliers: suppliers } }));
}

// When the page opens on another tab, AI Studio still needs its supplier list.
async function loadSupplierListForAiStudio() {
  try {
    var data = await getJson(page.dataset.urlSuppliers, periodParams());
    shareSuppliersWithAiStudio(data.allSuppliers);
  } catch (error) {
    showStatus(tr('The supplier list could not load: ', 'تعذر تحميل قائمة الموردين: ') + error.message, true);
  }
}

// Redraws the visible tab with the data it already has (used after a light/dark theme change).
function redrawCurrentTab() {
  if (currentTab === 'overview') {
    openOverviewTab();
  } else if (currentTab === 'suppliers') {
    openSuppliersTab();
  } else if (currentTab === 'tenders') {
    openTendersTab();
  }
}

function startIntelligencePage() {
  // Tab buttons, and links such as "Open Tenders" that jump to a tab.
  page.addEventListener('click', function (event) {
    var tabButton = event.target.closest('[data-tab-link], [data-tab-jump]');
    if (tabButton) {
      showTab(tabButton.dataset.tabLink || tabButton.dataset.tabJump);
    }
  });
  document.getElementById('page-tabs').addEventListener('keydown', onTabKeyDown);
  // The address bar hash changed without a reload (for example a link to /Intelligence#tenders): show that tab.
  window.addEventListener('hashchange', function () {
    if (readTabFromHash() !== currentTab) {
      showTab(readTabFromHash());
    }
  });

  // Period and refresh button.
  document.getElementById('rd-from').addEventListener('change', function () { onPeriodChanged(false); });
  document.getElementById('rd-to').addEventListener('change', function () { onPeriodChanged(false); });
  document.getElementById('ctx-refresh').addEventListener('click', function () { onPeriodChanged(true); });
  window.addEventListener('orange:themechange', function () { requestAnimationFrame(redrawCurrentTab); });

  // Each tab file attaches the events of its own buttons and selects.
  setupOverviewEvents();
  setupSuppliersEvents();
  setupTendersEvents();

  showTab(readTabFromHash());
  if (currentTab !== 'suppliers') {
    loadSupplierListForAiStudio();
  }
}

// DOMContentLoaded fires after all the page scripts have loaded, so every function above already exists.
document.addEventListener('DOMContentLoaded', startIntelligencePage);
