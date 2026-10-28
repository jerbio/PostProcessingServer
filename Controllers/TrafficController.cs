using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web.Mvc;
using PostProcessingServer.Filters;
using PostProcessingServer.Models;
using PostProcessingServer.Services.Analytics;

namespace PostProcessingServer.Controllers
{
    /// <summary>
    /// Internal Traffic Analytics dashboard (TRAFFIC_ANALYTICS_DESIGN.md). Login-protected:
    /// the default <c>[Authorize]</c> baseline blocks anonymous access, and the section is
    /// further gated by the <c>Analytics:Enabled</c> appSetting (default on). All heavy
    /// aggregation happens here on the server; the view only renders.
    /// </summary>
    [ChatAuditAuthorize]
    public class TrafficController : Controller
    {
        private const int DefaultMaxDays = 30;
        private const int DefaultWindowDays = 14;
        private static readonly string[] DefaultExcludedIdPrefixes = { "smoke-", "team-", "qa-" };

        [HttpGet]
        public ActionResult Index(string from = null, string to = null, string granularity = "day",
            string dimension = "channel", bool? includeTest = null)
        {
            if (!Enabled())
                return HttpNotFound();

            var maxDays = ReadInt(ConfigurationManager.AppSettings["Analytics:MaxDays"], DefaultMaxDays);
            var windowDays = ReadInt(ConfigurationManager.AppSettings["Analytics:WindowDays"], DefaultWindowDays);
            var includeTestTraffic = includeTest == true;
            var gran = string.Equals(granularity, "week", StringComparison.OrdinalIgnoreCase) ? "week" : "day";

            // Default to the trailing windowDays ending today (UTC dates at midnight).
            var now = DateTime.UtcNow.Date;
            var toUtc = ParseDate(to) ?? now;
            var fromUtc = ParseDate(from) ?? toUtc.AddDays(-(windowDays - 1));
            if (fromUtc > toUtc)
            {
                var tmp = fromUtc;
                fromUtc = toUtc;
                toUtc = tmp;
            }

            var prefixes = ParseCsv(ConfigurationManager.AppSettings["Analytics:ExcludeIdPrefixes"], DefaultExcludedIdPrefixes);
            var internalIds = new HashSet<string>(
                ParseCsv(ConfigurationManager.AppSettings["Analytics:InternalAnonymousIds"], new string[0]),
                StringComparer.OrdinalIgnoreCase);

            using (var ctx = new PostProcessorApplicationDbContext())
            {
                var vm = TrafficAggregator.Build(ctx, fromUtc, toUtc, maxDays, gran, dimension,
                    includeTestTraffic, prefixes, internalIds);
                return View(vm);
            }
        }

        private static bool Enabled()
        {
            return !string.Equals(ConfigurationManager.AppSettings["Analytics:Enabled"], "false", StringComparison.OrdinalIgnoreCase);
        }

        private static int ReadInt(string raw, int fallback)
        {
            int v;
            return (int.TryParse(raw, out v) && v > 0) ? v : fallback;
        }

        private static DateTime? ParseDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            DateTime d;
            if (DateTime.TryParseExact(s.Trim(), "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d))
                return d;
            return DateTime.TryParse(s, out d) ? d.Date : (DateTime?)null;
        }

        private static string[] ParseCsv(string raw, string[] fallback)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            var parts = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();
            return parts.Length > 0 ? parts : fallback;
        }
    }
}