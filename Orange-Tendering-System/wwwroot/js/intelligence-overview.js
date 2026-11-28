// ORANGE INTELLIGENCE - Tab 1 "Overview".
// Flow: showTab('overview') -> openOverviewTab() -> loadOverview() -> GET /Intelligence/Overview
//       -> IntelligenceController.Overview -> IntelligenceService.GetOverviewAsync -> OverviewCalculator.Build
//       -> JSON (OverviewData) -> renderOverview() draws the KPI tiles, the funnel, the attention list and the shortcuts.

var overviewData = null;

// Called by showTab: load the data the first time, otherwise just draw what we already have.
function openOverviewTab() {
  if (overviewData === null) {
    loadOverview(false);
  } else {
    renderOverview(overviewData);
  }
}

// Called by onPeriodChanged: the next openOverviewTab() will load fresh data.
function forgetOverviewData() {
  overviewData = null;
}

async function loadOverview(refresh) {
  showStatus(tr('Running the analysis on recorded procurement data…', 'جارٍ تحليل بيانات المشتريات المسجلة…'));
  var params = periodParams();
  params.refresh = refresh ? 'true' : 'false';
  try {
    overviewData = await getJson(page.dataset.urlOverview, params);
    showContext(overviewData.context);
    if (currentTab === 'overview') {
      renderOverview(overviewData);
    }
  } catch (error) {
    showStatus(tr('The analysis could not load: ', 'تعذر تحميل التحليل: ') + error.message, true);
  }
}

function renderOverview(data) {
  var period = periodParams();
  var periodText = period.from || period.to ? (period.from || '…') + ' → ' + (period.to || '…') : tr('all recorded history', 'كل التاريخ المسجل');
  document.getElementById('ov-lead').textContent = data.context.scope + ' · ' + periodText;
  renderOverviewKpis(data);
  renderOverviewFunnel(data);
  renderOverviewAttention(data);
  renderOverviewShortcuts(data);
}

function renderOverviewKpis(data) {
  var lifecycleValue = tr('Not yet', 'ليس بعد');
  var lifecycleUnit = '';
  var lifecycleSub = tr('no tender has a sent winner notification', 'لا يوجد عطاء أُرسل فيه إشعار الفائز');
  if (data.medianLifecycleDays !== null) {
    lifecycleValue = formatNumber(data.medianLifecycleDays, 1);
    lifecycleUnit = tr(' days', ' يوم');
    lifecycleSub = tr('creation → winner notification sent · ', 'الإنشاء ← إرسال إشعار الفائز · ') + data.completedTenders + tr(' tender(s)', ' عطاء');
  }

  renderKpiTiles('ov-kpis', [
    {
      label: tr('Tenders in scope', 'العطاءات في النطاق'), value: data.tenderCount, icon: 'doc', tone: 'orange',
      sub: data.completedTenders + tr(' completed · ', ' مكتمل · ') + data.inProgressTenders + tr(' in progress', ' قيد التنفيذ'),
      help: tr('Tenders with a round in the selected period, within your authorized scope.', 'العطاءات التي لها دورة ضمن الفترة المختارة وضمن نطاقك المصرح.')
    },
    {
      label: tr('Suppliers', 'الموردون'), value: data.supplierCount, icon: 'users', tone: 'teal',
      sub: formatInteger(data.participatingSuppliers) + tr(' submitted offers in closed bidding cycles', ' قدّموا عروضاً في دورات مغلقة'),
      help: tr('Every supplier company in your scope, whatever its registration status.', 'كل شركات الموردين في نطاقك أياً كانت حالة تسجيلها.')
    },
    {
      label: tr('Submitted offers', 'العروض المقدمة'), value: data.submittedOffers, icon: 'send', tone: 'violet',
      sub: data.sealedOffers ? data.sealedOffers + tr(' sealed offers excluded', ' عروض مختومة مستبعدة') : tr('no sealed offer pending', 'لا توجد عروض مختومة معلقة'),
      help: tr('Latest submitted revision per invitation, in closed bidding cycles only.', 'آخر نسخة مقدمة لكل دعوة، في الدورات المغلقة فقط.')
    },
    {
      label: tr('Median lifecycle', 'متوسط دورة الحياة'), value: lifecycleValue, unit: lifecycleUnit, sub: lifecycleSub, icon: 'clock', tone: 'blue',
      help: tr('Median of (winner notification sent - tender created), completed tenders only.', 'وسيط (إرسال إشعار الفائز - إنشاء العطاء) للعطاءات المكتملة فقط.')
    }
  ]);
}

