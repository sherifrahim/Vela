using System.Globalization;
using System.Text;
using Vela.Core;

namespace Vela.Connectors;

public sealed record CalEvent(string Title, DateTime Start, DateTime End, bool AllDay, string? Location);

/// <summary>
/// Calendar events from private iCal links (reference: Google, Outlook, iCloud). Each service can publish a secret
/// .ics address of a calendar; paste one or more, separated by spaces. Read-only, no sign-in, nothing stored but the
/// links (encrypted). Fetched every 15 minutes while a calendar tile is on the dock.
/// </summary>
public sealed class CalendarConnector : MetricConnector
{
    public override string Id => "calendar";
    public override string Name => "Calendar";
    public override string Description => "Events from Google, Outlook or iCloud through their private iCal links (Google: calendar settings → \"Secret address in iCal format\"; Outlook: Settings → Calendar → Shared calendars → Publish → ICS; iCloud: share the calendar as Public). Paste one or more links, separated by spaces.";
    public override string KeyHint => "https://…/basic.ics";
    public override string? HelpUrl => "https://support.google.com/calendar/answer/37648#zippy=%2Cget-your-calendar-view-only";
    public override string BadgeColor => "#FF3B30";
    public override string BadgeText => "31";
    public override string Unit => "next event";
    public override string SampleValue => "Standup";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(15);

    /// <summary>Upcoming events (now through two weeks), soonest first; events already under way are included.</summary>
    public List<CalEvent> Events { get; private set; } = new();

    protected override async Task FetchMetricAsync(string key)
    {
        var urls = key.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(u => u.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ? "https://" + u[9..] : u).ToList();
        var from = DateTime.Now.Date;
        var to = from.AddDays(14);
        var all = new List<CalEvent>();
        foreach (var url in urls)
        {
            using var res = await Http.Client.GetAsync(url);
            res.EnsureSuccessStatusCode();
            var text = await res.Content.ReadAsStringAsync();
            if (!text.Contains("BEGIN:VCALENDAR")) throw new InvalidOperationException("That link isn't an iCal (.ics) calendar");
            all.AddRange(Ics.Expand(text, from, to));
        }
        Events = all.Where(e => e.End > DateTime.Now).OrderBy(e => e.Start).ThenBy(e => e.Title).ToList();
        var next = Events.FirstOrDefault(e => !e.AllDay) ?? Events.FirstOrDefault();
        Value = next?.Title ?? "No upcoming events";
        Sub = next == null ? "next 14 days" : When(next);
        Rows = Events.Take(8).Select(e => new MetricRow(e.Title, When(e))).ToList();
    }

    public static string When(CalEvent e)
    {
        var now = DateTime.Now;
        string day = e.Start.Date == now.Date ? "Today" : e.Start.Date == now.Date.AddDays(1) ? "Tomorrow" : e.Start.ToString("ddd, MMM d", CultureInfo.CurrentCulture);
        if (e.AllDay) return $"{day} · all day";
        if (e.Start <= now && e.End > now) return $"now · until {e.End:t}";
        var until = e.Start - now;
        if (until.TotalMinutes < 60) return $"in {(int)Math.Ceiling(until.TotalMinutes)}m · {e.Start:t}";
        return $"{day} · {e.Start:t}";
    }
}

/// <summary>
/// Minimal iCalendar (RFC 5545) reader: VEVENTs with DTSTART/DTEND/DURATION, all-day dates, UTC / TZID / floating
/// times, RRULE (DAILY, WEEKLY with BYDAY, MONTHLY by day of month, YEARLY; INTERVAL, COUNT, UNTIL), EXDATE,
/// moved occurrences (RECURRENCE-ID) and cancelled events.
/// </summary>
public static class Ics
{
    sealed class Raw
    {
        public string Uid = "", Summary = "", Location = "", Status = "";
        public (DateTime t, bool date)? Start, End, RecurrenceId;
        public TimeSpan? Duration;
        public string? RRule;
        public List<DateTime> ExDates = new();
    }

    public static List<CalEvent> Expand(string text, DateTime from, DateTime to)
    {
        var events = Parse(text);
        // Occurrences that were moved or cancelled individually (RECURRENCE-ID) replace the series instance.
        var overridden = events.Where(e => e.RecurrenceId != null).Select(e => (e.Uid, e.RecurrenceId!.Value.t)).ToHashSet();
        var result = new List<CalEvent>();
        foreach (var e in events)
        {
            if (e.Start == null || e.Status == "CANCELLED") continue;
            var (start, allDay) = e.Start.Value;
            var length = e.End is { } end ? end.t - start : e.Duration ?? (allDay ? TimeSpan.FromDays(1) : TimeSpan.Zero);
            if (length < TimeSpan.Zero) length = TimeSpan.Zero;
            var title = e.Summary.Length > 0 ? e.Summary : "(No title)";
            var loc = e.Location.Length > 0 ? e.Location : null;

            IEnumerable<DateTime> starts = e.RRule == null || e.RecurrenceId != null ? new[] { start } : Occurrences(start, e.RRule, to);
            foreach (var s in starts)
            {
                if (e.RRule != null && e.RecurrenceId == null && (overridden.Contains((e.Uid, s)) || e.ExDates.Any(x => x == s || (allDay && x.Date == s.Date)))) continue;
                var en = s + length;
                if (en <= from && !(length == TimeSpan.Zero && s >= from)) continue;
                if (s >= to) continue;
                result.Add(new CalEvent(title, s, allDay && length == TimeSpan.Zero ? s.AddDays(1) : en, allDay, loc));
            }
        }
        return result;
    }

