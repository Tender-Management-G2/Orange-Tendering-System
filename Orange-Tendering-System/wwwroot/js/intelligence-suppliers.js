// ORANGE INTELLIGENCE - Tab 2 "Suppliers".
// Main flow: showTab('suppliers') -> openSuppliersTab() -> loadSuppliers() -> GET /Intelligence/Suppliers?filters
//            -> IntelligenceController.Suppliers -> IntelligenceService.GetSuppliersAsync -> SupplierCalculator.Build
//            -> JSON (SuppliersData) -> renderSuppliers() draws every part of the tab.
// The 3D Supplier Universe is drawn by orange-intelligence-universe.js; renderSupplierUniverse() hands it the same JSON.
// Other requests of this tab:
//   click a supplier row or dot  -> openSupplierDetails()  -> GET  /Intelligence/SupplierDetails
//   choose two suppliers          -> loadComparison()       -> GET  /Intelligence/CompareSuppliers
//   "Run scenario"               -> runSimulation()        -> POST /Intelligence/Simulate
// The browser never calculates a statistic: it only draws the numbers that the server sends.

var suppliersData = null;
var supplierFilters = { department: '', category: '', activity: 'all', search: '' };
var suppliersRequestNumber = 0;
var selectedSupplierId = null;
var supplierDetails = null;
var detailsTab = 'overview';
var insightTab = 'positions';
var comparison = { a: null, b: null, tenderId: null, rows: [] };
var searchTimer = 0;

// ======================================================================
// Loading
// ======================================================================

function openSuppliersTab() {
  if (suppliersData === null) {
    loadSuppliers(false);
  } else {
    renderSuppliers(suppliersData);
  }
}

function forgetSuppliersData() {
  suppliersData = null;
  supplierDetails = null;
}

// Used by the Overview "never submitted an offer" item: open this tab with the activity filter set.
function showSuppliersWithActivity(activity) {
  supplierFilters.activity = activity;
  suppliersData = null;
  showTab('suppliers');
}

async function loadSuppliers(refresh) {
  showStatus(tr('Running the analysis on recorded procurement data…', 'جارٍ تحليل بيانات المشتريات المسجلة…'));
  var params = periodParams();
  params.department = supplierFilters.department;
  params.category = supplierFilters.category;
  params.activity = supplierFilters.activity;
  params.search = supplierFilters.search;
  params.refresh = refresh ? 'true' : 'false';

  // When the user types fast, several requests can be running: only the newest answer is drawn.
  suppliersRequestNumber++;
  var myRequest = suppliersRequestNumber;
  try {
    var data = await getJson(page.dataset.urlSuppliers, params);
    if (myRequest !== suppliersRequestNumber) {
      return;
    }
    suppliersData = data;
    supplierFilters = data.filters;
    showContext(data.context);
    shareSuppliersWithAiStudio(data.allSuppliers);
    if (currentTab === 'suppliers') {
      renderSuppliers(data);
    }
    if (selectedSupplierId !== null && supplierDetails === null) {
      openSupplierDetails(selectedSupplierId);
    }
  } catch (error) {
    showStatus(tr('The analysis could not load: ', 'تعذر تحميل التحليل: ') + error.message, true);
  }
}

// Draws every part of the tab from one SuppliersData answer.
function renderSuppliers(data) {
  renderSupplierFilters(data);
  renderSupplierKpis(data);
  renderSupplierUniverse(data);
  renderCategoryDonut(data);
  renderParticipationTrend(data);
  renderSupplierTable(data);
  renderSupplierDetails();
  renderSupplierInsights(data);
  renderSupplierRelationships(data);
  renderComparisonControls();
  renderComparison();
  renderScenarioPick(data);
}

// ======================================================================
// Filters and KPI tiles
// ======================================================================

function renderSupplierFilters(data) {
  var departmentHtml = '<option value="">' + tr('All departments', 'جميع الجهات') + '</option>';
  for (var i = 0; i < data.departments.length; i++) {
    var department = data.departments[i];
    departmentHtml += '<option value="' + escapeHtml(department) + '">' + escapeHtml(department.replace(/^RADAR DEMO - /, '')) + '</option>';
  }
  var categoryHtml = '<option value="">' + tr('All categories', 'جميع الفئات') + '</option>';
  for (var j = 0; j < data.categories.length; j++) {
    categoryHtml += '<option value="' + escapeHtml(data.categories[j].name) + '">' + escapeHtml(data.categories[j].name) + '</option>';
  }
  document.getElementById('sup-department').innerHTML = departmentHtml;
  document.getElementById('sup-category').innerHTML = categoryHtml;
  document.getElementById('sup-department').value = data.filters.department;
  document.getElementById('sup-category').value = data.filters.category;
  document.getElementById('sup-activity').value = data.filters.activity;
  if (document.activeElement !== document.getElementById('sup-search')) {
    document.getElementById('sup-search').value = data.filters.search;
  }
}

// A filter changed: ask the server again (the server applies the filters and recalculates every number).
function setSupplierFilter(name, value) {
  supplierFilters[name] = value;
  loadSuppliers(false);
}

function renderSupplierKpis(data) {
  var total = data.registeredSuppliers;
  var responseValue = data.responseRate === null ? '—' : Math.round(data.responseRate * 100) + '%';
  var responseSub = data.responseRate === null
    ? tr('no closed bidding cycle invitation', 'لا توجد دعوات لدورات مغلقة')
    : formatInteger(data.offers) + tr(' offers ÷ ', ' عرض ÷ ') + formatInteger(data.invitations) + tr(' closed bidding cycle invitations', ' دعوة لدورات مغلقة');
  var largest = data.largestCategory
    ? tr('largest: ', 'الأكبر: ') + data.largestCategory + ' (' + data.largestCategoryCount + ')'
    : tr('none registered', 'لا توجد فئات');

  renderKpiTiles('sup-kpis', [
    {
      label: tr('Registered suppliers', 'الموردون المسجلون'), value: total, icon: 'users', tone: 'orange',
      sub: formatInteger(data.approvedSuppliers) + tr(' approved / eligible', ' معتمد / مؤهل'),
      bar: total ? data.approvedSuppliers / total : 0,
      help: tr('Every supplier company recorded in the system, whatever its registration status.', 'كل شركات الموردين المسجلة أياً كانت حالتها.')
    },
    {
      label: tr('Participating suppliers', 'الموردون المشاركون'), value: data.participatingSuppliers, icon: 'check', tone: 'teal',
      sub: formatPercent(total ? data.participatingSuppliers / total : null) + tr(' submitted an offer in a closed bidding cycle', ' قدّموا عرضاً في دورة مغلقة'),
      bar: total ? data.participatingSuppliers / total : 0,
      help: tr('Suppliers with at least one submitted offer in a closed bidding cycle of the period.', 'الموردون الذين قدموا عرضاً واحداً على الأقل في دورة مغلقة ضمن الفترة.')
    },
    {
      label: tr('Categories', 'الفئات'), value: data.categoryCount, icon: 'layers', tone: 'violet', sub: largest,
      bar: total && data.largestCategoryCount ? data.largestCategoryCount / total : 0,
      help: tr('Distinct registered business categories of the suppliers shown.', 'فئات النشاط المسجلة المختلفة للموردين المعروضين.')
    },
    {
      label: tr('Response rate', 'معدل الاستجابة'), value: responseValue, icon: 'chart', tone: 'blue', sub: responseSub,
      bar: data.responseRate === null ? 0 : data.responseRate,
      help: tr('Offers submitted in closed bidding cycles divided by invitations to those cycles.', 'العروض المقدمة في الدورات المغلقة مقسومة على الدعوات لتلك الدورات.')
    }
  ]);
}

// ======================================================================
// 3D Supplier Universe (the drawing itself is in orange-intelligence-universe.js)
// ======================================================================

var universeStarted = false;

// Gives the 3D engine the latest /Intelligence/Suppliers answer. The engine only draws: every number came from the server.
function renderSupplierUniverse(data) {
  if (!window.SupplierUniverse) {
    return;
  }
  if (!universeStarted) {
    window.SupplierUniverse.init(document.getElementById('sup-universe'), {
      onSupplierClick: onUniverseSupplierClick,
      onCategoryClick: toggleCategoryFilter,
      colorOf: categoryColor
    });
    universeStarted = true;
  }
  var shownIds = [];
  for (var i = 0; i < data.suppliers.length; i++) {
    shownIds.push(data.suppliers[i].supplierId);
  }
  window.SupplierUniverse.update({
    suppliers: data.allSuppliers,           // every supplier in the authorized scope = the dots
    shownIds: shownIds,                     // the suppliers that pass the filters = the bright dots
    rows: data.suppliers,                   // their statistics, for the hover card
    category: data.filters.category,        // a chosen category gathers into one cluster
    pairs: data.pairs,                      // competing pairs = the arcs of the Competition view
    competitionMinimum: data.competitionMinimum
  });
}

