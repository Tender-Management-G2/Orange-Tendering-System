// ORANGE INTELLIGENCE - Tab 3 "Tenders".
// Main flow: showTab('tenders') -> openTendersTab() -> loadTenders() -> GET /Intelligence/Tenders
//            -> IntelligenceController.Tenders -> IntelligenceService.GetTendersAsync -> TenderCalculator.Build
//            -> JSON (TendersData: tender list + market charts) -> renderTendersTab().
// Selecting a tender: selectTender() -> loadTenderDetails() -> GET /Intelligence/TenderDetails?tenderId=
//            -> TenderCalculator.BuildTender -> JSON (TenderDetails: lifecycle, cycles, offers) -> renderTenderDetails().
// Excel / PDF: exportTender() -> downloadFile() (intelligence-page.js) -> POST /Intelligence/Export -> IntelligenceExportService.
// Sealed cycles (bidding still open) never send an offer to the browser: the server leaves them empty.

var tendersData = null;
var tenderDetails = null;
var selectedTenderId = null;
var pendingTender = null;
var offerRoundId = null;
var offerGroupId = null;
var offerBidId = null;

// ======================================================================
// Loading
// ======================================================================

function openTendersTab() {
  if (tendersData === null) {
    loadTenders(false);
  } else if (pendingTender !== null) {
    applyPendingTender();
  } else {
    renderTendersTab();
  }
}

function forgetTendersData() {
  tendersData = null;
  tenderDetails = null;
}

// Used by the Overview, the supplier details and the offer comparison: open one tender (and one cycle) on this tab.
function openTenderFromAnotherTab(tenderId, roundId) {
  pendingTender = { tenderId: tenderId, roundId: roundId };
  if (currentTab === 'tenders') {
    openTendersTab();
  } else {
    showTab('tenders');
  }
}

async function loadTenders(refresh) {
  showStatus(tr('Running the analysis on recorded procurement data…', 'جارٍ تحليل بيانات المشتريات المسجلة…'));
  var params = periodParams();
  params.refresh = refresh ? 'true' : 'false';
  try {
    tendersData = await getJson(page.dataset.urlTenders, params);
    showContext(tendersData.context);
    if (pendingTender !== null) {
      applyPendingTender();
      return;
    }
    if (!findTenderInList(selectedTenderId)) {
      selectedTenderId = tendersData.defaultTenderId;
    }
    renderTendersTab();
    loadTenderDetails(selectedTenderId);
  } catch (error) {
    showStatus(tr('The analysis could not load: ', 'تعذر تحميل التحليل: ') + error.message, true);
  }
}

// Opens the tender requested by another tab. When it is outside the chosen period, the period is cleared first.
function applyPendingTender() {
  var wanted = pendingTender;
  if (findTenderInList(wanted.tenderId)) {
    pendingTender = null;
    selectedTenderId = wanted.tenderId;
    offerRoundId = wanted.roundId;
    offerGroupId = null;
    offerBidId = null;
    renderTendersTab();
    loadTenderDetails(wanted.tenderId);
    document.getElementById('ten-main').scrollIntoView({ behavior: 'smooth', block: 'start' });
    return;
  }
  var period = periodParams();
  if (period.from || period.to) {
    document.getElementById('rd-from').value = '';
    document.getElementById('rd-to').value = '';
    showToast(tr('The period was cleared to show this tender.', 'تم مسح الفترة لعرض هذا العطاء.'));
    onPeriodChanged(false);
    return;
  }
  pendingTender = null;
  showToast(tr('The tender details could not be loaded.', 'تعذر تحميل تفاصيل العطاء.'));
  renderTendersTab();
}

function findTenderInList(tenderId) {
  if (tendersData === null || tenderId === null) {
    return null;
  }
  for (var i = 0; i < tendersData.tenders.length; i++) {
    if (tendersData.tenders[i].tenderId === tenderId) {
      return tendersData.tenders[i];
    }
  }
  return null;
}

function selectTender(tenderId) {
  selectedTenderId = tenderId;
  offerRoundId = null;
  offerGroupId = null;
  offerBidId = null;
  renderTenderList();
  loadTenderDetails(tenderId);
}

async function loadTenderDetails(tenderId) {
  if (tenderId === null) {
    tenderDetails = null;
    renderTenderDetails();
    return;
  }
  try {
    var params = periodParams();
    params.tenderId = tenderId;
    var details = await getJson(page.dataset.urlTenderDetails, params);
    if (details.tenderId !== selectedTenderId) {
      return;
    }
    tenderDetails = details;
  } catch (error) {
    tenderDetails = null;
    showStatus(error.message, true);
  }
  renderTenderDetails();
}

function renderTendersTab() {
  renderTenderList();
  renderTenderDetails();
  renderCompetitionChart();
  renderDepartmentChart();
  renderRecurringPairs();
  renderAwardsChart();
}

// ======================================================================
// Tender list (left column) and the drop-down in the header
// ======================================================================

// Short label of the tender state, shown as a coloured pill.
function tenderStateTone(state) {
  if (state === 'Completed') { return 'ok'; }
  if (state === 'Stopped') { return 'muted'; }
  if (state === 'Awarded') { return 'award'; }
  return 'progress';
}

function tenderStateText(state) {
  if (state === 'Completed') { return tr('Completed', 'مكتمل'); }
  if (state === 'Stopped') { return tr('Stopped', 'متوقف'); }
  if (state === 'Awarded') { return tr('Awarded', 'مُرسى'); }
  return tr('In progress', 'قيد التنفيذ');
}

