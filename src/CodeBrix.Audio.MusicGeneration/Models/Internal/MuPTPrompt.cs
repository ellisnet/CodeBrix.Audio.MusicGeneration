using System;
using CodeBrix.Audio.MusicGeneration.Generation;

namespace CodeBrix.Audio.MusicGeneration.Models.Internal;

/// <summary>
/// What the model is actually given, and whether any of it has been heard already.
/// </summary>
/// <remarks>
/// <para>
/// THREE ROADS INTO ONE PROMPT. A request carrying MODEL-NATIVE TEXT is used verbatim - it is the
/// escape hatch for a caller who would rather write the ABC than describe it. A request carrying a
/// CONTINUATION is re-prompted with the original header and the last bars of the model's OWN text,
/// so the key, the metre and the idiom carry over. Anything else is a header built from the
/// musical intent.
/// </para>
/// <para>
/// A CONTINUATION'S PROMPT HAS BEEN HEARD and the music that follows it has not, which is the
/// difference that decides whether the prompt's own events are played again.
/// </para>
/// </remarks>
internal sealed class MuPTPrompt
{
    private MuPTPrompt(string text, string headerText, bool isAlreadyHeard)
    {
        Text = text;
        HeaderText = headerText;
        IsAlreadyHeard = isAlreadyHeard;
    }

    /// <summary>The prompt, in the model's own form.</summary>
    public string Text { get; }

    /// <summary>The header alone, remembered so that a later continuation is prompted with it.</summary>
    public string HeaderText { get; }

    /// <summary>Whether the prompt is music that has already been played.</summary>
    public bool IsAlreadyHeard { get; }