// A click on a dot opens its details; clicking the selected dot again (or Escape) closes them.
function onUniverseSupplierClick(supplierId) {
  if (supplierId === null) {
    closeSupplierDetails();
  } else {
    openSupplierDetails(supplierId);
  }
}

// Tells the 3D engine which supplier the details panel shows, and its competitors once they are loaded.
function showSelectedSupplierInUniverse() {
  if (!window.SupplierUniverse) {
    return;
  }
  var competitors = [];
  if (supplierDetails !== null && supplierDetails.supplier.supplierId === selectedSupplierId) {
    competitors = supplierDetails.competitors;
  }
  window.SupplierUniverse.selectSupplier(selectedSupplierId, competitors);
}

// ======================================================================
// Market snapshot: category donut + participation trend
// ======================================================================

function renderCategoryDonut(data) {
  var total = Math.max(1, data.registeredSuppliers);
  var legendHtml = '';
  var slices = [];
  for (var i = 0; i < data.categoryChart.length; i++) {
    var slice = data.categoryChart[i];
    var color = slice.isOthers ? cssValue('--rd-neutral') : categoryColor(slice.color);
    var dimmed = data.filters.category !== '' && data.filters.category !== slice.name;
    slices.push({ name: slice.name, value: slice.count, isOthers: slice.isOthers, itemStyle: { color: color, opacity: dimmed ? 0.35 : 1 } });

    var buttonAttributes = slice.isOthers ? 'disabled' : 'data-category="' + escapeHtml(slice.name) + '"';
    var activeClass = data.filters.category === slice.name ? 'is-on' : '';
    legendHtml += '<li><button type="button" ' + buttonAttributes + ' class="' + activeClass + '"><i style="background:' + color + '"></i>' +
      '<span>' + escapeHtml(slice.name) + '</span><b>' + slice.count + '</b><small>' + formatPercent(slice.count / total) + '</small></button></li>';
  }
  if (legendHtml === '') {
    legendHtml = '<li class="rd-muted">' + tr('No suppliers', 'لا يوجد موردون') + '</li>';
  }
  document.getElementById('sup-category-legend').innerHTML = legendHtml;

  var chart = getChart('chart-sup-categories');
  if (!chart) {
    return;
  }
  var options = chartBase();
  options.tooltip.formatter = function (item) {
    return escapeHtml(item.name) + '<br>' + item.value + tr(' supplier(s) · ', ' مورد · ') + formatPercent(item.value / total);
  };
  options.title = {
    text: formatInteger(data.registeredSuppliers), subtext: tr('Suppliers', 'الموردون'), left: 'center', top: '31%', itemGap: 2,
    textStyle: { color: cssValue('--rd-ink'), fontSize: 24, fontWeight: 800 }, subtextStyle: { color: cssValue('--rd-muted'), fontSize: 13 }
  };
  options.series = [{
    type: 'pie', radius: ['72%', '94%'], center: ['50%', '50%'], padAngle: slices.length > 1 ? 3 : 0,
    itemStyle: { borderRadius: 6 }, label: { show: false }, emphasis: { scale: true, scaleSize: 4 }, data: slices
  }];
  chart.setOption(options, true);

  // Clicking a slice filters the tab on that category (clicking it again removes the filter).
  chart.off('click');
  chart.on('click', function (item) {
    if (!item.data.isOthers) {
      toggleCategoryFilter(item.name);
    }
  });
}

function toggleCategoryFilter(category) {
  if (supplierFilters.category === category) {
    setSupplierFilter('category', '');
  } else {
    setSupplierFilter('category', category);
  }
}

function renderParticipationTrend(data) {
  var chart = getChart('chart-sup-trend');
  if (!chart) {
    return;
  }
  var trend = data.participationTrend;
  if (trend.length === 0) {
    showChartMessage(chart, tr('No closed bidding cycle in this period.', 'لا توجد دورة مغلقة في هذه الفترة.'));
    return;
  }
  if (trend.length === 1) {
    showChartMessage(chart, tr('All closed bidding cycles fall in ', 'كل الدورات المغلقة في ') + trend[0].month + ': ' + trend[0].suppliers + tr(' suppliers submitted offers.', ' مورداً قدموا عروضاً.'));
    return;
  }

  var months = [];
  var values = [];
  for (var i = 0; i < trend.length; i++) {
    months.push(trend[i].month);
    values.push(trend[i].suppliers);
  }
  var lineColor = cssValue('--c2');
  var options = chartBase();
  options.grid = { left: 6, right: 16, top: 14, bottom: 4, containLabel: true };
  options.tooltip.trigger = 'axis';
  options.tooltip.formatter = function (points) {
    return escapeHtml(points[0].axisValue) + '<br><b>' + points[0].value + '</b> ' + tr('supplier(s) submitted offers', 'مورد قدموا عروضاً');
  };
  var xAxis = chartAxis();
  xAxis.type = 'category';
  xAxis.data = months;
  xAxis.boundaryGap = false;
  xAxis.axisLabel = {
    color: cssValue('--rd-muted'), fontSize: 13, hideOverlap: true,
    formatter: function (month) { return new Date(month + '-01T00:00:00Z').toLocaleDateString('en-GB', { month: 'short', year: '2-digit' }); }
  };
  var yAxis = chartAxis();
  yAxis.minInterval = 1;
  yAxis.splitNumber = 2;
  options.xAxis = xAxis;
  options.yAxis = yAxis;
  options.series = [{
    type: 'line', smooth: 0.35, symbol: 'circle', symbolSize: 8, data: values,
    lineStyle: { width: 3, color: lineColor, shadowBlur: 12, shadowColor: withAlpha(lineColor, 0.45) },
    itemStyle: { color: lineColor, borderColor: cssValue('--rd-surface'), borderWidth: 2 },
    areaStyle: { color: gradient(true, withAlpha(lineColor, 0.3), withAlpha(lineColor, 0)) }
  }];
  chart.setOption(options, true);
}

// ======================================================================
// Supplier directory and details panel
// ======================================================================

function renderSupplierTable(data) {
  document.getElementById('sup-table-note').textContent = data.suppliers.length + tr(' suppliers in view · most active first · select a row for details', ' مورداً معروضاً · الأكثر نشاطاً أولاً · اختر صفاً للتفاصيل');
  if (data.suppliers.length === 0) {
    document.getElementById('sup-table').innerHTML = '<p class="oi-empty-note">' + tr('No supplier matches these filters.', 'لا يوجد مورد يطابق هذه المرشحات.') + '</p>';
    return;
  }
  var html = '<table class="oi-table"><thead><tr>' +
    '<th>' + tr('Supplier', 'المورد') + '</th><th>' + tr('Category', 'الفئة') + '</th><th>' + tr('Status', 'الحالة') + '</th>' +
    '<th class="num">' + tr('Invitations', 'الدعوات') + '</th><th class="num">' + tr('Cycles with an offer', 'دورات بعرض') + '</th>' +
    '<th class="num">' + tr('Awards', 'الترسيات') + '</th><th class="num">' + tr('Response rate', 'معدل الاستجابة') + '</th></tr></thead><tbody>';
  for (var i = 0; i < data.suppliers.length; i++) {
    var supplier = data.suppliers[i];
    var selectedClass = supplier.supplierId === selectedSupplierId ? 'is-selected' : '';
    html += '<tr class="' + selectedClass + '" data-supplier-row="' + supplier.supplierId + '">' +
      '<th><button type="button" data-supplier-open="' + supplier.supplierId + '"><i style="background:' + categoryColor(supplier.categoryColor) + '"></i>' + escapeHtml(supplier.name) + '</button></th>' +
      '<td>' + escapeHtml(supplier.category) + '</td><td>' + escapeHtml(supplier.status) + '</td>' +
      '<td class="num">' + formatInteger(supplier.invitations) + '</td><td class="num">' + formatInteger(supplier.participations) + '</td>' +
      '<td class="num">' + formatInteger(supplier.wins) + '</td><td class="num">' + formatPercent(supplier.participationRate) + '</td></tr>';
  }
  html += '</tbody></table>';
  document.getElementById('sup-table').innerHTML = html;
}

async function openSupplierDetails(supplierId) {
  selectedSupplierId = supplierId;
  markSelectedRow();
  showSelectedSupplierInUniverse();
  document.getElementById('sup-details-empty').hidden = false;
  document.getElementById('sup-details-content').hidden = true;
  document.getElementById('sup-details-empty').querySelector('b').textContent = tr('Loading supplier…', 'جارٍ تحميل المورد…');
  try {
    var params = periodParams();
    params.supplierId = supplierId;
    supplierDetails = await getJson(page.dataset.urlSupplierDetails, params);
    renderSupplierDetails();
    showSelectedSupplierInUniverse();
    document.getElementById('sup-details-title').focus();
  } catch (error) {
    supplierDetails = null;
    document.getElementById('sup-details-empty').querySelector('b').textContent = error.message;
  }
}