function renderTenderList() {
  var list = tendersData.tenders;
  document.getElementById('ten-count').textContent = list.length;

  var pickHtml = '';
  for (var i = 0; i < list.length; i++) {
    pickHtml += '<option value="' + list[i].tenderId + '">#' + list[i].tenderId + ' · ' + escapeHtml(list[i].title) + '</option>';
  }
  if (pickHtml === '') {
    pickHtml = '<option value="">' + tr('No authorized tenders in this scope', 'لا توجد عطاءات مصرح بها في هذا النطاق') + '</option>';
  }
  var pick = document.getElementById('ten-pick');
  pick.innerHTML = pickHtml;
  pick.value = selectedTenderId === null ? '' : String(selectedTenderId);

  // The search box only hides list items in the browser (it does not change any number).
  var search = document.getElementById('ten-search').value.trim().toLowerCase();
  var html = '';
  for (var j = 0; j < list.length; j++) {
    var tender = list[j];
    var text = ('#' + tender.tenderId + ' ' + tender.title + ' ' + tender.department).toLowerCase();
    if (search !== '' && text.indexOf(search) < 0) {
      continue;
    }
    var isSelected = tender.tenderId === selectedTenderId;
    var pill = tender.state === 'Completed' ? formatNumber(tender.daysToNotification, 1) + tr(' days', ' يوم') : tenderStateText(tender.state);
    html += '<li><button type="button" data-tender-open="' + tender.tenderId + '" class="' + (isSelected ? 'is-on' : '') + '" aria-current="' + isSelected + '">' +
      '<span class="oi-tl-title">' + escapeHtml(tender.title) + '</span>' +
      '<span class="oi-tl-meta">#' + tender.tenderId + ' · ' + escapeHtml(tender.department) + ' · ' + formatDate(tender.createdAt) + '</span>' +
      '<span class="oi-pill is-' + tenderStateTone(tender.state) + '">' + escapeHtml(pill) + '</span></button></li>';
  }
  if (html === '') {
    html = '<li class="rd-muted">' + (list.length === 0 ? tr('No authorized tenders in this scope.', 'لا توجد عطاءات مصرح بها في هذا النطاق.') : tr('No tender matches the search.', 'لا يوجد عطاء يطابق البحث.')) + '</li>';
  }
  document.getElementById('ten-list').innerHTML = html;
}

// ======================================================================
// Selected tender: hero, KPI tiles, lifecycle timeline
// ======================================================================

function renderTenderDetails() {
  var main = document.getElementById('ten-main');
  var tender = tenderDetails;
  main.classList.toggle('is-empty', tender === null);
  if (tender === null) {
    document.getElementById('ten-hero').innerHTML = '<div class="rd-empty-card"><strong>' + tr('No authorized tenders in this scope', 'لا توجد عطاءات مصرح بها في هذا النطاق') + '</strong><span>' +
      tr('Tenders appear here when one of their rounds falls inside the selected period.', 'تظهر العطاءات هنا عندما تقع إحدى دوراتها ضمن الفترة المختارة.') + '</span></div>';
    document.getElementById('ten-kpis').innerHTML = '';
    document.getElementById('ten-timeline').innerHTML = '';
    document.getElementById('off-section').hidden = true;
    return;
  }

  // Hero: title, status, number of cycles and estimated value.
  var statePill = '';
  if (tender.state !== 'Stopped') {
    var stateText = tender.state === 'Awarded' ? tr('Awarded · notification pending', 'مُرسى · الإشعار معلق') : tenderStateText(tender.state);
    statePill = '<span class="oi-pill is-' + tenderStateTone(tender.state) + '">' + escapeHtml(stateText) + '</span>';
  }
  document.getElementById('ten-hero').innerHTML = '<div><p class="oi-kicker">' + tr('Tender #', 'العطاء #') + tender.tenderId + ' · ' + escapeHtml(tender.department) + '</p>' +
    '<h3>' + escapeHtml(tender.title) + '</h3><p class="oi-tender-tags"><span class="oi-pill">' + escapeHtml(tender.status) + '</span>' + statePill +
    '<span>' + tender.rounds.length + tr(' round(s)', ' دورة') + '</span><span>' + tr('Estimated ', 'القيمة التقديرية ') + formatMoney(escapeHtml(tender.currency), tender.estimatedValue) + '</span></p></div>';

  renderTenderKpis(tender);
  renderTenderTimeline(tender);
  renderTenderOffers();
}

function renderTenderKpis(tender) {
  var latest = tender.rounds.length > 0 ? tender.rounds[tender.rounds.length - 1] : null;
  var invitedSub = tr('no bidding cycle', 'لا توجد دورة');
  if (latest !== null) {
    invitedSub = cycleLabel(latest.roundNumber);
    if (tender.rounds.length > 1) {
      invitedSub += ' · ' + tender.totalInvitations + tr(' invitations across all bidding cycles', ' دعوة في كل الدورات');
    }
  }
  var completed = tender.state === 'Completed';
  var lifecycleSub = tr('created → winner notified', 'الإنشاء ← إشعار الفائز');
  if (!completed) {
    lifecycleSub = tr('since ', 'منذ ') + formatDate(tender.createdAt) + (tender.state === 'Stopped' ? tr(' · stopped', ' · متوقف') : tr(' · not notified yet', ' · لم يُشعر بعد'));
  }
  var resultValue = tr('No award yet', 'لا ترسية بعد');
  var resultSub = tr('recommendations never count', 'التوصيات لا تُحتسب');
  if (tender.awardedSupplier) {
    resultValue = tender.awardedSupplier;
    resultSub = formatMoney(escapeHtml(tender.awardCurrency), tender.awardedValue) + ' · ' + formatDate(tender.awardedAt);
  }

  renderKpiTiles('ten-kpis', [
    { label: tr('Invited suppliers', 'الموردون المدعوون'), value: latest ? latest.invited : 0, sub: invitedSub, icon: 'mail', tone: 'violet' },
    {
      label: tr('Submitted offers', 'العروض المقدمة'),
      value: latest && latest.analysed ? latest.offers : tr('Sealed', 'مختومة'),
      sub: latest && latest.analysed ? cycleLabel(latest.roundNumber) : tr('hidden until bidding closes', 'مخفية حتى إغلاق العطاء'),
      icon: 'send', tone: 'teal'
    },
    {
      label: completed ? tr('Lifecycle', 'دورة الحياة') : tr('Lifecycle so far', 'دورة الحياة حتى الآن'),
      value: formatNumber(completed ? tender.daysToNotification : tender.daysSoFar, 1), unit: tr(' days', ' يوم'), sub: lifecycleSub, icon: 'clock', tone: 'blue'
    },
    { label: tr('Official result', 'النتيجة الرسمية'), value: resultValue, sub: resultSub, icon: 'award', tone: 'orange' }
  ]);
}

