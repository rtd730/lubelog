var scatterChartInstance = null;
var mapInstance = null;
var mapLayerGroup = null;
var editingChartAxisId = null;
var scatterColorField = null;
var scatterColorMin = 0;
var scatterColorMax = 0;
var lastMapPoints = null;
var chartFieldMap = {};  // key → displayName


function initCharts(vehicleId) {
    loadChartFields(vehicleId);
    loadSavedFilters(vehicleId);
}

function loadChartFields(vehicleId) {
    $.get('/Vehicle/GetAvailableFields?vehicleId=' + vehicleId + '&fileType=LOG', function (fields) {
        var xSelect = $('#chartXField');
        var ySelect = $('#chartYField');
        xSelect.empty();
        ySelect.empty();
        chartFieldMap = {};

        // Separate raw fields (for filters) from all fields
        var rawFields = [];

        fields.forEach(function (f) {
            chartFieldMap[f.key] = f.displayName;
            xSelect.append('<option value="' + f.key + '">' + f.displayName + '</option>');
            ySelect.append('<option value="' + f.key + '">' + f.displayName + '</option>');
            if (!f.isCalculated) rawFields.push(f);
        });

        // Pre-select defaults
        if (chartFieldMap['speed']) {
            xSelect.val('speed');
        } 
        if (chartFieldMap['rpm']) ySelect.val('rpm');

        // Filter rule dropdowns only get raw fields (calculated fields can't be filtered)
        updateFilterFieldDropdowns(rawFields);

        // Populate X2/Y2 dropdowns
        var x2Select = $('#chartX2Field');
        var y2Select = $('#chartY2Field');
        x2Select.empty().append('<option value="">None</option>');
        y2Select.empty().append('<option value="">None</option>');
        fields.forEach(function (f) {
            x2Select.append('<option value="' + f.key + '">' + f.displayName + '</option>');
            y2Select.append('<option value="' + f.key + '">' + f.displayName + '</option>');
        });

        // Map color dropdown
        var colorSelect = $('#mapColorField');
        colorSelect.empty().append('<option value="">None (track only)</option>');
        fields.forEach(function (f) {
            colorSelect.append('<option value="' + f.key + '">' + f.displayName + '</option>');
        });
    });
}

function updateFilterFieldDropdowns(fields) {
    $('#filterRulesContainer .filter-field-select').each(function () {
        var current = $(this).val();
        $(this).empty();
        fields.forEach(function (f) {
            $(this).append('<option value="' + f.key + '">' + f.displayName + '</option>');
        }.bind(this));
        if (current) {
            $(this).val(current);
        }
    });
}

function plotChart() {
    var viewMode = $('input[name="viewMode"]:checked').val();
    if (viewMode === 'map') {
        plotMap();
        return;
    }

    var vehicleId = parseInt($('#chartVehicleId').val());
    var xField = $('#chartXField').val();
    var yField = $('#chartYField').val();
    if (!xField || !yField) {
        errorToast('Select both X and Y fields');
        return;
    }

    var colorField = $('#mapColorField').val();
    var fields = [xField, yField];
    if (colorField) fields.push(colorField);

    var x2Field = $('#chartX2Field').val();
    var y2Field = $('#chartY2Field').val();

    var sourceFilenames = getSelectedSourceFilenames();

    var request = {
        vehicleId: vehicleId,
        fields: fields,
        startDate: $('#chartStartDate').val() || null,
        endDate: $('#chartEndDate').val() || null,
        fileType: 'LOG',
        filter: buildFilterFromUI(),
        maxPoints: 50000,
        downsample: true,
        sourceFilenames: sourceFilenames
    };

    // Clear axis scale inputs so chart auto-scales, then re-populates
    $('#chartXMin, #chartXMax, #chartYMin, #chartYMax, #chartX2Min, #chartX2Max, #chartY2Min, #chartY2Max').val('');

    $.ajax({
        url: '/Vehicle/GetTelemetryPoints',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(request),
        success: function (points1) {
            if (points1.error) { errorToast(points1.error); return; }

            if (x2Field && y2Field) {
                var fields2 = [x2Field, y2Field];
                if (colorField) fields2.push(colorField);
                var request2 = $.extend({}, request, { fields: fields2 });
                $.ajax({
                    url: '/Vehicle/GetTelemetryPoints',
                    type: 'POST',
                    contentType: 'application/json',
                    data: JSON.stringify(request2),
                    success: function (points2) {
                        if (points2.error) { errorToast(points2.error); return; }
                        $('#chartPointCount').text(points1.length + ' + ' + points2.length + ' points');
                        renderScatterChart(points1, xField, yField, points2, x2Field, y2Field);
                    },
                    error: function () { errorToast('Failed to load chart data'); }
                });
            } else {
                $('#chartPointCount').text(points1.length + ' points');
                renderScatterChart(points1, xField, yField, null, null, null);
            }
        },
        error: function () {
            errorToast('Failed to load chart data');
        }
    });
}

