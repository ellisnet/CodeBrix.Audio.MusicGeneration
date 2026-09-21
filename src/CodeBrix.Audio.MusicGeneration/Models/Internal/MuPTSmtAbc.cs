using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeBrix.Audio.MusicGeneration.Models.Internal; //was previously: README.md@bf8f270d11683f65aa83ccebc2e756b782b795d8

/// <summary>
/// The MuPT model's own text form, and how it becomes standard ABC notation. It is the publisher's
/// post-processing, ported: the model card of <c>m-a-p/MuPT-v1-8192-190M</c> carries it as Python,
/// under the Apache License 2.0, and THIRD-PARTY-NOTICES.txt records that.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THE MODEL WRITES is not standard ABC. Several parts were MERGED into one line when the
/// model was trained, so the body is a run of TIME SLICES separated by <c>&lt;|&gt;</c>, and inside
/// one slice each part's bar ends with its own bar line. Regrouping walks the slices and gives the
/// first bar of every slice to the first voice, the second to the second, and so on - which is why
/// a part that stops being written simply has fewer bars, rather than bars of rest.
/// </para>
/// <para>
/// THE OTHER THREE CONVENTIONS are handled here too: <c>&lt;n&gt;</c> is the model's newline,
/// <c>&lt;bos&gt;</c> and <c>&lt;eos&gt;</c> are its sequence markers, and it writes a numbered
/// ending DETACHED from the bar line it belongs to - "<c>:| 2</c>" where ABC wants "<c>:|2</c>".
/// </para>
/// <para>
/// WHAT IS DELIBERATELY DIFFERENT FROM THE PUBLISHER'S PYTHON, and why. It finds the head of the
/// tune at the FIRST <c>&lt;|&gt;</c>; this finds it at the end of the <c>K:</c> line, which is
/// what really closes an ABC header and is the only thing that works on text that is still being
/// written. It also has a special case for a trailing bar NUMBER (<c>%30</c>), which this model
/// does not write at this size; that case is not ported, and a slice with more bars than the first
/// slice had voices keeps only the bars there are voices for.
/// </para>
/// </remarks>
internal static class MuPTSmtAbc
{
    /// <summary>The model's newline.</summary>
    public const string NewLine = "<n>";

    /// <summary>What separates one time slice from the next.</summary>
    public const string SliceSeparator = "<|>";

    //was previously: SEPARATORS in the model card's post-processing. The order matters: the
    //single bar line is tried first, and " | " cannot match inside " || " or " |] ".
    private static readonly string[] Separators = { "|", "|]", "||", "[|", "|:", ":|", "::" };

    private static readonly Regex KeyLine = new Regex("^K:[^\n]*\n", RegexOptions.Multiline);

    // A numbered ending the model wrote away from its bar line: "| 1 ABC" or ":| 2 ABC".
    private static readonly Regex DetachedEnding =
        new Regex("(:\\||\\|)[ ]+([1-9])(?=[\"\\sA-Ga-gzx\\^_=\\(\\[])");

    private static readonly Regex RepeatedSpaces = new Regex(" {2,}");

    /// <summary>Whether the header has been closed by a <c>K:</c> line, which is what makes the
    /// music that follows readable at all.</summary>
    /// <param name="modelText">The model's text, as it stands.</param>
    /// <returns>True when a <c>K:</c> line has been written and ended.</returns>
    public static bool HeaderIsClosed(string modelText) => EndOfHeader(Decoded(modelText)) >= 0;

    /// <summary>
    /// The text up to and including the LAST slice separator: every slice that is complete, and
    /// nothing of the one still being written.
    /// </summary>
    /// <param name="modelText">The model's text, as it stands.</param>
    /// <returns>
    /// The prefix, or null when no slice has been completed yet. The separator itself is kept, so
    /// the prefix ends where a slice ends.
    /// </returns>
    public static string CompleteSlicePrefix(string modelText)
    {
        if (modelText == null)
        {
            return null;
        }

        var last = modelText.LastIndexOf(SliceSeparator, StringComparison.Ordinal);

        return last < 0 ? null : modelText.Substring(0, last + SliceSeparator.Length);
    }

