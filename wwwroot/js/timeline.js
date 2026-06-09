var timelineChartInstance = null;
var editingAxisId = null;
var timelineColors = [
    '#E6194B', '#3CB44B',  '#F58231','#FF6F00',
    '#4363D8','#911EB4', '#F032E6', '#2196F3', 
    '#469990', '#9A6324', '#DC143C', '#00897B',
    '#800000', '#808000', '#0b0ba8', '#FF4500',
    '#2E8B57', '#1E90FF', '#C62828', '#6A1B9A'
];


var timelineFieldMap = {};   // key → displayName
var timelineKeyMap = {};     // displayName → key

function initTimeline(vehicleId) {
    loadTimelineFields(vehicleId, true);
}

function updateTimelinePointCount() {
    if (!timelineChartInstance) return;
    var total = 0;
    timelineChartInstance.data.datasets.forEach(function (ds) {
        total += ds.data.length;
    });
    $('#timelinePointCount').text(total + ' pts');
}

function loadTimelineFields(vehicleId, autoRender) {
    $.get('/Vehicle/GetAvailableFields?vehicleId=' + vehicleId + '&fileType=', function (fields) {
        var container = $('#timelineFieldsContainer');
        container.empty();
        timelineFieldMap = {};
        timelineKeyMap = {};

        fields.forEach(function (f, idx) {
            // Build lookup maps
            timelineFieldMap[f.key] = f.displayName;
            timelineKeyMap[f.displayName] = f.key;

            // Sanitize key for use as DOM id (colons and spaces aren't valid in IDs)
            var safeId = f.key.replace(/[^a-zA-Z0-9]/g, '_');
            var checked = (f.key === 'speed' || f.key === 'calc:mpg_instant') ? 'checked' : '';
            var color = timelineColors[idx % timelineColors.length];

            container.append(
                '<div class="form-check mb-0">' +
                '<input class="form-check-input timeline-field-check" type="checkbox" value="' + f.key + '" id="tlf_' + safeId + '" ' + checked +
                ' style="border-color:' + color + ';" onchange="onTimelineFieldToggle(this)">' +
                '<label class="form-check-label small" for="tlf_' + safeId + '" style="color:' + color + ';">' + f.displayName + '</label>' +
                '</div>'
            );
        });
        if (autoRender) renderTimeline();
    });
}

function onTimelineFieldToggle(checkbox) {
    var fieldKey = $(checkbox).val();
    if ($(checkbox).is(':checked')) {
        addTimelineField(fieldKey);
    } else {
        removeTimelineField(fieldKey);
    }
}

function selectAllTimelineFields() {
    $('.timeline-field-check').prop('checked', true);
    renderTimeline();
}

function selectNoTimelineFields() {
    $('.timeline-field-check').prop('checked', false);
    if (timelineChartInstance) {
        timelineChartInstance.destroy();
        timelineChartInstance = null;
    }
}
function getFieldColor(fieldKey) {
    var allFieldKeys = [];
    $('#timelineFieldsContainer .timeline-field-check').each(function () {
        allFieldKeys.push($(this).val());
    });
    var idx = allFieldKeys.indexOf(fieldKey);
    if (idx < 0) idx = 0;
    return timelineColors[idx % timelineColors.length];
}

function addTimelineField(fieldKey) {
    var vehicleId = parseInt($('#timelineVehicleId').val());

    // If no chart exists yet, do a full render
    if (!timelineChartInstance) {
        renderTimeline();
        return;
    }

    var request = {
        vehicleId: vehicleId,
        fields: [fieldKey],
        startDate: '2020-01-01',
        endDate: null,
        fileType: '',
        maxPoints: 50000,
        downsample: true
    };

    $.ajax({
        url: '/Vehicle/GetTimelineData',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(request),
        success: function (result) {
            if (result.error || !result.datasets || result.datasets.length === 0) return;

            var ds = result.datasets[0];
            var safeKey = fieldKey.replace(/[^a-zA-Z0-9]/g, '_');
            var color = getFieldColor(fieldKey);
            var yAxisId = 'y_' + safeKey;

            // Count existing datasets to decide axis position (left/right alternation)
            var axisCount = timelineChartInstance.data.datasets.length;

            // Add the new dataset
            timelineChartInstance.data.datasets.push({
                label: ds.label,
                data: ds.data,
                borderColor: color,
                backgroundColor: color,
                pointRadius: 0,
                borderWidth: 1.5,
                fill: false,
                tension: 0.1,
                yAxisID: yAxisId,
                segment: ds.fileType === 'MON' ? {} : {
                    borderColor: function (ctx) {
                        var p0 = ctx.p0.parsed;
                        var p1 = ctx.p1.parsed;
                        if (p1.x - p0.x > 60000) return 'transparent';
                    }
                }
            });

            // Add the new Y axis
            timelineChartInstance.options.scales[yAxisId] = {
                position: axisCount % 2 === 0 ? 'left' : 'right',
                title: { display: true, text: ds.label, color: color },
                ticks: { color: color },
                grid: { drawOnChartArea: axisCount === 0 }
            };

            timelineChartInstance.update();
            updateTimelinePointCount();
        }
    });
}

