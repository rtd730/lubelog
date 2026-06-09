var currentSortKey = null;
var currentSortAsc = true;

function sortDriveTable(headerEl) {
    var key = $(headerEl).data('sort-key');

    // Toggle direction if same column, else default ascending
    if (currentSortKey === key) {
        currentSortAsc = !currentSortAsc;
    } else {
        currentSortKey = key;
        currentSortAsc = true;
    }

    // Update arrow icons on all headers
    $('.sort-icon').removeClass('bi-chevron-up bi-chevron-down').addClass('bi-chevron-expand');
    $(headerEl).find('.sort-icon')
        .removeClass('bi-chevron-expand')
        .addClass(currentSortAsc ? 'bi-chevron-up' : 'bi-chevron-down');

    // Get all rows and sort them
    var tbody = $('#drive-tab-pane table tbody');
    var rows = tbody.find('tr').toArray();

    rows.sort(function (a, b) {
        var aCell = $(a).find('td[data-column="' + key + '"]');
        var bCell = $(b).find('td[data-column="' + key + '"]');

        // Use data-sort-value if present (numeric sort), otherwise text sort
        var aVal = aCell.attr('data-sort-value');
        var bVal = bCell.attr('data-sort-value');

        if (aVal !== undefined && bVal !== undefined) {
            return currentSortAsc
                ? parseFloat(aVal) - parseFloat(bVal)
                : parseFloat(bVal) - parseFloat(aVal);
        } else {
            aVal = aCell.text().trim();
            bVal = bCell.text().trim();
            return currentSortAsc
                ? aVal.localeCompare(bVal)
                : bVal.localeCompare(aVal);
        }
    });

    // Re-append rows in sorted order
    rows.forEach(function (row) { tbody.append(row); });
}

var driveDetailMapInstance = null;

function showDriveDetail(driveId) {
    $.get(`/Vehicle/GetDriveRecordDetails?driveId=${driveId}`, function (data) {
        if (data) {
            $("#driveDetailModalContent").html(data);
            // Destroy old map instance if any
            if (driveDetailMapInstance) {
                driveDetailMapInstance.remove();
                driveDetailMapInstance = null;
            }
            $('#driveDetailModal').one('shown.bs.modal', function () {
                loadDriveMap();
            });
            $('#driveDetailModal').modal('show');
        }
    });
}

function loadDriveMap() {
    var modalBody = $('#driveDetailModalContent .modal-body');
    var vehicleId = parseInt(modalBody.data('vehicle-id'));
    var sourceFilename = modalBody.data('source-filename');
    var mapContainer = document.getElementById('driveDetailMap');

    if (!mapContainer || !vehicleId || !sourceFilename) return;

    // Initialize Leaflet map
    driveDetailMapInstance = L.map('driveDetailMap');
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; OpenStreetMap',
        maxZoom: 19
    }).addTo(driveDetailMapInstance);

    // Fetch GPS points for this drive's source file
    var request = {
        vehicleId: vehicleId,
        fields: ['lat', 'lng'],
        sourceFilenames: [sourceFilename],
        startDate: '2020-01-01',
        maxPoints: 5000,
        downsample: true
    };

    $.ajax({
        url: '/Vehicle/GetTelemetryPoints',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(request),
        success: function (points) {
            if (!points || points.length === 0 || points.error) {
                $('#driveDetailMap').html('<div class="d-flex align-items-center justify-content-center h-100 text-muted">No GPS data</div>');
                return;
            }

            // Filter out zero coords
            var coords = [];
            points.forEach(function (p) {
                if (p.x !== 0 && p.y !== 0) {
                    coords.push([p.x, p.y]);
                }
            });

            if (coords.length === 0) {
                $('#driveDetailMap').html('<div class="d-flex align-items-center justify-content-center h-100 text-muted">No GPS data</div>');
                return;
            }

            // Draw route polyline
            var polyline = L.polyline(coords, { color: '#2196F3', weight: 3 }).addTo(driveDetailMapInstance);

            // Start marker (green) and end marker (red)
            L.circleMarker(coords[0], { radius: 6, color: '#4CAF50', fillColor: '#4CAF50', fillOpacity: 1 })
                .bindTooltip('Start').addTo(driveDetailMapInstance);
            L.circleMarker(coords[coords.length - 1], { radius: 6, color: '#F44336', fillColor: '#F44336', fillOpacity: 1 })
                .bindTooltip('End').addTo(driveDetailMapInstance);

            // Fit map to route
            driveDetailMapInstance.fitBounds(polyline.getBounds(), { padding: [20, 20] });
        },
        error: function () {
            $('#driveDetailMap').html('<div class="d-flex align-items-center justify-content-center h-100 text-muted">Failed to load route</div>');
        }
    });
}
function refreshDriveCache() {
    var vehicleId = GetVehicleId().vehicleId;
    $.post(`/Vehicle/RefreshDriveCache?vehicleId=${vehicleId}`, function (data) {
        if (data.success) {
            successToast(data.message);
            saveScrollPosition();
            getVehicleDriveRecords(vehicleId);
        } else {
            errorToast(data.message);
        }
    });
}
function saveDriveNotes(driveId) {
    var notes = $("#driveNotes").val();
    $.post('/Vehicle/SaveDriveNotes', { driveId: driveId, notes: notes }, function (data) {
        if (data.success) {
            successToast(data.message);
        } else {
            errorToast(data.message);
        }
    });
}
function reassignDrive(driveId) {
    var newVehicleId = parseInt($('#reassignVehicleId').val());
    if (!newVehicleId || newVehicleId < 1) {
        errorToast('Enter a valid vehicle ID');
        return;
    }
    Swal.fire({
        title: 'Reassign Drive?',
        text: 'Move this drive and all its telemetry to Vehicle ' + newVehicleId + '?',
        icon: 'warning',
        showCancelButton: true,
        confirmButtonText: 'Reassign'
    }).then(function (result) {
        if (result.isConfirmed) {
            $.post('/Vehicle/ReassignDriveToVehicle', { driveId: driveId, newVehicleId: newVehicleId }, function (data) {
                if (data.success) {
                    successToast(data.message);
                    $('#driveDetailModal').modal('hide');
                    // Refresh the drives list
                    var vehicleId = GetVehicleId().vehicleId;
                    getVehicleDriveRecords(vehicleId);
                } else {
                    errorToast(data.message);
                }
            });
        }
    });
}
function searchDriveTableRows() {
    var tabName = 'drive-tab-pane';
    Swal.fire({
        title: 'Search Drives',
        html: '<input type="text" id="inputSearch" class="swal2-input" placeholder="Keyword" onkeydown="handleSwalEnter(event)">',
        confirmButtonText: 'Search',
        focusConfirm: false,
        preConfirm: () => {
            const searchString = $("#inputSearch").val();
            return { searchString }
        },
    }).then(function (result) {
        if (result.isConfirmed) {
            var rowData = $(`#${tabName} table tbody tr`);
            if (result.value.searchString.trim() == '') {
                rowData.removeClass('override-hide');
            } else {
                var filteredRows = $(`#${tabName} table tbody tr[data-search*='${result.value.searchString}']`);
                rowData.addClass('override-hide');
                filteredRows.removeClass('override-hide');
            }
        }
    });
}