function closeSupplierDetails() {
  selectedSupplierId = null;
  supplierDetails = null;
  markSelectedRow();
  renderSupplierDetails();
  showSelectedSupplierInUniverse();
}

function markSelectedRow() {
  var rows = document.querySelectorAll('[data-supplier-row]');
  for (var i = 0; i < rows.length; i++) {
    rows[i].classList.toggle('is-selected', Number(rows[i].dataset.supplierRow) === selectedSupplierId);
  }
}

function renderSupplierDetails() {
  var empty = document.getElementById('sup-details-empty');
  var content = document.getElementById('sup-details-content');
  if (supplierDetails === null) {
    empty.hidden = false;
    content.hidden = true;
    empty.querySelector('b').textContent = tr('Choose a supplier', 'اختر مورداً');
    return;
  }
  empty.hidden = true;
  content.hidden = false;

  var supplier = supplierDetails.supplier;
  var color = categoryColor(supplier.categoryColor);
  document.getElementById('sup-details-title').textContent = supplier.name;
  document.getElementById('sup-details-sub').innerHTML = '<i style="background:' + color + '"></i>' + escapeHtml(supplier.category);
  document.getElementById('sup-details-icon').style.setProperty('--c', color);
  var status = document.getElementById('sup-details-status');
  status.textContent = supplier.status;
  status.className = 'oi-pill ' + (supplier.isApproved ? 'is-ok' : 'is-muted');

  var tabs = document.querySelectorAll('[data-details-tab]');
  for (var i = 0; i < tabs.length; i++) {
    tabs[i].setAttribute('aria-selected', tabs[i].dataset.detailsTab === detailsTab ? 'true' : 'false');
  }
  var body = document.getElementById('sup-details-body');
  if (detailsTab === 'participation') {
    body.innerHTML = detailsParticipationHtml(supplierDetails);
  } else if (detailsTab === 'pricing') {
    body.innerHTML = detailsPricingHtml(supplierDetails);
  } else if (detailsTab === 'relationships') {
    body.innerHTML = detailsRelationshipsHtml(supplierDetails);
  } else {
    body.innerHTML = detailsOverviewHtml(supplierDetails);
  }
}

// One "label: value" line of the details overview.
function detailsLine(label, value) {
  return '<div><dt>' + label + '</dt><dd>' + value + '</dd></div>';
}

function detailsOverviewHtml(details) {
  var supplier = details.supplier;
  var awarded = supplier.awardedValue.length > 0 ? formatMoney(escapeHtml(supplier.awardedValue[0].currency), supplier.awardedValue[0].value) : '—';
  var wins = formatInteger(supplier.wins);
  if (supplier.winRate !== null) {
    wins += ' <small>(' + formatPercent(supplier.winRate) + ')</small>';
  }
  var active = supplier.firstActivity ? formatDate(supplier.firstActivity) + ' → ' + formatDate(supplier.lastActivity) : '—';
  return '<dl class="oi-kv">' +
    detailsLine(tr('Status', 'الحالة'), escapeHtml(supplier.status)) +
    detailsLine(tr('Business category', 'فئة النشاط'), escapeHtml(supplier.category)) +
    detailsLine(tr('Invitations', 'الدعوات'), formatInteger(supplier.invitations)) +
    detailsLine(tr('Bidding Cycles Participated In', 'دورات العطاء التي شارك فيها'), formatInteger(supplier.participations)) +
    detailsLine(tr('Response rate', 'معدل الاستجابة'), formatPercent(supplier.participationRate)) +
    detailsLine(tr('Official awards', 'الترسيات الرسمية'), wins) +
    detailsLine(tr('Awarded value', 'القيمة المرساة'), awarded) +
    detailsLine(tr('Price vs. round median', 'السعر مقابل الوسيط'), formatSignedPercent(supplier.relativePrice)) +
    detailsLine(tr('Active', 'النشاط'), active) +
    '</dl>';
}

// Bidding history: one line per closed cycle with an offer. Clicking a line opens that tender on the Tenders tab.
function detailsParticipationHtml(details) {
  if (details.rounds.length === 0) {
    return '<p class="oi-empty-note">' + tr('No offer in a closed round in this period. Invitations: ', 'لا عروض في جولات مغلقة ضمن هذه الفترة. الدعوات: ') + formatInteger(details.supplier.invitations) + '.</p>';
  }
  var html = '<ul class="oi-rounds">';
  for (var i = 0; i < details.rounds.length; i++) {
    var round = details.rounds[i];
    var width = (round.offers / Math.max(1, details.maxOffers)) * 100;
    html += '<li><button type="button" data-details-round="' + round.roundId + '" data-details-tender="' + round.tenderId + '">' +
      '<span><b>' + escapeHtml(round.tender) + '</b><small>' + cycleLabel(round.roundNumber) + ' · ' + formatDate(round.date) + ' · ' + escapeHtml(round.department) + '</small></span>' +
      '<i class="oi-bar" style="--w:' + width + '%"><u></u></i><em>' + round.offers + tr(' offers', ' عروض') + '</em>' +
      (round.awarded ? '<strong class="oi-pill is-award">' + tr('Awarded', 'مُرسى') + '</strong>' : '') + '</button></li>';
  }
  return html + '</ul>';
}

// Every comparable offer: how far it was from the median of its cycle (green = cheaper, amber = more expensive).
function detailsPricingHtml(details) {
  var supplier = details.supplier;
  var html = '';
  if (supplier.relativePrice !== null) {
    html += '<div class="oi-big"><strong class="' + (supplier.relativePrice > 0 ? 'is-up' : 'is-down') + '">' + formatSignedPercent(supplier.relativePrice) + '</strong>' +
      '<span>' + tr('average vs. round median', 'المتوسط مقابل وسيط الجولة') + ' · ' + supplier.pricedRounds + tr(' priced rounds', ' جولات مسعّرة') + '</span></div>';
  }
  if (details.prices.length === 0) {
    return html + '<p class="oi-empty-note">' + tr('No comparable priced round (needs another offer in the same currency).', 'لا توجد جولة مسعّرة قابلة للمقارنة.') + '</p>';
  }
  var largest = 0.05;
  for (var i = 0; i < details.prices.length; i++) {
    largest = Math.max(largest, Math.abs(details.prices[i].relative));
  }
  html += '<ul class="oi-diverge">';
  for (var j = 0; j < details.prices.length; j++) {
    var price = details.prices[j];
    var direction = price.relative > 0 ? 'is-up' : 'is-down';
    html += '<li><span>' + escapeHtml(price.tenderTitle) + '<small>' + formatDate(price.date) + ' · ' + formatMoney(escapeHtml(price.currency), price.total) + '</small></span>' +
      '<i><u class="' + direction + '" style="--w:' + (Math.abs(price.relative) / largest) * 50 + '%"></u></i><b>' + formatSignedPercent(price.relative) + '</b></li>';
  }
  return html + '</ul>';
}

// Competitors: suppliers that submitted offers in the same closed cycles. Clicking one opens its details.
function detailsRelationshipsHtml(details) {
  var html = '<h4>' + tr('Competes with', 'يتنافس مع') + '</h4>';
  if (details.competitors.length === 0) {
    return html + '<p class="oi-empty-note">' + tr('No shared closed round with another supplier.', 'لا جولات مغلقة مشتركة مع مورد آخر.') + '</p>';
  }
  html += '<ul class="oi-peers">';
  for (var i = 0; i < details.competitors.length; i++) {
    var competitor = details.competitors[i];
    html += '<li><button type="button" data-details-peer="' + competitor.supplierB + '">' + escapeHtml(competitor.nameB) +
      '<em>' + competitor.sharedRounds + tr(' shared rounds', ' جولات مشتركة') + '</em></button></li>';
  }
  return html + '</ul>';
}

// ======================================================================
// Supplier Performance Analytics (two charts + takeaways)
// ======================================================================

function setInsightTab(tab) {
  insightTab = tab;
  var buttons = document.querySelectorAll('[data-insight-tab]');
  for (var i = 0; i < buttons.length; i++) {
    var selected = buttons[i].dataset.insightTab === tab;
    buttons[i].setAttribute('aria-selected', selected ? 'true' : 'false');
    buttons[i].tabIndex = selected ? 0 : -1;
  }
  var panels = document.querySelectorAll('[data-insight-panel]');
  for (var j = 0; j < panels.length; j++) {
    panels[j].hidden = panels[j].dataset.insightPanel !== tab;
  }
  if (suppliersData !== null) {
    renderSupplierInsights(suppliersData);
  }
}

