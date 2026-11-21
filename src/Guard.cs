using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Keeps a mistake in Vintage-E from taking the game down with it (1.25.2). The game runs a mod's own tick
    /// listeners, delayed callbacks and packet handlers with nothing around them: an exception in one goes straight up
    /// into the game's tick or its packet handling (a block entity's tick is different: the game logs those). Wrapped
    /// here, the error is written to the log and the game carries on.
    ///
    /// A fault that happens on every tick would fill the log, so each place reports its first three errors in full and
    /// after that one in every two hundred, with the count.
    /// </summary>
    public static class Guard
    {
        public const int FullReports = 3, Every = 200;
        static readonly Dictionary<string, int> counts = new Dictionary<string, int>();

        /// <summary>How many errors <paramref name="what"/> has had (for the self-test).</summary>
        public static int Count(string what)
        {
            lock (counts) return counts.TryGetValue(what ?? "", out int n) ? n : 0;
        }

        /// <summary>Counts an error and says whether this one should be written to the log.</summary>
        public static bool ShouldReport(string what, out int count)
        {
            lock (counts)
            {
                counts.TryGetValue(what ?? "", out count);
                counts[what ?? ""] = ++count;
            }
            return count <= FullReports || count % Every == 0;
        }

        public static void Report(ILogger log, string what, Exception e)
        {
            try
            {
                if (!ShouldReport(what, out int count)) return;
                if (count <= FullReports) log?.Error("Vintage-E: {0} failed, and was skipped this time: {1}", what, e);
                else log?.Error("Vintage-E: {0} has failed {1} times; the latest: {2}", what, count, e?.Message);
            }
            catch { }
        }

        public static void Run(ILogger log, string what, Action body)
        {
            try { body(); }
            catch (Exception e) { Report(log, what, e); }
        }

        /// <summary>A tick listener or delayed callback that logs its errors.</summary>
        public static Action<float> Tick(ILogger log, string what, Action<float> body) => dt =>
        {
            try { body(dt); }
            catch (Exception e) { Report(log, what, e); }
        };

        public static Action<float> Tick(ILogger log, string what, Action body) => _ =>
        {
            try { body(); }
            catch (Exception e) { Report(log, what, e); }
        };

        /// <summary>A server-side handler for a packet from a player.</summary>
        public static NetworkClientMessageHandler<T> FromPlayer<T>(ILogger log, NetworkClientMessageHandler<T> body) => (player, packet) =>
        {
            try { body(player, packet); }
            catch (Exception e) { Report(log, "handling " + typeof(T).Name + " from " + (player?.PlayerName ?? "a player"), e); }
        };

        /// <summary>A client-side handler for a packet from the server.</summary>
        public static NetworkServerMessageHandler<T> FromServer<T>(ILogger log, NetworkServerMessageHandler<T> body) => packet =>
        {
            try { body(packet); }
            catch (Exception e) { Report(log, "handling " + typeof(T).Name + " from the server", e); }
        };
    }
}