    static List<Raw> Parse(string text)
    {
        // Unfold: a line starting with a space or tab continues the previous one.
        var lines = new List<string>();
        foreach (var l in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (l.Length > 0 && (l[0] == ' ' || l[0] == '\t') && lines.Count > 0) lines[^1] += l[1..];
            else lines.Add(l);
        }
        var list = new List<Raw>();
        Raw? cur = null;
        foreach (var line in lines)
        {
            if (line == "BEGIN:VEVENT") { cur = new Raw(); continue; }
            if (line == "END:VEVENT") { if (cur != null) list.Add(cur); cur = null; continue; }
            if (cur == null) continue;
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var head = line[..colon];
            var value = line[(colon + 1)..];
            var parts = head.Split(';');
            var name = parts[0].ToUpperInvariant();
            var prm = parts.Skip(1).Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].ToUpperInvariant(), p => p[1].Trim('"'));
            switch (name)
            {
                case "UID": cur.Uid = value; break;
                case "SUMMARY": cur.Summary = Unescape(value); break;
                case "LOCATION": cur.Location = Unescape(value); break;
                case "STATUS": cur.Status = value.ToUpperInvariant(); break;
                case "DTSTART": cur.Start = Time(value, prm); break;
                case "DTEND": cur.End = Time(value, prm); break;
                case "RECURRENCE-ID": cur.RecurrenceId = Time(value, prm); break;
                case "DURATION": cur.Duration = Duration(value); break;
                case "RRULE": cur.RRule = value; break;
                case "EXDATE":
                    foreach (var v in value.Split(',')) if (Time(v, prm) is { } x) cur.ExDates.Add(x.t);
                    break;
            }
        }
        return list;
    }

    static string Unescape(string s) => s.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();

    /// <summary>To local time. "…Z" = UTC; TZID = that zone (IANA or Windows names); otherwise floating (local).</summary>
    static (DateTime t, bool date)? Time(string v, Dictionary<string, string> prm)
    {
        v = v.Trim();
        if (v.Length == 8 && DateTime.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return (d, true);
        bool utc = v.EndsWith('Z');
        if (!DateTime.TryParseExact(v.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return null;
        if (utc) return (DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime(), false);
        if (prm.TryGetValue("TZID", out var tzid) && Zone(tzid) is { } zone)
        {
            try { return (TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), zone, TimeZoneInfo.Local), false); } catch { }
        }
        return (t, false);
    }

    static readonly Dictionary<string, TimeZoneInfo?> Zones = new();
    static TimeZoneInfo? Zone(string id)
    {
        lock (Zones)
        {
            if (Zones.TryGetValue(id, out var z)) return z;
            try { z = TimeZoneInfo.FindSystemTimeZoneById(id); } catch { z = null; } // .NET maps IANA ids on Windows
            return Zones[id] = z;
        }
    }

    static TimeSpan? Duration(string v)
    {
        try { return System.Xml.XmlConvert.ToTimeSpan(v.Trim()); } catch { return null; } // ISO 8601, e.g. PT1H30M
    }

    static readonly string[] Days = { "SU", "MO", "TU", "WE", "TH", "FR", "SA" };

    static IEnumerable<DateTime> Occurrences(DateTime start, string rrule, DateTime to)
    {
        var r = rrule.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].ToUpperInvariant(), p => p[1]);
        var freq = r.GetValueOrDefault("FREQ", "");
        int interval = int.TryParse(r.GetValueOrDefault("INTERVAL"), out var iv) && iv > 0 ? iv : 1;
        int? count = int.TryParse(r.GetValueOrDefault("COUNT"), out var c) ? c : null;
        DateTime until = DateTime.MaxValue;
        if (r.TryGetValue("UNTIL", out var u) && Time(u, new()) is { } ut) until = ut.date ? ut.t.AddDays(1).AddTicks(-1) : ut.t;
        var byDay = r.TryGetValue("BYDAY", out var bd)
            ? bd.Split(',').Select(x => Array.IndexOf(Days, x.Length >= 2 ? x[^2..].ToUpperInvariant() : x)).Where(i => i >= 0).ToHashSet()
            : null;
        int emitted = 0;
        for (int step = 0; step < 5000; step++)
        {
            IEnumerable<DateTime> batch;
            switch (freq)
            {
                case "DAILY": batch = new[] { start.AddDays((double)step * interval) }; break;
                case "WEEKLY":
                    var weekStart = start.Date.AddDays(-(int)start.DayOfWeek).AddDays(7.0 * step * interval);
                    batch = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i) + start.TimeOfDay)
                        .Where(x => byDay != null ? byDay.Contains((int)x.DayOfWeek) : x.DayOfWeek == start.DayOfWeek)
                        .Where(x => x >= start);
                    break;
                case "MONTHLY":
                    var m = start.AddMonths(step * interval);
                    batch = m.Day == start.Day ? new[] { m } : Array.Empty<DateTime>(); // skip months without that day (e.g. the 31st)
                    break;
                case "YEARLY": batch = new[] { start.AddYears(step * interval) }; break;
                default: yield break;
            }
            foreach (var x in batch)
            {
                if (x > until || (count != null && emitted >= count) || x >= to) yield break;
                emitted++;
                yield return x;
            }
        }
    }
}