// Six milestones, each with its recorded time (or "Not recorded") and the days since the tender was created.
function renderTenderTimeline(tender) {
  var labels = {
    created: tr('Tender created', 'إنشاء العطاء'),
    round: tr('Initial Bidding cycle created', 'إنشاء دورة العطاء الأولي'),
    invited: tr('First invitation', 'أول دعوة'),
    closed: tr('Bidding closed', 'إغلاق العطاء'),
    awarded: tr('Official award', 'الترسية الرسمية'),
    notified: tr('Winner notification sent', 'إرسال إشعار الفائز')
  };
  var html = '';
  for (var i = 0; i < tender.stages.length; i++) {
    var stage = tender.stages[i];
    var time = stage.at ? formatDateTime(stage.at) : tr('Not recorded', 'غير مسجل');
    var days = '—';
    if (i === 0) {
      days = tr('start', 'البداية');
    } else if (stage.daysFromCreation !== null) {
      days = '+' + formatNumber(stage.daysFromCreation, 1) + tr(' days', ' يوم');
    }
    html += '<li class="' + (stage.at ? 'is-done' : 'is-missing') + '" title="' + tr('Source: ', 'المصدر: ') + escapeHtml(stage.source) + '">' +
      '<span class="oi-tl-dot" aria-hidden="true"></span><b>' + (labels[stage.key] || escapeHtml(stage.label)) + '</b>' +
      '<time>' + time + '</time><small>' + days + '</small></li>';
  }
  document.getElementById('ten-timeline').innerHTML = html;
}

// ======================================================================
// Financial offers of one closed bidding cycle
// ======================================================================

// Closed cycles of the selected tender that have at least one offer.
function openedRounds() {
  var rounds = [];
  if (tenderDetails === null) {
    return rounds;
  }
  for (var i = 0; i < tenderDetails.rounds.length; i++) {
    if (tenderDetails.rounds[i].analysed && tenderDetails.rounds[i].offers > 0) {
      rounds.push(tenderDetails.rounds[i]);
    }
  }
  return rounds;
}

function chosenOfferRound() {
  var rounds = openedRounds();
  for (var i = 0; i < rounds.length; i++) {
    if (rounds[i].roundId === offerRoundId) {
      return rounds[i];
    }
  }
  return null;
}

function chosenOfferGroup(round) {
  for (var i = 0; i < round.groups.length; i++) {
    if (round.groups[i].groupId === offerGroupId) {
      return round.groups[i];
    }
  }
  return null;
}

function renderTenderOffers() {
  var tender = tenderDetails;
  var section = document.getElementById('off-section');
  section.hidden = tender === null;
  if (tender === null) {
    return;
  }

  // 1. Which closed cycle? (default: the latest one)
  var rounds = openedRounds();
  if (chosenOfferRound() === null) {
    offerRoundId = rounds.length > 0 ? rounds[rounds.length - 1].roundId : null;
    offerGroupId = null;
    offerBidId = null;
  }
  var roundHtml = '';
  for (var i = 0; i < rounds.length; i++) {
    roundHtml += '<option value="' + rounds[i].roundId + '">' + escapeHtml(cycleLabel(rounds[i].roundNumber)) + ' · #' + rounds[i].roundId + ' · ' + rounds[i].offers + tr(' offers', ' عروض') + '</option>';
  }
  if (roundHtml === '') {
    roundHtml = '<option value="">' + tr('No closed bidding cycle with offers', 'لا توجد دورة مغلقة بعروض') + '</option>';
  }
  var roundPick = document.getElementById('off-round');
  roundPick.innerHTML = roundHtml;
  roundPick.value = offerRoundId === null ? '' : String(offerRoundId);
  roundPick.disabled = rounds.length === 0;

  var round = chosenOfferRound();
  var chart = getChart('chart-ten-offers');
  var tenderLabel = '<span><small>' + tr('Tender', 'العطاء') + '</small><b>' + escapeHtml(tender.title) + ' <em>#' + tender.tenderId + '</em></b></span>';
  if (round === null) {
    document.getElementById('off-summary').innerHTML = tenderLabel + '<span>' + tr('No closed bidding cycle with submitted offers', 'لا توجد دورة مغلقة بعروض مقدمة') + '</span>';
    document.getElementById('off-group-wrap').hidden = true;
    document.getElementById('off-note').textContent = '';
    document.getElementById('off-insight').innerHTML = '';
    document.getElementById('off-extremes').innerHTML = '';
    document.getElementById('off-detail').hidden = true;
    document.getElementById('off-table-content').innerHTML = '';
    if (chart) {
      showChartMessage(chart, tender.rounds.length > 0
        ? tr('No closed bidding cycle with offers yet. Sealed offers are never shown.', 'لا توجد دورة مغلقة بعروض بعد. العروض المختومة لا تظهر أبداً.')
        : tr('This tender has no bidding cycle yet.', 'لا توجد دورة عطاء لهذا العطاء بعد.'));
    }
    return;
  }

  document.getElementById('off-summary').innerHTML = tenderLabel +
    '<span><small>' + tr('Bidding Cycle', 'دورة العطاء') + '</small><b>' + cycleLabel(round.roundNumber) + ' <em>#' + round.roundId + '</em></b></span>' +
    '<span><small>' + tr('Invited', 'المدعوون') + '</small><b>' + formatInteger(round.invited) + '</b></span>' +
    '<span><small>' + tr('Submitted', 'المقدمة') + '</small><b>' + formatInteger(round.offers) + '</b></span>' +
    '<span><small>' + tr('Response rate', 'معدل الاستجابة') + '</small><b>' + (round.invited ? formatPercent(round.offers / round.invited) : '—') + '</b></span>';

  // 2. Which comparable group? Offers are compared only inside one currency and one item structure.
  if (chosenOfferGroup(round) === null) {
    offerGroupId = round.groups.length > 0 ? round.groups[0].groupId : null;
  }
  var groupHtml = '';
  for (var g = 0; g < round.groups.length; g++) {
    groupHtml += '<option value="' + round.groups[g].groupId + '">' + escapeHtml(round.groups[g].currency) + ' · ' + round.groups[g].offers.length + tr(' structurally comparable offer(s)', ' عرض قابل للمقارنة هيكلياً') + '</option>';
  }
  document.getElementById('off-group').innerHTML = groupHtml;
  document.getElementById('off-group').value = offerGroupId === null ? '' : String(offerGroupId);
  document.getElementById('off-group-wrap').hidden = round.groups.length < 2;

  var group = chosenOfferGroup(round);
  var shownCount = group === null ? 0 : group.offers.length;
  var excluded = round.bids.length - shownCount;
  document.getElementById('off-note').textContent = excluded > 0
    ? excluded + tr(' offer(s) outside this comparable group; see the table for currency and structure notes.', ' عرض خارج هذه المجموعة؛ راجع الجدول لملاحظات العملة والهيكل.')
    : tr('Offers are grouped by bidding cycle, currency and recorded item structure. Check specifications separately.', 'تُجمّع العروض حسب الدورة والعملة وهيكل البنود المسجل. راجع المواصفات بشكل منفصل.');

  renderOfferTable(round, group);
  renderOfferDetail(round);

  if (group === null) {
    if (chart) {
      showChartMessage(chart, tr('No offers with a verified comparable currency and item structure. See the offer table for recorded totals.', 'لا توجد عروض بعملة وهيكل بنود قابلين للمقارنة. راجع جدول العروض للإجماليات المسجلة.'));
    }
    document.getElementById('off-insight').textContent = tr('No reliable median can be calculated for this round.', 'لا يمكن حساب وسيط موثوق لهذه الجولة.');
    document.getElementById('off-extremes').innerHTML = '';
    return;
  }

  renderOffersChart(chart, group);
  renderOfferInsight(round, group);
}