function removeTimelineField(fieldKey) {
    if (!timelineChartInstance) return;

    var safeKey = fieldKey.replace(/[^a-zA-Z0-9]/g, '_');
    var yAxisId = 'y_' + safeKey;

    // Find and remove the dataset
    var datasets = timelineChartInstance.data.datasets;
    for (var i = datasets.length - 1; i >= 0; i--) {
        if (datasets[i].yAxisID === yAxisId) {
            datasets.splice(i, 1);
            break;
        }
    }

    // Remove the Y axis
    delete timelineChartInstance.options.scales[yAxisId];

    // If no datasets left, destroy the chart
    if (datasets.length === 0) {
        timelineChartInstance.destroy();
        timelineChartInstance = null;
        $('#timelinePointCount').text('0 pts');
        return;
    }

    timelineChartInstance.update();
    updateTimelinePointCount();
}

function renderTimeline() {
    var vehicleId = parseInt($('#timelineVehicleId').val());
    var fields = [];
    $('.timeline-field-check:checked').each(function () {
        fields.push($(this).val());
    });

    if (fields.length === 0) {
        return;
    }

    var request = {
        vehicleId: vehicleId,
        fields: fields,
        startDate: '2020-01-01',
        endDate: null,
        fileType: '',
        maxPoints: 50000,
        downsample: true
    };

    $.ajax({
        url: '/Vehicle/GetTimelineData',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(request),
        success: function (result) {
            if (result.error) {
                errorToast(result.error);
                return;
            }
            var totalPoints = 0;
            result.datasets.forEach(function (ds) { totalPoints += ds.data.length; });
            $('#timelinePointCount').text(totalPoints + ' pts');
            renderTimelineChart(result.datasets);
        },
        error: function () {
            errorToast('Failed to load timeline data');
        }
    });
}

function interpolateAtX(data, targetX, maxGap) {
    if (!data || data.length === 0) return null;
    if (data.length === 1) return data[0].y;

    // Binary search for the two points surrounding targetX
    var lo = 0, hi = data.length - 1;
    while (lo < hi - 1) {
        var mid = Math.floor((lo + hi) / 2);
        if (data[mid].x < targetX) lo = mid;
        else hi = mid;
    }

    var p0 = data[lo];
    var p1 = data[hi];

    // Outside range — return nearest endpoint
    if (targetX <= p0.x) return p0.y;
    if (targetX >= p1.x) return p1.y;

    // Don't interpolate across time gaps (same 60s threshold as segment gaps)
    if (maxGap && (p1.x - p0.x) > maxGap) return null;

    // Linear interpolation
    var t = (targetX - p0.x) / (p1.x - p0.x);
    return p0.y + t * (p1.y - p0.y);
}