function renderScatterChart(points, xLabel, yLabel, points2, x2Label, y2Label) {
    if (scatterChartInstance) {
        scatterChartInstance.destroy();
    }

    var colorField = $('#mapColorField').val();

    // Compute color scale across both datasets
    var minC = 0, maxC = 0, range = 0;
    if (colorField) {
        var cValues = points.filter(function (p) { return p.c !== null; }).map(function (p) { return p.c; });
        if (points2) {
            var cValues2 = points2.filter(function (p) { return p.c !== null; }).map(function (p) { return p.c; });
            cValues = cValues.concat(cValues2);
        }
        minC = Math.min.apply(null, cValues);
        maxC = Math.max.apply(null, cValues);
        range = maxC - minC;
    }

    // Dataset 1 colors
    var colors1;
    if (colorField) {
        colors1 = points.map(function (p) {
            if (p.c === null) return 'rgba(54, 162, 235, 0.5)';
            var ratio = range > 0 ? (p.c - minC) / range : 0;
            return metricToColor(ratio);
        });
    } else {
        colors1 = 'rgba(54, 162, 235, 0.5)';
    }

    var xDisplayName = chartFieldMap[xLabel] || xLabel;
    var yDisplayName = chartFieldMap[yLabel] || yLabel;

    var datasets = [{
        label: yDisplayName + ' vs ' + xDisplayName,
        data: points,
        backgroundColor: colors1,
        pointRadius: 2,
        xAxisID: 'x',
        yAxisID: 'y'
    }];

    var scales = {
        x: {
            position: 'bottom',
            title: { display: true, text: xDisplayName, color: '#2196F3' },
            ticks: { color: '#2196F3' }
        },
        y: {
            position: 'left',
            title: { display: true, text: yDisplayName, color: '#2196F3' },
            ticks: { color: '#2196F3' }
        }
    };

    if (points2) {
        var colors2;
        if (colorField) {
            colors2 = points2.map(function (p) {
                if (p.c === null) return 'rgba(244, 67, 54, 0.5)';
                var ratio = range > 0 ? (p.c - minC) / range : 0;
                return metricToColor(ratio);
            });
        } else {
            colors2 = 'rgba(244, 67, 54, 0.5)';
        }

        var x2DisplayName = chartFieldMap[x2Label] || x2Label;
        var y2DisplayName = chartFieldMap[y2Label] || y2Label;

        datasets.push({
            label: y2DisplayName + ' vs ' + x2DisplayName,
            data: points2,
            backgroundColor: colors2,
            pointRadius: 2,
            xAxisID: 'x2',
            yAxisID: 'y2'
        });

        scales.x2 = {
            position: 'top',
            title: { display: true, text: x2DisplayName, color: '#F44336' },
            ticks: { color: '#F44336' },
            grid: { drawOnChartArea: false }
        };
        scales.y2 = {
            position: 'right',
            title: { display: true, text: y2DisplayName, color: '#F44336' },
            ticks: { color: '#F44336' },
            grid: { drawOnChartArea: false }
        };
    }

    var ctx = document.getElementById('scatterChart').getContext('2d');
    scatterChartInstance = new Chart(ctx, {
        type: 'scatter',
        data: { datasets: datasets },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                tooltip: {
                    callbacks: {
                        label: function (context) {
                            var p = context.raw;
                            var ds = context.dataset;
                            var tip = ds.label + ' — x: ' + p.x.toFixed(2) + ', y: ' + p.y.toFixed(2);
                            if (colorField && p.c !== null) tip += ', ' + (chartFieldMap[colorField] || colorField) + ': ' + p.c.toFixed(2);
                            return tip;
                        }
                    }
                }
            },
            scales: scales
        }
    });

    // Store color state for legend double-click rescaling
    scatterColorField = colorField || null;
    scatterColorMin = minC;
    scatterColorMax = maxC;

    updateColorLegend(colorField ? minC : 0, colorField ? maxC : 0, colorField || null);

    // Attach scroll-to-zoom
    var canvas = document.getElementById('scatterChart');
    canvas.removeEventListener('wheel', scatterWheelHandler);
    canvas.addEventListener('wheel', scatterWheelHandler, { passive: false });
}