// Horizontal bars: one per offer, highest at the top, the lowest in strong orange, dashed line = median.
function renderOffersChart(chart, group) {
  if (!chart) {
    return;
  }
  var currency = group.currency;
  var names = [];
  var bars = [];
  for (var i = 0; i < group.offers.length; i++) {
    var offer = group.offers[i];
    var isLowest = offer.bidId === group.lowest.bidId;
    names.push(offer.supplier);
    bars.push({
      value: offer.total, offer: offer,
      itemStyle: { color: isLowest ? gradient(false, '#ffae5c', '#ff7900') : gradient(false, '#ffe8d5', '#ffc58f'), borderRadius: [0, 15, 15, 0] }
    });
  }
  document.getElementById('chart-ten-offers').style.height = Math.max(390, group.offers.length * 68 + 140) + 'px';
  chart.resize();

  var options = chartBase();
  options.grid = { left: 12, right: 112, top: 54, bottom: 38, containLabel: true };
  options.tooltip.formatter = function (item) {
    var offer = item.data.offer;
    var relative = offer.relativeToMedian === null ? tr('Median unavailable', 'الوسيط غير متاح') : formatSignedPercent(offer.relativeToMedian) + tr(' vs bidding cycle median', ' مقابل وسيط الدورة');
    return '<b>' + escapeHtml(offer.supplier) + '</b><br>' + tr('Bid #', 'العرض #') + offer.bidId + ' · ' + formatMoney(escapeHtml(offer.currency), offer.total) +
      '<br>' + relative + '<br>' + escapeHtml(offer.compliance) + tr(' · click for record details', ' · انقر لتفاصيل السجل');
  };
  var xAxis = chartAxis();
  xAxis.type = 'value';
  xAxis.min = 0;
  xAxis.name = tr('Offer value (', 'قيمة العرض (') + currency + ')';
  xAxis.nameLocation = 'middle';
  xAxis.nameGap = 28;
  xAxis.splitNumber = 5;
  xAxis.axisLabel = { color: cssValue('--rd-muted'), fontSize: 14, formatter: function (value) { return formatNumber(value, 0); } };
  var yAxis = chartAxis();
  yAxis.type = 'category';
  yAxis.data = names;
  yAxis.axisLabel = { color: cssValue('--rd-ink'), fontSize: 16, fontWeight: 650, width: 230, overflow: 'break', lineHeight: 20 };
  options.xAxis = xAxis;
  options.yAxis = yAxis;

  var series = {
    type: 'bar', barMaxWidth: 36, barCategoryGap: '38%', data: bars,
    label: { show: true, position: 'right', color: cssValue('--rd-ink'), fontSize: 15, fontWeight: 750, formatter: function (item) { return currency + ' ' + formatNumber(item.value, 0); } }
  };
  if (group.offers.length > 1) {
    series.markLine = {
      silent: true, symbol: 'none', data: [{ xAxis: group.median }],
      lineStyle: { color: cssValue('--rd-ink-2'), type: [6, 5], width: 1.8 },
      label: { formatter: tr('Bidding cycle median', 'وسيط دورة العطاء') + '\n' + currency + ' ' + formatNumber(group.median, 0), rotate: 0, color: cssValue('--rd-surface'), backgroundColor: cssValue('--rd-ink-2'), borderRadius: 7, padding: [6, 10], fontSize: 13, position: 'insideEndTop' }
    };
  }
  options.series = [series];
  chart.setOption(options, true);

  chart.off('click');
  chart.on('click', function (item) {
    if (item.data && item.data.offer) {
      offerBidId = item.data.offer.bidId;
      renderOfferDetail(chosenOfferRound());
    }
  });
}

