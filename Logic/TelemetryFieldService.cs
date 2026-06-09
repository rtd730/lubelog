using CarCareTracker.External.Interfaces;
using System.Globalization;

namespace CarCareTracker.Logic
{
    // DTO returned to the JS frontend
    public class TelemetryFieldInfo
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsCalculated { get; set; }
        public string Category { get; set; } = string.Empty;
    }

    public interface ITelemetryFieldService
    {
        List<TelemetryFieldInfo> GetAvailableFields(List<string> rawFieldNames, int vehicleId);
        double? ResolveFieldValue(string fieldKey, Dictionary<string, string> rawFields, decimal? gasPricePerGallon);
        string GetDisplayName(string fieldKey);
    }

    public class TelemetryFieldService : ITelemetryFieldService
    {
        private readonly IGasRecordDataAccess _gasRecordDataAccess;
        public TelemetryFieldService(IGasRecordDataAccess gasRecordDataAccess)
        {
            _gasRecordDataAccess = gasRecordDataAccess;
        }
                private static readonly Dictionary<string, (string displayName, string category)> RenameMap = new()
        {
            // System
            ["esp_time"] = ("ESP Time (ms)", "System"),
            ["unixTime"] = ("Unix Time", "System"),
            ["datetime"] = ("Date/Time", "System"),
            ["bat_v"] = ("Battery (V)", "Engine"),
            ["pwr_state"] = ("Power State", "System"),

            // Engine
            ["rpm"] = ("RPM", "Engine"),
            ["speed"] = ("Speed (MPH)", "Engine"),
            ["throttle"] = ("Throttle (%)", "Engine"),
            ["maf"] = ("MAF (g/s)", "Engine"),
            ["engine_load"] = ("Engine Load (%)", "Engine"),
            ["intake_temp"] = ("Intake Temp (°F)", "Engine"),
            ["coolant"] = ("Coolant (°F)", "Engine"),
            ["timing"] = ("Timing (°)", "Engine"),
            ["stft1"] = ("Short Fuel Trim (%)", "Engine"),
            ["ltft1"] = ("Long Fuel Trim (%)", "Engine"),
            ["af_lambda"] = ("A/F Ratio", "Engine"),
            ["trans_temp"] = ("Trans Temp (°F)", "Engine"),
            ["obd_fail_pct"] = ("OBD Fail (%)", "Engine"),

            // Transmission
            ["xmsnOut (F)"] = ("Trans Out (°F)", "Transmission"),
            ["xmsnIn (F)"] = ("Trans In (°F)", "Transmission"),

            // Environment
            ["CabinTemp (C)"] = ("Cabin Temp (°C)", "Environment"),
            ["CabinHumidity (%)"] = ("Cabin Humidity (%)", "Environment"),
            ["OAT_c"] = ("Outside Temp (°C)", "Environment"),
            ["OAH_pct"] = ("Outside Humidity (%)", "Environment"),
            ["bme_pressure"] = ("Baro Pressure (hPa)", "Environment"),
            ["bme_gas_ohm"] = ("Air Quality (Ω)", "Environment"),

            // EGI
            ["heading"] = ("Heading (°)", "EGI"),
            ["mag_x"] = ("Mag X", "EGI"),
            ["mag_y"] = ("Mag Y", "EGI"),
            ["mag_z"] = ("Mag Z", "EGI"),
            ["lat"] = ("Latitude", "EGI"),
            ["lng"] = ("Longitude", "EGI"),
            ["alt"] = ("Altitude (m)", "EGI"),
            ["sats"] = ("EGI Sats", "EGI"),
            ["HDOP"] = ("HDOP", "EGI"),
            ["VDOP"] = ("VDOP", "EGI"),
            ["gps_speed_kts"] = ("GPS Speed (kts)", "EGI"),
            ["gps_course"] = ("GPS Course (°)", "EGI"),

            // IMU
            ["ax_avg"] = ("Accel X Avg (g)", "IMU"),
            ["ax_max"] = ("Accel X Max (g)", "IMU"),
            ["ax_min"] = ("Accel X Min (g)", "IMU"),
            ["ay_avg"] = ("Accel Y Avg (g)", "IMU"),
            ["ay_max"] = ("Accel Y Max (g)", "IMU"),
            ["ay_min"] = ("Accel Y Min (g)", "IMU"),
            ["az_avg"] = ("Accel Z Avg (g)", "IMU"),
            ["az_max"] = ("Accel Z Max (g)", "IMU"),
            ["az_min"] = ("Accel Z Min (g)", "IMU"),
            ["gx_avg"] = ("Gyro X Avg (°/s)", "IMU"),
            ["gx_max"] = ("Gyro X Max (°/s)", "IMU"),
            ["gx_min"] = ("Gyro X Min (°/s)", "IMU"),
            ["gy_avg"] = ("Gyro Y Avg (°/s)", "IMU"),
            ["gy_max"] = ("Gyro Y Max (°/s)", "IMU"),
            ["gy_min"] = ("Gyro Y Min (°/s)", "IMU"),
            ["gz_avg"] = ("Gyro Z Avg (°/s)", "IMU"),
            ["gz_max"] = ("Gyro Z Max (°/s)", "IMU"),
            ["gz_min"] = ("Gyro Z Min (°/s)", "IMU"),
        };

        private record CalculatedField(
            string Key,
            string DisplayName, 
            string Category,
            string[] Dependencies,
            Func<Dictionary<string, string>, decimal?, double?> Calculator
        );

        private static readonly List<CalculatedField> CalculatedFields = new()
        {
            new("calc:gps_speed_mph", "GPS Speed (mph)", "GPS",
                new[] { "gps_speed_kts" },
                (f, _) => GetDouble(f, "gps_speed_kts") * 1.15078),
            new("calc:alt_ft", "Altitude (ft)", "GPS",
                new[] { "alt" },
                (f, _) => GetDouble(f, "alt") * 3.28084),
            new("calc:cabin_temp_f", "Cabin Temp (°F)", "Environment",
                new[] { "CabinTemp (C)" },
                (f, _) => GetDouble(f, "CabinTemp (C)") * 9.0 / 5.0 + 32.0),

            new("calc:oat_f", "Outside Temp (°F)", "Environment",
                new[] { "OAT_c" },
                (f, _) => GetDouble(f, "OAT_c") * 9.0 / 5.0 + 32.0),

            new("calc:coolant_c", "Coolant (°C)", "Engine",
                new[] { "coolant" },
                (f, _) => (GetDouble(f, "coolant") - 32.0) * 5.0 / 9.0),

            new("calc:intake_temp_c", "Intake Temp (°C)", "Engine",
                new[] { "intake_temp" },
                (f, _) => (GetDouble(f, "intake_temp") - 32.0) * 5.0 / 9.0),
            new("calc:baro_inhg", "Baro Pressure (inHg)", "Environment",
                new[] { "bme_pressure" },
                (f, _) => GetDouble(f, "bme_pressure") * 0.02953),
            new("calc:fuel_rate_gph", "Fuel Rate (gal/hr)", "Fuel",
                new[] { "maf", "af_lambda" },
                (f, _) => {
                    var maf = GetDouble(f, "maf");
                    var lambda = GetDouble(f, "af_lambda");
                    if (maf == null || lambda == null || lambda < 0.1) return null;
                    return maf.Value * 3600.0 / (lambda.Value * 14.7 * 820.0 * 3.785);
                }),
            new("calc:mpg_instant", "MPG (instant)", "Fuel",
                new[] { "speed", "maf", "af_lambda" },
                (f, _) => {
                    var speedMph = GetDouble(f, "speed");
                    var maf = GetDouble(f, "maf");
                    var lambda = GetDouble(f, "af_lambda");
                    if (speedMph == null || maf == null || lambda == null || lambda < 0.1) return null;
                    var fuelRate = maf.Value * 3600.0 / (lambda.Value * 14.7 * 820.0 * 3.785);
                    if (fuelRate < 0.001) return null;  // avoid division by near-zero
                    return speedMph.Value / fuelRate;
                }),
            new("calc:cost_per_mile", "Cost/mi ($/mi)", "Fuel",
                new[] { "speed", "maf", "af_lambda" },
                (f, gasPrice) => {
                    if (!gasPrice.HasValue) return null;
                    var speedMph = GetDouble(f, "speed");
                    var maf = GetDouble(f, "maf");
                    var lambda = GetDouble(f, "af_lambda");
                    if (speedMph == null || maf == null || lambda == null || lambda < 0.1) return null;
                    var fuelRate = maf.Value * 3600.0 / (lambda.Value * 14.7 * 820.0 * 3.785);
                    if (fuelRate < 0.001) return null;
                    var mpg = speedMph.Value / fuelRate;
                    if (mpg < 0.1) return null;
                    return (double)gasPrice.Value / mpg;
                }),
            new("calc:trans_delta_f", "Trans ΔT (°F)", "Transmission",
                new[] { "xmsnOut (F)", "xmsnIn (F)" },
                (f, _) => {
                    var outT = GetDouble(f, "xmsnOut (F)");
                    var inT = GetDouble(f, "xmsnIn (F)");
                    if (outT == null || inT == null) return null;
                    return outT.Value - inT.Value;
                }),
            // new("calc:lateral_g", "Lateral G", "IMU",
            //     new[] { "ay_avg" },
            //     (f, _) => GetDouble(f, "ay_avg")),
            new("calc:ride_quality", "Ride Quality (g pk-pk)", "IMU",
                new[] { "ax_max", "ax_min", "ay_max", "ay_min", "az_max", "az_min" },
                (f, _) => {
                    var axR = GetDouble(f, "ax_max") - GetDouble(f, "ax_min");
                    var ayR = GetDouble(f, "ay_max") - GetDouble(f, "ay_min");
                    var azR = GetDouble(f, "az_max") - GetDouble(f, "az_min");
                    if (axR == null || ayR == null || azR == null) return null;
                    return Math.Sqrt(axR.Value * axR.Value + ayR.Value * ayR.Value + azR.Value * azR.Value);
                }),

            new("calc:vib_ips", "Vib Velocity (IPS)", "IMU",
                new[] { "ax_max", "ax_min", "ay_max", "ay_min", "az_max", "az_min" },
                (f, _) => {
                    var axR = GetDouble(f, "ax_max") - GetDouble(f, "ax_min");
                    var ayR = GetDouble(f, "ay_max") - GetDouble(f, "ay_min");
                    var azR = GetDouble(f, "az_max") - GetDouble(f, "az_min");
                    if (axR == null || ayR == null || azR == null) return null;
                    var resultant = Math.Sqrt(axR.Value * axR.Value + ayR.Value * ayR.Value + azR.Value * azR.Value);
                    return resultant * 61.4;
                }),
            new("calc:dew_point_f", "Dew Point (°F)", "Environment",
                new[] { "CabinTemp (C)", "CabinHumidity (%)" },
                (f, _) => {
                    var t = GetDouble(f, "CabinTemp (C)");
                    var rh = GetDouble(f, "CabinHumidity (%)");
                    if (t == null || rh == null || rh <= 0) return null;
                    // Magnus formula constants
                    double a = 17.27, b = 237.7;
                    var gamma = (a * t.Value) / (b + t.Value) + Math.Log(rh.Value / 100.0);
                    var dewC = (b * gamma) / (a - gamma);
                    return dewC * 9.0 / 5.0 + 32.0;  // Convert to °F
                }),
            new("calc:density_alt_ft", "Density Altitude (ft)", "Environment",
                new[] { "bme_pressure", "OAT_c" },
                (f, _) => {
                    var p = GetDouble(f, "bme_pressure");
                    var t = GetDouble(f, "OAT_c");
                    if (p == null || t == null || p <= 0) return null;
                    var pressAlt = (1.0 - Math.Pow(p.Value / 1013.25, 0.190284)) * 145366.45;
                    var isaTemp = 15.0 - (pressAlt * 0.001981);  // ISA temp at this altitude
                    return pressAlt + (120.0 * (t.Value - isaTemp));  // density alt correction
                }),
        };
        private static double? GetDouble(Dictionary<string, string> fields, string key)
        {
            if (fields.TryGetValue(key, out var val) &&
                double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
                return result;
            return null;
        }
        public string GetDisplayName(string fieldKey)
        {
            if (fieldKey.StartsWith("calc:"))
            {
                var calc = CalculatedFields.FirstOrDefault(c => c.Key == fieldKey);
                return calc?.DisplayName ?? fieldKey;
            }
            return RenameMap.TryGetValue(fieldKey, out var entry) ? entry.displayName : fieldKey;
        }

        public double? ResolveFieldValue(string fieldKey, Dictionary<string, string> rawFields, decimal? gasPricePerGallon)
        {
            if (fieldKey.StartsWith("calc:"))
            {
                var calc = CalculatedFields.FirstOrDefault(c => c.Key == fieldKey);
                return calc?.Calculator(rawFields, gasPricePerGallon);
            }
            // Raw field — direct lookup
            return GetDouble(rawFields, fieldKey);
        }

        public List<TelemetryFieldInfo> GetAvailableFields(List<string> rawFieldNames, int vehicleId)
        {
            var rawSet = new HashSet<string>(rawFieldNames);
            var result = new List<TelemetryFieldInfo>();

            // Renamed raw fields
            foreach (var raw in rawFieldNames)
            {
                var (displayName, category) = RenameMap.TryGetValue(raw, out var entry)
                    ? entry
                    : (raw, "Other");
                result.Add(new TelemetryFieldInfo
                {
                    Key = raw,
                    DisplayName = displayName,
                    IsCalculated = false,
                    Category = category
                });
            }

            // Calculated fields — only if all dependencies exist
            foreach (var calc in CalculatedFields)
            {
                if (calc.Dependencies.All(d => rawSet.Contains(d)))
                {
                    // Special check: cost_per_mile needs gas records
                    if (calc.Key == "calc:cost_per_mile")
                    {
                        var gasRecords = _gasRecordDataAccess.GetGasRecordsByVehicleId(vehicleId);
                        if (!gasRecords.Any()) continue;
                    }
                    result.Add(new TelemetryFieldInfo
                    {
                        Key = calc.Key,
                        DisplayName = calc.DisplayName,
                        IsCalculated = true,
                        Category = calc.Category
                    });
                }
            }

            return result.OrderBy(f => f.Category).ThenBy(f => f.DisplayName).ToList();
        }
    }
}