function scatterWheelHandler(e) {
    if (!scatterChartInstance) return;
    e.preventDefault();

    var chart = scatterChartInstance;
    var rect = chart.canvas.getBoundingClientRect();
    var chartArea = chart.chartArea;
    var mouseX = e.clientX - rect.left;
    var mouseY = e.clientY - rect.top;
    var fractionX = (mouseX - chartArea.left) / (chartArea.right - chartArea.left);
    var fractionY = (mouseY - chartArea.top) / (chartArea.bottom - chartArea.top);
    fractionX = Math.max(0, Math.min(1, fractionX));
    fractionY = Math.max(0, Math.min(1, fractionY));

    var zoomFactor = e.deltaY > 0 ? 1.2 : 0.8;

    // Zoom both X and Y axes anchored at cursor
    ['x', 'x2'].forEach(function (axisId) {
        var scale = chart.scales[axisId];
        if (!scale) return;
        var range = scale.max - scale.min;
        var newRange = range * zoomFactor;
        var anchor = scale.min + range * fractionX;
        chart.options.scales[axisId].min = anchor - newRange * fractionX;
        chart.options.scales[axisId].max = anchor + newRange * (1 - fractionX);
    });
    ['y', 'y2'].forEach(function (axisId) {
        var scale = chart.scales[axisId];
        if (!scale) return;
        var range = scale.max - scale.min;
        var newRange = range * zoomFactor;
        // Y is inverted (top=min visually)
        var anchor = scale.max - range * fractionY;
        chart.options.scales[axisId].min = anchor - newRange * (1 - fractionY);
        chart.options.scales[axisId].max = anchor + newRange * fractionY;
    });

    chart.update('none');
}

// Double-click: axis area opens config popup, chart area does nothing
$(document).on('dblclick', '#scatterChart', function (e) {
    if (!scatterChartInstance) return;

    var chart = scatterChartInstance;
    var rect = chart.canvas.getBoundingClientRect();
    var clickX = e.clientX - rect.left;
    var clickY = e.clientY - rect.top;

    var clickedAxisId = null;
    Object.keys(chart.scales).forEach(function (id) {
        var scale = chart.scales[id];
        if (clickX >= scale.left && clickX <= scale.right &&
            clickY >= scale.top && clickY <= scale.bottom) {
            clickedAxisId = id;
        }
    });

    if (clickedAxisId) {
        showChartAxisPopup(clickedAxisId, e.clientX, e.clientY);
    }
});

function showChartAxisPopup(axisId, mouseX, mouseY) {
    editingChartAxisId = axisId;
    var chart = scatterChartInstance;
    var scale = chart.scales[axisId];
    var optScale = chart.options.scales[axisId] || {};

    var titleText = optScale.title && optScale.title.text ? optScale.title.text : axisId;
    $('#chartAxisConfigTitle').text('Edit: ' + titleText);

    var curMin = optScale.min != null ? optScale.min : scale.min;
    var curMax = optScale.max != null ? optScale.max : scale.max;
    $('#chartAxisMin').val(Math.round(curMin * 100) / 100);
    $('#chartAxisMax').val(Math.round(curMax * 100) / 100);

    // Position popup near click
    var container = $('#chartAxisConfigPopup').parent();
    var containerRect = container[0].getBoundingClientRect();
    var popupLeft = mouseX - containerRect.left + 10;
    var popupTop = mouseY - containerRect.top - 50;
    popupLeft = Math.min(popupLeft, containerRect.width - 230);
    popupTop = Math.max(10, Math.min(popupTop, containerRect.height - 200));

    $('#chartAxisConfigPopup').css({ left: popupLeft + 'px', top: popupTop + 'px' }).show();
}