// The sentence under the chart and the three "lowest / median / highest" cards. The numbers come from the server.
function renderOfferInsight(round, group) {
  var currency = group.currency;
  var lowest = group.lowest;
  var highest = group.highest;
  var rate = round.invited ? ' (' + formatPercent(round.offers / round.invited) + ')' : '';
  var medianText = group.offers.length > 1
    ? tr('The comparable-group median is ', 'وسيط المجموعة القابلة للمقارنة ') + formatMoney(currency, group.median) + '.'
    : tr('A median comparison needs another comparable offer.', 'تحتاج مقارنة الوسيط إلى عرض آخر قابل للمقارنة.');
  var sentence = round.offers + tr(' of ', ' من ') + round.invited + tr(' invited suppliers submitted offers during ', ' مورداً مدعواً قدموا عروضاً خلال ') + cycleLabel(round.roundNumber) + rate + '. ' +
    lowest.supplier + tr(' submitted the lowest total in this group (', ' قدّم أقل إجمالي في هذه المجموعة (') + formatMoney(currency, lowest.total) + '); ' +
    highest.supplier + tr(' submitted the highest total in this group (', ' قدّم أعلى إجمالي في هذه المجموعة (') + formatMoney(currency, highest.total) + '). ' +
    medianText + ' ' + tr('Lowest price does not establish compliance or an award.', 'أقل سعر لا يثبت المطابقة ولا الترسية.');
  document.getElementById('off-insight').innerHTML = '<b>' + tr('What this bidding cycle shows', 'ما تُظهره هذه الدورة') + '</b><span>' + escapeHtml(sentence) + '</span>';

  var medianOwner = tr('Calculated midpoint; no single supplier', 'نقطة منتصف محسوبة؛ لا مورد واحد');
  if (group.offers.length === 1) {
    medianOwner = tr('One recorded offer; not a comparison', 'عرض مسجل واحد؛ ليست مقارنة');
  } else if (group.medianOwners === 1) {
    medianOwner = group.medianOwnerName;
  } else if (group.medianOwners > 1) {
    medianOwner = group.medianOwners + tr(' suppliers at the median', ' موردين عند الوسيط');
  }
  document.getElementById('off-extremes').innerHTML =
    extremeCardHtml(tr('Lowest offer', 'أقل عرض'), formatMoney(currency, lowest.total), lowest.supplier, 'low') +
    extremeCardHtml(tr('Median offer', 'العرض الوسيط'), formatMoney(currency, group.median), medianOwner, 'mid') +
    extremeCardHtml(tr('Highest offer', 'أعلى عرض'), formatMoney(currency, highest.total), highest.supplier, 'high');
}

function extremeCardHtml(title, amount, supplier, tone) {
  return '<div class="is-' + tone + '"><span>' + title + '</span><strong>' + escapeHtml(amount) + '</strong><small>' + escapeHtml(supplier) + '</small></div>';
}

// Every offer of the cycle (also those outside the comparable group), cheapest first.
function renderOfferTable(round, group) {
  var html = '<table class="rd-table"><thead><tr><th>' + tr('Bid', 'العرض') + '</th><th>' + tr('Supplier', 'المورد') + '</th><th>' + tr('Currency', 'العملة') + '</th>' +
    '<th class="num">' + tr('Total', 'الإجمالي') + '</th><th>' + tr('Comparison', 'المقارنة') + '</th><th>' + tr('Compliance', 'المطابقة') + '</th><th>' + tr('Result', 'النتيجة') + '</th></tr></thead><tbody>';
  for (var i = 0; i < round.bids.length; i++) {
    var offer = round.bids[i];
    var inGroup = group !== null && offer.comparisonGroup === group.groupId;
    var comparisonText = escapeHtml(offer.comparisonNote);
    if (inGroup) {
      comparisonText = offer.relativeToMedian === null ? tr('No second comparable offer', 'لا يوجد عرض ثانٍ قابل للمقارنة') : formatSignedPercent(offer.relativeToMedian) + tr(' vs median', ' مقابل الوسيط');
    }
    html += '<tr><td><button type="button" class="rd-link" data-offer-pick="' + offer.bidId + '">#' + offer.bidId + '</button></td><td>' + escapeHtml(offer.supplier) + '</td>' +
      '<td>' + escapeHtml(offer.currency) + '</td><td class="num">' + formatNumber(offer.total, 2) + '</td><td>' + comparisonText + '</td>' +
      '<td>' + escapeHtml(offer.compliance) + '</td><td>' + (offer.awarded ? tr('Official award', 'ترسية رسمية') : '—') + '</td></tr>';
  }
  document.getElementById('off-table-content').innerHTML = html + '</tbody></table>';
  document.getElementById('off-table-summary').textContent = tr('View offer table (', 'عرض جدول العروض (') + round.bids.length + ')';
}

// The small box under the chart with the record details of the clicked offer.
function renderOfferDetail(round) {
  var box = document.getElementById('off-detail');
  var offer = null;
  if (round !== null) {
    for (var i = 0; i < round.bids.length; i++) {
      if (round.bids[i].bidId === offerBidId) {
        offer = round.bids[i];
      }
    }
  }
  box.hidden = offer === null;
  if (offer === null) {
    return;
  }
  box.innerHTML = '<div><b>' + tr('Bid #', 'العرض #') + offer.bidId + ' · ' + escapeHtml(offer.supplier) + '</b><span>' + formatMoney(escapeHtml(offer.currency), offer.total) + ' · ' +
    escapeHtml(offer.comparisonNote) + tr(' · compliance: ', ' · المطابقة: ') + escapeHtml(offer.compliance) + (offer.awarded ? tr(' · official award', ' · ترسية رسمية') : '') + '</span></div>' +
    '<a class="rd-link" href="' + escapeHtml(page.dataset.urlOfferRecord) + '?id=' + offer.bidId + '">' + tr('Open offer record', 'افتح سجل العرض') + '</a>' +
    '<button type="button" class="rd-link" id="off-detail-close">' + tr('Close', 'إغلاق') + '</button>';
}