function renderSupplierInsights(data) {
  var period = periodParams();
  var shown = insightTab === 'positions' ? data.positions.length : data.suppliers.length;
  document.getElementById('ins-suppliers').textContent = shown + tr(' in view', ' معروض');
  document.getElementById('ins-category').textContent = data.filters.category || tr('All categories', 'جميع الفئات');
  document.getElementById('ins-period').textContent = period.from || period.to ? (period.from || tr('Start', 'البداية')) + ' – ' + (period.to || tr('Today', 'اليوم')) : tr('All history', 'كل الفترات');
  document.getElementById('ins-compare').textContent = insightTab === 'positions' ? tr('vs. bidding cycle median', 'مقابل وسيط دورة العطاء') : tr('offers ÷ invitations', 'العروض ÷ الدعوات');

  var items = [];
  if (insightTab === 'positions') {
    items.push([data.averageRelative === null ? '—' : formatSignedPercent(data.averageRelative, 0), tr('Avg. price position (shown offers)', 'متوسط موقع السعر (العروض المعروضة)')]);
    items.push([data.relativeSpread === null ? '—' : formatPercent(data.relativeSpread, 0), tr('Range across shown offers', 'المدى بين العروض المعروضة')]);
    items.push([data.lowestAverageName || '—', data.lowestAverageName ? tr('Lowest average (', 'أدنى متوسط (') + formatSignedPercent(data.lowestAverage, 0) + ')' : tr('No comparable offers', 'لا توجد عروض قابلة للمقارنة')]);
    items.push([formatInteger(data.shownOffers), tr('Comparable offers shown', 'العروض القابلة للمقارنة المعروضة')]);
    renderPositionsChart(data);
  } else {
    items.push([formatInteger(data.registeredSuppliers), tr('Suppliers in focus', 'الموردون في التركيز')]);
    items.push([formatInteger(data.participatingSuppliers), tr('Submitted an offer', 'قدموا عرضاً')]);
    items.push([data.responseRate === null ? '—' : formatPercent(data.responseRate), tr('Offers ÷ invitations', 'العروض ÷ الدعوات')]);
    items.push([data.leaderName || '—', tr('Most rounds with an offer', 'أكثر الجولات بعرض')]);
    renderLandscapeChart(data);
  }
  var html = '<div class="oi-takeaway-label"><strong>✦ ' + tr('Key Takeaways', 'أهم الاستنتاجات') + '</strong><small>' + tr('Calculated from the current scope', 'محسوبة من النطاق الحالي') + '</small></div>';
  for (var i = 0; i < items.length; i++) {
    html += '<div><strong>' + escapeHtml(items[i][0]) + '</strong><small>' + escapeHtml(items[i][1]) + '</small></div>';
  }
  document.getElementById('ins-takeaways').innerHTML = html;
}

// Scatter chart: x = cycles with an offer, y = win rate (or response rate), dot size = awarded value, colour = category.
function renderLandscapeChart(data) {
  var useWin = data.useWinRate;
  document.getElementById('ins-landscape-title').textContent = useWin ? tr('Participation vs win rate', 'المشاركة مقابل معدل الفوز') : tr('Participation vs response rate', 'المشاركة مقابل معدل الاستجابة');
  var note = useWin
    ? tr('Win rate = awards ÷ awarded tenders bid on.', 'معدل الفوز = الترسيات ÷ العطاءات المرساة التي شارك فيها.')
    : tr('No official awards in this period, so win rate is undefined; response = offers ÷ closed bidding cycle invitations.', 'لا ترسيات رسمية في هذه الفترة، لذا معدل الفوز غير معرف؛ الاستجابة = العروض ÷ دعوات الدورات المغلقة.');
  note += ' ' + tr('Colour = category.', 'اللون = الفئة.');
  if (data.notPlotted > 0) {
    note += ' ' + data.notPlotted + tr(' supplier(s) without an offer are not plotted.', ' مورد دون عرض غير معروض.');
  }
  document.getElementById('ins-landscape-note').textContent = note;

  var chart = getChart('chart-sup-landscape');
  if (!chart) {
    return;
  }
  if (data.landscape.length === 0) {
    showChartMessage(chart, tr('No supplier has an offer in a closed bidding cycle of this period.', 'لا يوجد مورد لديه عرض في دورة مغلقة ضمن هذه الفترة.'));
    return;
  }

  var series = [];
  for (var i = 0; i < data.landscape.length; i++) {
    var group = data.landscape[i];
    var color = categoryColor(group.color);
    var points = [];
    for (var j = 0; j < group.points.length; j++) {
      var supplier = group.points[j];
      var rate = useWin ? supplier.winRate : supplier.participationRate;
      var awarded = supplier.awardedValue.length > 0 ? supplier.awardedValue[0].value : 0;
      var isSelected = supplier.supplierId === selectedSupplierId;
      points.push({
        value: [supplier.participations, Number((rate * 100).toFixed(1)), awarded],
        supplier: supplier,
        itemStyle: { opacity: 0.95, borderColor: isSelected ? cssValue('--rd-orange') : cssValue('--rd-surface'), borderWidth: isSelected ? 3 : 1.5 }
      });
    }
    series.push({
      name: group.category, type: 'scatter', data: points, color: color,
      symbolSize: function (value) { return useWin ? 18 + Math.sqrt(value[2] / data.maxAwardedValue) * 34 : 22; },
      itemStyle: { shadowBlur: 14, shadowColor: withAlpha(color, 0.45) },
      emphasis: { focus: 'self', scale: 1.2 },
      label: { show: true, position: 'right', distance: 6, color: cssValue('--rd-ink-2'), fontSize: 14, fontWeight: 600, formatter: function (item) { return item.data.supplier.name; } },
      labelLayout: { hideOverlap: true }
    });
  }

  var options = chartBase();
  var legend = chartLegend();
  legend.top = 0;
  legend.left = 0;
  legend.type = 'scroll';
  options.legend = legend;
  options.grid = { left: 14, right: 40, top: 48, bottom: 30, containLabel: true };
  options.tooltip.formatter = function (item) {
    var supplier = item.data.supplier;
    var winText = supplier.winRate === null
      ? tr('undefined - no awarded tender bid on', 'غير معرف - لم يشارك في عطاء مرسى')
      : formatPercent(supplier.winRate, 1) + ' (' + supplier.wins + tr(' of ', ' من ') + supplier.awardedTendersBid + ')';
    var awardedText = supplier.awardedValue.length > 0 ? formatMoney(escapeHtml(supplier.awardedValue[0].currency), supplier.awardedValue[0].value) : tr('none', 'لا يوجد');
    return '<b>' + escapeHtml(supplier.name) + '</b><br>' + escapeHtml(supplier.category) +
      '<br>' + tr('Offers in ', 'عروض في ') + supplier.participations + tr(' closed bidding cycles (invited ', ' دورة مغلقة (دُعي ') + supplier.invitations + ')' +
      '<br>' + tr('Response rate: ', 'معدل الاستجابة: ') + formatPercent(supplier.participationRate) +
      '<br>' + tr('Win rate: ', 'معدل الفوز: ') + winText + '<br>' + tr('Awarded: ', 'المرسى: ') + awardedText;
  };
  var xAxis = chartAxis();
  xAxis.name = tr('Closed bidding cycles with an offer', 'دورات مغلقة بعرض');
  xAxis.nameLocation = 'middle';
  xAxis.nameGap = 32;
  xAxis.minInterval = 1;
  var yAxis = chartAxis();
  yAxis.name = useWin ? tr('Win rate %', 'معدل الفوز %') : tr('Response rate %', 'معدل الاستجابة %');
  yAxis.min = 0;
  yAxis.max = 100;
  options.xAxis = xAxis;
  options.yAxis = yAxis;
  options.series = series;
  chart.setOption(options, true);

  chart.off('click');
  chart.on('click', function (item) {
    if (item.data && item.data.supplier) {
      openSupplierDetails(item.data.supplier.supplierId);
    }
  });
}

