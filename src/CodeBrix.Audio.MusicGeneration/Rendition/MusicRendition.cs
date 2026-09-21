using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Audio.MusicGeneration.Rendition;

/// <summary>
/// How a piece is voiced: an ordered list of voices, a gain for the percussion part, and a master
/// gain over all of them. A rendition is DATA - a consumer writes one, registers it under a name of
/// their own, and names it in <see cref="MusicGenerationOptions.Rendition"/>.
/// </summary>
/// <remarks>
/// <para>
/// VOICES GO TO PARTS IN THE ORDER THE PARTS FIRST SOUND. A rendition is written before anybody
/// knows what the music will be, so it cannot name channels: the first melodic part to sound takes
/// the first voice, the second takes the second, and so on. THE PERCUSSION PART IS NOT IN THE LIST
/// - it is whatever the music writes on the percussion channel, at
/// <see cref="PercussionGain"/>.
/// </para>
/// <para>
/// A PART BEYOND THE LIST IS VOICED AUTOMATICALLY: the music's own program change is honoured,
/// then the request's instrument hints, and anything still unvoiced is given a DISTINCT instrument
/// from a small table of voicings that were actually rated well. A rendition with NO voices at all
/// is therefore the automatic rendition, which is what plays when nothing is named.
/// </para>
/// <para>
/// A LATER PROGRAM CHANGE re-voices a part that was voiced AUTOMATICALLY - always, whatever this
/// rendition says. The parts THIS RENDITION'S OWN VOICES cover are the ones
/// <see cref="FollowsProgramChanges"/> governs, and it is false by default: generated music cannot
/// silently re-voice a part a rendition voiced on purpose.
/// </para>
/// </remarks>
public sealed class MusicRendition
{
    /// <summary>The master gain a rendition takes unless it says otherwise: unity.</summary>
    public const float DefaultMasterGain = 1.0F;

    /// <summary>
    /// The percussion gain a rendition takes unless it says otherwise. It is below unity because a
    /// General MIDI kit is usually the loudest thing in an arrangement.
    /// </summary>
    public const float DefaultPercussionGain = 0.8F;

    private readonly List<RenditionVoice> voices = new List<RenditionVoice>();

    private string description;
    private float masterGain = DefaultMasterGain;
    private float percussionGain = DefaultPercussionGain;

    /// <summary>Creates a rendition with no voices - which is the automatic rule, and nothing else.</summary>
    /// <param name="name">The name it is registered and asked for under.</param>
    /// <param name="description">A sentence a developer can read to know what it sounds like.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is null.</exception>
    public MusicRendition(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A rendition must have a name: it is how a consumer asks for it.", nameof(name));
        }

        if (description == null)
        {
            throw new ArgumentNullException(nameof(description));
        }

        Name = name;
        this.description = description;
    }

    /// <summary>The name the rendition is registered and asked for under, matched without case.</summary>
    public string Name { get; }

    /// <summary>A sentence a developer can read to know what this rendition sounds like.</summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public string Description
    {
        get => description;
        set => description = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// The gain over the whole arrangement, where 1 is unity. It multiplies every part's own gain.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not a finite number.</exception>
    public float MasterGain
    {
        get => masterGain;
        set
        {
            RequireGain(value, nameof(value));
            masterGain = value;
        }
    }

    /// <summary>The gain the percussion part sits at, where 1 is unity.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not a finite number.</exception>
    public float PercussionGain
    {
        get => percussionGain;
        set
        {
            RequireGain(value, nameof(value));
            percussionGain = value;
        }
    }

    /// <summary>
    /// The voices, in the order they are given to the melodic parts as those parts first sound.
    /// A part beyond the end of this list is voiced automatically.
    /// </summary>
    public IList<RenditionVoice> Voices => voices;

    /// <summary>
    /// Whether a program change later in the piece re-voices a part THIS RENDITION'S OWN
    /// <see cref="Voices"/> gave an instrument to. It is false: naming a rendition is a decision,
    /// and music written afterwards does not get to overrule it.
    /// </summary>
    /// <remarks>
    /// IT GOVERNS ONLY THIS RENDITION'S OWN VOICES. A part beyond the end of <see cref="Voices"/> -
    /// one voiced from the music, from the request's instrument hints or from the taste table -
    /// ALWAYS follows the music's later program changes, because nothing deliberate was chosen for
    /// it to protect. The automatic rendition has no voices at all, so it is unaffected either way.
    /// </remarks>
    public bool FollowsProgramChanges { get; set; }

    /// <summary>
    /// Copies the rendition and every voice in it, so that a session cannot be changed underneath
    /// itself by a later edit to the registered original.
    /// </summary>
    /// <returns>The copy.</returns>
    public MusicRendition Clone()
    {
        var copy = new MusicRendition(Name, description)
        {
            masterGain = masterGain,
            percussionGain = percussionGain,
            FollowsProgramChanges = FollowsProgramChanges
        };

        foreach (var voice in voices)
        {
            copy.voices.Add(voice == null ? null : voice.Clone());
        }

        return copy;
    }

    /// <summary>
    /// Registers the rendition, which is the same thing as
    /// <see cref="MusicRenditionRegistry.Register"/> and reads better at a call site.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The name belongs to a built-in rendition, or a different rendition already holds it.
    /// </exception>
    public void Register() => MusicRenditionRegistry.Register(this);

    /// <summary>Describes the rendition for a diagnostic or a test failure.</summary>
    /// <returns>A short description.</returns>
    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "{0} ({1} voice(s), master gain {2})", Name,
            voices.Count, masterGain);

    private static void RequireGain(float value, string parameterName)
    {
        if (!(value >= 0.0F) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value,
                "A gain is a non-negative, finite multiple of the part's own level, where 1 is unity.");
        }
    }
}