// ======================================================================
// Market charts under the tender: participation per cycle, departments, competitor pairs, awards
// ======================================================================

// Bars: grey = invited, orange = submitted (amber when below the minimum). Clicking a bar opens that tender.
function renderCompetitionChart() {
  var chart = getChart('chart-ten-competition');
  if (!chart) {
    return;
  }
  var rounds = tendersData.competition;
  if (rounds.length === 0) {
    showChartMessage(chart, tr('No closed bidding cycles in this period. Sealed rounds are never shown.', 'لا توجد دورات مغلقة في هذه الفترة. الجولات المختومة لا تظهر أبداً.'));
    return;
  }
  // Bar colour: red when the cycle has fewer offers than the minimum (3), green otherwise.
  // round.lowCompetition is decided on the server (TenderCalculator.BuildCompetition).
  var green = cssValue('--rd-good');
  var red = cssValue('--rd-bad');
  var neutral = cssValue('--rd-neutral');
  var labels = [];
  var invited = [];
  var submitted = [];
  for (var i = 0; i < rounds.length; i++) {
    var round = rounds[i];
    labels.push(round.tender.slice(0, 26) + (round.roundNumber > 1 ? ' · ' + tr('Cycle', 'الدورة') + ' ' + round.roundNumber : ''));
    invited.push(round.invited);
    var color = round.lowCompetition ? red : green;
    var faded = selectedTenderId !== null && round.tenderId !== selectedTenderId;
    submitted.push({ value: round.submitted, itemStyle: { color: gradient(true, color, withAlpha(color, 0.6)), borderRadius: [8, 8, 3, 3], opacity: faded ? 0.3 : 1 } });
  }

  var options = chartBase();
  var legend = chartLegend();
  legend.top = 0;
  legend.right = 0;
  options.legend = legend;
  options.grid = { left: 14, right: 30, top: 48, bottom: 14, containLabel: true };
  options.tooltip.trigger = 'axis';
  options.tooltip.axisPointer = { type: 'shadow' };
  options.tooltip.formatter = function (points) {
    var round = rounds[points[0].dataIndex];
    var text = '<b>' + escapeHtml(round.tender) + '</b> · ' + escapeHtml(cycleLabel(round.roundNumber)) + '<br>' + formatDate(round.date) + ' · ' + escapeHtml(round.department) +
      '<br>' + tr('Invited ', 'مدعو ') + round.invited + tr(' · submitted ', ' · مقدم ') + round.submitted + tr(' · compliant ', ' · مطابق ') + round.compliant;
    if (round.lowCompetition) {
      text += '<br><span style="color:' + red + '">' + tr('Below the minimum of ', 'دون الحد الأدنى ') + tendersData.minimumOffers + '</span>';
    }
    if (round.bidders.length > 0) {
      text += '<br><span style="color:' + cssValue('--rd-muted') + '">' + escapeHtml(round.bidders.join(', ')) + '</span>';
    }
    return text;
  };
  var xAxis = chartAxis();
  xAxis.type = 'category';
  xAxis.data = labels;
  if (rounds.length > 12) {
    xAxis.axisLabel = { color: cssValue('--rd-ink-2'), fontSize: 14, interval: 0, rotate: 35, width: 150, overflow: 'truncate' };
  } else {
    xAxis.axisLabel = { color: cssValue('--rd-ink-2'), fontSize: 14, interval: 0, lineHeight: 18, width: Math.max(64, (chart.getWidth() - 90) / rounds.length - 8), overflow: 'break' };
  }
  var yAxis = chartAxis();
  yAxis.minInterval = 1;
  yAxis.name = tr('Suppliers', 'الموردون');
  options.xAxis = xAxis;
  options.yAxis = yAxis;
  options.series = [
    {
      name: tr('Invited', 'المدعوون'), type: 'bar', data: invited, barGap: '-100%', barMaxWidth: 56, z: 1,
      itemStyle: { color: withAlpha(neutral, 0.22), borderColor: withAlpha(neutral, 0.6), borderWidth: 1, borderRadius: [10, 10, 4, 4] }
    },
    {
      name: tr('Submitted', 'المقدمة'), type: 'bar', data: submitted, color: green, barMaxWidth: 56, z: 2,
      label: { show: true, position: 'top', color: cssValue('--rd-ink'), fontSize: 14, fontWeight: 700 },
      markLine: {
        silent: true, symbol: 'none', data: [{ yAxis: tendersData.minimumOffers }],
        lineStyle: { color: red, type: [6, 6], width: 1.6 },
        label: { color: red, fontSize: 13.5, fontWeight: 700, formatter: tr('minimum ', 'الحد الأدنى ') + tendersData.minimumOffers, position: 'insideEndTop' }
      }
    }
  ];
  chart.setOption(options, true);

  chart.off('click');
  chart.on('click', function (item) {
    var round = rounds[item.dataIndex];
    offerRoundId = round.roundId;
    selectedTenderId = round.tenderId;
    offerGroupId = null;
    offerBidId = null;
    renderTenderList();
    renderCompetitionChart();
    loadTenderDetails(round.tenderId);
    document.getElementById('ten-main').scrollIntoView({ behavior: 'smooth', block: 'start' });
  });
}