function applyChartAxisConfig() {
    if (!editingChartAxisId || !scatterChartInstance) return;
    var chart = scatterChartInstance;

    var minVal = $('#chartAxisMin').val();
    var maxVal = $('#chartAxisMax').val();
    chart.options.scales[editingChartAxisId].min = minVal !== '' ? parseFloat(minVal) : undefined;
    chart.options.scales[editingChartAxisId].max = maxVal !== '' ? parseFloat(maxVal) : undefined;
    chart.update('none');
    $('#chartAxisConfigPopup').hide();
}

function resetChartAxisConfig() {
    if (!editingChartAxisId || !scatterChartInstance) return;
    scatterChartInstance.options.scales[editingChartAxisId].min = undefined;
    scatterChartInstance.options.scales[editingChartAxisId].max = undefined;
    scatterChartInstance.update('none');
    $('#chartAxisConfigPopup').hide();
}

// Double-click color legend to edit color scale range
$(document).on('dblclick', '#colorLegend', function (e) {
    if (!scatterColorField) return;

    $('#colorScaleTitle').text('Edit: ' + (chartFieldMap[scatterColorField] || scatterColorField));
    $('#colorScaleMin').val(Math.round(scatterColorMin * 100) / 100);
    $('#colorScaleMax').val(Math.round(scatterColorMax * 100) / 100);

    var container = $('#colorScalePopup').parent();
    var containerRect = container[0].getBoundingClientRect();
    var popupLeft = e.clientX - containerRect.left - 230;
    var popupTop = e.clientY - containerRect.top - 50;
    popupLeft = Math.max(10, popupLeft);
    popupTop = Math.max(10, Math.min(popupTop, containerRect.height - 200));

    $('#colorScalePopup').css({ left: popupLeft + 'px', top: popupTop + 'px' }).show();
});

function applyColorScale() {
    if (!scatterColorField) return;

    var newMin = parseFloat($('#colorScaleMin').val());
    var newMax = parseFloat($('#colorScaleMax').val());
    if (isNaN(newMin) || isNaN(newMax) || newMin >= newMax) return;

    scatterColorMin = newMin;
    scatterColorMax = newMax;

    var viewMode = $('input[name="viewMode"]:checked').val();

    if (viewMode === 'map') {
        recolorMap(newMin, newMax);
    } else {
        var range = newMax - newMin;
        scatterChartInstance.data.datasets.forEach(function (ds) {
            if (!Array.isArray(ds.data) || ds.data.length === 0) return;
            if (ds.data[0].c === undefined) return;

            var defaultColor = ds.yAxisID === 'y2' ? 'rgba(244, 67, 54, 0.5)' : 'rgba(54, 162, 235, 0.5)';
            ds.backgroundColor = ds.data.map(function (p) {
                if (p.c === null) return defaultColor;
                var ratio = range > 0 ? Math.max(0, Math.min(1, (p.c - newMin) / range)) : 0;
                return metricToColor(ratio);
            });
        });
        scatterChartInstance.update('none');
    }

    updateColorLegend(newMin, newMax, scatterColorField);
    $('#colorScalePopup').hide();
}

function resetColorScale() {
    if (!scatterColorField) return;

    // Recalculate min/max from data
    var cValues = [];
    var viewMode = $('input[name="viewMode"]:checked').val();

    if (viewMode === 'map') {
        if (lastMapPoints) {
            lastMapPoints.forEach(function (p) {
                if (p.c !== null && p.c !== undefined) cValues.push(p.c);
            });
        }
    } else {
        if (scatterChartInstance) {
            scatterChartInstance.data.datasets.forEach(function (ds) {
                if (!Array.isArray(ds.data)) return;
                ds.data.forEach(function (p) {
                    if (p.c !== null && p.c !== undefined) cValues.push(p.c);
                });
            });
        }
    }
    if (cValues.length === 0) return;

    var newMin = Math.min.apply(null, cValues);
    var newMax = Math.max.apply(null, cValues);
    scatterColorMin = newMin;
    scatterColorMax = newMax;

    if (viewMode === 'map') {
        recolorMap(newMin, newMax);
    } else {
        var range = newMax - newMin;
        scatterChartInstance.data.datasets.forEach(function (ds) {
            if (!Array.isArray(ds.data) || ds.data.length === 0) return;
            if (ds.data[0].c === undefined) return;

            var defaultColor = ds.yAxisID === 'y2' ? 'rgba(244, 67, 54, 0.5)' : 'rgba(54, 162, 235, 0.5)';
            ds.backgroundColor = ds.data.map(function (p) {
                if (p.c === null) return defaultColor;
                var ratio = range > 0 ? (p.c - newMin) / range : 0;
                return metricToColor(ratio);
            });
        });
        scatterChartInstance.update('none');
    }

    updateColorLegend(newMin, newMax, scatterColorField);
    $('#colorScalePopup').hide();
}