// Line chart: each offer of the four most-priced suppliers, as % above (+) or below (-) its cycle median.
function renderPositionsChart(data) {
  var chart = getChart('chart-sup-positions');
  if (!chart) {
    return;
  }
  if (data.positions.length === 0) {
    showChartMessage(chart, tr('No round had two comparable offers in the same currency, so relative prices cannot be calculated yet.', 'لا توجد جولة بعرضين قابلين للمقارنة بالعملة نفسها، لذا لا يمكن حساب الأسعار النسبية بعد.'));
    return;
  }

  var series = [];
  for (var i = 0; i < data.positions.length; i++) {
    var line = data.positions[i];
    var color = supplierColor(line.supplierColor);
    var points = [];
    for (var j = 0; j < line.points.length; j++) {
      points.push({ value: [line.points[j].date, Number((line.points[j].relative * 100).toFixed(2))], position: line.points[j] });
    }
    var item = {
      name: line.name, type: 'line', symbol: 'circle', symbolSize: 12, smooth: 0.3, emphasis: { focus: 'series' }, data: points,
      lineStyle: { width: 3.2, color: color, shadowBlur: 12, shadowColor: withAlpha(color, 0.35) },
      itemStyle: { color: color, borderColor: cssValue('--rd-surface'), borderWidth: 2.5 }
    };
    if (data.positions.length === 1) {
      item.areaStyle = { origin: 0, color: gradient(true, withAlpha(color, 0.22), withAlpha(color, 0.02)) };
    }
    if (i === 0) {
      // Dashed line at 0% = exactly the cycle median.
      item.markLine = {
        silent: true, symbol: 'none', data: [{ yAxis: 0 }],
        lineStyle: { color: cssValue('--rd-muted'), type: [6, 6], width: 1.6 },
        label: { formatter: tr('bidding cycle median', 'وسيط دورة العطاء'), position: 'insideEndTop', color: cssValue('--rd-muted'), fontSize: 13.5, fontWeight: 600 }
      };
    }
    series.push(item);
  }

  var options = chartBase();
  var legend = chartLegend();
  legend.top = 'middle';
  legend.right = 4;
  legend.orient = 'vertical';
  options.legend = legend;
  options.grid = { left: 14, right: '23%', top: 22, bottom: 14, containLabel: true };
  options.tooltip.trigger = 'item';
  options.tooltip.formatter = function (point) {
    var position = point.data.position;
    return '<b>' + escapeHtml(position.supplierName) + '</b><br>' + formatDate(position.date) + tr(' · round #', ' · الجولة #') + position.roundId +
      '<br>' + tr('Offer ', 'العرض ') + formatMoney(escapeHtml(position.currency), position.total) + tr(' vs median ', ' مقابل الوسيط ') + formatMoney(escapeHtml(position.currency), position.median) +
      '<br>' + tr('Position ', 'الموقع ') + formatSignedPercent(position.relative) + ' (' + position.comparable + tr(' comparable offers)', ' عروض قابلة للمقارنة)');
  };
  var xAxis = chartAxis();
  xAxis.type = 'time';
  var yAxis = chartAxis();
  yAxis.type = 'value';
  yAxis.name = tr('vs bidding cycle median', 'مقابل وسيط دورة العطاء');
  yAxis.axisLabel = { color: cssValue('--rd-muted'), fontSize: 14, formatter: function (value) { return (value > 0 ? '+' : '') + value + '%'; } };
  options.xAxis = xAxis;
  options.yAxis = yAxis;
  options.series = series;
  chart.setOption(options, true);

  chart.off('click');
  chart.on('click', function (point) {
    if (point.data && point.data.position) {
      openSupplierDetails(point.data.position.supplierId);
    }
  });
}

// The "Table" link under a chart shows the same numbers as an accessible table (and hides it on the next click).
function supplierChartTableHtml(name) {
  var data = suppliersData;
  var html = '';
  if (name === 'landscape') {
    html = '<table class="rd-table"><thead><tr><th>' + tr('Supplier', 'المورد') + '</th><th>' + tr('Category', 'الفئة') + '</th><th class="num">' + tr('Invited', 'مدعو') + '</th>' +
      '<th class="num">' + tr('Bidding Cycles Participated In', 'دورات شارك فيها') + '</th><th class="num">' + tr('Response', 'الاستجابة') + '</th>' +
      '<th class="num">' + tr('Wins / awarded bid', 'الفوز / المرسى') + '</th><th>' + tr('Awarded value', 'القيمة المرساة') + '</th><th class="num">' + tr('Rel. price', 'السعر النسبي') + '</th></tr></thead><tbody>';
    for (var i = 0; i < data.suppliers.length; i++) {
      var supplier = data.suppliers[i];
      var values = [];
      for (var v = 0; v < supplier.awardedValue.length; v++) {
        values.push(formatMoney(escapeHtml(supplier.awardedValue[v].currency), supplier.awardedValue[v].value));
      }
      html += '<tr data-supplier-row-open="' + supplier.supplierId + '"><td>' + escapeHtml(supplier.name) + '</td><td>' + escapeHtml(supplier.category) + '</td>' +
        '<td class="num">' + supplier.invitations + '</td><td class="num">' + supplier.participations + '</td><td class="num">' + formatPercent(supplier.participationRate) + '</td>' +
        '<td class="num">' + supplier.wins + ' / ' + supplier.awardedTendersBid + '</td><td>' + (values.join(', ') || '—') + '</td><td class="num">' + formatSignedPercent(supplier.relativePrice) + '</td></tr>';
    }
    return html + '</tbody></table>';
  }
  html = '<table class="rd-table"><thead><tr><th>' + tr('Supplier', 'المورد') + '</th><th>' + tr('Date', 'التاريخ') + '</th><th>' + tr('Bidding Cycle ID', 'رقم الدورة') + '</th>' +
    '<th class="num">' + tr('Offer', 'العرض') + '</th><th class="num">' + tr('Median', 'الوسيط') + '</th><th class="num">' + tr('Position', 'الموقع') + '</th></tr></thead><tbody>';
  for (var j = 0; j < data.positionRows.length; j++) {
    var row = data.positionRows[j];
    html += '<tr data-supplier-row-open="' + row.supplierId + '"><td>' + escapeHtml(row.supplierName) + '</td><td>' + formatDate(row.date) + '</td><td>#' + row.roundId + '</td>' +
      '<td class="num">' + formatMoney(escapeHtml(row.currency), row.total) + '</td><td class="num">' + formatMoney(escapeHtml(row.currency), row.median) + '</td><td class="num">' + formatSignedPercent(row.relative) + '</td></tr>';
  }
  return html + '</tbody></table>';
}

// ======================================================================
// Market relationships (who competes with whom)
// ======================================================================

function renderSupplierRelationships(data) {
  document.getElementById('rel-note').textContent = tr('Suppliers that submitted offers in the same closed bidding cycles. ', 'موردون قدموا عروضاً في دورات العطاء المغلقة نفسها. ') +
    data.pairCount + tr(' pair(s)', ' زوج') + (data.pairCount > data.pairs.length ? tr(' · top ', ' · أعلى ') + data.pairs.length + tr(' shown', ' معروضة') : '') +
    tr('. Meeting in tenders is ordinary market context, never a suspicion.', '. الالتقاء في العطاءات سياق سوق عادي وليس شبهة.');

  var pairsHtml = '<p class="oi-empty-note">' + tr('No supplier pair met in a closed bidding cycle.', 'لم يلتقِ أي زوج من الموردين في دورة مغلقة.') + '</p>';
  if (data.pairs.length > 0) {
    pairsHtml = '<table class="oi-table"><thead><tr><th>' + tr('Supplier', 'المورد') + '</th><th>' + tr('Competitor', 'المنافس') + '</th>' +
      '<th class="num">' + tr('Shared cycles', 'دورات مشتركة') + '</th><th></th></tr></thead><tbody>';
    for (var i = 0; i < data.pairs.length; i++) {
      var pair = data.pairs[i];
      pairsHtml += '<tr><td><button type="button" class="rd-link" data-supplier-open="' + pair.supplierA + '">' + escapeHtml(pair.nameA) + '</button></td>' +
        '<td><button type="button" class="rd-link" data-supplier-open="' + pair.supplierB + '">' + escapeHtml(pair.nameB) + '</button></td>' +
        '<td class="num"><b>' + pair.sharedRounds + '</b></td>' +
        '<td><button type="button" class="rd-btn oi-mini-btn" data-compare-pair="' + pair.supplierA + ',' + pair.supplierB + '">' + tr('Compare offers', 'قارن العروض') + '</button></td></tr>';
    }
    pairsHtml += '</tbody></table>';
  }
  document.getElementById('rel-pairs').innerHTML = pairsHtml;

  var groupsHtml = '';
  for (var g = 0; g < data.groups.length; g++) {
    groupsHtml += '<div class="oi-group"><b>' + tr('Group ', 'المجموعة ') + (g + 1) + ' · ' + data.groups[g].names.length + tr(' suppliers', ' موردين') + '</b>' +
      '<span>' + escapeHtml(data.groups[g].names.join(', ')) + '</span></div>';
  }
  if (groupsHtml === '') {
    groupsHtml = '<p class="oi-empty-note">' + tr('No group: no pair shares ', 'لا توجد مجموعات: لا يشترك أي زوج في ') + data.competitionMinimum + tr('+ closed cycles.', '+ دورات مغلقة.') + '</p>';
  }
  groupsHtml += '<p class="oi-note">' + tr('A group links suppliers that met in ', 'تربط المجموعة موردين التقوا في ') + data.competitionMinimum +
    tr('+ shared closed cycles, directly or through each other. ', '+ دورات مغلقة مشتركة، مباشرة أو عبر بعضهم. ') + data.unconnectedSuppliers + tr(' supplier(s) have no such link.', ' مورد بلا رابط كهذا.') + '</p>';
  document.getElementById('rel-groups').innerHTML = groupsHtml;
}