function renderDepartmentChart() {
  var chart = getChart('chart-ten-departments');
  if (!chart) {
    return;
  }
  var departments = tendersData.departments;
  if (departments.length === 0) {
    showChartMessage(chart, tr('No closed bidding cycles.', 'لا توجد دورات مغلقة.'));
    return;
  }
  var names = [];
  var values = [];
  for (var i = 0; i < departments.length; i++) {
    var color = departments[i].averageBids < tendersData.minimumOffers ? cssValue('--rd-review') : cssValue('--c1');
    names.push(departments[i].department);
    values.push({ value: departments[i].averageBids, itemStyle: { color: gradient(false, withAlpha(color, 0.5), color), borderRadius: 8 } });
  }
  var options = chartBase();
  options.grid = { left: 8, right: 44, top: 8, bottom: 8, containLabel: true };
  options.tooltip.formatter = function (item) {
    return escapeHtml(item.name) + '<br>' + formatNumber(item.value) + tr(' offers per round over ', ' عرض لكل جولة على ') + departments[item.dataIndex].rounds + tr(' rounds', ' جولات');
  };
  var xAxis = chartAxis();
  xAxis.type = 'value';
  xAxis.minInterval = 1;
  var yAxis = chartAxis();
  yAxis.type = 'category';
  yAxis.data = names;
  yAxis.axisLabel = { color: cssValue('--rd-ink-2'), fontSize: 14, width: 150, overflow: 'truncate' };
  options.xAxis = xAxis;
  options.yAxis = yAxis;
  options.series = [{
    type: 'bar', data: values, barMaxWidth: 20, showBackground: true, backgroundStyle: { color: withAlpha(cssValue('--rd-neutral'), 0.12), borderRadius: 8 },
    label: { show: true, position: 'right', color: cssValue('--rd-ink'), fontSize: 14, fontWeight: 700, formatter: function (item) { return formatNumber(item.value); } }
  }];
  chart.setOption(options, true);
}

function renderRecurringPairs() {
  var pairs = tendersData.recurringPairs;
  if (pairs.length === 0) {
    document.getElementById('ten-pairs').innerHTML = '<p class="rd-muted">' + tr('No supplier pair met in a closed bidding cycle.', 'لم يلتقِ أي زوج من الموردين في دورة مغلقة.') + '</p>';
    return;
  }
  var html = '<p class="rd-section-title">' + tr('Most frequent competitor pairs', 'أكثر أزواج المنافسين تكراراً') + '</p>';
  for (var i = 0; i < pairs.length; i++) {
    html += '<button type="button" data-tender-pair="' + pairs[i].supplierA + ',' + pairs[i].supplierB + '"><span>' + escapeHtml(pairs[i].nameA) + ' · ' + escapeHtml(pairs[i].nameB) + '</span>' +
      '<b title="' + tr('Shared Bidding Cycles', 'دورات مشتركة') + '">' + pairs[i].sharedRounds + '</b></button>';
  }
  document.getElementById('ten-pairs').innerHTML = html;
}

// Donut of official award value in the first currency (other currencies are listed in the table).
function renderAwardsChart() {
  var chart = getChart('chart-ten-awards');
  if (!chart) {
    return;
  }
  var awards = tendersData.awards;
  var card = document.getElementById('ten-awards-card');
  card.classList.toggle('is-sparse', awards.length === 0);
  if (awards.length === 0) {
    document.getElementById('ten-award-note').textContent = tr('Official awards per currency. Recommendations are never counted as awards.', 'الترسيات الرسمية لكل عملة. التوصيات لا تُحتسب ترسيات.');
    showChartMessage(chart, tr('No official award is recorded in this period - this is missing data, not a zero.', 'لا توجد ترسية رسمية مسجلة في هذه الفترة - هذه بيانات ناقصة وليست صفراً.'));
    return;
  }
  var first = awards[0];
  document.getElementById('ten-award-note').textContent = tr('Official award value in ', 'قيمة الترسيات الرسمية بـ ') + first.currency +
    (awards.length > 1 ? tr(' (other currencies in the table)', ' (العملات الأخرى في الجدول)') : '') +
    tr('. Concentration (HHI) ', '. التركّز (HHI) ') + formatNumber(first.hhi, 3) + tr('. Not payments.', '. ليست مدفوعات.');

  var slices = [];
  for (var i = 0; i < first.bySupplier.length; i++) {
    var share = first.bySupplier[i];
    slices.push({ value: share.value, name: share.name, share: share, itemStyle: { color: supplierColor(share.supplierColor) } });
  }
  var options = chartBase();
  options.tooltip.formatter = function (item) {
    var share = item.data.share;
    return '<b>' + escapeHtml(share.name) + '</b><br>' + formatMoney(escapeHtml(first.currency), share.value) + ' · ' + formatPercent(share.share, 1) + tr(' of ', ' من ') + first.currency + tr(' awards · ', ' ترسيات · ') + share.count + tr(' award(s)', ' ترسية');
  };
  options.title = {
    text: formatMoney(first.currency, first.total), subtext: first.count + tr(' official award(s)', ' ترسية رسمية'), left: 'center', top: '44%', itemGap: 4,
    textStyle: { color: cssValue('--rd-ink'), fontSize: 17, fontWeight: 800 }, subtextStyle: { color: cssValue('--rd-muted'), fontSize: 14 }
  };
  options.series = [{
    type: 'pie', radius: ['48%', '68%'], center: ['50%', '52%'], padAngle: slices.length > 1 ? 2 : 0, data: slices,
    itemStyle: { borderRadius: 10, borderColor: cssValue('--rd-surface'), borderWidth: 3 }, minShowLabelAngle: 22, labelLayout: { hideOverlap: true },
    label: {
      show: true, color: cssValue('--rd-ink-2'), fontSize: 14, fontWeight: 600, alignTo: 'edge', edgeDistance: 6, width: 96, overflow: 'break',
      formatter: function (item) { return item.name + '\n{b|' + formatPercent(item.data.share.share, 0) + '}'; },
      rich: { b: { color: cssValue('--rd-ink'), fontSize: 15, fontWeight: 800, lineHeight: 22 } }
    },
    labelLine: { length: 10, length2: 12, lineStyle: { color: cssValue('--rd-line') } }, emphasis: { scale: true, scaleSize: 6 }
  }];
  chart.setOption(options, true);
}

