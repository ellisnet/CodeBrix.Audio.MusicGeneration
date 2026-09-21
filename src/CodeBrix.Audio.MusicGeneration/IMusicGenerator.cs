using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MusicGeneration.Generation;

namespace CodeBrix.Audio.MusicGeneration;

/// <summary>
/// A thing that produces music. Not a model file: a generator may be backed by no model at all, by
/// one, or by two, and it is the only thing anything in this library asks for music.
/// </summary>
/// <remarks>
/// <para>
/// THE CONTRACT, in full, because everything else is built on it.
/// </para>
/// <para>
/// PULL-BASED. <see cref="GenerateAsync"/> returns a sequence, and work happens only as the caller
/// pulls from it. NOT PULLING IS PAUSING - a caller that stops asking costs a generator nothing,
/// and a caller that stops asking for good disposes the enumerator and the generation stops. An
/// implementation must not run ahead of the caller into a queue of its own.
/// </para>
/// <para>
/// TICKS ARE SEGMENT-RELATIVE. One call produces ONE SEGMENT, whose ticks start at 0, at the
/// resolution <see cref="MusicRequest.TicksPerQuarterNote"/> names. THE CALLER FIXES THE
/// RESOLUTION, because several segments - possibly from several generators - land on one timeline,
/// and a timeline has one resolution. Where a segment sits on that timeline, and whether it begins
/// on a bar line, is the caller's business and never the generator's.
/// </para>
/// <para>
/// EVENTS MAY ARRIVE OUT OF TICK ORDER, and each one carries the generator's SETTLED TICK - the
/// tick through which nothing will change or be added. The caller holds an event until the settled
/// tick has passed it and then plays it in tick order, so a generator is free to write a bar and
/// then go back and fill in a part, as long as it does not claim to have settled what it is still
/// deciding. An item with NO event carries a settled tick on its own, which is how a generator
/// says that a stretch of silence is decided.
/// </para>
/// <para>
/// CHANNELS COUNT 1 TO 16, the way CodeBrix.Audio's <c>MidiEvent</c> counts them. A model that
/// counts 0 to 15 is corrected in its adapter, which is the one place that rule belongs.
/// </para>
/// <para>
/// A REQUEST IS EITHER HONOURED OR REFUSED. <see cref="Honours"/> declares what this generator acts
/// on, and an implementation calls
/// <see cref="MusicGeneratorCapabilities.EnsureHonoured"/> before it generates anything, so a
/// request that relies on something it cannot do is refused by name. Nothing is silently ignored.
/// </para>
/// <para>
/// LOADING IS SEPARATE FROM REGISTERING. Constructing a generator loads nothing and registering one
/// loads nothing, so an application that registers two models and uses one never pays for the
/// other. A generator loads on first use, or earlier if <see cref="PreloadAsync"/> is called - a
/// game takes that delay during a loading screen. Once loaded it STAYS loaded and serves every
/// later segment. <see cref="Release"/> gives the memory back and leaves the generator registered
/// and usable: the next request loads it again.
/// </para>
/// <para>
/// THREAD SAFETY. <see cref="Name"/>, <see cref="Family"/>, <see cref="Description"/>,
/// <see cref="Honours"/> and <see cref="IsLoaded"/> are safe to read from any thread, because a
/// registry hands the same instance to anyone who asks for it. One generation at a time per
/// generator is all an implementation need support.
/// </para>
/// </remarks>
public interface IMusicGenerator
{
    /// <summary>
    /// The name this generator is registered and asked for under, matched without regard to case.
    /// It is also what a diagnostic reports, so it must say plainly what is playing.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The family the generator belongs to - "Replay" for one that replays a piece it already has,
    /// or the name of the model family behind it. A consumer writing their own generator picks
    /// their own.
    /// </summary>
    string Family { get; }

    /// <summary>A sentence a developer can read to know what this generator produces.</summary>
    string Description { get; }

    /// <summary>
    /// The parts of a <see cref="MusicRequest"/> this generator acts on. Everything outside this
    /// set is refused by name rather than ignored.
    /// </summary>
    MusicRequestFeatures Honours { get; }

    /// <summary>
    /// Whether the generator is loaded and ready. A generator that needs nothing loaded reports
    /// true once it has read whatever it replays, and false again after <see cref="Release"/>.
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Loads whatever the generator needs, ahead of the first request. Calling it when the
    /// generator is already loaded does nothing.
    /// </summary>
    /// <param name="cancellationToken">Stops the load.</param>
    /// <returns>A task that completes when the generator is ready.</returns>
    Task PreloadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gives back the memory the generator is holding. The generator stays registered and stays
    /// usable: the next request loads it again.
    /// </summary>
    void Release();

    /// <summary>
    /// Generates one segment of music, released as the caller pulls it.
    /// </summary>
    /// <param name="request">
    /// What the music should be. Every part is optional, and anything this generator does not
    /// honour is refused rather than ignored.
    /// </param>
    /// <param name="cancellationToken">Stops the generation.</param>
    /// <returns>
    /// The segment: MIDI events at ticks measured from the start of the segment, each carrying the
    /// tick through which the generator's output has settled, and items carrying a settled tick
    /// alone where nothing sounds.
    /// </returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="MusicRequestNotHonouredException">
    /// The request relies on something this generator does not honour.
    /// </exception>
    IAsyncEnumerable<GeneratedMusicEvent> GenerateAsync(MusicRequest request,
        CancellationToken cancellationToken);
}