// --- Filter management ---

function loadSavedFilters(vehicleId) {
    $.get('/Vehicle/GetFilters?vehicleId=' + vehicleId, function (filters) {
        var select = $('#chartFilterSelect');
        select.empty().append('<option value="">None</option>');
        filters.forEach(function (f) {
            select.append('<option value="' + f.id + '" data-filter=\'' + JSON.stringify(f) + '\'>' + f.name + '</option>');
        });
    });
}

function toggleFilterBuilder() {
    $('#filterBuilderCard').toggle();
}

function addFilterRule() {
    // Get current fields from the X axis dropdown
    var options = '';
    $('#chartXField option').each(function () {
        options += '<option value="' + $(this).val() + '">' + $(this).text() + '</option>';
    });

    var operators = '<option value="Eq">=</option>' +
        '<option value="Ne">≠</option>' +
        '<option value="Gt">&gt;</option>' +
        '<option value="Gte">≥</option>' +
        '<option value="Lt">&lt;</option>' +
        '<option value="Lte">≤</option>' +
        '<option value="Contains">contains</option>';

    var row = '<div class="filter-rule d-flex gap-2 mb-1 align-items-center">' +
        '<select class="form-select form-select-sm filter-field-select">' + options + '</select>' +
        '<select class="form-select form-select-sm filter-op-select" style="width:80px;">' + operators + '</select>' +
        '<input type="text" class="form-control form-control-sm filter-value-input" placeholder="value" />' +
        '<button class="btn btn-sm btn-outline-danger" onclick="removeFilterRule(this)"><i class="bi bi-x"></i></button>' +
        '</div>';

    $('#filterRulesContainer').append(row);
}

function removeFilterRule(btn) {
    $(btn).closest('.filter-rule').remove();
}

function buildFilterFromUI() {
    var rules = [];
    $('#filterRulesContainer .filter-rule').each(function () {
        rules.push({
            field: $(this).find('.filter-field-select').val(),
            operator: $(this).find('.filter-op-select').val(),
            value: $(this).find('.filter-value-input').val()
        });
    });

    if (rules.length === 0) {
        // Check if a saved filter is selected
        var selected = $('#chartFilterSelect option:selected');
        if (selected.val() && selected.data('filter')) {
            return selected.data('filter');
        }
        return null;
    }

    var logic = $('input[name="filterLogic"]:checked').val() || 'And';
    return {
        vehicleId: parseInt($('#chartVehicleId').val()),
        name: $('#filterName').val() || '',
        logic: logic,
        rules: rules
    };
}

function saveCurrentFilter() {
    var filter = buildFilterFromUI();
    if (!filter || !filter.rules || filter.rules.length === 0) {
        errorToast('Add at least one rule before saving');
        return;
    }
    if (!filter.name) {
        errorToast('Enter a filter name');
        return;
    }
    filter.vehicleId = parseInt($('#chartVehicleId').val());

    $.ajax({
        url: '/Vehicle/SaveFilter',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(filter),
        success: function (result) {
            if (result.success) {
                successToast(result.message);
                loadSavedFilters(filter.vehicleId);
            } else {
                errorToast(result.message);
            }
        }
    });
}

function deleteSelectedFilter() {
    var filterId = $('#chartFilterSelect').val();
    if (!filterId) {
        errorToast('Select a filter to delete');
        return;
    }
    $.post('/Vehicle/DeleteFilter?filterId=' + filterId, function (result) {
        if (result.success) {
            successToast(result.message);
            loadSavedFilters(parseInt($('#chartVehicleId').val()));
        } else {
            errorToast(result.message);
        }
    });
}