// ======================================================================
// Supplier offer comparison
// ======================================================================

function renderComparisonControls() {
  var suppliers = suppliersData ? suppliersData.allSuppliers : [];
  fillComparisonSupplierSelect('cmp-supplier-a', suppliers, comparison.a, comparison.b);
  fillComparisonSupplierSelect('cmp-supplier-b', suppliers, comparison.b, comparison.a);

  var tenderSelect = document.getElementById('cmp-tender');
  var html = '<option value="">' + tr('Choose a shared tender', 'اختر عطاءً مشتركاً') + '</option>';
  for (var i = 0; i < comparison.rows.length; i++) {
    html += '<option value="' + comparison.rows[i].tenderId + '">' + escapeHtml(comparison.rows[i].title) + '</option>';
  }
  tenderSelect.innerHTML = html;
  tenderSelect.value = comparison.tenderId === null ? '' : String(comparison.tenderId);
  tenderSelect.disabled = comparison.rows.length === 0;
}

// Fills one supplier drop-down; the supplier chosen in the other drop-down is left out.
function fillComparisonSupplierSelect(selectId, suppliers, selectedId, otherId) {
  var select = document.getElementById(selectId);
  var html = '<option value="">' + tr('Choose a supplier', 'اختر مورداً') + '</option>';
  for (var i = 0; i < suppliers.length; i++) {
    if (suppliers[i].supplierId !== otherId) {
      html += '<option value="' + suppliers[i].supplierId + '">' + escapeHtml(suppliers[i].name) + '</option>';
    }
  }
  select.innerHTML = html;
  select.value = selectedId === null ? '' : String(selectedId);
  select.disabled = suppliers.length === 0;
}

async function loadComparison() {
  comparison.rows = [];
  var wantedTender = comparison.tenderId;
  comparison.tenderId = null;
  renderComparisonControls();
  if (comparison.a === null || comparison.b === null) {
    renderComparison();
    return;
  }
  renderComparison(tr('Finding shared tenders…', 'جارٍ البحث عن العطاءات المشتركة…'));
  try {
    var rows = await getJson(page.dataset.urlCompareSuppliers, { supplierA: comparison.a, supplierB: comparison.b });
    comparison.rows = rows;
    // Keep the tender chosen before if it is still there; with only one shared tender, choose it.
    for (var i = 0; i < rows.length; i++) {
      if (rows[i].tenderId === wantedTender) {
        comparison.tenderId = wantedTender;
      }
    }
    if (comparison.tenderId === null && rows.length === 1) {
      comparison.tenderId = rows[0].tenderId;
    }
    renderComparisonControls();
    if (rows.length === 0) {
      renderComparison(tr('These suppliers have no eligible shared tenders. Both must have submitted in the same closed bidding cycle.',
        'لا توجد عطاءات مشتركة مؤهلة لهذين الموردين. يجب أن يقدم كلاهما عرضاً في دورة العطاء المغلقة نفسها.'));
    } else {
      renderComparison();
    }
  } catch (error) {
    renderComparison(tr('The comparison could not be loaded. Select a supplier again to retry.', 'تعذر تحميل المقارنة. أعد اختيار أحد الموردين للمحاولة مجدداً.'));
  }
}

// Used by "Compare offers" in the details panel and in the relationships table.
function compareSuppliers(supplierA, supplierB) {
  comparison.a = supplierA;
  comparison.b = supplierB;
  comparison.tenderId = null;
  loadComparison();
  document.getElementById('cmp-section').scrollIntoView({ behavior: 'smooth', block: 'start' });
}

function selectedComparisonRow() {
  for (var i = 0; i < comparison.rows.length; i++) {
    if (comparison.rows[i].tenderId === comparison.tenderId) {
      return comparison.rows[i];
    }
  }
  return null;
}

// 93784.5 seconds -> "1 d 2 h 3 min 4.5 s"
function formatDuration(seconds) {
  var days = Math.floor(seconds / 86400);
  var hours = Math.floor((seconds % 86400) / 3600);
  var minutes = Math.floor((seconds % 3600) / 60);
  var rest = Number((seconds % 60).toFixed(7));
  var parts = [];
  if (days !== 0) { parts.push(days + ' ' + tr('d', 'يوم')); }
  if (hours !== 0) { parts.push(hours + ' ' + tr('h', 'ساعة')); }
  if (minutes !== 0) { parts.push(minutes + ' ' + tr('min', 'د')); }
  if (rest !== 0 || parts.length === 0) { parts.push(rest.toLocaleString('en-US', { maximumFractionDigits: 7 }) + ' ' + tr('s', 'ث')); }
  return parts.join(' ');
}

function comparisonAmount(currency, total) {
  if (total === null) {
    return tr('Not recorded', 'غير مسجل');
  }
  return total.toLocaleString('en-US', { maximumFractionDigits: 6 }) + ' ' + (currency || tr('(currency unrecorded)', '(العملة غير مسجلة)'));
}

// Dates and times of the comparison are shown in Amman time (the server sends them with the +03:00 offset).
function ammanDate(value) {
  return new Intl.DateTimeFormat(pageIsArabic ? 'ar-JO' : 'en-JO', { timeZone: 'Asia/Amman', dateStyle: 'medium' }).format(new Date(value));
}

function ammanTime(value) {
  return new Intl.DateTimeFormat(pageIsArabic ? 'ar-JO' : 'en-JO', { timeZone: 'Asia/Amman', timeStyle: 'medium' }).format(new Date(value));
}

// One offer in the price card: name, amount, a bar (relative to the higher offer) and a link to the offer record.
function comparisonPriceHtml(row, offer, key) {
  var isLower = row.lowerOffer === key;
  var bar = '';
  if (row.priceIssue === null) {
    var width = offer.total / Math.max(row.a.total, row.b.total) * 100;
    bar = '<div class="oi-comparison-bar" aria-hidden="true"><span style="width:' + width + '%"></span></div>';
  }
  return '<div class="oi-comparison-offer ' + (isLower ? 'is-lower' : '') + '">' +
    '<div class="oi-comparison-name"><strong>' + escapeHtml(offer.supplier) + '</strong>' + (isLower ? '<span class="oi-comparison-badge">' + tr('Lower offer', 'العرض الأقل') + '</span>' : '') + '</div>' +
    '<bdi class="oi-comparison-amount">' + escapeHtml(comparisonAmount(offer.currency, offer.total)) + '</bdi>' + bar +
    '<a class="rd-link" href="' + escapeHtml(page.dataset.urlOfferRecord) + '?id=' + offer.bidId + '">' + tr('Open offer record', 'افتح سجل العرض') + ' #' + offer.bidId + '</a></div>';
}

// One offer in the timing card: submission time and its distance to the deadline.
function comparisonTimingHtml(row, offer, key) {
  var before = offer.secondsBeforeDeadline;
  var deadlineText = tr('Time to deadline unavailable', 'المدة حتى الموعد النهائي غير متاحة');
  if (before === 0) {
    deadlineText = tr('At the deadline', 'عند الموعد النهائي');
  } else if (before > 0) {
    deadlineText = tr(formatDuration(before) + ' before the deadline', 'قبل الموعد النهائي بـ ' + formatDuration(before));
  } else if (before < 0) {
    deadlineText = tr(formatDuration(-before) + ' after the deadline', 'بعد الموعد النهائي بـ ' + formatDuration(-before));
  }
  var timeText = tr('Not recorded', 'غير مسجل');
  if (offer.submittedAt) {
    timeText = ammanDate(offer.submittedAt) + ' · ' + String(offer.recordedTime).split('.')[0];
  }
  return '<div class="oi-comparison-event">' +
    '<div class="oi-comparison-name"><strong>' + escapeHtml(offer.supplier) + '</strong>' + (row.firstSubmission === key ? '<span class="oi-comparison-badge">' + tr('Submitted first', 'قُدّم أولاً') + '</span>' : '') + '</div>' +
    '<bdi class="oi-comparison-time" title="' + escapeHtml(offer.recordedTime || '') + '">' + escapeHtml(timeText) + '</bdi>' +
    '<span class="oi-comparison-meta">' + deadlineText + '</span></div>';
}

