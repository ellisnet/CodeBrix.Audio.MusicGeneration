using System;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Rendering;
using CodeBrix.Audio.MusicGeneration.Streaming;

namespace CodeBrix.Audio.MusicGeneration;

/// <summary>
/// Everything a <see cref="MusicSession"/> can be told. Every part of it is optional: an options
/// object with nothing set is the zero-configuration path, and is the same thing as a bare
/// <c>new MusicSession()</c>.
/// </summary>
/// <remarks>
/// <para>
/// IT IS A PLAIN OBJECT, built with an object initializer in a console application, and there is
/// no builder anywhere. Three strings do almost all of the work:
/// </para>
/// <code>
/// GeneralMidiInstrumentLibrary.Register();          // the consumer's job, always
///
/// using var music = new MusicSession(new MusicGenerationOptions
/// {
///     Generator         = "MyGenerator",            // omitted = the embedded replay
///     InstrumentLibrary = "FluidR3Gm",              // omitted = the default library
///     Rendition         = "AmbientDuet",            // omitted = voiced automatically
/// });
/// music.Play();
/// </code>
/// <para>
/// REGISTERING IS NOT SPECIFYING. Naming a generator here is the only way to play it; registering
/// it is not enough, and with nothing named the embedded replay plays however many models have
/// been registered.
/// </para>
/// </remarks>
public sealed class MusicGenerationOptions
{
    /// <summary>The rate the music is rendered at when the application owns its audio output.</summary>
    public const int DefaultSampleRate = 44100;

    /// <summary>The volume a session plays at unless it is told otherwise: unity.</summary>
    public const float DefaultMasterVolume = 1.0F;

    /// <summary>
    /// How much music is written ahead of the play head before playing starts, and before it
    /// starts again after running dry. It is about five seconds: a slightly later start buys
    /// longer uninterrupted stretches of music, and the application always comes before the music.
    /// </summary>
    public static readonly TimeSpan DefaultPreroll = StreamingDefaults.Preroll;

    /// <summary>
    /// How much settled music is generated ahead of the play head before generation pauses: half a
    /// minute. It is a STARTING VALUE that the model adapters' own measurements confirm or correct.
    /// </summary>
    public static readonly TimeSpan DefaultGenerateAhead = StreamingDefaults.GenerateAhead;

    /// <summary>
    /// The crossfade at a fresh seam unless the options say otherwise: NONE, so a fresh segment
    /// joins the music before it with a hard cut at a bar line, exactly as a primed one does.
    /// </summary>
    public static readonly TimeSpan DefaultSeamCrossfade = TimeSpan.Zero;

    /// <summary>
    /// The shape of a seam crossfade unless the options say otherwise: equal power, which keeps
    /// the loudness steady while two unrelated pieces overlap.
    /// </summary>
    public const MusicFadeCurve DefaultSeamCrossfadeCurve = MusicFadeCurve.EqualPower;

    /// <summary>
    /// How far, as a fraction, a fresh piece's opening tempo may be from the session tempo and
    /// still be played at its own tempo under <see cref="MusicGeneration.SessionTempoPolicy.CarryOutsideBand"/>:
    /// fifteen per cent either way.
    /// </summary>
    public const double DefaultTempoBand = 0.15;

    private MusicRequest request = new MusicRequest();
    private int sampleRate = DefaultSampleRate;
    private TimeSpan preroll = DefaultPreroll;
    private TimeSpan generateAhead = DefaultGenerateAhead;
    private float masterVolume = DefaultMasterVolume;
    private int? inferenceThreadCount;
    private TimeSpan seamCrossfade = DefaultSeamCrossfade;
    private double tempoBand = DefaultTempoBand;
    private double? sessionBeatsPerMinute;

    /// <summary>
    /// The name of the generator to play. Null, empty or blank means the embedded replay - always,
    /// however many generators have been registered.
    /// </summary>
    public string Generator { get; set; }

    /// <summary>
    /// The name of the instrument library to play the parts with. Null, empty or blank means the
    /// default library, which is the first one the application registered.
    /// </summary>
    public string InstrumentLibrary { get; set; }