function setViewMode(mode) {
    if (mode === 'map') {
        $('#chartContainer').hide();
        $('#mapContainer').show();
        $('#axisScaleRow').hide();
        $('#axisRow1').hide();
        $('#axisRow2').hide();
        if (!mapInstance) {
            initMap();
        }
        mapInstance.invalidateSize();
    } else {
        $('#chartContainer').show();
        $('#mapContainer').hide();
        $('#axisScaleRow').show();
        $('#axisRow1').show();
        $('#axisRow2').show();
    }
}

function initMap() {
    mapInstance = L.map('mapContainer').setView([0, 0], 2);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; OpenStreetMap contributors',
        maxZoom: 19
    }).addTo(mapInstance);
    mapLayerGroup = L.layerGroup().addTo(mapInstance);
}

function plotMap() {
    var vehicleId = $('#chartVehicleId').val();
    var colorField = $('#mapColorField').val();
    var fields = ['lat', 'lng'];
    if (colorField) fields.push(colorField);

    var sourceFilenames = getSelectedSourceFilenames();

    var request = {
        vehicleId: parseInt(vehicleId),
        fields: fields,
        startDate: $('#chartStartDate').val(),
        endDate: $('#chartEndDate').val(),
        fileType: 'LOG',
        maxPoints: 50000,
        downsample: true,
        sourceFilenames: sourceFilenames
    };

    var filterVal = $('#chartFilterSelect').val();
    if (filterVal) {
        request.filter = JSON.parse(filterVal);
    }

    $.ajax({
        url: '/Vehicle/GetTelemetryPoints',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(request),
        success: function (points) {
            renderMap(points, colorField);
            $('#chartPointCount').text(points.length + ' pts');
        },
        error: function () { errorToast('Failed to load map data'); }
    });
}

function renderMap(points, colorField) {
    mapLayerGroup.clearLayers();

    if (!points || points.length === 0) {
        errorToast('No GPS data found for this range');
        return;
    }

    var coords = points.map(function (p) { return [p.x, p.y]; });

    // Always draw the route line
    var lineColor = colorField ? '#999' : '#3388ff';
    var polyline = L.polyline(coords, { color: lineColor, weight: colorField ? 2 : 3 });
    mapLayerGroup.addLayer(polyline);

    var minC = 0, maxC = 0;

    // If color field, draw colored circle markers
    if (colorField) {
        var cValues = points.filter(function (p) { return p.c !== null; }).map(function (p) { return p.c; });
        minC = Math.min.apply(null, cValues);
        maxC = Math.max.apply(null, cValues);
        var range = maxC - minC;

        points.forEach(function (p) {
            if (p.c === null) return;
            var ratio = range > 0 ? (p.c - minC) / range : 0;
            var color = metricToColor(ratio);
            var marker = L.circleMarker([p.x, p.y], {
                radius: 4,
                color: color,
                fillColor: color,
                fillOpacity: 0.8,
                weight: 1
            });
            marker.bindTooltip((chartFieldMap[colorField] || colorField) + ': ' + p.c.toFixed(2) + '<br>' + p.label);
            mapLayerGroup.addLayer(marker);
        });
    }

    mapInstance.fitBounds(polyline.getBounds(), { padding: [20, 20] });

    // Store color state so legend double-click works in map mode too
    scatterColorField = colorField || null;
    scatterColorMin = minC;
    scatterColorMax = maxC;
    lastMapPoints = points;

    updateColorLegend(colorField ? minC : 0, colorField ? maxC : 0, colorField || null);

}

function recolorMap(newMin, newMax) {
    if (!mapLayerGroup || !lastMapPoints || !scatterColorField) return;

    mapLayerGroup.clearLayers();

    var coords = lastMapPoints.map(function (p) { return [p.x, p.y]; });
    var polyline = L.polyline(coords, { color: '#999', weight: 2 });
    mapLayerGroup.addLayer(polyline);

    var range = newMax - newMin;
    lastMapPoints.forEach(function (p) {
        if (p.c === null) return;
        var ratio = range > 0 ? Math.max(0, Math.min(1, (p.c - newMin) / range)) : 0;
        var color = metricToColor(ratio);
        var marker = L.circleMarker([p.x, p.y], {
            radius: 4,
            color: color,
            fillColor: color,
            fillOpacity: 0.8,
            weight: 1
        });
        marker.bindTooltip((chartFieldMap[scatterColorField] || scatterColorField) + ': ' + p.c.toFixed(2) + '<br>' + p.label);
        mapLayerGroup.addLayer(marker);
    });
}