function renderTimelineChart(datasets) {
    if (timelineChartInstance) {
        timelineChartInstance.destroy();
    }

    // Get all field keys to assign consistent colors
    var allFieldKeys = [];
    $('#timelineFieldsContainer .timeline-field-check').each(function () {
        allFieldKeys.push($(this).val());
    });

    var chartDatasets = [];
    var scales = {
        x: {
            type: 'linear',
            title: { display: true, text: 'Time' },
            min: new Date().setFullYear(new Date().getFullYear() - 1),
            max: Date.now(),
            ticks: {
                callback: function (value) {
                    var d = new Date(value);
                    return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
                },
                maxTicksLimit: 10
            }
        }
    };

    datasets.forEach(function (ds, i) {
        // Reverse-lookup: server returns displayName as label, find the key
        var fieldKey = timelineKeyMap[ds.label] || ds.label;
        var safeKey = fieldKey.replace(/[^a-zA-Z0-9]/g, '_');

        var colorIndex = allFieldKeys.indexOf(fieldKey);
        if (colorIndex < 0) colorIndex = i;
        var color = timelineColors[colorIndex % timelineColors.length];
        var yAxisId = 'y_' + safeKey;

        chartDatasets.push({
            label: ds.label,
            data: ds.data,
            borderColor: color,
            backgroundColor: color,
            pointRadius: 0,
            borderWidth: 1.5,
            fill: false,
            tension: 0.1,
            yAxisID: yAxisId,
            segment: ds.fileType === 'MON' ? {} : {
                borderColor: function (ctx) {
                    var p0 = ctx.p0.parsed;
                    var p1 = ctx.p1.parsed;
                    if (p1.x - p0.x > 60000) return 'transparent';
                }
            }
        });

        // Each metric gets its own Y axis
        scales[yAxisId] = {
            position: i % 2 === 0 ? 'left' : 'right',
            title: {
                display: true,
                text: ds.label,
                color: color
            },
            ticks: {
                color: color
            },
            grid: {
                drawOnChartArea: i === 0 // only first axis draws grid
            }
        };
    });

    var ctx = document.getElementById('timelineChart').getContext('2d');
    timelineChartInstance = new Chart(ctx, {
        type: 'line',
        data: { datasets: chartDatasets },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: {
                mode: 'nearest',
                axis: 'x',
                intersect: false
            },
            plugins: {
                tooltip: {
                    callbacks: {
                        title: function (items) {
                            if (items.length === 0) return '';
                            // Use cursor pixel position to get exact x value on the time axis
                            var chart = items[0].chart;
                            var caretPixel = items[0].element.x;
                            var cursorX = chart.scales.x.getValueForPixel(caretPixel);
                            chart._tooltipTargetX = cursorX;
                            return new Date(cursorX).toLocaleString();
                        },
                        label: function () {
                            return null;
                        },
                        afterBody: function (items) {
                            if (items.length === 0) return [];
                            var chart = items[0].chart;
                            var targetX = chart._tooltipTargetX || items[0].raw.x;
                            var lines = [];

                            chart.data.datasets.forEach(function (ds) {
                                var val = interpolateAtX(ds.data, targetX, 300000);
                                if (val !== null) {
                                    lines.push(ds.label + ': ' + val.toFixed(2));
                                }
                            });
                            return lines;
                        }
                    }
                }
            },
            scales: scales
        }
    });

    // Attach scroll-to-zoom on X axis
    var canvas = document.getElementById('timelineChart');
    canvas.removeEventListener('wheel', timelineWheelHandler);
    canvas.addEventListener('wheel', timelineWheelHandler, { passive: false });
}

function timelineWheelHandler(e) {
    if (!timelineChartInstance) return;
    e.preventDefault();

    var chart = timelineChartInstance;
    var xScale = chart.scales.x;
    var xMin = xScale.min;
    var xMax = xScale.max;
    var range = xMax - xMin;

    if (e.shiftKey) {
        // Shift+scroll = pan left/right
        var panAmount = range * 0.1 * (e.deltaY > 0 ? 1 : -1);
        chart.options.scales.x.min = xMin + panAmount;
        chart.options.scales.x.max = xMax + panAmount;
        chart.update('none');
        return;
    }

    var rect = chart.canvas.getBoundingClientRect();
    var mouseX = e.clientX - rect.left;
    var chartArea = chart.chartArea;
    var fraction = (mouseX - chartArea.left) / (chartArea.right - chartArea.left);
    fraction = Math.max(0, Math.min(1, fraction));

    var zoomFactor = e.deltaY > 0 ? 1.2 : 0.8;
    var newRange = range * zoomFactor;

    var anchor = xMin + range * fraction;
    var newMin = anchor - newRange * fraction;
    var newMax = anchor + newRange * (1 - fraction);

    chart.options.scales.x.min = newMin;
    chart.options.scales.x.max = newMax;
    chart.update('none');
}

// Double-click: axis area opens config popup, chart area resets zoom
$(document).on('dblclick', '#timelineChart', function (e) {
    if (!timelineChartInstance) return;

    var chart = timelineChartInstance;
    var rect = chart.canvas.getBoundingClientRect();
    var clickX = e.clientX - rect.left;
    var clickY = e.clientY - rect.top;

    // Check if click landed on a Y axis
    var clickedAxisId = null;
    Object.keys(chart.scales).forEach(function (id) {
        if (!id.startsWith('y_')) return;
        var scale = chart.scales[id];
        if (clickX >= scale.left && clickX <= scale.right &&
            clickY >= scale.top && clickY <= scale.bottom) {
            clickedAxisId = id;
        }
    });

    if (clickedAxisId) {
        showAxisConfigPopup(clickedAxisId, e.clientX, e.clientY);
    } else {
        // Reset X zoom
        $('#axisConfigPopup').hide();
        chart.options.scales.x.min = undefined;
        chart.options.scales.x.max = undefined;
        chart.update();
    }
});