// The five lifecycle steps: how many tenders reached each one, and after how many days (median).
function renderOverviewFunnel(data) {
  var labels = {
    created: tr('Created', 'تم الإنشاء'),
    invited: tr('Suppliers invited', 'دعوة الموردين'),
    closed: tr('Bidding closed', 'إغلاق العطاء'),
    awarded: tr('Officially awarded', 'الترسية الرسمية'),
    notified: tr('Winner notified', 'إشعار الفائز')
  };
  if (data.tenderCount > 0) {
    document.getElementById('ov-life-note').textContent = tr('Tenders in scope that reached each recorded milestone · median days from creation', 'العطاءات التي بلغت كل مرحلة مسجلة · وسيط الأيام من الإنشاء');
  } else {
    document.getElementById('ov-life-note').textContent = tr('No authorized tenders in this scope.', 'لا توجد عطاءات مصرح بها في هذا النطاق.');
  }

  var html = '';
  for (var i = 0; i < data.lifecycle.length; i++) {
    var step = data.lifecycle[i];
    var detail = tr('start of the lifecycle', 'بداية دورة الحياة');
    if (i > 0) {
      detail = step.medianDays === null
        ? tr('not recorded yet', 'غير مسجلة بعد')
        : tr('median ', 'الوسيط ') + formatNumber(step.medianDays, 1) + tr(' days from creation', ' يوم من الإنشاء');
    }
    html += '<li style="--w:' + (step.share * 100) + '%">' +
      '<span class="oi-funnel-step">' + (i + 1) + '</span>' +
      '<div><b>' + labels[step.key] + '</b><small>' + detail + '</small></div>' +
      '<i aria-hidden="true"><u></u></i><strong>' + step.reached + '</strong></li>';
  }
  document.getElementById('ov-funnel').innerHTML = html;
}

// "Worth your attention": low-competition cycles, long-running tenders and silent suppliers.
// Each item has a button that opens the evidence (a tender, or the Suppliers tab filtered on "No offer yet").
function renderOverviewAttention(data) {
  var html = '';
  for (var i = 0; i < data.attention.length; i++) {
    var item = data.attention[i];
    var icon = 'chart';
    var tone = 'orange';
    var title = '';
    var sub = '';
    var button = '';
    if (item.kind === 'low-competition') {
      title = item.count + tr(' closed bidding cycle(s) below ', ' دورة عطاء مغلقة دون ') + item.minimumOffers + tr(' offers', ' عروض');
      sub = item.examples.join(' · ');
      button = '<button type="button" class="rd-link" data-overview-tender="' + item.tenderId + '">' + tr('Open', 'فتح') + ' →</button>';
    } else if (item.kind === 'in-progress') {
      icon = 'clock';
      tone = 'blue';
      title = item.tenderTitle;
      sub = tr('In progress for ', 'قيد التنفيذ منذ ') + formatNumber(item.days, 0) + tr(' days · ', ' يوم · ') + item.status;
      button = '<button type="button" class="rd-link" data-overview-tender="' + item.tenderId + '">' + tr('Lifecycle', 'دورة الحياة') + ' →</button>';
    } else if (item.kind === 'silent-suppliers') {
      icon = 'mail';
      tone = 'violet';
      title = item.count + tr(' invited supplier(s) never submitted an offer', ' مورد مدعو لم يقدم أي عرض');
      sub = tr('Closed-round invitations without a submitted offer', 'دعوات لدورات مغلقة دون عرض مقدم');
      button = '<button type="button" class="rd-link" id="ov-open-silent">' + tr('Suppliers', 'الموردون') + ' →</button>';
    }
    html += '<li class="is-' + tone + '"><span class="oi-att-icon" aria-hidden="true"><svg viewBox="0 0 24 24">' + ICONS[icon] + '</svg></span>' +
      '<div><b>' + escapeHtml(title) + '</b><small>' + escapeHtml(sub) + '</small></div>' + button + '</li>';
  }
  if (html === '') {
    html = '<li class="oi-att-empty"><div><b>' + tr('Nothing needs attention', 'لا شيء يحتاج انتباهك') + '</b><small>' +
      tr('No low-competition round or long-running tender in this scope.', 'لا توجد دورة ضعيفة المنافسة أو عطاء طويل في هذا النطاق.') + '</small></div></li>';
  }
  document.getElementById('ov-attention').innerHTML = html;
}

function renderOverviewShortcuts(data) {
  var cards = [
    ['suppliers', tr('Suppliers', 'الموردون'), data.supplierCount + tr(' suppliers · directory, analytics and relationships', ' مورداً · الدليل والتحليلات والعلاقات'), ICONS.users],
    ['tenders', tr('Tenders', 'العطاءات'), data.tenderCount + tr(' tenders · lifecycle, offers and outcome', ' عطاءً · دورة الحياة والعروض والنتيجة'), ICONS.doc],
    ['studio', tr('AI Studio', 'استوديو الذكاء'), tr('Supplier summary, text chat and live voice with Gemini', 'ملخص المورد والمحادثة النصية والصوتية مع Gemini'), ICONS.spark]
  ];
  var html = '';
  for (var i = 0; i < cards.length; i++) {
    html += '<button type="button" class="oi-shortcut" data-tab-jump="' + cards[i][0] + '">' +
      '<span aria-hidden="true"><svg viewBox="0 0 24 24">' + cards[i][3] + '</svg></span>' +
      '<b>' + cards[i][1] + '</b><small>' + escapeHtml(cards[i][2]) + '</small><i aria-hidden="true">→</i></button>';
  }
  document.getElementById('ov-shortcuts').innerHTML = html;
}

function setupOverviewEvents() {
  // The attention list is redrawn often, so one listener on the list handles all of its buttons.
  document.getElementById('ov-attention').addEventListener('click', function (event) {
    var tenderButton = event.target.closest('[data-overview-tender]');
    if (tenderButton) {
      openTenderFromAnotherTab(Number(tenderButton.dataset.overviewTender), null);
    } else if (event.target.closest('#ov-open-silent')) {
      showSuppliersWithActivity('silent');
    }
  });
}
