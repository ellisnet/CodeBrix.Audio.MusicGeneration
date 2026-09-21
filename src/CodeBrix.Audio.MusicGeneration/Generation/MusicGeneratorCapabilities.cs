using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.MusicGeneration.Generation;

/// <summary>
/// The one place a request is checked against what a generator says it honours, and the one place
/// the refusal is worded. Every generator calls it; nothing else needs to.
/// </summary>
/// <remarks>
/// <para>
/// A request that relies on something a generator cannot do is REFUSED BY NAME. The alternative -
/// generating something and saying nothing - is the worst outcome available: an application asks
/// for a waltz in A minor, gets a reel in G, and has no way to find out why.
/// </para>
/// <para>
/// "Relies on" means the caller set it. A request left at its defaults relies on nothing, so a
/// generator that honours nothing at all still takes it - which is what the zero-configuration
/// path sends.
/// </para>
/// </remarks>
public static class MusicGeneratorCapabilities
{
    /// <summary>Works out which parts of a request the caller has actually asked for.</summary>
    /// <param name="request">The request to read.</param>
    /// <returns>
    /// The parts that are set. <see cref="MusicRequestFeatures.None"/> when the request asks for
    /// nothing beyond the tick resolution.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public static MusicRequestFeatures FeaturesUsedBy(MusicRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var used = MusicRequestFeatures.None;

        if (!string.IsNullOrWhiteSpace(request.Text))
        {
            used |= MusicRequestFeatures.FreeText;
        }

        if (!string.IsNullOrWhiteSpace(request.ModelNativeText))
        {
            used |= MusicRequestFeatures.ModelNativeText;
        }

        if (request.Primer != null)
        {
            used |= MusicRequestFeatures.Primer;
        }

        var intent = request.Intent;
        if (intent != null)
        {
            if (!string.IsNullOrWhiteSpace(intent.Key) || intent.Mode.HasValue)
            {
                used |= MusicRequestFeatures.Key;
            }

            if (intent.Meter.HasValue)
            {
                used |= MusicRequestFeatures.Meter;
            }

            if (intent.UnitNoteLength.HasValue)
            {
                used |= MusicRequestFeatures.UnitNoteLength;
            }

            if (intent.BeatsPerMinute.HasValue)
            {
                used |= MusicRequestFeatures.Tempo;
            }

            if (intent.CharacterWords.Count > 0)
            {
                used |= MusicRequestFeatures.CharacterWords;
            }

            if (intent.VoiceCount.HasValue)
            {
                used |= MusicRequestFeatures.VoiceCount;
            }

            if (intent.TargetLength.HasValue)
            {
                used |= MusicRequestFeatures.TargetLength;
            }

            if (intent.DrumKit.HasValue)
            {
                used |= MusicRequestFeatures.DrumKit;
            }
        }

        if (request.InstrumentHints.Count > 0)
        {
            used |= MusicRequestFeatures.InstrumentHints;
        }

        if (request.Seed.HasValue)
        {
            used |= MusicRequestFeatures.Seed;
        }

        if (!request.Controls.SamplingIsDefault)
        {
            used |= MusicRequestFeatures.SamplingControls;
        }

        if (request.Controls.MaximumEvents.HasValue)
        {
            used |= MusicRequestFeatures.MaximumEvents;
        }

        if (request.InferenceThreadCount.HasValue)
        {
            used |= MusicRequestFeatures.InferenceThreadCount;
        }

        if (request.Continuation != null)
        {
            used |= MusicRequestFeatures.Continuation;
        }

        return used;
    }

    /// <summary>Works out what a request asks for that a generator does not honour.</summary>
    /// <param name="generator">The generator being asked.</param>
    /// <param name="request">The request.</param>
    /// <returns>
    /// The parts the generator will not act on, or <see cref="MusicRequestFeatures.None"/> when it
    /// honours everything the request asks for.
    /// </returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static MusicRequestFeatures UnhonouredFeatures(IMusicGenerator generator, MusicRequest request)
    {
        if (generator == null)
        {
            throw new ArgumentNullException(nameof(generator));
        }

        return FeaturesUsedBy(request) & ~generator.Honours;
    }

    /// <summary>
    /// Checks a request against a generator and throws when it asks for something the generator
    /// does not honour. This is the call a generator makes at the top of its own
    /// <see cref="IMusicGenerator.GenerateAsync"/>.
    /// </summary>
    /// <param name="generator">The generator being asked.</param>
    /// <param name="request">The request.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="MusicRequestNotHonouredException">
    /// The request asks for something the generator does not honour. The message names the
    /// generator and every part it will not act on.
    /// </exception>
    public static void EnsureHonoured(IMusicGenerator generator, MusicRequest request)
    {
        var unhonoured = UnhonouredFeatures(generator, request);

        if (unhonoured == MusicRequestFeatures.None)
        {
            return;
        }

        throw new MusicRequestNotHonouredException(
            Describe(generator.Name, unhonoured), generator.Name, unhonoured);
    }

    /// <summary>Words the refusal, naming the generator and each part of the request it refuses.</summary>
    /// <param name="generatorName">The generator that was asked.</param>
    /// <param name="unhonouredFeatures">The parts it does not honour.</param>
    /// <returns>The message.</returns>
    public static string Describe(string generatorName, MusicRequestFeatures unhonouredFeatures)
    {
        var parts = NamesOf(unhonouredFeatures);

        return $"The music generator '{generatorName}' does not honour these parts of the request: " +
               $"{string.Join(", ", parts)}. Leave them unset, or use a generator that honours them.";
    }

    /// <summary>Names each part of a request a set of flags stands for, the way a message says it.</summary>
    /// <param name="features">The parts to name.</param>
    /// <returns>
    /// One readable name per flag, in the order the flags are declared. Empty when there are none.
    /// </returns>
    public static IReadOnlyList<string> NamesOf(MusicRequestFeatures features)
    {
        var names = new List<string>();

        Add(features, MusicRequestFeatures.FreeText, "free text", names);
        Add(features, MusicRequestFeatures.ModelNativeText, "model-native text", names);
        Add(features, MusicRequestFeatures.Primer, "a primer", names);
        Add(features, MusicRequestFeatures.Key, "key and mode", names);
        Add(features, MusicRequestFeatures.Meter, "metre", names);
        Add(features, MusicRequestFeatures.UnitNoteLength, "unit note length", names);
        Add(features, MusicRequestFeatures.Tempo, "tempo", names);
        Add(features, MusicRequestFeatures.CharacterWords, "character words", names);
        Add(features, MusicRequestFeatures.VoiceCount, "voice count", names);
        Add(features, MusicRequestFeatures.TargetLength, "target length", names);
        Add(features, MusicRequestFeatures.DrumKit, "a drum kit", names);
        Add(features, MusicRequestFeatures.InstrumentHints, "instrument hints", names);
        Add(features, MusicRequestFeatures.Seed, "seed", names);
        Add(features, MusicRequestFeatures.SamplingControls, "sampling controls", names);
        Add(features, MusicRequestFeatures.MaximumEvents, "a maximum event count", names);
        Add(features, MusicRequestFeatures.InferenceThreadCount, "an inference thread count", names);
        Add(features, MusicRequestFeatures.Continuation, "a continuation", names);

        return names;
    }

    private static void Add(MusicRequestFeatures features, MusicRequestFeatures one, string name,
        List<string> names)
    {
        if ((features & one) == one)
        {
            names.Add(name);
        }
    }
}
