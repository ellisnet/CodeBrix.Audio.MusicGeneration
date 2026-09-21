using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.MusicGeneration.Generation;

/// <summary>
/// THE WORDS A GENERATOR CAN ACTUALLY ACT ON, and what each of them means musically. It is the one
/// place <see cref="MusicIntent.CharacterWords"/> is turned into something a model understands.
/// </summary>
/// <remarks>
/// <para>
/// NO MODEL HERE READS PROSE. A word is therefore worth keeping only where it maps onto a real
/// musical setting - a tempo, a mode, or both - and a word that maps onto nothing is REFUSED BY
/// NAME. Nothing is dropped quietly: an application that asks for "menacing" is told that the word
/// is not one this generator knows, rather than being handed something cheerful.
/// </para>
/// <para>
/// WORDS ARE READ IN ORDER AND THE FIRST ONE THAT NAMES A THING SETTLES IT. "gentle, minor" is a
/// gentle tempo in a minor mode; "slow, fast" is slow, because the first word that names a tempo
/// decides it.
/// </para>
/// <para>
/// WHAT THE REQUEST SAYS OUTRIGHT ALWAYS WINS. A word never overrides an explicit
/// <see cref="MusicIntent.BeatsPerMinute"/> or <see cref="MusicIntent.Mode"/>: a word is the vaguer
/// way of saying the same thing, so it fills in only what has been left unsaid.
/// </para>
/// </remarks>
public static class MusicCharacterWords
{
    private static readonly Dictionary<string, MusicCharacter> Table =
        new Dictionary<string, MusicCharacter>(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] KnownWords;

    static MusicCharacterWords()
    {
        // TEMPO WORDS. The numbers are quarter notes a minute, and they are the tempi the pieces
        // of this library's own listening sessions were written at, rounded to the speeds a
        // musician would name.
        Add("slow", 60.0, null);
        Add("mournful", 60.0, MusicMode.Minor);
        Add("calm", 66.0, null);
        Add("peaceful", 66.0, null);
        Add("ambient", 66.0, null);
        Add("solemn", 66.0, MusicMode.Minor);
        Add("gentle", 72.0, null);
        Add("dreamy", 72.0, null);
        Add("sad", 72.0, MusicMode.Minor);
        Add("dark", 76.0, MusicMode.Minor);
        Add("melancholy", 76.0, MusicMode.Minor);
        Add("stately", 84.0, null);
        Add("wistful", 84.0, MusicMode.Minor);
        Add("moderate", 100.0, null);
        Add("flowing", 108.0, null);
        Add("triumphant", 112.0, MusicMode.Major);
        Add("heroic", 120.0, MusicMode.Major);
        Add("happy", 126.0, MusicMode.Major);
        Add("lively", 132.0, null);
        Add("bright", 132.0, MusicMode.Major);
        Add("cheerful", 132.0, MusicMode.Major);
        Add("joyful", 132.0, MusicMode.Major);
        Add("driving", 138.0, null);
        Add("energetic", 144.0, null);
        Add("urgent", 152.0, null);
        Add("fast", 168.0, null);

        // MODE WORDS. A mode names itself, and the two words a non-musician reaches for - major
        // and minor - name the two modes everything else is heard against.
        Add("major", null, MusicMode.Major);
        Add("minor", null, MusicMode.Minor);
        Add("ionian", null, MusicMode.Ionian);
        Add("dorian", null, MusicMode.Dorian);
        Add("phrygian", null, MusicMode.Phrygian);
        Add("lydian", null, MusicMode.Lydian);
        Add("mixolydian", null, MusicMode.Mixolydian);
        Add("aeolian", null, MusicMode.Aeolian);
        Add("locrian", null, MusicMode.Locrian);

        var known = new List<string>(Table.Count);

        foreach (var pair in Table)
        {
            known.Add(pair.Key);
        }

        known.Sort(StringComparer.Ordinal);
        KnownWords = known.ToArray();
    }

