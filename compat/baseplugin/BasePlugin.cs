using System;
using System.Collections.Generic;
using System.Globalization;

namespace Turbo.Plugins.Default
{
    /// <summary>
    /// The base class TurboHUD plugins inherit from.
    ///
    /// This is an independent, C# 5 compatible implementation of the public
    /// plugin base contract. It only depends on the public interfaces of the
    /// plugin API; no engine binary is referenced.
    /// </summary>
    public abstract class BasePlugin : IPlugin
    {
        private Dictionary<string, IPerfCounter> _performanceCounters;

        public IController Hud { get; private set; }
        public bool Enabled { get; set; }
        public int Order { get; set; }

        public Dictionary<string, IPerfCounter> PerformanceCounters
        {
            get
            {
                if (_performanceCounters == null)
                    _performanceCounters = new Dictionary<string, IPerfCounter>();
                return _performanceCounters;
            }
        }

        public virtual void Load(IController hud)
        {
            Hud = hud;
        }

        public static string ValueToString(long value, ValueFormat format)
        {
            if (value < 0)
                return "-" + ValueToString(-value, format);

            switch (format)
            {
                case ValueFormat.NormalNumber:
                    return value.ToString("#,0.#", CultureInfo.InvariantCulture);
                case ValueFormat.NormalNumberNoDecimal:
                    return value.ToString("#,0", CultureInfo.InvariantCulture);
                case ValueFormat.AlwaysK:
                    return (value / 1000.0).ToString("#,0.#K", CultureInfo.InvariantCulture);
                case ValueFormat.AlwaysKNoDecimal:
                    return (value / 1000.0).ToString("#,0K", CultureInfo.InvariantCulture);
                case ValueFormat.AlwaysM:
                    return (value / 1000000.0).ToString("#,0.#M", CultureInfo.InvariantCulture);
                case ValueFormat.AlwaysMNoDecimal:
                    return (value / 1000000.0).ToString("#,0M", CultureInfo.InvariantCulture);
                case ValueFormat.LongNumber:
                case ValueFormat.ShortNumber:
                    return Compact(value);
                case ValueFormat.LongTime:
                    return FormatTime(value, false);
                case ValueFormat.LongTimeNoSeconds:
                    return FormatTime(value, true);
            }

            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string ValueToString(double value, ValueFormat format)
        {
            if (double.IsNaN(value)) return "NaN";
            if (value < 0) return "-" + ValueToString(-value, format);

            switch (format)
            {
                case ValueFormat.NormalNumber:
                    return value.ToString("#,0.0", CultureInfo.InvariantCulture);
                case ValueFormat.NormalNumberNoDecimal:
                    return Math.Round(value, 0).ToString("#,0", CultureInfo.InvariantCulture);
                case ValueFormat.AlwaysK:
                    return (value / 1000.0).ToString("#,0.0K", CultureInfo.InvariantCulture);
                case ValueFormat.AlwaysKNoDecimal:
                    return (value / 1000.0).ToString("#,0K", CultureInfo.InvariantCulture);
                case ValueFormat.AlwaysM:
                    return (value / 1000000.0).ToString("#,0.0M", CultureInfo.InvariantCulture);
                case ValueFormat.AlwaysMNoDecimal:
                    return (value / 1000000.0).ToString("#,0M", CultureInfo.InvariantCulture);
                case ValueFormat.LongNumber:
                case ValueFormat.ShortNumber:
                    return Compact((long)value);
                case ValueFormat.LongTime:
                    return FormatTime((long)value, false);
                case ValueFormat.LongTimeNoSeconds:
                    return FormatTime((long)value, true);
            }

            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Compact(long value)
        {
            if (value == 0) return "0";
            if (value < 1000L) return value.ToString("0", CultureInfo.InvariantCulture);
            if (value < 1000000L)
                return (value / 1000.0).ToString("#,0.#k", CultureInfo.InvariantCulture);
            if (value < 1000000000L)
                return (value / 1000000.0).ToString("#,0.0M", CultureInfo.InvariantCulture);
            if (value < 1000000000000L)
                return (value / 1000000000.0).ToString("#,0.0bn", CultureInfo.InvariantCulture);
            return (value / 1000000000000.0).ToString("#,0.0tr", CultureInfo.InvariantCulture);
        }

        private static string FormatTime(long ticks, bool withoutSeconds)
        {
            TimeSpan span = new TimeSpan(ticks);
            string text = string.Format(CultureInfo.InvariantCulture, "{0}m {1}s",
                                        (int)span.TotalMinutes, span.Seconds);
            if (withoutSeconds)
                text = string.Format(CultureInfo.InvariantCulture, "{0}m", (int)span.TotalMinutes);
            if (span.TotalHours >= 1)
                text = string.Format(CultureInfo.InvariantCulture, "{0}h ", (int)span.TotalHours) + text;
            return text;
        }
    }
}
