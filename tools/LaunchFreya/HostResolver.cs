using System;

namespace LaunchFreya
{
    /// <summary>
    /// Pure host/URL string logic split out of <c>MainWindow</c> (Phase AT-5) so it
    /// can be unit-tested without an Avalonia window. No UI, no I/O, no state.
    /// </summary>
    public static class HostResolver
    {
        /// <summary>
        /// Strip a leading <c>scheme://</c> and any trailing <c>/path</c> from a
        /// user-typed server address, leaving just the bare host[:port].
        /// </summary>
        public static string NormalizeHost(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            var s = raw.Trim();
            int scheme = s.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) s = s.Substring(scheme + 3);
            int slash = s.IndexOf('/');
            if (slash >= 0) s = s.Substring(0, slash);
            return s.Trim();
        }

        /// <summary>
        /// Map a server hostname/URL to its Freya Online website URL. Empty or
        /// loopback maps to the play-local dev site; anything else maps to https
        /// on the same host (a typed-in :port is dropped).
        /// </summary>
        public static string WebsiteUrlFor(string rawServer)
        {
            var host = NormalizeHost(rawServer);
            int colon = host.IndexOf(':');          // drop a typed-in :port
            if (colon >= 0) host = host.Substring(0, colon);

            if (string.IsNullOrEmpty(host) ||
                host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host == "127.0.0.1")
            {
                return "http://localhost:8088";
            }
            return "https://" + host;
        }
    }
}
