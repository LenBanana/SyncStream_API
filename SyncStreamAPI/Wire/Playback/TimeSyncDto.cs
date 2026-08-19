namespace SyncStreamAPI.Wire.Playback;

/// <summary>
///     A playback position, dated so a viewer can tell how stale it is.
/// </summary>
/// <remarks>
///     The previous payload was a bare <c>double</c>. A viewer compared it against its own present with no way
///     to know the value had been sampled on the host a network hop earlier, which systematically dragged
///     high-latency viewers behind and made every correction a guess.
///     <para>
///         <see cref="ageMs" /> is measured entirely on the server clock: the gap between now and when the
///         host's report arrived. That matters, because it means no clock synchronisation between host, server
///         and viewer is needed anywhere. The viewer adds its own one-way latency estimate, measured against
///         its own clock, so every term comes from a single clock.
///     </para>
/// </remarks>
public class TimeSyncDto
{
    public TimeSyncDto()
    {
    }

    public TimeSyncDto(double mediaTime, double ageMs, bool isPlaying)
    {
        this.mediaTime = mediaTime;
        this.ageMs = ageMs;
        this.isPlaying = isPlaying;
    }

    /// <summary>Playback position in seconds, as the host last reported it.</summary>
    public double mediaTime { get; set; }

    /// <summary>Milliseconds since that position was reported. Zero when the host is reporting right now.</summary>
    public double ageMs { get; set; }

    /// <summary>
    ///     Whether playback was running. A paused position does not advance, so a viewer must not extrapolate
    ///     from it.
    /// </summary>
    public bool isPlaying { get; set; }
}
