using System;

namespace Zarus.Systems
{
    /// <summary>
    /// Immutable snapshot of the in-game clock used by UI and lighting systems.
    /// </summary>
    [Serializable]
    public struct InGameTimeSnapshot
    {
        public enum DaySegment
        {
            Night,
            Dawn,
            Day,
            Dusk
        }

        public DateTime DateTime { get; }
        public int DayIndex { get; }
        public float NormalizedTimeOfDay { get; }
        public float TimeOfDayMinutes { get; }
        public DaySegment Segment { get; }

        public InGameTimeSnapshot(DateTime dateTime, int dayIndex, float timeOfDayMinutes, float normalizedTimeOfDay, DaySegment segment)
        {
            DateTime = dateTime;
            DayIndex = dayIndex;
            TimeOfDayMinutes = timeOfDayMinutes;
            NormalizedTimeOfDay = normalizedTimeOfDay;
            Segment = segment;
        }

        public bool IsNight => Segment == DaySegment.Night;
        public bool IsDaytime => Segment == DaySegment.Day || Segment == DaySegment.Dawn;

        public string GetIndicatorLabel()
        {
            return Segment switch
            {
                DaySegment.Dawn => "DAWN",
                DaySegment.Day => "DAY",
                DaySegment.Dusk => "DUSK",
                _ => "NIGHT"
            };
        }
    }
}
