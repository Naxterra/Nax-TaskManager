using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;

namespace BetterTaskManager
{
    internal sealed class AdapterCounters
    {
        public long Received;
        public long Sent;
    }

    internal sealed class BandwidthSnapshot
    {
        public DateTime Timestamp;
        public bool SampleAvailable;
        public double DownKilobytesPerSecond;
        public double UpKilobytesPerSecond;
        public int MatchedAdapters;
    }

    internal sealed class NetworkBandwidthSampler
    {
        private readonly object syncRoot = new object();
        private Dictionary<string, AdapterCounters> previous = new Dictionary<string, AdapterCounters>(StringComparer.OrdinalIgnoreCase);
        private DateTime previousTimestamp = DateTime.MinValue;

        public BandwidthSnapshot GetSnapshot()
        {
            DateTime now = DateTime.UtcNow;
            Dictionary<string, AdapterCounters> current = ReadCurrentCounters();
            lock (syncRoot)
            {
                double down = 0;
                double up = 0;
                int matched = 0;
                bool available = previousTimestamp != DateTime.MinValue &&
                    TryCalculateRates(previous, current, (now - previousTimestamp).TotalSeconds, out down, out up, out matched);
                previous = current;
                previousTimestamp = now;
                return new BandwidthSnapshot
                {
                    Timestamp = now,
                    SampleAvailable = available,
                    DownKilobytesPerSecond = available ? down : 0,
                    UpKilobytesPerSecond = available ? up : 0,
                    MatchedAdapters = available ? matched : 0
                };
            }
        }

        internal static bool TryCalculateRates(
            Dictionary<string, AdapterCounters> previous,
            Dictionary<string, AdapterCounters> current,
            double elapsedSeconds,
            out double downKilobytesPerSecond,
            out double upKilobytesPerSecond,
            out int matchedAdapters)
        {
            downKilobytesPerSecond = 0;
            upKilobytesPerSecond = 0;
            matchedAdapters = 0;
            if (previous == null || current == null || elapsedSeconds <= 0) return false;

            long receivedDelta = 0;
            long sentDelta = 0;
            foreach (var pair in current)
            {
                AdapterCounters old;
                AdapterCounters value = pair.Value;
                if (value == null || !previous.TryGetValue(pair.Key, out old) || old == null) continue;
                if (value.Received < old.Received || value.Sent < old.Sent) continue;
                receivedDelta += value.Received - old.Received;
                sentDelta += value.Sent - old.Sent;
                matchedAdapters++;
            }
            if (matchedAdapters == 0) return false;

            downKilobytesPerSecond = receivedDelta / 1024d / elapsedSeconds;
            upKilobytesPerSecond = sentDelta / 1024d / elapsedSeconds;
            return true;
        }

        private static Dictionary<string, AdapterCounters> ReadCurrentCounters()
        {
            var counters = new Dictionary<string, AdapterCounters>(StringComparer.OrdinalIgnoreCase);
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    IPv4InterfaceStatistics statistics = adapter.GetIPv4Statistics();
                    counters[adapter.Id] = new AdapterCounters { Received = statistics.BytesReceived, Sent = statistics.BytesSent };
                }
                catch (NetworkInformationException) { }
                catch (InvalidOperationException) { }
            }
            return counters;
        }
    }
}