    /// <summary>Works out what to give the model for one request.</summary>
    /// <param name="request">The request.</param>
    /// <param name="options">The generator's own settings.</param>
    /// <param name="rememberedHeader">
    /// The header of the last piece this generator wrote, or null when it has written none.
    /// </param>
    /// <param name="rememberedText">
    /// Everything the last pass wrote, in the model's own form, or null when there is none. It is
    /// where a continuation's tail comes from: nothing else in this library holds the model's own
    /// text, because the engine carries music as MIDI events.
    /// </param>
    /// <returns>The prompt.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public static MuPTPrompt For(MusicRequest request, MuPTGeneratorOptions options,
        string rememberedHeader, string rememberedText)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var continuation = request.Continuation;

        // A CONTINUATION WINS OVER THE PIECE'S OWN OPENING, and getting that round the wrong way
        // is silent: a later segment re-prompted with the opening writes the piece AGAIN, in the
        // same key and metre, instead of carrying on from where the music got to.
        if (continuation != null)
        {
            return Continuing(request, options, continuation, rememberedHeader, rememberedText);
        }

        if (!string.IsNullOrWhiteSpace(request.ModelNativeText))
        {
            // VERBATIM, apart from the newline: a caller writes ABC with real line breaks and the
            // model reads its own newline, which is a spelling and not a change to the notation.
            var written = AsModelText(request.ModelNativeText);

            return new MuPTPrompt(written, HeadOf(written), false);
        }

        var fresh = MuPTHeader.For(request.Intent, null);

        return new MuPTPrompt(fresh.ToPrompt(), fresh.ToHeaderText(), false);
    }

    private static MuPTPrompt Continuing(MusicRequest request, MuPTGeneratorOptions options,
        MusicContinuation continuation, string rememberedHeader, string rememberedText)
    {
        var tail = TailFor(continuation, options, rememberedText);
        var header = HeaderFor(request, continuation, rememberedHeader);

        if (tail.Length == 0)
        {
            // Nothing of the model's own text survives - it was released, or the music so far came
            // from somewhere else - so the piece starts again from what the music has been doing.
            return new MuPTPrompt(header, header, false);
        }

        if (MuPTSmtAbc.HeaderIsClosed(tail))
        {
            // The caller handed over a whole tune of its own; it is the prompt, header and all.
            return new MuPTPrompt(tail, HeadOf(tail), true);
        }

        return new MuPTPrompt(header + tail, header, true);
    }

    private static string HeaderFor(MusicRequest request, MusicContinuation continuation,
        string rememberedHeader)
    {
        if (!string.IsNullOrEmpty(rememberedHeader))
        {
            return rememberedHeader;
        }

        // The piece's own opening is not played again, but the header it began with still says
        // what key, metre and tempo the music is in.
        var written = string.IsNullOrWhiteSpace(request.ModelNativeText)
            ? null
            : HeadOf(AsModelText(request.ModelNativeText));

        return written ?? MuPTHeader.For(request.Intent, continuation).ToHeaderText();
    }

    private static string TailFor(MusicContinuation continuation, MuPTGeneratorOptions options,
        string rememberedText)
    {
        if (!string.IsNullOrWhiteSpace(continuation.ModelNativeTail))
        {
            // A CALLER'S OWN TAIL WINS. The engine cannot set one - it carries music as MIDI
            // events and the model's text never leaves this adapter - so this is a consumer
            // driving the generator itself, and it knows what it wants continued.
            return AsModelText(continuation.ModelNativeTail);
        }

        return string.IsNullOrEmpty(rememberedText)
            ? string.Empty
            : OpenTheEnd(MuPTSmtAbc.LastSlices(rememberedText, options.MaximumPromptBars));
    }

    /// <summary>
    /// Turns a tail that ENDS THE TUNE - on a closing repeat (<c>:|</c>), a final bar line
    /// (<c>|]</c>) or a double bar (<c>||</c>) - into one that carries on, by making that last bar
    /// line a plain one.
    /// </summary>
    /// <param name="tail">The last slices of what the model wrote, in its own form.</param>
    /// <returns>The same tail, with its last bar line opened when it closed the tune.</returns>
    /// <remarks>
    /// MEASURED ON THE MODEL: a short tune that ends <c>... G4 :|</c> and is shown back to MuPT as
    /// the music so far is very often answered with NOTHING - the model reads a closed tune and
    /// ends it. Over twenty seeds, the tail exactly as written came back empty six times; with the
    /// closing repeat made a plain bar line it came back empty none. The music the tail stands for
    /// has been heard already and is not played again, so only the model's reading of it changes.
    /// A caller's own tail is never touched.
    /// </remarks>
    internal static string OpenTheEnd(string tail)
    {
        if (string.IsNullOrEmpty(tail))
        {
            return tail;
        }

        // The last bar line is followed only by separators, line breaks and spaces.
        var end = tail.Length;

        while (true)
        {
            var trimmed = tail.Substring(0, end).TrimEnd();

            if (trimmed.EndsWith(MuPTSmtAbc.SliceSeparator, StringComparison.Ordinal))
            {
                end = trimmed.Length - MuPTSmtAbc.SliceSeparator.Length;
                continue;
            }

            if (trimmed.EndsWith(MuPTSmtAbc.NewLine, StringComparison.Ordinal))
            {
                end = trimmed.Length - MuPTSmtAbc.NewLine.Length;
                continue;
            }

            end = trimmed.Length;
            break;
        }

        foreach (var closing in new[] { ":|]", ":|", "|]", "||" })
        {
            if (tail.Substring(0, end).EndsWith(closing, StringComparison.Ordinal))
            {
                return tail.Substring(0, end - closing.Length) + "|" + tail.Substring(end);
            }
        }

        return tail;
    }

    private static string AsModelText(string text) =>
        text.Replace("\r\n", "\n").Replace("\n", MuPTSmtAbc.NewLine);

    private static string HeadOf(string modelText)
    {
        var key = modelText.IndexOf("K:", StringComparison.Ordinal);

        if (key < 0)
        {
            return null;
        }

        var line = modelText.IndexOf(MuPTSmtAbc.NewLine, key, StringComparison.Ordinal);

        return line < 0 ? null : modelText.Substring(0, line + MuPTSmtAbc.NewLine.Length);
    }
}
