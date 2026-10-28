using System;
using System.Collections.Generic;
using System.Linq;
using PostProcessingServer.Services.Analytics;
using Xunit;

namespace PostProcessingServer.Tests
{
    // TDD for TRAFFIC_ANALYTICS_DESIGN.md §9.1 — TrafficMath is pure and framework-free,
    // exercised through the production PPS assembly, without compiling a second copy of its source.
    public class UtcBucketingTests
    {
        [Fact]
        public void DayBuckets_cover_the_window_with_midnight_utc_starts()
        {
            var buckets = TrafficMath.MakeBuckets(
                new DateTime(2026, 10, 3, 5, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 10, 5, 23, 59, 59, DateTimeKind.Utc),
                TrafficMath.BucketGranularity.Day);

            Assert.Equal(3, buckets.Count);
            Assert.Equal(new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc), buckets[0].StartUtc);
            Assert.Equal(new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc), buckets[1].StartUtc);
            Assert.Equal(new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc), buckets[2].StartUtc);
            Assert.All(buckets, b => Assert.Equal(DateTimeKind.Utc, b.StartUtc.Kind));
        }

        [Fact]
        public void DayBuckets_are_half_open_inclusive_end()
        {
            // Window [from, to+1day): `to` belongs to the window even late at night;
            // the following day gets no bucket.
            var buckets = TrafficMath.MakeBuckets(
                new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 10, 3, 23, 59, 59, DateTimeKind.Utc),
                TrafficMath.BucketGranularity.Day);

            var starts = buckets.Select(b => b.StartUtc).ToList();
            Assert.Contains(new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc), starts);
            Assert.DoesNotContain(new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc), starts);
        }

        [Fact]
        public void WeekBuckets_start_on_monday_consistently()
        {
            var first = TrafficMath.MakeBuckets(
                new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc),   // Monday
                new DateTime(2026, 10, 12, 6, 0, 0, DateTimeKind.Utc),  // Monday
                TrafficMath.BucketGranularity.Week);

            var second = TrafficMath.MakeBuckets(
                new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc),   // Wednesday
                new DateTime(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc),   // Friday
                TrafficMath.BucketGranularity.Week);

            // 2026-10-05 is a Monday.
            Assert.Equal(new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc), first[0].StartUtc);
            Assert.Equal(new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc), first[1].StartUtc);
            Assert.Equal(1, second.Count);
            Assert.Equal(new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc), second[0].StartUtc);
            Assert.Equal(DayOfWeek.Monday, first[0].StartUtc.DayOfWeek);
            Assert.Equal(DayOfWeek.Monday, first[1].StartUtc.DayOfWeek);
        }

        [Fact]
        public void Buckets_are_contiguous_and_equally_spaced_no_gaps()
        {
            // Empty buckets are rendered as zero, not omitted: the returned series must
            // always be contiguous with a fixed step so the caller can zero-fill.
            var days = TrafficMath.MakeBuckets(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
                TrafficMath.BucketGranularity.Day);

            Assert.Equal(20, days.Count);
            for (int i = 1; i < days.Count; i++)
                Assert.Equal(TimeSpan.FromDays(1), days[i].StartUtc - days[i - 1].StartUtc);

            var weeks = TrafficMath.MakeBuckets(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc),
                TrafficMath.BucketGranularity.Week);

            for (int i = 1; i < weeks.Count; i++)
                Assert.Equal(TimeSpan.FromDays(7), weeks[i].StartUtc - weeks[i - 1].StartUtc);
        }

        [Fact]
        public void BucketLabels_are_iso_dates_of_the_start()
        {
            var buckets = TrafficMath.MakeBuckets(
                new DateTime(2026, 10, 3, 5, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 10, 3, 5, 0, 0, DateTimeKind.Utc),
                TrafficMath.BucketGranularity.Day);

            Assert.Equal("2026-10-03", buckets[0].Label);
        }
    }

    public class RateTests
    {
        [Fact]
        public void Rate_divides_numerator_by_denominator()
        {
            Assert.Equal(0.5, TrafficMath.Rate(1, 2));
            Assert.Equal(0.75, TrafficMath.Rate(3, 4));
        }

        [Fact]
        public void Rate_zero_numerator_is_zero()
        {
            Assert.Equal(0.0, TrafficMath.Rate(0, 10));
        }

        [Fact]
        public void Rate_zero_denominator_is_null_not_nan()
        {
            // Design §9.1: zero denominator → "—", not NaN or a crash.
            var result = TrafficMath.Rate(5, 0);
            Assert.Null(result);
        }

        [Fact]
        public void Rate_negative_or_zero_denominator_is_null()
        {
            Assert.Null(TrafficMath.Rate(5, -1));
            Assert.Null(TrafficMath.Rate(0, 0));
        }
    }
    public class WilsonTests
    {
        [Fact]
        public void Wilson_interval_matches_hand_computed_bounds()
        {
            // s=196, n=1000, z=1.96 (95%):
            //   p̂ = 0.196, z² = 3.8416
            //   denom = 1 + z²/n = 1.0038416
            //   center = (p̂ + z²/(2n)) / denom ≈ 0.197162
            //   half   = (z/denom) · √(p̂(1−p̂)/n + z²/(4n²)) ≈ 0.024583
            //   lower ≈ 0.172579, upper ≈ 0.221745
            var w = TrafficMath.Wilson(196, 1000);

            Assert.InRange(w.Lower, 0.1725, 0.1727);
            Assert.InRange(w.Upper, 0.2216, 0.2219);
            Assert.True(w.Lower < w.Upper);
        }

        [Fact]
        public void Wilson_zero_successes_has_lower_bound_zero()
        {
            var w = TrafficMath.Wilson(0, 500);

            Assert.InRange(w.Lower, 0.0, 1e-9);
            Assert.InRange(w.Upper, 0.0, 0.02); // small but non-zero
        }

        [Fact]
        public void Wilson_zero_trials_is_a_closed_zero_interval()
        {
            var w = TrafficMath.Wilson(0, 0);

            Assert.Equal(0.0, w.Lower);
            Assert.Equal(0.0, w.Upper);
        }

        [Fact]
        public void Wilson_bounds_are_clamped_to_unit_interval()
        {
            var all = TrafficMath.Wilson(1000, 1000);
            Assert.InRange(all.Lower, 0.0, 1.0);
            Assert.InRange(all.Upper, 0.0, 1.0);

            var none = TrafficMath.Wilson(0, 1000);
            Assert.InRange(none.Lower, 0.0, 1.0);
            Assert.InRange(none.Upper, 0.0, 1.0);
        }
    }

    public class ChiSquareSrmTests
    {
        [Fact]
        public void Balanced_arms_pass()
        {
            var srm = TrafficMath.ChiSquareSrm(new[] { 5000L, 5000L }, new[] { 0.5, 0.5 });

            Assert.True(srm.IsBalanced);
            Assert.Equal(0.0, srm.ChiSquare, 9);
            Assert.Equal(1, srm.Dof);
            Assert.InRange(srm.PValue, 0.999, 1.0);
        }

        [Fact]
        public void Known_60_40_split_on_large_n_fails()
        {
            // 6000/4000 vs expected 50/50: chi² = 2·(1000²/5000) = 400 → p ≈ 0.
            var srm = TrafficMath.ChiSquareSrm(new[] { 6000L, 4000L }, new[] { 0.5, 0.5 });

            Assert.False(srm.IsBalanced);
            Assert.Equal(400.0, srm.ChiSquare, 6);
            Assert.True(srm.PValue < 0.001);
        }

        [Fact]
        public void Mild_skew_on_small_n_passes()
        {
            // 52/48 vs 50/50: chi² = 2·(2²/50) = 0.16 → p ≈ 0.687.
            var srm = TrafficMath.ChiSquareSrm(new[] { 52L, 48L }, new[] { 0.5, 0.5 });

            Assert.True(srm.IsBalanced);
            Assert.Equal(0.16, srm.ChiSquare, 9);
        }

        [Fact]
        public void Three_arms_balanced_pass_and_dof_is_k_minus_one()
        {
            var srm = TrafficMath.ChiSquareSrm(
                new[] { 1000L, 1000L, 1000L }, new[] { 1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0 });

            Assert.True(srm.IsBalanced);
            Assert.Equal(2, srm.Dof);
        }

        [Fact]
        public void Skewed_three_arms_fail()
        {
            var srm = TrafficMath.ChiSquareSrm(
                new[] { 1500L, 1000L, 1000L }, new[] { 1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0 });

            Assert.False(srm.IsBalanced);
            Assert.InRange(srm.PValue, 0.0, 0.05);
        }

        [Fact]
        public void Zero_total_is_balanced_with_zero_statistic()
        {
            var srm = TrafficMath.ChiSquareSrm(new[] { 0L, 0L }, new[] { 0.5, 0.5 });

            Assert.True(srm.IsBalanced);
            Assert.Equal(0.0, srm.ChiSquare);
        }
    }

    public class ReferrerHostTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Empty_referrer_maps_to_direct(string referrer)
        {
            Assert.Equal("direct", TrafficMath.ReferrerHost(referrer));
        }

        [Fact]
        public void Host_is_extracted_and_scheme_and_path_are_stripped()
        {
            Assert.Equal("www.google.com", TrafficMath.ReferrerHost("https://www.google.com/search?q=x"));
            Assert.Equal("news.ycombinator.com", TrafficMath.ReferrerHost("http://news.ycombinator.com/item?id=1"));
        }

        [Fact]
        public void Schemeless_referrer_is_still_parsed()
        {
            Assert.Equal("reddit.com", TrafficMath.ReferrerHost("reddit.com/r/programming/1"));
        }

        [Theory]
        [InlineData("https://tiler.app/")]
        [InlineData("https://www.tiler.app/signin")]
        [InlineData("https://beta.tiler.app/x")]
        [InlineData("tiler.app/x")]
        [InlineData("https://localhost:3000/x")]
        [InlineData("https://127.0.0.1:5000/redirect")]
        public void Self_referrals_are_dropped_to_direct(string referrer)
        {
            Assert.Equal("direct", TrafficMath.ReferrerHost(referrer));
        }

        [Fact]
        public void Unparseable_referrer_maps_to_direct()
        {
            Assert.Equal("direct", TrafficMath.ReferrerHost("not a url at all"));
        }
    }

    public class DeviceClassTests
    {
        [Theory]
        [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1", "mobile")]
        [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Mobile Safari/537.36", "mobile")]
        [InlineData("Mozilla/5.0 (iPod touch; CPU iPhone OS 16_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.5 Mobile/15E148 Safari/604.1", "mobile")]
        [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36", "desktop")]
        [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36", "desktop")]
        [InlineData("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36", "desktop")]
        [InlineData("Mozilla/5.0 (iPad; CPU OS 16_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.6 Mobile/15E148 Safari/604.1", "tablet")]
        [InlineData("Mozilla/5.0 (Linux; Android 5.1.1; Nexus 7 Build/LMY47V) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Tablet Safari/537.36", "tablet")]
        [InlineData(null, "unknown")]
        [InlineData("", "unknown")]
        [InlineData("curl/8.1.2", "unknown")]
        [InlineData("facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)", "unknown")]
        public void DeviceClass_classifies_real_user_agents(string ua, string expected)
        {
            Assert.Equal(expected, TrafficMath.DeviceClass(ua));
        }
    }

    public class ExclusionPredicateTests
    {
        private static readonly string[] Prefixes = { "test-", "anon-smoke" };
        private static readonly ISet<string> Internal = new HashSet<string> { "anon-team-001" };

        [Fact]
        public void EventId_with_configured_prefix_is_excluded()
        {
            Assert.True(TrafficMath.IsExcludedConversion(
                "test-smoke-123", "anon-clean-1", "/", "https://google.com", Prefixes, Internal));
        }

        [Fact]
        public void AnonymousId_with_configured_prefix_is_excluded()
        {
            Assert.True(TrafficMath.IsExcludedConversion(
                "evt-abc", "anon-smoke-42", "/", "https://google.com", Prefixes, Internal));
        }

        [Fact]
        public void Prefix_match_is_case_insensitive()
        {
            Assert.True(TrafficMath.IsExcludedConversion(
                "TEST-UPPER", "anon-clean-1", "/", null, Prefixes, Internal));
            Assert.True(TrafficMath.IsExcludedConversion(
                "evt-abc", "ANON-SMOKE-UPPER", "/", null, Prefixes, Internal));
        }

        [Fact]
        public void Clean_conversion_row_is_not_excluded()
        {
            Assert.False(TrafficMath.IsExcludedConversion(
                "evt-abc", "anon-clean-1", "/pricing", "https://google.com", Prefixes, Internal));
        }

        [Fact]
        public void Internal_anonymous_id_is_excluded_case_insensitively()
        {
            Assert.True(TrafficMath.IsExcludedConversion(
                "evt-abc", "Anon-Team-001", "/", null, Prefixes, Internal));
        }

        [Fact]
        public void Localhost_page_path_is_excluded()
        {
            Assert.True(TrafficMath.IsExcludedConversion(
                "evt-abc", "anon-clean-1", "http://localhost:3000/signup", null, Prefixes, Internal));
        }

        [Fact]
        public void Staging_referrer_is_excluded()
        {
            Assert.True(TrafficMath.IsExcludedConversion(
                "evt-abc", "anon-clean-1", "/", "https://staging.tiler.app/", Prefixes, Internal));
        }

        [Fact]
        public void Production_page_and_referrer_are_not_excluded()
        {
            Assert.False(TrafficMath.IsExcludedConversion(
                "evt-abc", "anon-clean-1", "/signup", "https://www.google.com", Prefixes, Internal));
        }

        [Fact]
        public void Experiment_forced_is_always_excluded_and_cannot_be_toggled_off()
        {
            // Design §6: Forced = 1 is always excluded — even with no configured
            // prefixes and no internal list there is no flag that keeps it in.
            var excluded = TrafficMath.IsExcludedExperiment(
                "anon-clean-1", forced: true, excludedIdPrefixes: new string[0], internalAnonymousIds: new HashSet<string>());

            Assert.True(excluded);
        }

        [Fact]
        public void Experiment_clean_non_forced_row_is_not_excluded()
        {
            var excluded = TrafficMath.IsExcludedExperiment(
                "anon-clean-1", forced: false, excludedIdPrefixes: new string[0], internalAnonymousIds: new HashSet<string>());

            Assert.False(excluded);
        }

        [Fact]
        public void Experiment_bad_ids_are_excluded()
        {
            Assert.True(TrafficMath.IsExcludedExperiment("anon-smoke-7", false, Prefixes, Internal));
            Assert.True(TrafficMath.IsExcludedExperiment("anon-team-001", false, Prefixes, Internal));
        }
    }

    public class FilterClampingTests
    {
        [Fact]
        public void Window_longer_than_max_is_clamped_keeping_the_recent_end()
        {
            var w = TrafficMath.ClampWindow(
                new DateTime(2026, 1, 1, 14, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 31, 9, 0, 0, DateTimeKind.Utc),
                maxDays: 14);

            Assert.Equal(new DateTime(2026, 1, 18, 0, 0, 0, DateTimeKind.Utc), w.FromUtc);
            Assert.Equal(new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc), w.ToUtc);
        }

        [Fact]
        public void To_before_from_is_swapped()
        {
            var w = TrafficMath.ClampWindow(
                new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                maxDays: 30);

            Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), w.FromUtc);
            Assert.Equal(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), w.ToUtc);
        }

        [Fact]
        public void In_range_window_passes_through_floored_to_midnight_utc()
        {
            var w = TrafficMath.ClampWindow(
                new DateTime(2026, 1, 5, 13, 45, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 9, 1, 0, 0, DateTimeKind.Utc),
                maxDays: 30);

            Assert.Equal(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), w.FromUtc);
            Assert.Equal(new DateTime(2026, 1, 9, 0, 0, 0, DateTimeKind.Utc), w.ToUtc);
            Assert.Equal(DateTimeKind.Utc, w.FromUtc.Kind);
            Assert.Equal(DateTimeKind.Utc, w.ToUtc.Kind);
        }

        [Fact]
        public void One_day_max_collapses_to_a_single_day()
        {
            var w = TrafficMath.ClampWindow(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
                maxDays: 1);

            Assert.Equal(new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), w.FromUtc);
            Assert.Equal(new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), w.ToUtc);
        }

        [Theory]
        [InlineData("channel", true)]
        [InlineData("CHANNEL", true)]
        [InlineData("utm_source", false)]
        [InlineData(null, false)]
        [InlineData("", false)]
        public void Unknown_dimension_is_rejected_known_ones_pass(string dimension, bool expected)
        {
            var allowed = new[] { "channel", "campaign", "source" };
            Assert.Equal(expected, TrafficMath.IsKnownDimension(dimension, allowed));
        }
    }
}