    /// <summary>
    /// The BODY of the model's text from a slice boundary near its end: the last
    /// <paramref name="sliceCount"/> slices that hold anything, and everything after them.
    /// </summary>
    /// <param name="modelText">The model's text.</param>
    /// <param name="sliceCount">How many slices to keep. Fewer than one keeps none.</param>
    /// <returns>
    /// The tail, in the model's OWN merged form - which is what a continuation has to be prompted
    /// with, because that is the form the model was trained on. The header is not in it.
    /// </returns>
    /// <remarks>
    /// EMPTY SLICES ARE NOT COUNTED and are not dropped. The model writes a slice separator twice
    /// over between one bar and the next, so counting separators would count half the bars.
    /// </remarks>
    public static string LastSlices(string modelText, int sliceCount)
    {
        if (string.IsNullOrEmpty(modelText) || sliceCount < 1)
        {
            return string.Empty;
        }

        var body = modelText.Substring(EndOfHeaderIn(modelText));
        var pieces = body.Split(new[] { SliceSeparator }, StringSplitOptions.None);
        var kept = 0;
        var from = pieces.Length - 1;

        for (var i = pieces.Length - 1; i >= 0; i--)
        {
            from = i;

            if (!string.IsNullOrWhiteSpace(pieces[i]))
            {
                kept++;
            }

            if (kept >= sliceCount)
            {
                break;
            }
        }

        if (from < 0)
        {
            from = 0;
        }

        return string.Join(SliceSeparator, pieces, from, pieces.Length - from);
    }

    /// <summary>
    /// Turns the model's text into standard ABC notation - the whole of the publisher's
    /// post-processing.
    /// </summary>
    /// <param name="modelText">The model's text: the prompt and everything it wrote.</param>
    /// <param name="title">
    /// A title to write as a <c>T:</c> field after the <c>X:</c> line, or null for none. The model
    /// writes no title, and a player shows one.
    /// </param>
    /// <returns>Standard ABC notation, with one <c>V:</c> voice per part when there is more than one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="modelText"/> is null.</exception>
    public static string ToStandardAbc(string modelText, string title)
    {
        if (modelText == null)
        {
            throw new ArgumentNullException(nameof(modelText));
        }

        var piece = Decoded(modelText);
        var at = EndOfHeader(piece);

        var decoded = at < 0 || piece.IndexOf(SliceSeparator, StringComparison.Ordinal) < 0
            ? piece.Replace(SliceSeparator, " ")
            : Regroup(piece, at);

        decoded = DetachedEnding.Replace(decoded, "$1$2");
        decoded = RepeatedSpaces.Replace(decoded, " ");

        return Titled(decoded, title);
    }

    /// <summary>
    /// Whether any voice's music ends with a TIE THAT HAS NO PARTNER YET - the one thing that makes
    /// a prefix of this text produce an event the whole text would change.
    /// </summary>
    /// <param name="standardAbc">The standard ABC notation the regrouping produced.</param>
    /// <returns>True when at least one voice is left holding a tie.</returns>
    /// <remarks>
    /// The converter holds a tied note until the note it joins to arrives and, when the text ends
    /// first, plays it SHORT on its own - so <c>A2-|</c> at the end of a prefix is a short note
    /// that the next slice turns into one long one.
    /// </remarks>
    public static bool AVoiceIsHoldingATie(string standardAbc)
    {
        if (string.IsNullOrEmpty(standardAbc))
        {
            return false;
        }

        var lines = standardAbc.Split('\n');
        var lastOfTheVoice = string.Empty;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (IsAVoiceLine(line))
            {
                if (EndsWithAnOpenTie(lastOfTheVoice))
                {
                    return true;
                }

                lastOfTheVoice = string.Empty;

                continue;
            }

            if (line.Length > 0 && !IsAField(line))
            {
                lastOfTheVoice = line;
            }
        }