function metricToColor(ratio) {
    var r, g;
    if (ratio < 0.5) {
        r = Math.round(255 * ratio * 2);
        g = 255;
    } else {
        r = 255;
        g = Math.round(255 * (1 - (ratio - 0.5) * 2));
    }
    return 'rgb(' + r + ',' + g + ',0)';
}

function updateColorLegend(minVal, maxVal, fieldName) {
    if (fieldName) {
        $('#colorLegend').removeClass('d-none').addClass('d-flex');
        $('#colorLegendMin').text(minVal.toFixed(2));
        $('#colorLegendMax').text(maxVal.toFixed(2));
        $('#colorLegendLabel').text(chartFieldMap[fieldName] || fieldName);
    } else {
        $('#colorLegend').removeClass('d-flex').addClass('d-none');
    }
}

// --- Source file picker ---

function loadSourceFiles() {
    var vehicleId = $('#chartVehicleId').val();
    var startDate = $('#chartStartDate').val();
    var endDate = $('#chartEndDate').val();
    if (!startDate && !endDate) {
        $('#sourceFileContainer').html('<small class="text-muted">Set a date range to load files</small>');
        $('#sourceFileCount').text('');
        return;
    }
    $.get('/Vehicle/GetSourceFilesInRange?vehicleId=' + vehicleId +
        '&startDate=' + (startDate || '') + '&endDate=' + (endDate || ''), function (files) {
        var container = $('#sourceFileContainer');
        container.empty();
        if (!files || files.length === 0) {
            container.html('<small class="text-muted">No files in range</small>');
            $('#sourceFileCount').text('');
            return;
        }
        files.forEach(function (f) {
            var id = 'sf_' + f.filename.replace(/[^a-zA-Z0-9]/g, '_');
            var label = '<div class="form-check form-check-inline mb-0">' +
                '<input class="form-check-input source-file-check" type="checkbox" id="' + id + '" ' +
                'value="' + f.filename + '" data-filetype="' + f.fileType + '" checked>' +
                '<label class="form-check-label small" for="' + id + '">' +
                '<span class="badge bg-' + (f.fileType === 'LOG' ? 'primary' : f.fileType === 'IMU' ? 'info' : 'secondary') +
                ' me-1" style="font-size:0.6rem;">' + f.fileType + '</span>' +
                f.filename + '</label></div>';
            container.append(label);
        });
        $('#sourceFileCount').text(files.length + ' files');
    });
}

function toggleAllSourceFiles(checked) {
    $('.source-file-check').prop('checked', checked);
}

function getSelectedSourceFilenames() {
    var selected = [];
    $('.source-file-check:checked').each(function () {
        selected.push($(this).val());
    });
    return selected;
}

// Hide color legend immediately when Color By changes to None
$(document).on('change', '#mapColorField', function () {
    if (!$(this).val()) {
        $('#colorLegend').removeClass('d-flex').addClass('d-none');
    }
});

// Load filter rules into builder when a saved filter is selected
$(document).on('change', '#chartFilterSelect', function () {
    var selected = $(this).find('option:selected');
    if (!selected.val() || !selected.data('filter')) {
        $('#filterRulesContainer').empty();
        return;
    }
    var filter = selected.data('filter');
    $('#filterRulesContainer').empty();
    $('#filterName').val(filter.name);
    if (filter.logic === 'Or') {
        $('#filterLogicOr').prop('checked', true);
    } else {
        $('#filterLogicAnd').prop('checked', true);
    }
        filter.rules.forEach(function (rule) {
        addFilterRule();
        var lastRow = $('#filterRulesContainer .filter-rule:last');
        lastRow.find('.filter-field-select').val(rule.field);
        lastRow.find('.filter-op-select').val(rule.operator);
        lastRow.find('.filter-value-input').val(rule.value);
    });
});

$(document).on('change', 'input[name="viewMode"]', function () {
    setViewMode(this.value);
});

// Reload source files when date range changes
$(document).on('change', '#chartStartDate, #chartEndDate', function () {
    loadSourceFiles();
});
