using System;
using System.Globalization;

public class BasicDateTimer
{
    private DateTime startTime;
    private int duration;

    // Constructor that accepts a serialized string ("yyyyMMddHHmm")
    public static BasicDateTimer ServerSet(MyPlayer player, string saveName, int duration)
    {
        var basicDateTime = new BasicDateTimer();
        var serializedStart = AO.Save.GetString(player, saveName, "");
        if (serializedStart != "")
        {
            basicDateTime.startTime = DateTime.ParseExact(serializedStart, "yyyyMMddHHmm", CultureInfo.InvariantCulture);
        }
        else
        {
            basicDateTime.startTime = DateTime.UtcNow;
            AO.Save.SetString(player, saveName, basicDateTime.startTime.ToString("yyyyMMddHHmm"));
        }
        basicDateTime.duration = duration;
        return basicDateTime;
    }

    public static BasicDateTimer ClientSet(MyPlayer player, string serializedStart, int duration)
    {
        var basicDateTime = new BasicDateTimer();
        basicDateTime.duration = duration;
        basicDateTime.startTime = DateTime.ParseExact(serializedStart, "yyyyMMddHHmm", CultureInfo.InvariantCulture);
        return basicDateTime;
    }

    // Returns the serialized start time
    public string GetSerializedStartTime() => startTime.ToString("yyyyMMddHHmm");

    // Returns the remaining time from now until the end time,
    // formatted as "d:hh:mm" if at least one day remains, otherwise "hh:mm"
    public string GetRemainingTimeFormatted()
    {
        TimeSpan ts = startTime.AddDays(duration) - DateTime.UtcNow;
        if (ts.TotalMinutes < 0)
            return "00:00"; // Timer expired

        if (ts.TotalDays >= 1)
            return $"{(int)ts.TotalDays}:{ts.Hours:D2}:{ts.Minutes:D2}";

        return $"{ts.Hours:D2}:{ts.Minutes:D2}";
    }

    public string GetRemainingTimeFormattedFancy()
    {
        TimeSpan ts = startTime.AddDays(duration) - DateTime.UtcNow;
        if (ts.TotalMinutes < 0)
            return ""; // Timer expired

        if (ts.TotalDays >= 1)
            return $"{(int)ts.TotalDays}D {ts.Hours:D2}H {ts.Minutes:D2}M";

        return $"{ts.Hours:D2}H {ts.Minutes:D2}M ";
    }

    public bool IsExpired()
    {
        return DateTime.UtcNow > startTime.AddDays(duration);
    }
}