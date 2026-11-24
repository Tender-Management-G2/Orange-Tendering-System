(function () {
    'use strict';
    var root = document.querySelector('[data-dr-item]');
    if (!root) return;
    var form = root.closest('form');
    var $ = function (selector, scope) { return (scope || form).querySelector(selector); };

    /* ---------- Specification catalog: dropdown lists that follow the item name and the category ---------- */
    function S(label, options, on) { return { label: label, options: options, on: on !== false }; }
    var BRAND = S('Brand', ['Any (as per offer)', 'Dell', 'HP', 'Lenovo', 'Cisco', 'Huawei', 'Samsung'], false);
    var WARRANTY = S('Warranty', ['1 year', '2 years', '3 years', '5 years']);
    var BY_ITEM = [
        { match: /laptop|notebook|ultrabook/i, specs: [S('Processor', ['Intel Core i5', 'Intel Core i7', 'Intel Core i9', 'AMD Ryzen 5', 'AMD Ryzen 7']), S('RAM', ['8 GB', '16 GB', '32 GB', '64 GB']), S('Storage', ['256 GB SSD', '512 GB SSD', '1 TB SSD', '2 TB SSD']), S('Display', ['13"', '14"', '15.6"', '17"']), S('Graphics', ['Integrated', 'Dedicated 4 GB', 'Dedicated 8 GB'], false), S('Operating System', ['Windows 11 Pro', 'Windows 11 Home', 'Ubuntu Linux', 'macOS'], false)] },
        { match: /desktop|workstation|\bpc\b|computer/i, specs: [S('Processor', ['Intel Core i5', 'Intel Core i7', 'Intel Core i9', 'AMD Ryzen 5', 'AMD Ryzen 7']), S('RAM', ['8 GB', '16 GB', '32 GB', '64 GB']), S('Storage', ['256 GB SSD', '512 GB SSD', '1 TB SSD', '2 TB HDD']), S('Graphics', ['Integrated', 'Dedicated 4 GB', 'Dedicated 8 GB'], false), S('Form Factor', ['Tower', 'Small Form Factor', 'All-in-One'], false), S('Operating System', ['Windows 11 Pro', 'Windows 11 Home', 'Ubuntu Linux'], false)] },
        { match: /server/i, specs: [S('Processor', ['Intel Xeon Silver', 'Intel Xeon Gold', 'AMD EPYC']), S('RAM', ['32 GB', '64 GB', '128 GB', '256 GB']), S('Storage', ['2 x 1 TB SSD', '4 x 2 TB SSD', '8 x 4 TB HDD']), S('Form Factor', ['1U Rack', '2U Rack', 'Tower']), S('Redundant Power', ['Yes', 'No']), S('Network Ports', ['4 x 1 GbE', '2 x 10 GbE', '4 x 10 GbE'], false)] },
        { match: /monitor|screen|display/i, specs: [S('Screen Size', ['21.5"', '24"', '27"', '32"']), S('Resolution', ['Full HD (1920x1080)', 'QHD (2560x1440)', '4K UHD']), S('Panel Type', ['IPS', 'VA', 'TN']), S('Refresh Rate', ['60 Hz', '75 Hz', '144 Hz'], false), S('Ports', ['HDMI + DisplayPort', 'HDMI', 'USB-C'], false)] },
        { match: /printer|scanner|copier/i, specs: [S('Type', ['Laser', 'Inkjet', 'Multifunction']), S('Colour', ['Black & White', 'Colour']), S('Print Speed', ['Up to 30 ppm', 'Up to 40 ppm', 'Up to 55 ppm']), S('Duplex', ['Automatic', 'Manual'], false), S('Connectivity', ['USB + Ethernet', 'USB + Ethernet + Wi-Fi'], false)] },
        { match: /switch|router|firewall|access point|wifi|wi-fi|network/i, specs: [S('Ports', ['8 ports', '24 ports', '48 ports']), S('Speed', ['1 GbE', '10 GbE', '25 GbE']), S('PoE', ['PoE+', 'No PoE'], false), S('Management', ['Managed (Layer 2)', 'Managed (Layer 3)', 'Unmanaged']), S('Mounting', ['Rack mount', 'Desktop', 'Wall mount'], false)] },
        { match: /phone|mobile|tablet/i, specs: [S('Storage', ['64 GB', '128 GB', '256 GB']), S('RAM', ['4 GB', '6 GB', '8 GB']), S('Screen Size', ['6.1"', '6.7"', '10.9"']), S('Connectivity', ['5G', '4G LTE', 'Wi-Fi only'])] },
        { match: /chair|desk|table|cabinet|sofa|shelf|locker/i, specs: [S('Material', ['Wood', 'Metal', 'Plastic', 'Leather', 'Fabric']), S('Dimensions', ['Standard', 'Small', 'Large', 'Custom (see notes)']), S('Colour', ['Black', 'White', 'Grey', 'Brown']), S('Adjustable', ['Yes', 'No'], false), WARRANTY] },
        { match: /paper|pen|stationery|toner|ink|folder|notebook/i, specs: [S('Size', ['A4', 'A3', 'Letter']), S('Quality', ['Standard', 'Premium']), S('Pack Size', ['Single', 'Pack of 10', 'Box of 50', 'Carton']), S('Colour', ['Black', 'Blue', 'Assorted'], false)] }
    ];
    var BY_CATEGORY = [
        { match: /it|electronic/i, specs: [BRAND, S('Model / Standard', ['As per tender', 'Enterprise grade', 'Standard grade']), WARRANTY, S('Delivery Time', ['2 weeks', '4 weeks', '8 weeks'], false)] },
        { match: /telecom/i, specs: [S('Technology', ['Fibre', '5G', '4G LTE', 'Microwave']), S('Speed / Capacity', ['100 Mbps', '1 Gbps', '10 Gbps']), S('Interfaces', ['Ethernet', 'SFP+', 'Voice (SIP)'], false), WARRANTY] },
        { match: /software|licen/i, specs: [S('License Type', ['Perpetual', 'Subscription', 'Open source support']), S('Users / Seats', ['1-10', '11-50', '51-200', '200+']), S('Duration', ['1 year', '2 years', '3 years']), S('Deployment', ['Cloud', 'On-premises', 'Hybrid'], false), S('Support', ['Business hours', '24/7'], false)] },
        { match: /office|suppl/i, specs: [S('Size', ['A4', 'A3', 'Standard']), S('Quality', ['Standard', 'Premium']), S('Pack Size', ['Single', 'Pack of 10', 'Box', 'Carton']), S('Colour', ['Black', 'Blue', 'Assorted'], false)] },
        { match: /furniture|fixture/i, specs: [S('Material', ['Wood', 'Metal', 'Plastic', 'Leather', 'Fabric']), S('Dimensions', ['Standard', 'Small', 'Large', 'Custom (see notes)']), S('Colour', ['Black', 'White', 'Grey', 'Brown']), WARRANTY] },
        { match: /electrical/i, specs: [S('Voltage', ['12 V', '24 V', '110 V', '230 V', '400 V']), S('Power Rating', ['Up to 1 kW', '1-10 kW', '10+ kW']), S('Standard', ['IEC', 'BS', 'Jordanian standard']), WARRANTY] },
        { match: /construction|maintenance/i, specs: [S('Material', ['Concrete', 'Steel', 'Wood', 'Aluminium', 'Paint / Coating']), S('Grade / Standard', ['As per BOQ', 'Class A', 'Class B']), S('Dimensions', ['As per drawings', 'Standard sizes']), S('Warranty', ['1 year', '2 years', '5 years'], false)] },
        { match: /medical|safety/i, specs: [S('Certification', ['CE', 'ISO 13485', 'FDA', 'Local approval']), S('Size', ['Small', 'Medium', 'Large', 'One size']), S('Sterile', ['Yes', 'No'], false), S('Pack Size', ['Box of 50', 'Box of 100', 'Carton'], false)] },
        { match: /clean|facilit/i, specs: [S('Type', ['Liquid', 'Powder', 'Equipment', 'Consumable']), S('Volume / Size', ['1 L', '5 L', '20 L', 'Drum']), S('Concentration', ['Ready to use', 'Concentrate'], false), S('Pack Size', ['Single', 'Carton'], false)] },
        { match: /transport|logistic/i, specs: [S('Vehicle Type', ['Van', 'Truck', 'Pick-up', 'Bus']), S('Capacity', ['Up to 1 ton', '1-5 tons', '5+ tons']), S('Duration', ['Per trip', 'Per day', 'Per month']), S('Insurance', ['Included', 'Not included'], false)] },
        { match: /food|hospital/i, specs: [S('Type', ['Hot meals', 'Cold meals', 'Beverages', 'Catering service']), S('Servings', ['Up to 20', '20-100', '100+']), S('Dietary', ['Standard', 'Vegetarian', 'Halal certified'], false), S('Delivery', ['One-off', 'Daily', 'Weekly'], false)] },
        { match: /professional|service|consult/i, specs: [S('Scope', ['Consulting', 'Implementation', 'Training', 'Audit']), S('Duration', ['1 month', '3 months', '6 months', '12 months']), S('Deliverables', ['Report', 'Working system', 'Training material'], false), S('Experience Level', ['Senior', 'Mid-level', 'Mixed team'], false)] },
        { match: /marketing|advertis/i, specs: [S('Channel', ['Digital', 'Print', 'Outdoor', 'Events']), S('Duration', ['1 month', '3 months', '6 months']), S('Deliverables', ['Creative design', 'Campaign management', 'Reporting'], false)] }
    ];
    var GENERIC = [BRAND, S('Model / Standard', ['As per tender', 'Standard grade', 'Premium grade']), WARRANTY, S('Delivery Time', ['2 weeks', '4 weeks', '8 weeks'], false)];
    function templateFor(categoryName, itemName) {
        var i, name = (itemName || '').trim();
        if (name) for (i = 0; i < BY_ITEM.length; i++) if (BY_ITEM[i].match.test(name)) return BY_ITEM[i].specs;
        if (categoryName) for (i = 0; i < BY_CATEGORY.length; i++) if (BY_CATEGORY[i].match.test(categoryName)) return BY_CATEGORY[i].specs;
        return GENERIC;
    }

    /* ---------- Specification rows ---------- */
    var nameInput = $('[data-dr-name]'), category = $('[data-dr-category]'), grid = $('[data-dr-specs]'), hint = $('[data-dr-specs-hint]');
    var valueInput = $('[data-dr-spec-value]'), extra = $('[data-dr-extra]'), specError = $('[data-dr-spec-error]');
    var kept = {}; // label -> { on, value } so a change of item keeps what the employee already chose
    function parseInitial(text) {
        var lines = (text || '').split(/\r?\n|;\s+/).map(function (l) { return l.trim(); }).filter(Boolean), extras = [];
        lines.forEach(function (line) {
            var m = /^([^:]{2,40}):\s*(.+)$/.exec(line);
            if (m) kept[m[1].trim().toLowerCase()] = { on: true, value: m[2].trim() }; else extras.push(line);
        });
        extra.value = extras.join('\n');
    }
    function categoryName() { var o = category.options[category.selectedIndex]; return o && o.value ? o.textContent.trim() : ''; }
    function remember() {
        grid.querySelectorAll('[data-dr-spec]').forEach(function (row) {
            kept[row.dataset.label.toLowerCase()] = { on: row.querySelector('input[type=checkbox]').checked, value: row.querySelector('select').value };
        });
    }
    function compose() {
        var lines = [];
        grid.querySelectorAll('[data-dr-spec]').forEach(function (row) {
            if (row.querySelector('input[type=checkbox]').checked) lines.push(row.dataset.label + ': ' + row.querySelector('select').value);
        });
        // Specs remembered from an earlier item type stay in the request only while their row is visible.
        var more = extra.value.trim(); if (more) lines.push(more);
        valueInput.value = lines.join('\n');
        if (specError && valueInput.value.trim().length >= 3) specError.hidden = true;
    }
    function render() {
        remember();
        var list = templateFor(categoryName(), nameInput.value), labels = {};
        grid.innerHTML = '';
        list.forEach(function (spec, index) {
            var key = spec.label.toLowerCase(), saved = kept[key], row = document.createElement('div'), id = 'dr-spec-' + index;
            row.className = 'dr-spec'; row.dataset.drSpec = ''; row.dataset.label = spec.label;
            var opts = spec.options.slice(); if (saved && saved.value && opts.indexOf(saved.value) < 0) opts.push(saved.value);
            var on = saved ? saved.on : spec.on;
            row.innerHTML = '<label class="dr-spec-check" for="' + id + '"><input type="checkbox" id="' + id + '"><span></span></label><select aria-label=""></select>';
            row.querySelector('span').textContent = spec.label; row.querySelector('select').setAttribute('aria-label', spec.label);
            var select = row.querySelector('select');
            opts.forEach(function (o) { var el = document.createElement('option'); el.textContent = o; el.value = o; select.appendChild(el); });
            if (saved && saved.value) select.value = saved.value;
            var box = row.querySelector('input'); box.checked = on; select.disabled = !on; row.classList.toggle('is-on', on);
            box.addEventListener('change', function () { select.disabled = !box.checked; row.classList.toggle('is-on', box.checked); compose(); });
            select.addEventListener('change', compose);
            grid.appendChild(row); labels[key] = true;
        });
        hint.hidden = true; compose();
    }
    parseInitial(valueInput.value);
    var firstRender = true;
    [nameInput, category].forEach(function (el) { el.addEventListener(el === nameInput ? 'input' : 'change', function () { render(); preview(); }); });
    extra.addEventListener('input', compose);
    render(); firstRender = false;

    /* ---------- Quantity stepper, title preview ---------- */
    var qty = $('[data-dr-quantity]');
    form.querySelectorAll('[data-dr-qty]').forEach(function (button) {
        button.addEventListener('click', function () {
            var next = Math.max(1, Math.floor(Number(qty.value) || 1) + Number(button.dataset.drQty));
            qty.value = String(next); qty.dispatchEvent(new Event('input', { bubbles: true }));
        });
    });
    qty.addEventListener('change', function () { qty.value = String(Math.max(1, Math.floor(Number(qty.value) || 1))); });
    var title = $('[data-title-preview]');
    function preview() { var n = nameInput.value.trim(); if (n && title) title.textContent = n + ' — ' + title.dataset.department; }

    /* ---------- Item image: upload, preview, change, remove ---------- */
    var imageBox = $('[data-dr-image]'), file = $('[data-dr-file]'), img = $('[data-dr-preview]'), empty = $('[data-dr-empty]'), tools = $('[data-dr-tools]');
    var removed = $('[data-dr-removed]'), imageError = $('[data-dr-image-error]'), changeLabel = $('[data-dr-change-label]'), drop = $('[data-dr-drop]');
    var hadStored = imageBox.dataset.hasImage === 'true', storedSrc = img.getAttribute('src');
    function showError(message) { imageError.textContent = message || ''; }
    function hasImage() { return !img.hidden && !!img.getAttribute('src'); }
    function setImage(src) {
        img.src = src; img.hidden = false; empty.hidden = true; tools.hidden = false; changeLabel.textContent = 'Change Image'; removed.value = 'false'; showError('');
    }
    function accept(f) {
        if (!f) return;
        if (!/^image\/(png|jpeg)$/.test(f.type) && !/\.(png|jpe?g)$/i.test(f.name)) { file.value = ''; showError('Use a PNG or JPEG picture.'); return; }
        if (f.size > 3 * 1024 * 1024) { file.value = ''; showError('The picture is larger than 3 MB.'); return; }
        setImage(URL.createObjectURL(f));
    }
    form.querySelectorAll('[data-dr-change]').forEach(function (b) { b.addEventListener('click', function () { file.click(); }); });
    file.addEventListener('change', function () { accept(file.files[0]); });
    $('[data-dr-remove]').addEventListener('click', function () {
        file.value = ''; img.removeAttribute('src'); img.hidden = true; empty.hidden = false; tools.hidden = true; changeLabel.textContent = 'Upload Image';
        removed.value = hadStored ? 'true' : 'false';
    });
    ['dragenter', 'dragover'].forEach(function (n) { drop.addEventListener(n, function (e) { e.preventDefault(); drop.classList.add('is-over'); }); });
    ['dragleave', 'drop'].forEach(function (n) { drop.addEventListener(n, function (e) { e.preventDefault(); drop.classList.remove('is-over'); }); });
    drop.addEventListener('drop', function (e) {
        var f = e.dataTransfer && e.dataTransfer.files[0]; if (!f) return;
        var t = new DataTransfer(); t.items.add(f); file.files = t.files; accept(f);
    });
    empty.addEventListener('click', function () { file.click(); });

    /* ---------- Supporting documents counter ---------- */
    var attachments = document.getElementById('Attachments'), count = $('[data-file-count]');
    if (attachments && count) attachments.addEventListener('change', function () { count.textContent = attachments.files.length ? attachments.files.length + ' file(s) selected' : ''; });

    /* ---------- One page: check the few things the browser cannot check before the request is sent ---------- */
    var submit = $('[data-dr-submit]'), method = document.getElementById('EvaluationMethod');
    if (submit) submit.addEventListener('click', function (event) {
        compose();
        if (!form.checkValidity()) return; // the browser shows its own message for the required fields
        var problem = null;
        if (valueInput.value.trim().length < 3) { specError.hidden = false; problem = specError; }
        else if (!hasImage()) { showError('Upload a picture of the item before submitting.'); problem = imageBox; }
        else if (method && !method.value) { method.setCustomValidity('Choose an evaluation method before sending the request.'); method.reportValidity(); method.addEventListener('change', function () { method.setCustomValidity(''); }, { once: true }); problem = method; }
        if (problem) { event.preventDefault(); event.stopImmediatePropagation(); problem.scrollIntoView({ block: 'center', behavior: 'smooth' }); }
    }, true);
    preview();
}());
