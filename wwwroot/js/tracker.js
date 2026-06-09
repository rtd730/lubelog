function refreshTrackerManagement() {
    var vehicleId = GetVehicleId().vehicleId;
    getVehicleTrackerManagement(vehicleId);
}

function uploadFirmware() {
    var notes = $('#fwNotes').val();
    var fileInput = document.getElementById('fwFile');

    if (!fileInput.files || fileInput.files.length === 0) {
        errorToast('Select a .bin file');
        return;
    }

    var formData = new FormData();

    formData.append('notes', notes);
    formData.append('firmwareFile', fileInput.files[0]);

    $('#fwUploadBtn').prop('disabled', true).html('<span class="spinner-border spinner-border-sm me-1"></span>Uploading...');

    $.ajax({
        url: '/Vehicle/UploadFirmwareFromUI',
        type: 'POST',
        data: formData,
        processData: false,
        contentType: false,
        success: function (result) {
            if (result.success) {
                successToast(result.message);
                refreshTrackerManagement();
            } else {
                errorToast(result.message);
            }
            $('#fwUploadBtn').prop('disabled', false).html('<i class="bi bi-cloud-upload me-1"></i>Upload');
        },
        error: function () {
            errorToast('Upload failed');
            $('#fwUploadBtn').prop('disabled', false).html('<i class="bi bi-cloud-upload me-1"></i>Upload');
        }
    });
}

async function uploadTelemetryFiles() {
    var vehicleId = GetVehicleId().vehicleId;
    var fileInput = document.getElementById('csvFolder');

    if (!fileInput.files || fileInput.files.length === 0) {
        errorToast('Select your SD card folder');
        return;
    }

    var validTypes = ['LOG', 'IMU', 'MON'];

    // Build the list of telemetry files with their normalized (type-relative) paths.
    var items = [];
    for (var i = 0; i < fileInput.files.length; i++) {
        var file = fileInput.files[i];
        var segments = file.webkitRelativePath.split('/');
        var typeIndex = segments.findIndex(function (s) {
            return validTypes.indexOf(s) !== -1;
        });
        if (typeIndex === -1) {
            continue; // not a telemetry file
        }
        items.push({ file: file, relPath: segments.slice(typeIndex).join('/') });
    }

    if (items.length === 0) {
        errorToast('No LOG/IMU/MON files found in that folder');
        return;
    }

    // Upload in small batches: each batch is its own request, which keeps us under
    // the server's form-entry limit and lets us show progress as we go.
    var batchSize = 25;
    var total = items.length;
    var uploaded = 0;
    var failed = 0;

    $('#csvUploadBtn').prop('disabled', true).html('<span class="spinner-border spinner-border-sm me-1"></span>Uploading...');

    for (var start = 0; start < total; start += batchSize) {
        var batch = items.slice(start, start + batchSize);

        var formData = new FormData();
        formData.append('vehicleId', vehicleId);
        formData.append('detectDrives', 'false');
        for (var j = 0; j < batch.length; j++) {
            formData.append('files', batch[j].file);
            formData.append('relativePaths', batch[j].relPath);
        }

        $('#csvUploadStatus').text('Uploading ' + Math.min(start + batchSize, total) + ' of ' + total + ' files...');

        try {
            var result = await $.ajax({
                url: '/Vehicle/UploadTelemetryFromUI',
                type: 'POST',
                data: formData,
                processData: false,
                contentType: false
            });
            if (result && result.success) {
                uploaded += batch.length;
            } else {
                failed += batch.length;
            }
        } catch (e) {
            failed += batch.length;
        }
    }
    
    // All files uploaded — detect drives ONCE over everything.
    $('#csvUploadStatus').text('Detecting drives...');
    try {
        await $.ajax({ url: '/Vehicle/DetectDrivesFromUI', type: 'POST', data: { vehicleId: vehicleId } });
    } catch (e) { /* non-fatal */ }
    $('#csvUploadBtn').prop('disabled', false).html('<i class="bi bi-cloud-upload me-1"></i>Upload');
    $('#csvUploadStatus').text('');

    if (failed === 0) {
        successToast('Uploaded all ' + uploaded + ' file(s)');
    } else {
        errorToast('Uploaded ' + uploaded + ' file(s), ' + failed + ' failed');
    }
    refreshTrackerManagement();
}

