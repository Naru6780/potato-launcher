namespace PotatoLauncher;

internal static class PerClientTrimPolicy
{
    public static bool IsEligible(double residentMb, int thresholdMb, DateTime? lastTrim,
        DateTime now, int cooldownSeconds, bool force)
    {
        if (!double.IsFinite(residentMb) || residentMb <= 0) return false;
        return force || residentMb >= thresholdMb &&
            (!lastTrim.HasValue || (now - lastTrim.Value).TotalSeconds >= cooldownSeconds);
    }
}