    /// <summary>
    /// Every word a generator can act on, in alphabetical order. A word outside this list is
    /// refused by name.
    /// </summary>
    public static IReadOnlyList<string> Known => KnownWords;

    /// <summary>Whether a word is one a generator can act on.</summary>
    /// <param name="word">The word, matched without regard to case or surrounding space.</param>
    /// <returns>True when it names something musical.</returns>
    public static bool IsKnown(string word) => Find(word) != null;

    /// <summary>What one word means.</summary>
    /// <param name="word">The word, matched without regard to case or surrounding space.</param>
    /// <returns>What it means, or null when it is not a word this library knows.</returns>
    public static MusicCharacter Find(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return null;
        }

        return Table.TryGetValue(word.Trim(), out var found) ? found : null;
    }

    /// <summary>Reads a whole list of words, and refuses by name any one of them it cannot act on.</summary>
    /// <param name="words">The words, or null when there are none.</param>
    /// <param name="generatorName">The generator being asked, so a refusal names it.</param>
    /// <returns>What the words mean together, or null when there are none.</returns>
    /// <exception cref="MusicRequestNotHonouredException">
    /// One of the words is not one this library knows.
    /// </exception>
    public static MusicCharacter Resolve(IList<string> words, string generatorName)
    {
        if (words == null || words.Count == 0)
        {
            return null;
        }

        var read = new List<string>(words.Count);
        double? beatsPerMinute = null;
        MusicMode? mode = null;

        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            var found = Find(word);

            if (found == null)
            {
                throw new MusicRequestNotHonouredException(
                    $"The music generator '{generatorName}' does not honour this request: the " +
                    $"character word '{word}' is not one it can act on. This model reads no prose, " +
                    "so a word is only worth having where it names a tempo or a mode; " +
                    "MusicCharacterWords.Known lists the words that do. Leave the word out, or say " +
                    "the same thing with MusicIntent.BeatsPerMinute or MusicIntent.Mode.",
                    generatorName, MusicRequestFeatures.CharacterWords);
            }

            read.Add(found.Words[0]);

            // THE FIRST WORD THAT NAMES A THING SETTLES IT, so a later word can add what an
            // earlier one left unsaid but never overrule it.
            if (!beatsPerMinute.HasValue)
            {
                beatsPerMinute = found.BeatsPerMinute;
            }

            if (!mode.HasValue)
            {
                mode = found.Mode;
            }
        }

        return new MusicCharacter(read.ToArray(), beatsPerMinute, mode);
    }

    /// <summary>
    /// The request to generate from, with whatever its character words mean written into its
    /// intent - which is how an adapter honours words without knowing anything about them.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="generatorName">The generator being asked, so a refusal names it.</param>
    /// <returns>
    /// The same request when it carries no character words, and otherwise a copy whose intent
    /// carries the tempo and the mode the words name. What the request already said outright is
    /// left exactly as it was.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="MusicRequestNotHonouredException">
    /// One of the words is not one this library knows.
    /// </exception>
    public static MusicRequest ApplyTo(MusicRequest request, string generatorName)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var intent = request.Intent;

        if (intent == null || intent.CharacterWords.Count == 0)
        {
            return request;
        }

        var character = Resolve(intent.CharacterWords, generatorName);
        var applied = request.Clone();
        var target = applied.Intent;

        if (character.BeatsPerMinute.HasValue && !target.BeatsPerMinute.HasValue)
        {
            target.BeatsPerMinute = character.BeatsPerMinute;
        }

        if (character.Mode.HasValue && !target.Mode.HasValue)
        {
            target.Mode = character.Mode;
        }

        // The words have been read; leaving them in place would make the copy look as though it
        // still relied on something nobody has acted on.
        target.CharacterWords.Clear();

        return applied;
    }

    private static void Add(string word, double? beatsPerMinute, MusicMode? mode) =>
        Table.Add(word, new MusicCharacter(new[] { word }, beatsPerMinute, mode));
}
