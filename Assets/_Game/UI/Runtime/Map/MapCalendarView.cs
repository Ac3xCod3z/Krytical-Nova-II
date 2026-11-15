using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Map window's Calendar page (owner, 15 Nov 2026) and the date line under the minimap: today's date (season, day of 14, year,
    /// in-game day and time), the moon with the next full and new moon, tonight's constellation, a month grid of four moon cycles with the
    /// sky's events, the coming events with countdowns in real time, and a 7-day forecast. Everything comes from <see cref="GameCalendar"/>
    /// on this machine; the text refreshes once a second and the grid, events and forecast once an in-game day.
    /// </summary>
    public sealed class MapCalendarView
    {
        const int GridColumns = 8;
        const int GridRows = 4;
        const int ForecastDays = 7;
        const float RefreshSeconds = 1f;

        readonly VisualElement page;
        readonly Label minimapLine;
        Label dateLabel, timeLabel, seasonNextLabel, moonName, moonNext, tonightLabel;
        VisualElement seasonFill;
        MoonGlyph moonGlyph;
        VisualElement grid, upcomingList, forecastRow;

        readonly List<CalendarDay> days = new List<CalendarDay>();
        readonly List<UpcomingEvent> events = new List<UpcomingEvent>();
        readonly List<ForecastDay> forecast = new List<ForecastDay>();
        readonly List<Label> eventTimes = new List<Label>();
        long builtForDay = long.MinValue;
        bool builtAlwaysWinter, builtCelsius;
        float nextRefresh;

        public MapCalendarView(VisualElement page, Label minimapLine)
        {
            this.page = page;
            this.minimapLine = minimapLine;
            if (page != null) Build();
        }

        public static string SeasonWord(Season s) => SeasonRules.Name(s).ToUpperInvariant();

        /// <summary>Call every frame; works only once a second. <paramref name="pageShown"/>: the Calendar page is open.</summary>
        public void Tick(bool pageShown)
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            if (!GameCalendarSource.TryRead(out CalendarClock c))
            {
                if (minimapLine != null) minimapLine.text = "";
                return;
            }
            SeasonSettings s = GameCalendarSource.Seasons;
            SeasonCalendar cal = s.Calendar;
            CalendarDate date = GameCalendar.Date(c, cal);
            UpdateMinimapLine(date);
            if (!pageShown || page == null) return;

            if (c.Day != builtForDay || c.AlwaysWinter != builtAlwaysWinter || TemperatureUnits.Celsius != builtCelsius) Rebuild(c, s, cal);
            dateLabel.text = $"{SeasonWord(date.Season)}  ·  DAY {date.DayOfSeason} OF {date.SeasonDays}  ·  YEAR {date.Year}";
            timeLabel.text = $"Game day {date.Day:N0}  ·  {date.Hour:00}:{date.Minute:00}  (one game day = {GameCalendar.Span(c.DaySeconds)} real time)";
            seasonNextLabel.text = $"{SeasonRules.Name(date.NextSeason)} begins in {GameCalendar.Span(date.SecondsToNextSeason)}";
            seasonFill.style.width = Length.Percent(date.SeasonProgress * 100f);

            MoonNow moon = GameCalendar.Moon(c);
            moonGlyph.Phase = moon.Phase01;
            moonName.text = $"{GameCalendar.MoonNames[moon.Index].ToUpperInvariant()}  ·  {moon.Lit:P0} lit";
            moonNext.text = $"Full in {Days(moon.DaysToFull)} ({GameCalendar.Span(moon.DaysToFull * c.DaySeconds)})  ·  "
                            + $"New in {Days(moon.DaysToNew)} ({GameCalendar.Span(moon.DaysToNew * c.DaySeconds)})";

            for (int i = 0; i < events.Count && i < eventTimes.Count; i++)
            {
                UpcomingEvent e = events[i];
                double left = e.StartUnix - c.UnixNow;
                eventTimes[i].text = e.Now || left <= 0 ? "NOW" : $"in {GameCalendar.Span(left)}  ·  {Local(e.StartUnix):ddd HH:mm}";
            }
        }

        void UpdateMinimapLine(in CalendarDate date)
        {
            if (minimapLine == null) return;
            string weather = GameCalendarSource.WeatherNow();
            string air = GameCalendarSource.TryTemperature(out float t) ? "  " + TemperatureUnits.Format(t) : "";
            minimapLine.text = $"{SeasonWord(date.Season)} {date.DayOfSeason}/{date.SeasonDays}  ·  {date.Hour:00}:{date.Minute:00}{air}"
                               + (weather.Length > 0 ? $"  ·  {weather.ToUpperInvariant()}" : "");
        }

        static string Days(double d) => d < 0.05 ? "now" : d < 1.5 ? "1 day" : $"{Math.Round(d):0} days";
        static DateTime Local(double unix) => DateTime.UnixEpoch.AddSeconds(unix).ToLocalTime();

        // ------------------------------------------------------------------ build (once)

        void Build()
        {
            page.Clear();
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("calendar-scroll");
            page.Add(scroll);

            // today
            var today = Box("calendar-today");
            dateLabel = new Label { name = "calendar-date" };
            dateLabel.AddToClassList("calendar-date");
            timeLabel = new Label();
            timeLabel.AddToClassList("calendar-sub");
            var bar = new VisualElement();
            bar.AddToClassList("calendar-season-bar");
            seasonFill = new VisualElement();
            seasonFill.AddToClassList("calendar-season-fill");
            bar.Add(seasonFill);
            seasonNextLabel = new Label();
            seasonNextLabel.AddToClassList("calendar-sub");
            today.Add(dateLabel); today.Add(timeLabel); today.Add(bar); today.Add(seasonNextLabel);
            scroll.Add(today);

            // moon + tonight
            var row = new VisualElement();
            row.AddToClassList("calendar-row");
            var moonBox = Box("calendar-moon");
            moonBox.Add(Heading("THE MOON"));
            var moonLine = new VisualElement();
            moonLine.AddToClassList("calendar-moon-line");
            moonGlyph = new MoonGlyph();
            moonGlyph.AddToClassList("calendar-moon-glyph");
            var moonText = new VisualElement();
            moonText.AddToClassList("calendar-moon-text");
            moonName = new Label();
            moonName.AddToClassList("calendar-strong");
            moonNext = new Label();
            moonNext.AddToClassList("calendar-sub");
            moonText.Add(moonName); moonText.Add(moonNext);
            moonLine.Add(moonGlyph); moonLine.Add(moonText);
            moonBox.Add(moonLine);
            var tonightBox = Box("calendar-tonight");
            tonightBox.Add(Heading("TONIGHT"));
            tonightLabel = new Label();
            tonightLabel.AddToClassList("calendar-sub");
            tonightBox.Add(tonightLabel);
            row.Add(moonBox); row.Add(tonightBox);
            scroll.Add(row);

            // month grid
            var gridBox = Box("calendar-grid-box");
            gridBox.Add(Heading("FOUR MOONS  ·  ONE SQUARE = ONE GAME DAY"));
            grid = new VisualElement();
            grid.AddToClassList("calendar-grid");
            gridBox.Add(grid);
            var legend = new VisualElement();
            legend.AddToClassList("calendar-legend");
            foreach (SkyMarks m in LegendMarks)
            {
                legend.Add(Dot(m));
                var l = new Label(MarkName(m));
                l.AddToClassList("calendar-legend-text");
                legend.Add(l);
            }
            gridBox.Add(legend);
            scroll.Add(gridBox);

            // coming up
            var upBox = Box("calendar-upcoming");
            upBox.Add(Heading("COMING UP"));
            upcomingList = new VisualElement();
            upBox.Add(upcomingList);
            scroll.Add(upBox);

            // forecast
            var fcBox = Box("calendar-forecast-box");
            fcBox.Add(Heading("7-DAY FORECAST  ·  GAME DAYS  ·  HIGH / LOW"));
            forecastRow = new VisualElement();
            forecastRow.AddToClassList("calendar-forecast");
            fcBox.Add(forecastRow);
            var note = new Label("Rain · snow · storm: the share of the day. Highs and lows before the passing weather's own few degrees.");
            note.AddToClassList("help");
            fcBox.Add(note);
            scroll.Add(fcBox);
        }

        static readonly SkyMarks[] LegendMarks =
        {
            SkyMarks.Emberfall, SkyMarks.PaleWanderer, SkyMarks.Eclipse, SkyMarks.Aurora, SkyMarks.Nebula,
        };

        static string MarkName(SkyMarks m) => m switch
        {
            SkyMarks.Emberfall => "The Emberfall",
            SkyMarks.PaleWanderer => "The Pale Wanderer",
            SkyMarks.Eclipse => "Eclipse",
            SkyMarks.Aurora => "Aurora",
            SkyMarks.Nebula => "Nebula",
            _ => m.ToString(),
        };

        static VisualElement Box(string cls)
        {
            var e = new VisualElement();
            e.AddToClassList("calendar-box");
            e.AddToClassList(cls);
            return e;
        }

        static Label Heading(string text)
        {
            var l = new Label(text);
            l.AddToClassList("calendar-heading");
            return l;
        }

        static VisualElement Dot(SkyMarks m)
        {
            var d = new VisualElement { pickingMode = PickingMode.Ignore };
            d.AddToClassList("calendar-dot");
            d.AddToClassList("calendar-dot-" + m.ToString().ToLowerInvariant());
            return d;
        }

        // ------------------------------------------------------------------ rebuild (once an in-game day)

        void Rebuild(in CalendarClock c, SeasonSettings s, in SeasonCalendar cal)
        {
            builtForDay = c.Day;
            builtAlwaysWinter = c.AlwaysWinter;
            builtCelsius = TemperatureUnits.Celsius;
            SkyEventSettings sky = GameCalendarSource.Sky;

            // tonight
            long night = SkyCalendar.NightOf(c.Day, c.Time01);
            SkyMarks tonight = GameCalendar.MarksOf(c, sky, cal, night);
            string star = sky != null ? StarCharts.All[GameCalendar.ConstellationOf(c, sky, night)].Name : "";
            var parts = new List<string>();
            if (star.Length > 0) parts.Add($"Overhead at midnight: {star}");
            foreach (SkyMarks m in LegendMarks) if ((tonight & m) != 0) parts.Add(MarkName(m) + " tonight");
            if (parts.Count <= 1) parts.Add("A quiet sky.");
            tonightLabel.text = string.Join("\n", parts);

            // the grid: four moon cycles from the start of this one, a row per moon
            grid.Clear();
            GameCalendar.Days(c, sky, cal, GameCalendar.MoonCycleStart(c), GridColumns * GridRows, days);
            for (int r = 0; r < GridRows; r++)
            {
                var line = new VisualElement();
                line.AddToClassList("calendar-grid-row");
                var tag = new Label(r == 0 ? "THIS\nMOON" : $"+{r}");
                tag.AddToClassList("calendar-grid-tag");
                line.Add(tag);
                for (int k = 0; k < GridColumns; k++)
                {
                    int i = r * GridColumns + k;
                    if (i >= days.Count) break;
                    line.Add(Cell(days[i], c));
                }
                grid.Add(line);
            }

            // coming up
            upcomingList.Clear();
            eventTimes.Clear();
            GameCalendar.Upcoming(c, sky, cal, events);
            foreach (UpcomingEvent e in events)
            {
                var item = new VisualElement();
                item.AddToClassList("calendar-event");
                string name = GameCalendar.EventName(e.Kind, e.Season);
                if (e.Kind == CalendarEventKind.PaleWanderer && e.Nights > 1) name += $" ({e.Nights} nights)";
                var n = new Label(name.ToUpperInvariant());
                n.AddToClassList("calendar-event-name");
                n.AddToClassList("calendar-event-" + e.Kind.ToString().ToLowerInvariant());
                var t = new Label();
                t.AddToClassList("calendar-event-time");
                item.Add(n); item.Add(t);
                upcomingList.Add(item);
                eventTimes.Add(t);
            }

            // forecast
            forecastRow.Clear();
            GameCalendar.Forecast(c, s, GameCalendarSource.Catalog, ForecastDays, forecast);
            foreach (ForecastDay d in forecast) forecastRow.Add(ForecastCell(d, c));
        }

        static VisualElement Cell(in CalendarDay d, in CalendarClock c)
        {
            var cell = new VisualElement();
            cell.AddToClassList("calendar-cell");
            if (d.Today) cell.AddToClassList("today");
            if (d.Day < c.Day) cell.AddToClassList("past");
            var time = new Label(Local(d.StartUnix).ToString("HH:mm"));
            time.AddToClassList("calendar-cell-time");
            cell.Add(time);
            var glyph = new MoonGlyph { Phase = d.MoonIndex / 8f };
            glyph.AddToClassList("calendar-cell-moon");
            cell.Add(glyph);
            var dots = new VisualElement { pickingMode = PickingMode.Ignore };
            dots.AddToClassList("calendar-cell-dots");
            foreach (SkyMarks m in LegendMarks) if ((d.Marks & m) != 0) dots.Add(Dot(m));
            cell.Add(dots);
            cell.tooltip = $"Game day {d.Day:N0}, from {Local(d.StartUnix):ddd HH:mm} your time\n{GameCalendar.MoonNames[d.MoonIndex]}"
                           + $"\nOverhead: {StarCharts.All[d.Constellation].Name}";
            return cell;
        }

        static VisualElement ForecastCell(in ForecastDay d, in CalendarClock c)
        {
            var cell = new VisualElement();
            cell.AddToClassList("calendar-fc");
            cell.AddToClassList("look-" + d.Look.ToString().ToLowerInvariant());
            var when = new Label(d.Day == c.Day ? "TODAY" : Local(d.StartUnix).ToString("HH:mm"));
            when.AddToClassList("calendar-fc-when");
            var look = new Label(GameCalendar.LookName(d.Look).ToUpperInvariant());
            look.AddToClassList("calendar-fc-look");
            var spell = new Label(d.SpellName.Length > 0 ? d.SpellName : SeasonRules.Name(d.Season));
            spell.AddToClassList("calendar-fc-spell");
            var temp = new Label($"{TemperatureUnits.Format(d.HighC, false)} / {TemperatureUnits.Format(d.LowC, false)}");
            temp.AddToClassList("calendar-fc-temp");
            var chance = new Label($"R {d.Rain:P0}\nS {d.Snow:P0}\nT {d.Storm:P0}");
            chance.AddToClassList("calendar-fc-chance");
            cell.Add(when); cell.Add(look); cell.Add(spell); cell.Add(temp); cell.Add(chance);
            cell.tooltip = $"Game day {d.Day:N0} ({SeasonRules.Name(d.Season)}), from {Local(d.StartUnix):ddd HH:mm} your time"
                           + (d.SpellName.Length > 0 ? $"\nWeather spell: {d.SpellName}" : "")
                           + $"\nRain {d.Rain:P0} · Snow {d.Snow:P0} · Thunderstorms {d.Storm:P0} of the day"
                           + (d.HeatHaze ? "\nHot enough for heat haze" : "");
            return cell;
        }
    }

    /// <summary>A moon drawn by phase (0 new, 0.5 full): the lit side between the limb and the terminator.</summary>
    public sealed class MoonGlyph : VisualElement
    {
        static readonly Color Dark = new Color(0.1f, 0.14f, 0.2f, 1f);
        static readonly Color Lit = new Color(0.93f, 0.89f, 0.76f, 1f);
        static readonly Color Rim = new Color(0.45f, 0.87f, 0.77f, 0.8f);
        const int Steps = 24;
        float phase;

        public float Phase
        {
            get => phase;
            set { if (Mathf.Approximately(phase, value)) return; phase = value; MarkDirtyRepaint(); }
        }

        public MoonGlyph()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            float radius = Mathf.Min(r.width, r.height) * 0.5f - 1f;
            if (radius <= 1f) return;
            Vector2 o = r.center;
            Painter2D p = ctx.painter2D;
            p.fillColor = Dark;
            p.BeginPath();
            p.Arc(o, radius, 0f, 360f);
            p.Fill();

            float ph = Mathf.Repeat(phase, 1f);
            float k = Mathf.Cos(ph * Mathf.PI * 2f);                // 1 new .. -1 full
            float side = ph < 0.5f ? 1f : -1f;                       // waxing: lit on the right
            if (k < 0.999f)
            {
                p.fillColor = Lit;
                p.BeginPath();
                for (int i = 0; i <= Steps; i++)                     // the limb, top to bottom
                {
                    float a = Mathf.Lerp(-90f, 90f, i / (float)Steps) * Mathf.Deg2Rad;
                    var pt = new Vector2(o.x + side * radius * Mathf.Cos(a), o.y + radius * Mathf.Sin(a));
                    if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
                }
                for (int i = Steps; i >= 0; i--)                     // the terminator, back up
                {
                    float a = Mathf.Lerp(-90f, 90f, i / (float)Steps) * Mathf.Deg2Rad;
                    p.LineTo(new Vector2(o.x + side * k * radius * Mathf.Cos(a), o.y + radius * Mathf.Sin(a)));
                }
                p.ClosePath();
                p.Fill();
            }
            p.strokeColor = Rim;
            p.lineWidth = 1f;
            p.BeginPath();
            p.Arc(o, radius, 0f, 360f);
            p.Stroke();
        }
    }
}