        return EndsWithAnOpenTie(lastOfTheVoice);
    }

    private static string Decoded(string modelText) =>
        modelText == null
            ? string.Empty
            : modelText.Replace(NewLine, "\n").Replace("<eos>", string.Empty)
                .Replace("<bos>", string.Empty);

    private static int EndOfHeader(string piece)
    {
        var key = KeyLine.Match(piece);

        return key.Success ? key.Index + key.Length : -1;
    }

    private static int EndOfHeaderIn(string modelText)
    {
        // The model's own text, where the newline is still <n>: the tail has to start after the
        // header rather than repeat it.
        var key = modelText.IndexOf("K:", StringComparison.Ordinal);

        if (key < 0)
        {
            return 0;
        }

        var line = modelText.IndexOf(NewLine, key, StringComparison.Ordinal);

        return line < 0 ? modelText.Length : line + NewLine.Length;
    }

    //was previously: decode() in the model card's post-processing.
    private static string Regroup(string piece, int endOfHeader)
    {
        var heads = piece.Substring(0, endOfHeader);
        var scores = piece.Substring(endOfHeader);
        var voices = new List<List<string>>();
        var slices = scores.Split(new[] { SliceSeparator }, StringSplitOptions.None);

        for (var s = 0; s < slices.Length; s++)
        {
            if (string.IsNullOrWhiteSpace(slices[s]))
            {
                continue;
            }

            // A line break inside the merged form is layout, not music.
            var slice = " " + slices[s].Replace("\n", " ").TrimEnd();
            var bars = SplitAtBarLines(slice);

            if (voices.Count == 0)
            {
                for (var b = 0; b < bars.Length; b++)
                {
                    voices.Add(new List<string>());
                }
            }

            for (var b = 0; b < bars.Length && b < voices.Count; b++)
            {
                voices[b].Add(bars[b]);
            }
        }

        DropEmptyVoices(voices);

        var text = new StringBuilder(heads.TrimEnd()).Append('\n');

        for (var v = 0; v < voices.Count; v++)
        {
            if (voices.Count > 1)
            {
                text.Append("V:").Append(v + 1).Append('\n');
            }

            text.Append(Joined(voices[v]).Trim()).Append('\n');
        }

        return text.ToString();
    }

    // The separators are swapped for markers, the slice is cut at the markers, and the markers are
    // swapped back - so a bar line stays attached to the bar it ends.
    private static string[] SplitAtBarLines(string slice)
    {
        var tokenized = slice;

        for (var i = 0; i < Separators.Length; i++)
        {
            tokenized = tokenized.Replace(" " + Separators[i] + " ", " <" + (i + 1) + "><=> ");
        }

        var bars = tokenized.Split(new[] { "<=>" }, StringSplitOptions.None);

        for (var b = 0; b < bars.Length; b++)
        {
            for (var i = 0; i < Separators.Length; i++)
            {
                bars[b] = bars[b].Replace(" <" + (i + 1) + ">", " " + Separators[i] + " ");
            }
        }

        return bars;
    }

    // A trailing column of empty bars is not a voice: every slice ends with its last bar line, and
    // what follows it is the empty remainder of the line.
    private static void DropEmptyVoices(List<List<string>> voices)
    {
        for (var v = voices.Count - 1; v >= 0; v--)
        {
            var anything = false;

            for (var b = 0; b < voices[v].Count && !anything; b++)
            {
                anything = !string.IsNullOrWhiteSpace(voices[v][b]);
            }

            if (!anything)
            {
                voices.RemoveAt(v);
            }
        }
    }

    private static string Joined(List<string> bars)
    {
        var text = new StringBuilder();

        for (var b = 0; b < bars.Count; b++)
        {
            text.Append(bars[b]);
        }

        return text.ToString();
    }

    private static string Titled(string decoded, string title)
    {
        var lines = new List<string>(decoded.Split('\n'));

        if (title != null)
        {
            var x = -1;

            for (var i = 0; i < lines.Count && x < 0; i++)
            {
                if (lines[i].StartsWith("X:", StringComparison.Ordinal))
                {
                    x = i;
                }
            }

            lines.Insert(x < 0 ? 0 : x + 1, "T:" + title);
        }

        return string.Join("\n", lines).TrimEnd() + "\n";
    }

    private static bool IsAVoiceLine(string line) =>
        line.StartsWith("V:", StringComparison.Ordinal);

    private static bool IsAField(string line) =>
        line.StartsWith("%", StringComparison.Ordinal) ||
        (line.Length > 1 && line[1] == ':' && char.IsLetter(line[0]));

    // A tie is the last thing written, and what may follow it is the bar line it sits before. A
    // chord's own closing bracket is not a bar line, and "]" after a note letter cannot be one
    // either, so stepping over "]" is safe: it only ever reaches a "-" that really is a tie.
    private static bool EndsWithAnOpenTie(string voiceLine)
    {
        for (var i = voiceLine.Length - 1; i >= 0; i--)
        {
            var character = voiceLine[i];

            if (char.IsWhiteSpace(character) || character == '|' || character == ':' ||
                character == '[' || character == ']' || character == '{' || character == '}')
            {
                continue;
            }

            if (character == '"')
            {
                // A chord symbol or an annotation, which may hold anything at all: step over it.
                var opening = voiceLine.LastIndexOf('"', i - 1 < 0 ? 0 : i - 1);

                if (opening < 0)
                {
                    return false;
                }

                i = opening;

                continue;
            }

            return character == '-';
        }

        return false;
    }
}
