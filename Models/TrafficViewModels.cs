using System;
using System.Collections.Generic;

namespace PostProcessingServer.Models
{
    // View-models for the Traffic Analytics dashboard (TRAFFIC_ANALYTICS_DESIGN.md §2–§8).
    //
    // Conventions (design §1.2 / §1.4):
    //  * every date is UTC;
    //  * every "users" number is a DISTINCT-identity count (anonymousId, else user id) —
    //    never a row count — so the same visitor is counted once per stage;
    //  * every "rate" is nullable and is null when its denominator is 0 (design §1.4);
    //  * intervals/boundaries are half-open [start, nextStart) and buckets are contiguous.

    /// <summary>Root model bound to the dashboard view (ViewData["Vm"]).</summary>
    public class TrafficViewModel
    {
        public bool Enabled { get; set; }
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public string Granularity { get; set; }      // "day" | "week"
        public string Dimension { get; set; }        // "channel" | "campaign" | "source"
        public bool IncludeTestTraffic { get; set; } // when true, §6 exclusions are not applied

        public TrafficOverviewModel Overview { get; set; }
        public List<TrafficTimePoint> TimeSeries { get; set; }
        public TrafficFunnelModel Funnel { get; set; }
        public TrafficLandingModel Landing { get; set; }
        public TrafficConversionModel Conversion { get; set; }
        public List<TrafficExperimentModel> Experiments { get; set; }
        public List<TrafficReferrerRow> Referrers { get; set; }
        public List<TrafficDeviceRow> Devices { get; set; }
        public TrafficSignupReconciliationModel SignupReconciliation { get; set; }
        public string Notes { get; set; }
    }

    /// <summary>One point on the time series (per-day / per-week event counts, zero-filled).</summary>
    public class TrafficTimePoint
    {
        public DateTime BucketUtc { get; set; }
        public long Value { get; set; }

        /// <summary>
        /// Registered (non-anonymous, non-deleted) <c>TilerUser</c> rows created in this bucket.
        /// Aligned to <see cref="BucketUtc"/> so the chart can overlay traffic and registrations
        /// on one time axis. Mirrors the SignupTracker "registered" definition (design §9.3 / F9).
        /// </summary>
        public long RegisteredUsers { get; set; }
    }

    /// <summary>Section 1 — overview counts + derived ratios.</summary>
    public class TrafficOverviewModel
    {
        public long TotalEvents { get; set; }
        public long UniqueAnonymous { get; set; }   // distinct non-null anonymousId in window
        public long ResolvedToUser { get; set; }    // distinct anonymousId that has an alias row
        public long WithReferrer { get; set; }      // distinct identities with a non-internal referrer
        public double? ResolutionRate { get; set; } // resolved / unique anonymous (null if none)
        public double? ReferralShare { get; set; }  // with referrer / unique identities (null if none)
    }

    /// <summary>Section 3 — one funnel stage.</summary>
    public class TrafficFunnelStage
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public long Users { get; set; }
        public double? DropFromPrevious { get; set; } // 0..1 fraction lost vs the stage above (null for first)
    }

    /// <summary>Section 3 — the whole funnel.</summary>
    public class TrafficFunnelModel
    {
        public List<TrafficFunnelStage> Stages { get; set; }
        public double? TopToBottom { get; set; }
        public string TopStageKey { get; set; }
    }

    /// <summary>Section 4 — one landing path row.</summary>
    public class TrafficLandingRow
    {
        public string Path { get; set; }
        public long Views { get; set; }   // distinct identities that landed on this path
        public long Clicks { get; set; }  // distinct identities that clicked a CTA on this path
        public double? Ctr { get; set; }  // clicks / views (null when views == 0)
    }

    /// <summary>Section 4 — landing paths (top-N by views) + totals.</summary>
    public class TrafficLandingModel
    {
        public List<TrafficLandingRow> Rows { get; set; }
        public long TotalViews { get; set; }
        public long TotalClicks { get; set; }
        public double? TotalCtr { get; set; }
    }

    /// <summary>Section 5 — conversions grouped by canonical stage.</summary>
    public class TrafficConversionRow
    {
        public string Stage { get; set; }
        public long Users { get; set; }
    }

    public class TrafficConversionModel
    {
        public List<TrafficConversionRow> Rows { get; set; }
    }

    /// <summary>Section 6 — one experiment arm.</summary>
    public class TrafficExperimentArm
    {
        public string Arm { get; set; }
        public long Users { get; set; }
        public double Share { get; set; } // fraction of this experiment's users (0..1)
        public long CtrCount { get; set; } // distinct assigned users who clicked a CTA
        public double? Ctr { get; set; }   // CtrCount / Users (null when Users == 0)
    }

    /// <summary>Section 6 — one experiment with its arm counts + SRM test.</summary>
    public class TrafficExperimentModel
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public List<TrafficExperimentArm> Arms { get; set; }
        public long TotalUsers { get; set; }
        public double? ChiSquare { get; set; }
        public double? PValue { get; set; }
        public bool IsBalanced { get; set; }
    }

    /// <summary>Section 7 — one referrer host row (user-deduped, with Wilson CI on conv rate).</summary>
    public class TrafficReferrerRow
    {
        public string Name { get; set; }
        public long Views { get; set; }       // distinct identities with a conversion event from this host
        public long Clicks { get; set; }      // distinct identities that clicked a CTA
        public long Conversions { get; set; } // distinct identities that reached the success stage
        public double? ClickRate { get; set; }
        public double? ConversionRate { get; set; }
        public double? WilsonLower { get; set; }
        public double? WilsonUpper { get; set; }
    }

    /// <summary>Section 8 — one device-class row.</summary>
    public class TrafficDeviceRow
    {
        public string DeviceClass { get; set; }
        public long Users { get; set; }
        public double Share { get; set; } // fraction of unique identities (0..1)
    }

    /// <summary>
    /// Section 10 — data-quality reconciliation (design §9.3): compare the dashboard's
    /// own <c>signup_verified</c> distinct-identity count against the registered
    /// <c>AspNetUsers</c> created in the same UTC window (the SignupTracker numbers).
    /// Tolerance is ±2% relative difference; both counts are null when the window has
    /// no signups of either kind.
    /// </summary>
    public class TrafficSignupReconciliationModel
    {
        /// <summary>Distinct identities reaching <c>signup_verified</c> in the window (Traffic's own number, §3 funnel); 0 when none.</summary>
        public long SignupVerifiedIdentities { get; set; }

        /// <summary>Registered (non-anonymous, non-deleted) <c>TilerUser</c> rows created in the window — matches SignupTracker's "registered" count.</summary>
        public long RegisteredCreatedInWindow { get; set; }

        /// <summary>Anonymous <c>TilerUser</c> rows created in the window — matches SignupTracker's "anonymous" count (F9: never a signup signal).</summary>
        public long AnonymousCreatedInWindow { get; set; }

        /// <summary><c>RegisteredCreatedInWindow − SignupVerifiedIdentities</c> (null when either side is missing).</summary>
        public long? Delta { get; set; }

        /// <summary>|Delta| / max(SignupVerifiedIdentities, RegisteredCreatedInWindow), 0..∞ (null when the denominator is 0).</summary>
        public double? RelativeDifference { get; set; }

        /// <summary>True when the two counts are within the ±2% tolerance (null when not comparable).</summary>
        public bool? WithinTolerance { get; set; }
    }
}