    /// <summary>
    /// The name of the rendition to voice the music with. Null, empty or blank means the music is
    /// voiced automatically.
    /// </summary>
    public string Rendition { get; set; }

    /// <summary>
    /// What the music should be. It is never null: setting it to null puts a bare request back,
    /// which is the request the embedded replay accepts.
    /// </summary>
    public MusicRequest Request
    {
        get => request;
        set => request = value ?? new MusicRequest();
    }

    /// <summary>
    /// The rate the music is rendered at when <see cref="ApplicationOwnsAudioOutput"/> is true.
    /// When the session opens the audio device it uses THE DEVICE'S OWN RATE instead, because a
    /// synthesizer built at the wrong rate plays the whole piece transposed and stretched.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int SampleRate
    {
        get => sampleRate;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "The sample rate must be a positive value.");
            }

            sampleRate = value;
        }
    }

    /// <summary>How much music is written ahead of the play head before playing starts.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public TimeSpan Preroll
    {
        get => preroll;
        set
        {
            if (value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "The pre-roll must be a non-negative value.");
            }

            preroll = value;
        }
    }

    /// <summary>
    /// How much settled music to keep ahead of the play head. Generation STOPS when the lead
    /// reaches this and starts again when it falls to half of it, so generation runs in short
    /// bursts with idle time between them - and while it is paused it costs nothing at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// IT IS MEASURED IN SECONDS OF MUSIC, not events, ticks or tokens, because the play head moves
    /// in time and the tempo changes.
    /// </para>
    /// <para>
    /// A BIGGER WINDOW CANNOT RESCUE A DEVICE THAT GENERATES SLOWER THAN REAL TIME; it only
    /// postpones the gap, and it makes a follow-up prompt throw more away. What it does buy is
    /// cover for the pause at a seam.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public TimeSpan GenerateAhead
    {
        get => generateAhead;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "The generate-ahead window is a positive amount of music.");
            }

            generateAhead = value;
        }
    }

    /// <summary>
    /// What happens when the generator reaches the end of what it was writing. The default is
    /// <see cref="EndOfPiecePolicy.KeepGenerating"/>: the music carries on, which for the embedded
    /// replay means it loops.
    /// </summary>
    public EndOfPiecePolicy EndOfPiece { get; set; } = EndOfPiecePolicy.KeepGenerating;

    /// <summary>
    /// How each segment the engine asks for at the end of a pass starts: primed with the music so
    /// far, fresh, or taking turns. The default is <see cref="MusicGeneration.SegmentPriming.Primed"/>,
    /// which is what the engine has always done.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY IT IS HERE: a model that is strongly led by the bars it is shown writes the same
    /// material again after every primed seam, and changing the seed does not stop it - the primer
    /// dominates the sampling. A fresh segment (a new piece on a new derived seed, joined at a bar
    /// line with the instruments and the tempo carried across) is what really moves such music on,
    /// and <see cref="MusicGeneration.SegmentPriming.Alternate"/> keeps some continuity while it does.
    /// </para>
    /// <para>
    /// A generator that cannot be shown the music so far always starts its segments fresh,
    /// whatever this says. <see cref="MusicDiagnostics.GeneratingSegmentKind"/> says which kind of
    /// segment is being generated now.
    /// </para>
    /// </remarks>
    public SegmentPriming SegmentPriming { get; set; } = SegmentPriming.Primed;

    /// <summary>
    /// How long the outgoing piece and the incoming one OVERLAP at a FRESH seam, the first fading
    /// out while the second fades in. The default is <see cref="TimeSpan.Zero"/>: no crossfade, and
    /// a fresh segment joins with a hard cut at a bar line, exactly as it always has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ONLY FRESH SEAMS ARE CROSSFADED. A primed segment carries on from the music before it, and
    /// its hard join at a bar line already sounds right; a fresh segment is a different piece, and
    /// a crossfade is what turns the cut between two pieces into a hand-over. Nothing else is
    /// crossfaded either - a follow-up prompt still takes over at a bar line.
    /// </para>
    /// <para>
    /// HOW IT SOUNDS: the outgoing piece plays on to its own last bar line, and the incoming piece
    /// starts this long BEFORE that bar line, through a second set of instruments from the same
    /// instrument library (the two pieces use the same channels, so they cannot share one set).
    /// The two are mixed with <see cref="SeamCrossfadeCurve"/>, and when the fade is over the
    /// outgoing instruments are let go. The incoming piece keeps its own bar lines from the moment
    /// it starts, and the join is placed on a beat of the outgoing piece when at least a beat fits.
    /// If the incoming piece OPENS WITH SILENT BARS they are skipped, so the outgoing piece fades
    /// into music rather than into silence; the tempo, metre and programs in them still apply. A
    /// hard join plays those bars as written.
    /// </para>
    /// <para>
    /// IT NEVER COSTS THE MUSIC A GAP. The incoming piece is started early only when it has
    /// already generated the whole fade plus its own pre-roll; when it has not by the time the
    /// outgoing music has to be committed, the fade is SHORTENED to what is available - down to a
    /// hard join - and <see cref="MusicDiagnostics.ShortenedCrossfadeCount"/> counts it. An offline
    /// render has no play head to protect, so it waits for the whole fade instead, and a rendered
    /// file crossfades exactly as asked. While the engine is delivering a whole segment at a time
    /// (see <see cref="MusicDeliveryMode.SegmentAtATime"/>) fresh seams are hard joins.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public TimeSpan SeamCrossfade
    {
        get => seamCrossfade;
        set
        {
            if (value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "The seam crossfade is a non-negative length of time; zero means no crossfade.");
            }

            seamCrossfade = value;
        }
    }

    /// <summary>
    /// The shape a seam crossfade follows: the outgoing piece's gain falls along it while the
    /// incoming piece's rises along its mirror image. The default is
    /// <see cref="MusicFadeCurve.EqualPower"/>, which keeps the loudness steady while two unrelated
    /// pieces overlap; <see cref="MusicFadeCurve.StraightLine"/> keeps the SUM of the two gains at
    /// one instead.
    /// </summary>
    public MusicFadeCurve SeamCrossfadeCurve { get; set; } = DefaultSeamCrossfadeCurve;

    /// <summary>
    /// Whether fresh pieces play at the tempo they were written at or at the tempo the music has
    /// been keeping. The default is <see cref="MusicGeneration.SessionTempoPolicy.Adopt"/>: every
    /// piece keeps its own tempo, which is what the engine has always done.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY: a model picks a new tempo for every fresh piece, so music made of fresh segments
    /// lurches from one pulse to another at every seam.
    /// <see cref="MusicGeneration.SessionTempoPolicy.Carry"/> plays every fresh piece at the session
    /// tempo; <see cref="MusicGeneration.SessionTempoPolicy.CarryOutsideBand"/> lets a piece already
    /// close to it (see <see cref="TempoBand"/>) keep its own. A piece is judged by the tempo in
    /// force where its FIRST NOTE sounds, not by a default stated at its first tick.
    /// </para>
    /// <para>
    /// A CARRIED PIECE KEEPS ITS TICKS: its tempo events are dropped - every one of them, so it has
    /// one tempo - and the session tempo is stated where it starts, at the start of the crossfade
    /// or at the bar line of a hard join. So it is heard a little faster or slower than the model
    /// wrote it, and its bar lines are where they always were in ticks.
    /// </para>
    /// <para>
    /// THE SESSION TEMPO is <see cref="SessionBeatsPerMinute"/> when set, otherwise the request's
    /// intent tempo when set, otherwise the first piece's opening tempo. Given up front, it is
    /// also imposed on the first piece. <see cref="MusicDiagnostics.SessionTempo"/>,
    /// <see cref="MusicDiagnostics.CarriedTempoCount"/> and
    /// <see cref="MusicDiagnostics.AdoptedTempoCount"/> report what happened. Primed segments
    /// and follow-up prompts are never touched.
    /// </para>
    /// </remarks>
    public SessionTempoPolicy TempoPolicy { get; set; } = SessionTempoPolicy.Adopt;

    /// <summary>
    /// Under <see cref="MusicGeneration.SessionTempoPolicy.CarryOutsideBand"/>, how far - as a
    /// fraction of the session tempo, either way - a fresh piece's opening tempo may be and still
    /// be played at its own tempo. The default is <see cref="DefaultTempoBand"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not a finite number.</exception>
    public double TempoBand
    {
        get => tempoBand;
        set
        {
            if (!(value >= 0.0) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "The tempo band is a non-negative fraction of the session tempo.");
            }

            tempoBand = value;
        }
    }

    /// <summary>
    /// The session tempo, in beats per minute, given up front - or null, the default, for the
    /// intent's tempo or the first piece's.
    /// </summary>
    /// <remarks>
    /// IT IS NEVER SENT TO THE GENERATOR, so it works with ANY generator. The request's own
    /// <see cref="Generation.MusicIntent.BeatsPerMinute"/> is a request to the generator, and a
    /// generator that does not honour a tempo refuses it by name when the music starts - that is
    /// unchanged. To give such a generator's music a fixed tempo, set this instead: with
    /// <see cref="TempoPolicy"/> other than Adopt the engine states it at the start and drops
    /// the model's own tempo events. Under Adopt it has no effect.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is set and is not a positive, finite number.</exception>
    public double? SessionBeatsPerMinute
    {
        get => sessionBeatsPerMinute;
        set
        {
            if (value.HasValue && (!(value.Value > 0.0) || double.IsInfinity(value.Value)))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "A session tempo is a positive number of beats per minute, or null.");
            }

            sessionBeatsPerMinute = value;
        }
    }

    /// <summary>
    /// How many threads the generator may use for inference, or null - the default - for the
    /// generator's own conservative choice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// GENERATION COMPETES WITH THE APPLICATION FOR THE MACHINE. Inference threads, the synthesizer
    /// and a game loop share the same cores, so the default is deliberately modest: a few seconds of
    /// silence while music buffers is acceptable, and a game that stutters is not.
    /// </para>
    /// <para>
    /// When it is set it is written into <see cref="Request"/> before the generator is asked, so a
    /// generator that cannot honour a thread count refuses the request BY NAME rather than quietly
    /// using as many as it likes. A generator with no model behind it has nothing to honour.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is set and is not positive.</exception>
    public int? InferenceThreadCount
    {
        get => inferenceThreadCount;
        set
        {
            if (value.HasValue && value.Value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "A thread count is a positive number of threads, or null for the generator's own default.");
            }

            inferenceThreadCount = value;
        }
    }

    /// <summary>
    /// The volume the whole arrangement plays at, where 1 is unity. It multiplies the rendition's
    /// own master gain rather than replacing it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not a finite number.</exception>
    public float MasterVolume
    {
        get => masterVolume;
        set
        {
            if (!(value >= 0.0F) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "The volume is a non-negative, finite multiple of unity.");
            }

            masterVolume = value;
        }
    }

    /// <summary>
    /// Whether the APPLICATION owns its audio output. False - the default - means the session
    /// opens the audio device and plays the music itself. True means it opens no device at all and
    /// hands back <see cref="MusicSession.Renderer"/> for the application's own mixer to pull from.
    /// </summary>
    public bool ApplicationOwnsAudioOutput { get; set; }

    /// <summary>
    /// Copies the options and the request in them, so that a session cannot be changed underneath
    /// itself by a later edit to the object it was built from.
    /// </summary>
    /// <returns>The copy.</returns>
    public MusicGenerationOptions Clone() =>
        new MusicGenerationOptions
        {
            Generator = Generator,
            InstrumentLibrary = InstrumentLibrary,
            Rendition = Rendition,
            request = request.Clone(),
            sampleRate = sampleRate,
            preroll = preroll,
            generateAhead = generateAhead,
            masterVolume = masterVolume,
            inferenceThreadCount = inferenceThreadCount,
            EndOfPiece = EndOfPiece,
            SegmentPriming = SegmentPriming,
            seamCrossfade = seamCrossfade,
            SeamCrossfadeCurve = SeamCrossfadeCurve,
            TempoPolicy = TempoPolicy,
            tempoBand = tempoBand,
            sessionBeatsPerMinute = sessionBeatsPerMinute,
            ApplicationOwnsAudioOutput = ApplicationOwnsAudioOutput
        };
}
