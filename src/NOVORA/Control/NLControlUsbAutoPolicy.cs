// NOVORA_AUTOUSB_V1 - pure event policy; no IO, timers, threads or polling.
using System;
using System.Collections.Generic;

namespace NOVORA.Control
{
    public sealed record NLControlUsbCandidate(
        string Serial,
        bool Connected,
        bool Wifi);

    public sealed class NLControlUsbAutoPolicy
    {
        private string _serial = String.Empty;
        private bool _attempted;
        private bool _paused;
        public string Serial { get { return _serial; } }
        public long Epoch { get; private set; }

        // Call on the UI thread for each device event, even during preparation.
        public bool Observe(string serial, bool connected, bool wifi, bool adbOnline)
        {
            string next = connected && !wifi && adbOnline && !String.IsNullOrWhiteSpace(serial)
                ? serial.Trim() : String.Empty;
            if (String.Equals(next, _serial, StringComparison.Ordinal)) return false;
            _serial = next;
            Epoch++;
            _attempted = false;
            _paused = false;
            return true;
        }

        public bool TryBegin(bool blocked)
        {
            if (blocked || _serial.Length == 0 || _paused || _attempted) return false;
            _attempted = true;
            return true;
        }

        public bool IsCurrent(string serial, long epoch)
        {
            return !_paused && _serial.Length != 0 && epoch == Epoch &&
                String.Equals(serial, _serial, StringComparison.Ordinal);
        }

        public void Pause() { _paused = true; Epoch++; }
        public void Retry() { _paused = false; _attempted = false; Epoch++; }

        public static string SelectPhysicalSerial(
            IEnumerable<NLControlUsbCandidate> candidates,
            IReadOnlySet<string>? online,
            string? selectedSerial)
        {
            ArgumentNullException.ThrowIfNull(candidates);

            NLControlUsbCandidate[] physical = candidates
                .Where(candidate =>
                    candidate.Connected &&
                    !candidate.Wifi &&
                    !String.IsNullOrWhiteSpace(candidate.Serial) &&
                    (online is null || online.Contains(candidate.Serial)))
                .ToArray();

            NLControlUsbCandidate? selected = physical.FirstOrDefault(candidate =>
                String.Equals(candidate.Serial, selectedSerial, StringComparison.OrdinalIgnoreCase));

            return (selected ?? physical.FirstOrDefault())?.Serial.Trim() ?? String.Empty;
        }

        public static HashSet<string> ParseOnline(string snapshot)
        {
            var online = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in (snapshot ?? String.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] columns = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length >= 2 && columns[1] == "device") online.Add(columns[0]);
            }
            return online;
        }
    }
}