function renderComparison(message) {
  var row = selectedComparisonRow();
  var result = document.getElementById('cmp-result');
  var status = document.getElementById('cmp-status');
  result.hidden = row === null;
  status.hidden = row !== null;
  if (row === null) {
    result.innerHTML = '';
    if (message) {
      status.textContent = message;
    } else if (comparison.a !== null && comparison.b !== null) {
      status.textContent = tr('Choose a tender to see both suppliers’ recorded offers.', 'اختر عطاءً لعرض العرضين المسجلين.');
    } else {
      status.textContent = tr('Choose two different suppliers to find their shared tenders.', 'اختر موردين مختلفين للعثور على عطاءاتهما المشتركة.');
    }
    return;
  }

  var issues = {
    missing_currency: tr('A currency is missing.', 'عملة أحد العرضين غير مسجلة.'),
    different_currency: tr('The offers use different currencies.', 'العرضان بعملتين مختلفتين.'),
    missing_price: tr('Both offers need recorded items and positive totals.', 'تتطلب المقارنة بنوداً مسجلة وإجماليات موجبة لكلا العرضين.'),
    different_structure: tr('The recorded items, quantities or units differ or are incomplete.', 'البنود أو الكميات أو الوحدات المسجلة مختلفة أو غير مكتملة.'),
    different_specifications: tr('The recorded offered specifications or deviations differ.', 'المواصفات أو الانحرافات المسجلة في العرضين مختلفة.')
  };

  // Price summary text.
  var priceText = '';
  if (row.priceIssue !== null) {
    priceText = issues[row.priceIssue];
  } else if (row.lowerOffer !== null) {
    var lower = row.lowerOffer === 'a' ? row.a : row.b;
    var higher = row.lowerOffer === 'a' ? row.b : row.a;
    var percent = (row.relativeDifference * 100).toLocaleString('en-US', { maximumFractionDigits: 2 }) + '%';
    priceText = tr(lower.supplier + ' offered ' + percent + ' less than ' + higher.supplier + '. The percentage is the difference divided by the higher offer.',
      'عرض ' + lower.supplier + ' أقل بنسبة ' + percent + ' من عرض ' + higher.supplier + '. تُحسب النسبة بقسمة الفرق على العرض الأعلى.');
  } else {
    priceText = tr('Both financial totals are equal. The difference is 0% of either offer.', 'إجمالي العرضين متساوٍ. الفرق 0٪ من أيٍّ من العرضين.');
  }

  // Timing summary text.
  var timeText = '';
  if (row.secondsApart === null) {
    timeText = tr('One or both submission timestamps are missing.', 'وقت تقديم أحد العرضين أو كليهما غير مسجل.');
  } else if (row.secondsApart === 0) {
    timeText = tr('Both offers have the same recorded timestamp.', 'للعرضين وقت تقديم مسجل متطابق.');
  } else {
    var first = row.firstSubmission === 'a' ? row.a.supplier : row.b.supplier;
    timeText = tr(first + ' submitted first.', 'قدّم ' + first + ' عرضه أولاً.');
  }

  var deadline = tr('Deadline not recorded', 'الموعد النهائي غير مسجل');
  if (row.deadline) {
    deadline = tr('Deadline', 'الموعد النهائي') + ' · <bdi>' + ammanDate(row.deadline) + ' · ' + ammanTime(row.deadline) + '</bdi>';
  }
  var timeline = row.firstSubmission === 'b'
    ? comparisonTimingHtml(row, row.b, 'b') + comparisonTimingHtml(row, row.a, 'a')
    : comparisonTimingHtml(row, row.a, 'a') + comparisonTimingHtml(row, row.b, 'b');

  result.innerHTML =
    '<div class="oi-comparison-context"><div><strong>' + escapeHtml(row.title) + ' · ' + escapeHtml(cycleLabel(row.roundNumber)) + '</strong>' +
    '<span>' + (row.priceIssue ? tr('Latest shared closed bidding cycle.', 'أحدث دورة عطاء مغلقة مشتركة.') : tr('Based on the latest comparable closed bidding cycle.', 'استناداً إلى أحدث دورة عطاء مغلقة قابلة للمقارنة.')) + '</span></div>' +
    '<button type="button" class="rd-btn" id="cmp-open-tender">' + tr('Open tender details', 'افتح تفاصيل العطاء') + '</button></div>' +
    '<div class="oi-comparison-sections">' +
    '<section class="oi-comparison-card"><h4>' + tr('Price Comparison', 'مقارنة الأسعار') + '</h4>' +
    '<p class="oi-comparison-meta">' + tr('Total financial offers', 'إجمالي العروض المالية') + '</p>' +
    '<div class="oi-comparison-price-rows">' + comparisonPriceHtml(row, row.a, 'a') + comparisonPriceHtml(row, row.b, 'b') + '</div>' +
    '<div class="oi-comparison-summary"><small>' + (row.priceIssue ? tr('Price comparison', 'مقارنة الأسعار') : tr('Absolute difference', 'الفرق المطلق')) + '</small>' +
    '<strong>' + (row.priceIssue ? tr('Not directly comparable', 'غير قابل للمقارنة المباشرة') : escapeHtml(comparisonAmount(row.a.currency, row.difference))) + '</strong>' +
    '<p>' + escapeHtml(priceText) + '</p></div>' +
    '<p class="oi-comparison-meta">' + tr('The award depends on the applicable evaluation criteria and official decision.', 'تخضع الترسية لمعايير التقييم والقرار الرسمي.') + '</p></section>' +
    '<section class="oi-comparison-card"><h4>' + tr('Submission Timing', 'توقيت التقديم') + '</h4>' +
    '<p class="oi-comparison-meta">' + deadline + ' · ' + tr('Amman time', 'توقيت عمّان') + '</p>' +
    '<div class="oi-comparison-timeline">' + timeline + '</div>' +
    '<div class="oi-comparison-summary"><small>' + tr('Time between submissions', 'المدة بين التقديمين') + '</small>' +
    '<strong>' + (row.secondsApart === null ? tr('Unavailable', 'غير متاح') : formatDuration(row.secondsApart)) + '</strong><p>' + escapeHtml(timeText) + '</p></div>' +
    '<p class="oi-comparison-meta">' + tr('Close submission times describe timing alone and do not establish wrongdoing.', 'تقارب أوقات التقديم يصف التوقيت وحده ولا يثبت أي مخالفة.') + '</p></section>' +
    '</div>';

  document.getElementById('cmp-open-tender').addEventListener('click', function () {
    openTenderFromAnotherTab(row.tenderId, row.roundId);
  });
}

// ======================================================================
// What-if scenario
// ======================================================================

function renderScenarioPick(data) {
  var box = document.getElementById('sim-pick');
  // Keep the boxes the user already ticked.
  var checked = [];
  var inputs = box.querySelectorAll('input:checked');
  for (var i = 0; i < inputs.length; i++) {
    checked.push(Number(inputs[i].value));
  }
  var html = '';
  for (var j = 0; j < data.scenarioSuppliers.length; j++) {
    var supplier = data.scenarioSuppliers[j];
    var isChecked = checked.indexOf(supplier.supplierId) >= 0;
    html += '<label class="' + (isChecked ? 'is-on' : '') + '"><input type="checkbox" value="' + supplier.supplierId + '" ' + (isChecked ? 'checked' : '') + ' />' +
      '<i class="rd-dot" style="background:' + categoryColor(supplier.categoryColor) + '"></i>' + escapeHtml(supplier.name) + '<small>' + supplier.participations + '</small></label>';
  }
  if (html === '') {
    html = '<span class="rd-muted">' + tr('No participating suppliers.', 'لا يوجد موردون مشاركون.') + '</span>';
  }
  box.innerHTML = html;
}

async function runSimulation() {
  var result = document.getElementById('sim-result');
  var ids = [];
  var inputs = document.querySelectorAll('#sim-pick input:checked');
  for (var i = 0; i < inputs.length; i++) {
    ids.push(Number(inputs[i].value));
  }
  if (ids.length === 0) {
    result.innerHTML = '<p class="rd-muted">' + tr('Choose at least one supplier to exclude.', 'اختر مورداً واحداً على الأقل لاستبعاده.') + '</p>';
    return;
  }
  result.innerHTML = '<p class="rd-muted">' + tr('Replaying history without the chosen suppliers…', 'جارٍ إعادة التاريخ دون الموردين المختارين…') + '</p>';
  try {
    var period = periodParams();
    var simulation = await postJson(page.dataset.urlSimulate, { from: period.from, to: period.to, supplierIds: ids });
    result.innerHTML = simulationHtml(simulation);
  } catch (error) {
    result.innerHTML = '<p class="rd-note">' + escapeHtml(error.message) + '</p>';
  }
}

function beforeAfterHtml(label, before, after) {
  return '<div class="rd-before-after"><span>' + label + '</span><b>' + before + '</b><i aria-hidden="true">→</i><b class="' + (after > before ? 'is-worse' : '') + '">' + after + '</b></div>';
}