function tenderChartTableHtml(name) {
  var html = '';
  if (name === 'competition') {
    html = '<table class="rd-table"><thead><tr><th>' + tr('Tender', 'العطاء') + '</th><th>' + tr('Bidding Cycle', 'الدورة') + '</th><th>' + tr('Date', 'التاريخ') + '</th><th>' + tr('Department', 'الجهة') + '</th>' +
      '<th class="num">' + tr('Invited', 'مدعو') + '</th><th class="num">' + tr('Submitted', 'مقدم') + '</th><th class="num">' + tr('Compliant', 'مطابق') + '</th></tr></thead><tbody>';
    for (var i = 0; i < tendersData.competition.length; i++) {
      var round = tendersData.competition[i];
      html += '<tr><td>' + escapeHtml(round.tender) + '</td><td>' + escapeHtml(cycleLabel(round.roundNumber)) + '</td><td>' + formatDate(round.date) + '</td><td>' + escapeHtml(round.department) + '</td>' +
        '<td class="num">' + round.invited + '</td><td class="num">' + round.submitted + '</td><td class="num">' + round.compliant + '</td></tr>';
    }
    return html + '</tbody></table>';
  }
  if (tendersData.awards.length === 0) {
    return '<p class="rd-muted" style="padding:12px">' + tr('No official awards in this period.', 'لا توجد ترسيات رسمية في هذه الفترة.') + '</p>';
  }
  for (var a = 0; a < tendersData.awards.length; a++) {
    var award = tendersData.awards[a];
    html += '<table class="rd-table"><thead><tr><th>' + tr('Supplier (', 'المورد (') + escapeHtml(award.currency) + ')</th><th class="num">' + tr('Value', 'القيمة') + '</th><th class="num">' + tr('Share', 'الحصة') + '</th><th class="num">' + tr('Awards', 'الترسيات') + '</th></tr></thead><tbody>';
    for (var s = 0; s < award.bySupplier.length; s++) {
      var share = award.bySupplier[s];
      html += '<tr><td>' + escapeHtml(share.name) + '</td><td class="num">' + formatNumber(share.value) + '</td><td class="num">' + formatPercent(share.share, 1) + '</td><td class="num">' + share.count + '</td></tr>';
    }
    for (var d = 0; d < award.byDepartment.length; d++) {
      var department = award.byDepartment[d];
      html += '<tr><td>' + tr('Department: ', 'الجهة: ') + escapeHtml(department.name) + '</td><td class="num">' + formatNumber(department.value) + '</td><td class="num">' + formatPercent(department.share, 1) + '</td><td class="num">' + department.count + '</td></tr>';
    }
    html += '</tbody></table>';
  }
  return html;
}

// ======================================================================
// Excel and PDF
// ======================================================================

// kind: 'lifecycle' (Excel), 'pdf' (PDF report) or 'offers' (Excel of the chosen cycle).
function exportTender(kind) {
  if (tenderDetails === null) {
    return;
  }
  var period = periodParams();
  var request = { tenderId: tenderDetails.tenderId, from: period.from, to: period.to };
  if (kind === 'lifecycle') {
    request.format = 'xlsx';
    request.reportType = 'tender_lifecycle';
  } else if (kind === 'pdf') {
    request.format = 'pdf';
    request.reportType = 'tender_summary';
  } else {
    var round = chosenOfferRound();
    if (round === null) {
      return;
    }
    request.format = 'xlsx';
    request.reportType = 'tender_offers';
    request.roundId = round.roundId;
  }
  downloadFile(request);
}

// ======================================================================
// Events of this tab
// ======================================================================

function setupTendersEvents() {
  document.getElementById('ten-pick').addEventListener('change', function (event) {
    if (event.target.value) {
      selectTender(Number(event.target.value));
    }
  });
  document.getElementById('ten-list').addEventListener('click', function (event) {
    var button = event.target.closest('[data-tender-open]');
    if (button) {
      selectTender(Number(button.dataset.tenderOpen));
    }
  });
  document.getElementById('ten-search').addEventListener('input', function () {
    if (tendersData !== null) {
      renderTenderList();
    }
  });

  document.getElementById('ten-export-lifecycle').addEventListener('click', function () { exportTender('lifecycle'); });
  document.getElementById('ten-export-pdf').addEventListener('click', function () { exportTender('pdf'); });
  document.getElementById('off-export').addEventListener('click', function () { exportTender('offers'); });

  document.getElementById('off-round').addEventListener('change', function (event) {
    offerRoundId = event.target.value ? Number(event.target.value) : null;
    offerGroupId = null;
    offerBidId = null;
    renderTenderOffers();
  });
  document.getElementById('off-group').addEventListener('change', function (event) {
    offerGroupId = event.target.value ? Number(event.target.value) : null;
    offerBidId = null;
    renderTenderOffers();
  });
  document.getElementById('off-table-toggle').addEventListener('click', function () {
    var table = document.getElementById('off-table');
    table.open = !table.open;
    table.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  });
  document.getElementById('off-table-content').addEventListener('click', function (event) {
    var button = event.target.closest('[data-offer-pick]');
    if (button) {
      offerBidId = Number(button.dataset.offerPick);
      renderOfferDetail(chosenOfferRound());
      document.getElementById('off-detail').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
  });
  document.getElementById('off-detail').addEventListener('click', function (event) {
    if (event.target.closest('#off-detail-close')) {
      offerBidId = null;
      document.getElementById('off-detail').hidden = true;
    }
  });

  // "Table" links under the charts, and competitor pairs (-> Suppliers tab offer comparison).
  document.getElementById('oi-tenders').addEventListener('click', function (event) {
    var tableButton = event.target.closest('[data-table-for]');
    var pairButton = event.target.closest('[data-tender-pair]');
    if (tableButton && tendersData !== null) {
      toggleChartTable(tableButton, tenderChartTableHtml(tableButton.dataset.tableFor));
    } else if (pairButton) {
      var ids = pairButton.dataset.tenderPair.split(',');
      showTab('suppliers');
      compareSuppliers(Number(ids[0]), Number(ids[1]));
    }
  });
}
