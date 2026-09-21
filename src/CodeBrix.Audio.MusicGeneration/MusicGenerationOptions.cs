using System;
using CodeBrix.Audio.MusicGeneration.Generation;
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

    private MusicRequest request = new MusicRequest();
    private int sampleRate = DefaultSampleRate;
    private TimeSpan preroll = DefaultPreroll;
    private TimeSpan generateAhead = DefaultGenerateAhead;
    private float masterVolume = DefaultMasterVolume;
    private int? inferenceThreadCount;

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
            ApplicationOwnsAudioOutput = ApplicationOwnsAudioOutput
        };
}