function simulationHtml(simulation) {
  var html = '<div class="rd-note"><b>' + tr('Hypothetical.', 'افتراضي.') + '</b> ' + escapeHtml(simulation.notice) + '</div>';
  html += '<div class="rd-ba-grid">' +
    beforeAfterHtml(tr('Bidding cycles below ', 'دورات دون ') + simulation.minimumOffers + tr(' offers', ' عروض'), simulation.roundsBelowMinimumBefore, simulation.roundsBelowMinimumAfter) +
    beforeAfterHtml(tr('Offer concentration (HHI)', 'تركّز العروض (HHI)'), formatNumber(simulation.hhiBefore, 3), formatNumber(simulation.hhiAfter, 3)) +
    '<div class="rd-before-after"><span>' + tr('Bidding cycles left with no bidder', 'دورات بلا أي مقدم') + '</span><b>' + simulation.roundsWithoutBidders + '</b></div>' +
    '<div class="rd-before-after"><span>' + tr('Share of offers removed', 'نسبة العروض المحذوفة') + '</span><b>' + formatPercent(simulation.bidShareRemoved, 1) + '</b></div></div>';

  var exposure = [];
  for (var i = 0; i < simulation.awardExposure.length; i++) {
    exposure.push(formatMoney(escapeHtml(simulation.awardExposure[i].currency), simulation.awardExposure[i].value) + ' (' + simulation.awardExposure[i].count + ')');
  }
  var excluded = [];
  for (var e = 0; e < simulation.excludedNames.length; e++) {
    excluded.push(escapeHtml(simulation.excludedNames[e]));
  }
  html += '<dl class="rd-kv"><dt>' + tr('Excluded', 'المستبعدون') + '</dt><dd>' + excluded.join('<br>') + '</dd>' +
    '<dt>' + tr('Bidding cycles affected', 'الدورات المتأثرة') + '</dt><dd>' + simulation.roundsAffected + '</dd>' +
    '<dt>' + tr('Award value previously won', 'قيمة الترسيات السابقة') + '</dt><dd>' + (exposure.join('<br>') || tr('none', 'لا يوجد')) + '</dd></dl>';

  html += '<h4 class="rd-section-title">' + tr('Affected bidding cycles', 'الدورات المتأثرة') + '</h4><ul class="rd-sim-rounds">';
  var largest = 1;
  for (var r = 0; r < simulation.rounds.length; r++) {
    largest = Math.max(largest, simulation.rounds[r].before);
  }
  for (var k = 0; k < simulation.rounds.length; k++) {
    var round = simulation.rounds[k];
    html += '<li><span>' + escapeHtml(round.tender) + ' · ' + cycleLabel(round.roundNumber) + '</span>' +
      '<i><u style="width:' + (round.before / largest) * 100 + '%"></u><u class="is-after" style="width:' + (round.after / largest) * 100 + '%"></u></i>' +
      '<b>' + round.before + ' → ' + round.after + '</b>' +
      (round.dropsBelowMinimum ? '<em>' + tr('below minimum', 'دون الحد الأدنى') + '</em>' : '') +
      (round.losesAward ? '<em>' + tr('loses awarded supplier', 'يفقد المورد المرسى عليه') + '</em>' : '') + '</li>';
  }
  if (simulation.rounds.length === 0) {
    html += '<li class="rd-muted">' + tr('None', 'لا يوجد') + '</li>';
  }
  html += '</ul>';

  html += '<h4 class="rd-section-title">' + tr('Eligible alternatives in the same category', 'بدائل مؤهلة من الفئة نفسها') + '</h4><div class="rd-chip-list">';
  for (var a = 0; a < simulation.alternatives.length; a++) {
    var alternative = simulation.alternatives[a];
    html += '<button type="button" data-supplier-open="' + alternative.supplierId + '">' + escapeHtml(alternative.name) + ' · ' + alternative.participations + tr(' rounds', ' جولات') + '</button>';
  }
  if (simulation.alternatives.length === 0) {
    html += '<span class="rd-muted">' + tr('No other approved supplier in the same category.', 'لا يوجد مورد معتمد آخر في الفئة نفسها.') + '</span>';
  }
  return html + '</div>';
}

// ======================================================================
// Events of this tab
// ======================================================================

function setupSuppliersEvents() {
  document.getElementById('sup-department').addEventListener('change', function (event) { setSupplierFilter('department', event.target.value); });
  document.getElementById('sup-category').addEventListener('change', function (event) { setSupplierFilter('category', event.target.value); });
  document.getElementById('sup-activity').addEventListener('change', function (event) { setSupplierFilter('activity', event.target.value); });

  // Search: wait until the user stops typing for 300 ms, then ask the server.
  document.getElementById('sup-search').addEventListener('input', function (event) {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(function () { setSupplierFilter('search', event.target.value.trim()); }, 300);
  });

  document.getElementById('sup-category-legend').addEventListener('click', function (event) {
    var button = event.target.closest('[data-category]');
    if (button) {
      toggleCategoryFilter(button.dataset.category);
    }
  });

  // One click listener for the whole Suppliers panel: every button that opens a supplier or compares two suppliers.
  document.getElementById('oi-suppliers').addEventListener('click', function (event) {
    var openButton = event.target.closest('[data-supplier-open], [data-supplier-row-open]');
    var pairButton = event.target.closest('[data-compare-pair]');
    var tableButton = event.target.closest('[data-table-for]');
    if (pairButton) {
      var ids = pairButton.dataset.comparePair.split(',');
      compareSuppliers(Number(ids[0]), Number(ids[1]));
    } else if (openButton) {
      openSupplierDetails(Number(openButton.dataset.supplierOpen || openButton.dataset.supplierRowOpen));
      // A button below the universe (directory, relationships, scenario, chart table): scroll up to the universe and details panel.
      if (!openButton.closest('.oi-explorer')) {
        document.querySelector('.oi-explorer').scrollIntoView({ behavior: 'smooth', block: 'start' });
      }
    } else if (tableButton && suppliersData !== null) {
      toggleChartTable(tableButton, supplierChartTableHtml(tableButton.dataset.tableFor));
    }
  });

  // Clicking anywhere on a directory row opens its details.
  document.getElementById('sup-table').addEventListener('click', function (event) {
    var row = event.target.closest('[data-supplier-row]');
    if (row && !event.target.closest('[data-supplier-open]')) {
      openSupplierDetails(Number(row.dataset.supplierRow));
    }
  });

  // Details panel: tabs, close, a cycle (-> Tenders tab), a competitor (-> its details), Ask AI, Compare offers.
  document.getElementById('sup-details-close').addEventListener('click', closeSupplierDetails);
  document.getElementById('sup-details-content').addEventListener('click', function (event) {
    var tabButton = event.target.closest('[data-details-tab]');
    var roundButton = event.target.closest('[data-details-round]');
    var peerButton = event.target.closest('[data-details-peer]');
    if (tabButton) {
      detailsTab = tabButton.dataset.detailsTab;
      renderSupplierDetails();
    } else if (roundButton) {
      openTenderFromAnotherTab(Number(roundButton.dataset.detailsTender), Number(roundButton.dataset.detailsRound));
    } else if (peerButton) {
      openSupplierDetails(Number(peerButton.dataset.detailsPeer));
    }
  });
  document.getElementById('sup-ask-ai').addEventListener('click', function () {
    // AI Studio's own function (intelligence-ai.js): selects the supplier there and opens the AI Studio tab.
    if (window.OrangeIntelligenceAI && selectedSupplierId !== null) {
      window.OrangeIntelligenceAI.chooseSupplier(selectedSupplierId);
    }
  });
  document.getElementById('sup-compare-offers').addEventListener('click', function () {
    compareSuppliers(selectedSupplierId, comparison.b === selectedSupplierId ? null : comparison.b);
  });

  // Insight tabs (click and arrow keys).
  var insightButtons = document.querySelectorAll('[data-insight-tab]');
  for (var i = 0; i < insightButtons.length; i++) {
    insightButtons[i].addEventListener('click', function (event) { setInsightTab(event.currentTarget.dataset.insightTab); });
    insightButtons[i].addEventListener('keydown', function (event) {
      if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
        event.preventDefault();
        var next = insightTab === 'positions' ? 'landscape' : 'positions';
        setInsightTab(next);
        document.querySelector('[data-insight-tab="' + next + '"]').focus();
      }
    });
  }

  // Comparison drop-downs.
  document.getElementById('cmp-supplier-a').addEventListener('change', function (event) {
    comparison.a = event.target.value ? Number(event.target.value) : null;
    loadComparison();
  });
  document.getElementById('cmp-supplier-b').addEventListener('change', function (event) {
    comparison.b = event.target.value ? Number(event.target.value) : null;
    loadComparison();
  });
  document.getElementById('cmp-tender').addEventListener('change', function (event) {
    comparison.tenderId = event.target.value ? Number(event.target.value) : null;
    renderComparison();
  });

  // What-if scenario.
  document.getElementById('sim-pick').addEventListener('change', function (event) {
    event.target.closest('label').classList.toggle('is-on', event.target.checked);
  });
  document.getElementById('sim-run').addEventListener('click', runSimulation);
}