function showAxisConfigPopup(axisId, mouseX, mouseY) {
    editingAxisId = axisId;
    var chart = timelineChartInstance;
    var safeKey = axisId.replace('y_', '');
    // Find the original key by matching sanitized versions
    var fieldKey = null;
    Object.keys(timelineFieldMap).forEach(function (k) {
        if (k.replace(/[^a-zA-Z0-9]/g, '_') === safeKey) fieldKey = k;
    });
    var displayName = fieldKey ? timelineFieldMap[fieldKey] : safeKey;

    $('#axisConfigTitle').text('Edit: ' + displayName);

    // Populate min/max — use configured values, fall back to rendered scale
    var optScale = chart.options.scales[axisId] || {};
    var renderedScale = chart.scales[axisId];
    var curMin = optScale.min != null ? optScale.min : renderedScale.min;
    var curMax = optScale.max != null ? optScale.max : renderedScale.max;
    $('#axisMin').val(Math.round(curMin * 100) / 100);
    $('#axisMax').val(Math.round(curMax * 100) / 100);

    // Find current color from the dataset
    var curColor = '#FF6384';
    chart.data.datasets.forEach(function (ds) {
        if (ds.yAxisID === axisId) curColor = ds.borderColor;
    });
    $('#axisCustomColor').val(curColor);

    // Highlight matching swatch
    $('.axis-color-swatch').removeClass('selected');
    $('.axis-color-swatch').each(function () {
        if ($(this).data('color').toLowerCase() === curColor.toLowerCase()) {
            $(this).addClass('selected');
        }
    });

    // Zero line checkbox
    var gridOpt = optScale.grid || {};
    var hasZeroLine = gridOpt.drawOnChartArea && typeof gridOpt.color === 'function';
    $('#axisZeroLine').prop('checked', hasZeroLine);

    // Position popup near click, clamped to container
    var container = $('#axisConfigPopup').parent();
    var containerRect = container[0].getBoundingClientRect();
    var popupLeft = mouseX - containerRect.left + 10;
    var popupTop = mouseY - containerRect.top - 50;
    // Clamp so it doesn't overflow right/bottom
    popupLeft = Math.min(popupLeft, containerRect.width - 230);
    popupTop = Math.max(10, Math.min(popupTop, containerRect.height - 280));

    $('#axisConfigPopup').css({ left: popupLeft + 'px', top: popupTop + 'px' }).show();
}

function applyAxisConfig() {
    if (!editingAxisId || !timelineChartInstance) return;
    var chart = timelineChartInstance;

    // Read min/max
    var minVal = $('#axisMin').val();
    var maxVal = $('#axisMax').val();
    chart.options.scales[editingAxisId].min = minVal !== '' ? parseFloat(minVal) : undefined;
    chart.options.scales[editingAxisId].max = maxVal !== '' ? parseFloat(maxVal) : undefined;

    // Read color
    var color = $('#axisCustomColor').val();
    var selectedSwatch = $('.axis-color-swatch.selected');
    if (selectedSwatch.length) color = selectedSwatch.data('color');

    // Update axis colors
    chart.options.scales[editingAxisId].title.color = color;
    chart.options.scales[editingAxisId].ticks.color = color;

    // Update dataset color
    var safeKey = editingAxisId.replace('y_', '');
    chart.data.datasets.forEach(function (ds) {
        if (ds.yAxisID === editingAxisId) {
            ds.borderColor = color;
            ds.backgroundColor = color;
        }
    });

    // Update sidebar checkbox/label colors (using sanitized key as DOM id)
    $('#tlf_' + safeKey).css('border-color', color);
    $('label[for="tlf_' + safeKey + '"]').css('color', color);

    // Zero line
    var showZero = $('#axisZeroLine').is(':checked');
    if (showZero) {
        chart.options.scales[editingAxisId].grid = {
            drawOnChartArea: true,
            color: function (context) {
                if (context.tick.value === 0) return 'rgba(0,0,0,0.3)';
                return 'transparent';
            }
        };
    } else {
        chart.options.scales[editingAxisId].grid = {
            drawOnChartArea: false
        };
    }

    chart.update('none');
    $('#axisConfigPopup').hide();
}

function resetAxisConfig() {
    if (!editingAxisId || !timelineChartInstance) return;
    var chart = timelineChartInstance;
    chart.options.scales[editingAxisId].min = undefined;
    chart.options.scales[editingAxisId].max = undefined;
    chart.update('none');
    $('#axisConfigPopup').hide();
}

// Color swatch click handlers
$(document).on('click', '.axis-color-swatch', function () {
    $('.axis-color-swatch').removeClass('selected');
    $(this).addClass('selected');
    $('#axisCustomColor').val($(this).data('color'));
});
$(document).on('input', '#axisCustomColor', function () {
    $('.axis-color-swatch').removeClass('selected');
});
