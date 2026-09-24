================================================================================
AGENT-README: CodeBrix.Audio.MusicGeneration
A Guide for AI Coding Agents - CONSUMING the
CodeBrix.Audio.MusicGeneration.MitLicenseForever NuGet package
================================================================================

OVERVIEW
================================================================================
CodeBrix.Audio.MusicGeneration produces music for a .NET application, as MIDI
events that arrive WHILE THE MUSIC IS STILL BEING WRITTEN. The application plays
them as they appear, renders them to an audio file, or both; it never waits for
a finished file.

A GENERATOR is "a thing that produces music". It is deliberately not "a model
file": a generator may be backed by no model at all, by one, or by two. Four
generators are BUILT IN and replay pieces of music that ship inside the package,
so an application makes music with no model, no download and no configuration at
all. TWO MODEL ADAPTERS ARE ALSO BUILT IN - one for a model that writes MIDI
events and one for a model that writes ABC NOTATION - and each is pointed at
model files the application supplies; the adapters are code, and no model is in
this package. A generator of your own - over your own music, or over anything
that can write MIDI - is a class you register.

THE ENTRY POINT IS PLAIN. MusicSession plays music; MusicGenerationOptions says
what to play, what to play it with and how. There is no builder and no host to
configure - a console application constructs both directly, and two lines get
music:

    GeneralMidiInstrumentLibrary.Register();      // your decision, always
    using var music = new MusicSession();
    music.Play();

REGISTERING AN INSTRUMENT LIBRARY IS THE ONE THING YOU MUST DO. This library
registers none - not at start-up and not as a fallback - because which
instruments an application uses is the application's decision. You already have
the General MIDI library through the dependencies below, so unless you want a
different one, that first line is the whole of it. See "INSTALLATION".

WHAT THOSE TWO LINES PLAY is a piece of MIDI embedded in this package, voiced
automatically. It is a RECORDING of what a model once wrote - not a model - and
it plays the same piece every time. music.ActiveSource says so outright, so no
application ever ships believing a model is playing when it is not. See "THE
MUSIC IN THE PACKAGE, AND THE THREE NEXT STEPS".

THE MUSIC DOES NOT STOP. When a generator reaches the end of what it was
writing, the same generator is asked to carry on and the next segment is placed
at the next BAR LINE; for the embedded replays that means they loop. Ask for
something different while it is playing with FollowUp(), and it takes over at a
safe bar line after the new pre-roll is ready. See "THE LIFE OF A STREAM".

OR RENDER IT TO A FILE INSTEAD - the second first-class way to use this package,
and the same music either way:

    var result = await music.RenderToFileAsync("theme.wav",
        new MusicRenderOptions { TargetLength = TimeSpan.FromMinutes(6.0) },
        cancellationToken);

No audio device, nothing paced, as fast as the machine allows, to any format a
writer is registered for - the file's extension chooses it - and optionally to
an EXACT length with a fade, a hard cut or the generator's own ending. See
"RENDERING TO A FILE".

THE APPLICATION COMES FIRST AND THE MUSIC WAITS. Silence while music buffers is
an accepted outcome and never a failure. Generation runs in short bursts and
stops when it is far enough ahead, so it costs nothing between them; on a
machine that cannot compose as fast as it plays, the music comes out a phrase at
a time with rests between, which sounds intentional, instead of stalling
mid-phrase, which sounds broken. music.Diagnostics says which it is doing.

Target framework: .NET 10. The application runtime requires no Python or
ModelManager. MuPT uses ModelRunner's bundled native inference engine; the
SkyTNT and MuseCoco ONNX adapters use its managed runtime.


INSTALLATION
================================================================================
    dotnet add package CodeBrix.Audio.MusicGeneration.MitLicenseForever

  PackageId   CodeBrix.Audio.MusicGeneration.MitLicenseForever
  Assembly    CodeBrix.Audio.MusicGeneration
  Licence     MIT
  Requires    .NET 10 or later

Dependencies, by id and nothing else:

    CodeBrix.Audio                  MIDI, ABC, synthesis, playback and rendering
    CodeBrix.Audio.ModestSynth      the General MIDI instrument library
    CodeBrix.Ollama.ModelRunner     the engine a model-backed generator runs on

THE ModestSynth DEPENDENCY IS DELIBERATE even though this library's own code
never uses it: one package reference has to bring you everything needed to make
a sound, and ModestSynth is where the instruments are.

TO HEAR ANYTHING, REGISTER AN INSTRUMENT LIBRARY. The line to write, unless you
want a different library, is:

    using CodeBrix.Audio.ModestSynth;

    GeneralMidiInstrumentLibrary.Register();      // registers as "ModestSynthGm"

That gives the whole General MIDI sound set - every program and the percussion
kit, synthesized - with no SoundFont to find and nothing to download. Play or
render with NOTHING registered and the call throws, with this message, word for
word:

    No instrument library is registered, so no music can be played or rendered.
    Register the provided ModestSynth.GeneralMidiInstrumentLibrary - call
    GeneralMidiInstrumentLibrary.Register() - or register another instrument
    library first.

It names that call because you already have that package. Registering is
idempotent, so calling it on every start-up path is safe. Other libraries, and
the rule for which one is the DEFAULT, are in "INSTRUMENT LIBRARIES".


KEY NAMESPACES / USINGS
================================================================================
    using CodeBrix.Audio.MusicGeneration;             // MusicSession,
                                                      // MusicGenerationOptions,
                                                      // MusicDiagnostics,
                                                      // ActiveMusicSource,
                                                      // EndOfPiecePolicy,
                                                      // MusicDeliveryMode,
                                                      // SegmentPriming,
                                                      // MusicSegmentKind,
                                                      // IMusicGenerator,
                                                      // MusicGeneratorRegistry,
                                                      // MusicGenerationException
    using CodeBrix.Audio.MusicGeneration.Generation;  // MusicRequest and what it
                                                      // is made of - MusicIntent,
                                                      // MusicCharacterWords;
                                                      // GeneratedMusicEvent
    using CodeBrix.Audio.MusicGeneration.Replay;      // ReplayMusicGenerator,
                                                      // EmbeddedReplay
    using CodeBrix.Audio.MusicGeneration.Rendition;   // MusicRendition,
                                                      // RenditionVoice,
                                                      // MusicRenditionRegistry,
                                                      // BuiltInRenditions
    using CodeBrix.Audio.MusicGeneration.Rendering;   // MusicRenderOptions,
                                                      // RenderEnding, MusicFade,
                                                      // MusicRenderResult
    using CodeBrix.Audio.MusicGeneration.Models;      // MuPTMusicGenerator,
                                                      // SkyTNTMusicGenerator and
                                                      // their options
    using CodeBrix.Audio.MusicGeneration.Presets;     // MuPTPresets,
                                                      // SkyTNTPresets,
                                                      // MusicPreset

    using CodeBrix.Audio.ModestSynth;                 // GeneralMidiInstrumentLibrary
    using CodeBrix.Audio.Midi;                        // the MIDI events themselves
    using CodeBrix.Audio.Instruments;                 // InstrumentLibraryRegistry

The MIDI events are CodeBrix.Audio's own, from CodeBrix.Audio.Midi - so nothing
has to be translated to play them, save them or look at them.


THE THREE LAYERS
================================================================================
Each layer is a superset of the one before it. EVERY ONE OF THEM STARTS BY
REGISTERING AN INSTRUMENT LIBRARY, because nothing sounds until one is there.

--- LAYER 1: one registration line, and there is music --------------------------
    using CodeBrix.Audio.ModestSynth;
    using CodeBrix.Audio.MusicGeneration;

    GeneralMidiInstrumentLibrary.Register();      // unless you want another one

    using var music = new MusicSession();         // nothing specified
    music.Play();

    Console.WriteLine("Playing. Press Enter to stop.");
    Console.ReadLine();                           // the music plays meanwhile

No model, no instrument file, no configuration: the embedded MIDI replay, the
General MIDI instruments, and the music voiced automatically. Play() opens the
audio device itself.

--- LAYER 2: named choices ------------------------------------------------------
    using CodeBrix.Audio.ModestSynth;
    using CodeBrix.Audio.MusicGeneration;
    using CodeBrix.Audio.MusicGeneration.Rendition;
    using CodeBrix.Audio.MusicGeneration.Replay;

    GeneralMidiInstrumentLibrary.Register();      // "ModestSynthGm"

    using var music = new MusicSession(new MusicGenerationOptions
    {
        Generator         = EmbeddedReplay.Abc,   // or a name YOU registered
        InstrumentLibrary = "ModestSynthGm",      // omitted = the default one
        Rendition         = BuiltInRenditions.AmbientDuet,
    });
    music.Play();

    Console.WriteLine(music.ActiveSource);
    // EmbeddedReplayAbc (Replay) through ModestSynthGm, voiced by AmbientDuet

Three strings do almost all of the work, and every one of them is optional. A
generator that is registered but NOT named does not play: with nothing named the
embedded replay does.

--- LAYER 3: everything ---------------------------------------------------------
    using CodeBrix.Audio.Midi;
    using CodeBrix.Audio.ModestSynth;
    using CodeBrix.Audio.MusicGeneration;
    using CodeBrix.Audio.MusicGeneration.Generation;
    using CodeBrix.Audio.MusicGeneration.Rendering;
    using CodeBrix.Audio.MusicGeneration.Rendition;
    using CodeBrix.Audio.MusicGeneration.Replay;

    GeneralMidiInstrumentLibrary.Register();               // "ModestSynthGm"

    // A generator of your own: your music, under your own name.
    MusicGeneratorRegistry.Register(
        ReplayMusicGenerator.FromMidiFile("MyTheme", "theme.mid"));

    // A voicing of your own: one voice per part, each with its own level, and a
    // pad layered under the part that carries the tune.
    var voicing = new MusicRendition("MyGame", "The voicing my game ships with");
    voicing.Voices.Add(new RenditionVoice(GeneralMidiProgram.Celesta, 0.85F,
                                          GeneralMidiProgram.Pad2Warm, 0.5F));
    voicing.Voices.Add(new RenditionVoice(GeneralMidiProgram.ChoirAahs, 0.55F));
    voicing.PercussionGain = 0.7F;
    voicing.Register();

    using var music = new MusicSession(new MusicGenerationOptions
    {
        Generator         = "MyTheme",
        InstrumentLibrary = "ModestSynthGm",
        Rendition         = "MyGame",
        MasterVolume      = 0.9F,
        Preroll           = TimeSpan.FromSeconds(3.0),
        GenerateAhead     = TimeSpan.FromSeconds(20.0),
        EndOfPiece        = EndOfPiecePolicy.KeepGenerating,
    });

    // The same music, to an audio file of an exact length with a fade of your
    // own - no device, and as fast as the machine allows.
    var result = await music.RenderToFileAsync("theme.wav",
        new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromMinutes(3.0),
            Ending       = RenderEnding.Fade,
            FadeCurve    = MusicFadeCurve.EqualPower,
            FadeLength   = TimeSpan.FromSeconds(8.0),
        },
        cancellationToken);

    MidiFile.Export("theme.mid", result.Music);            // the MIDI of it, too

    foreach (var part in result.Source.Voicing.Parts)
    {
        Console.WriteLine(part);       // channel, instrument, gain, and why
    }

Beyond this there is a request that says what the music should BE - a key, a
metre, a tempo, a number of parts, a drum kit, a seed - and a MODEL that reads
it: both model adapters are in this package, pointed at files you supply. See
"ASKING FOR MUSIC IN MUSICAL TERMS", "THE PRESETS" and, for a generator of your
own, "WRITING YOUR OWN GENERATOR". A request part that the chosen generator does
not honour is REFUSED BY NAME rather than ignored, and the replay generators
honour a continuation and nothing else.


THE MUSIC IN THE PACKAGE, AND THE THREE NEXT STEPS
================================================================================
THIS IS WHAT PLAYS WHEN NOTHING IS SPECIFIED, and it is worth being plain about
what it is. Four pieces of music ship inside the assembly and four REPLAY
generators play them. They are recordings of what music models once wrote,
generated for this package and stored as a few kilobytes of notes each. They are
not models: they play the same piece every time, and the longest of them is a
couple of minutes that then loops.

    EmbeddedReplay.Midi        the default - the piece that plays when no
                               generator has been named
    EmbeddedReplay.MidiSecond  a second, sparser piece
    EmbeddedReplay.Abc         a two-voice waltz written in ABC notation
    EmbeddedReplay.AbcSecond   a slow single-voice air

They exist so that the plumbing can be proved - playback, voicing, the streaming
lifecycle, rendering to a file - without a model, a download or any
configuration. THE ACTIVE SOURCE IS ALWAYS REPORTABLE, so a demo loop can never
masquerade as a model:

    Console.WriteLine(music.ActiveSource);            // one line for a log
    if (music.ActiveSource.IsReplay) { /* not a model */ }

THE THREE NEXT STEPS, once the placeholder has served its purpose:

  1. NAME A REAL GENERATOR - AND THIS PACKAGE ALREADY CARRIES TWO. The MuPT and
     SkyTNT adapters are in this assembly. Point one at the model files on
     disk, register it, and then NAME it. Registering is not specifying: until
     the name is asked for, the embedded replay plays, however many generators
     are registered.

         var mupt = new MuPTMusicGenerator("MuPT", "/models/MuPT-190M.gguf");
         MusicGeneratorRegistry.Register(mupt);               // loads nothing

         GeneralMidiInstrumentLibrary.Register();
         using var music = new MusicSession(new MusicGenerationOptions
         {
             Generator = "MuPT",                              // now it plays
             Rendition = "AmbientDuet",
         });
         music.Play();

     Your own generator registers in exactly the same way - see "WRITING YOUR
     OWN GENERATOR".

  2. BRING YOUR OWN INSTRUMENT LIBRARY. "ModestSynthGm" is synthesized and
     costs nothing to ship. A SoundFont of your own, a package of recorded
     instruments, or one voice at a time swapped out of either is one line -
     see "INSTRUMENT LIBRARIES".

  3. WRITE YOUR OWN PROMPT - OR START FROM A PRESET. MusicGenerationOptions
     .Request is where an application says what the music should be: plain
     words, notation the generator reads itself, a MIDI primer, musical intent,
     instrument hints, a drum kit, a seed, generation controls. A replay does
     not quietly ignore any of that - it REFUSES what it cannot honour, by name
     - so a request and a generator that reads one go together.

         options.Request = MuPTPresets.WaltzDuetInAMinor.CreateRequest();

     See "ASKING FOR MUSIC IN MUSICAL TERMS" and "THE PRESETS".

WHERE A MODEL COMES FROM, AND WHY THERE IS NO MODEL IN THIS PACKAGE
  AN ADAPTER IS CODE; A MODEL IS FILES. The two adapters here are code, they
  bundle nothing, and every one of them takes A PATH YOU SUPPLY - a file for
  MuPT, a folder or a map of file names for SkyTNT. That is the first-class
  route, not a fallback: get the files however you like - downloaded once at
  install time, shipped beside your application, or kept in a store of your own
  - and hand over the path.

  IF YOU ALREADY KEEP MODELS IN A CodeBrix.Ollama.ModelManager STORE, it is one
  call: ask the store to MATERIALIZE the model into a directory you name -
  MaterializeAsync gives you real files on disk and hands back their paths - and
  then pass that directory to the SkyTNT adapter, or the one GGUF path to the
  MuPT adapter. A store that keeps its files under digests rather than under
  their real names is exactly why the SkyTNT adapter also takes a MAP of file
  names against paths. THIS PACKAGE DOES NOT REFERENCE ModelManager and never
  will: the application that wants a store references it and passes paths in.

  A GENERATOR MAY ALSO ARRIVE AS ITS OWN PACKAGE, and that is the same
  mechanism: such a package exposes a static Register() entry point, the call
  adds it to MusicGeneratorRegistry under a name, and the application then
  names it. The contract already carries everything a model-backed generator
  needs - loading that is separate from registering, an inference thread count,
  a seed, sampling controls, and a continuation so that one segment follows
  another.


THE SkyTNT MODEL ADAPTER
================================================================================
SkyTNTMusicGenerator plays the SkyTNT MIDI model, which writes MIDI events
directly. It runs on the managed road inside CodeBrix.Ollama.ModelRunner: no
Python, no runtime to install, no native library, and nothing downloaded.

    var skytnt = new SkyTNTMusicGenerator("SkyTNT", "/models/skytnt-int4");
    MusicGeneratorRegistry.Register(skytnt);          // loads nothing

    using var music = new MusicSession(new MusicGenerationOptions
    {
        Generator = "SkyTNT",                         // registering is not specifying
    });
    music.Play();

WHERE THE MODEL COMES FROM IS YOURS. A bundle is three files - config.json,
model_base.onnx and model_token.onnx - and the generator takes either the FOLDER
they are laid out in or a MAP of those names against the paths they are really
at. The map is a first-class route, not a fallback: a store that keeps its files
under digests has no folder to hand over, only paths - see "WHERE A MODEL COMES
FROM". A folder that holds nothing runnable is an error that says what IS in it
and what is wanted.

    new SkyTNTMusicGenerator("SkyTNT", bundleDirectory);
    new SkyTNTMusicGenerator("SkyTNT", new Dictionary<string, string>
    {
        ["config.json"] = ..., ["model_base.onnx"] = ..., ["model_token.onnx"] = ...,
    });

SkyTNTGeneratorOptions is what is settled when the model LOADS, and what it
writes with when a request says nothing:

    InferenceThreadCount    how many threads the arithmetic is spread over. It
                            is a LOAD-time setting, because a loaded graph
                            cannot change it - so a request naming a different
                            count RELOADS the model rather than ignoring it.
                            The default is A SHARE OF THE MACHINE: a quarter of
                            the processors it reports, at least one and never
                            more than four.
    MaximumEventsPerPass    how many events one pass writes; a thousand by
                            default. Longer passes mean fewer seams, and this
                            model slows down as its context grows, so it is a
                            trade - see the measurements below.
    MaximumPromptBars       the most bars of the music so far handed to the
                            model as its prompt at a seam; four, which is a
                            phrase, and the same as the tail the session sends.
    DrumKit                 the DEFAULT program the percussion channel takes,
                            or null for no percussion. A request overrides it
                            through MusicIntent.DrumKit, and music already
                            under way overrides it through the continuation's
                            own program for channel 10.
    AllowControlChange      whether the model may write pedals, volume and pan.
                            IT IS OFF BY DEFAULT, and that is a measurement
                            rather than a preference: with controller events
                            allowed this model can spend a WHOLE PASS writing
                            them at tick 0 and write no notes at all. One
                            measured pass produced 250 events inside the first
                            bar, every one a control change; the same request
                            with this off produced 219 events over twelve
                            seconds of music. Switch it on for a piece that
                            wants the expression.

WHAT IT HONOURS: instrument hints, a DRUM KIT, tempo, metre, key and mode,
CHARACTER WORDS, a seed, the sampling settings, a length cap, an inference
thread count, a MIDI primer and a continuation. WHAT IT REFUSES BY NAME: free
text and model-native text (it reads music, not words), a unit note length (an
ABC idea), a voice count - the instruments decide the parts - a target length,
a character word it cannot turn into a tempo or a mode, and a REPETITION
PENALTY, because this model applies none at all and music repeats on purpose.

PERFORMANCE DEPENDS ON THE PASS, PROMPT AND RUNTIME. The original four-thread
laptop measurements are historical baselines: opening 250-event SkyTNT passes
reached 3.2-3.7x real time, but dense 1,000-event passes over a six-minute piece
averaged 0.22x. A 60-event experiment was faster but later listening found weak
joins and long silence for some prompts. The accepted presets therefore retain
the 1,000-event default. A short fast opening does not prove sustained playback.
Render ahead when the chosen model, prompt and deployment machine cannot keep
pace. A session can fall back to complete segments separated by rests.

MODEL SIZE IS NOT RUNTIME MEMORY. The reduced SkyTNT graphs total about 145 MiB;
that does not include inference state, temporary buffers, the audio engine or
other process memory. Earlier warm-load measurements around 310 MiB describe
one short case. Later managed-runtime measurements reached several GiB; do not
use either the package size or that early measurement as a memory budget.
Measure representative long requests on the runtime and hardware you deploy.
The conservative default uses a quarter of available processors, capped at four.

THE ELECTRONICA PRESETS are written for this model - see "THE PRESETS". All
three were accepted after listening to two seeds each on 2026-09-21.

A PASS IS A WHOLE NUMBER OF BARS. The model stops where its event cap lands,
usually part-way through a bar; the ragged last bar is held back rather than
played, and the pass's final settled tick is the bar line - so the segment after
it starts against a bar line and no seam falls mid-bar. It costs one bar of
latency and the events of one bar per pass.

A CONTINUATION IS THE GOOD PATH, not a fallback. The tail of the music so far
goes in as the model's PROMPT, its own events are not played again, and the new
music is written from the tail's END. The best-sounding piece of the listening
session this library's taste decisions rest on was a continuation.

ONE GENERATION AT A TIME. This model's graphs run one step at a time, so a
second generation asked for while one is running is refused with a message that
says so. A FOLLOW-UP PROMPT over this generator is not that: the session cancels
the generation that is running and waits for it to end before it asks for the
new music, and everything already settled - up to the generate-ahead window -
plays on through the change. Only a caller driving the generator itself has to
finish one generation before it asks for another.


THE MuseCoco MODEL ADAPTER
================================================================================
MuseCocoMusicGenerator accepts a caller-staged MuseCoco music bundle. ModelRunner
streams MIDI events before the complete piece exists. A separate, optional text
bundle converts natural-language prompts to musical attributes. Neither staging,
ModelManager nor Python runs in the consuming application.

    using CodeBrix.Audio.ModestSynth;
    using CodeBrix.Audio.MusicGeneration;
    using CodeBrix.Audio.MusicGeneration.Generation;
    using CodeBrix.Audio.MusicGeneration.Models;

    GeneralMidiInstrumentLibrary.Register();
    var muse = new MuseCocoMusicGenerator("MuseCoco", "/models/music-int4",
        new MuseCocoGeneratorOptions
        {
            TextBundleDirectory = "/models/text-int8" // omit for attributes only
        });
    MusicGeneratorRegistry.Register(muse);           // no model load
    await muse.PreloadAsync(cancellationToken);      // music only
    var request = new MusicRequest
    {
        Seed = 20260921,
        Controls = new MusicGenerationControls { Temperature = 1, TopK = 15, TopP = 1 }
    };
    request.ModelAttributes["instrument.piano"] = "present";
    request.ModelAttributes["tempo"] = "moderate";
    // Optional: request.Text = "A calm piece led by piano";
    using var session = new MusicSession(new MusicGenerationOptions
    {
        Generator = muse.Name,
        InstrumentLibrary = "ModestSynthGm",
        Request = request,
        EndOfPiece = EndOfPiecePolicy.Stop
    });
    session.Play();

Keep the session alive while listening. Stop/dispose it and await any direct
GenerateAsync enumeration's disposal before Release or Dispose on the generator.
A session does not own the registered model's lifetime. Release unloads both
models and allows reuse; Dispose permanently closes the generator. Concurrent
inference, preload, release or disposal during inference is refused.

Use Schema after preloading to inspect the music bundle's attribute names and
allowed values. ModelAttributes is a mutable string dictionary on MusicRequest;
requests are snapshotted for generation. Explicit entries override text-model
predictions. Unknown names or values fail. The optional text model loads only
when Text is used and must have a compatible schema. IsTextModelLoaded reports
that separate load. Supplying text without a text bundle is refused by name.

The adapter honours ModelAttributes, Seed, SamplingControls, MaximumEvents and
InferenceThreadCount, plus FreeText when a text bundle was supplied. MaximumEvents
counts REMIGEN2 TOKENS for this model, not emitted MIDI notes. Generic MusicIntent,
model-native text, MIDI primers, program hints and MusicRequest.Continuation are
refused. Request instrument.piano or another model attribute to condition the
music, then use a rendition and Audio's program substitutions to voice it.
Non-default repetition penalties, zero TopK and negative seeds are refused.
MusicGeneration's ordinary sampling defaults still apply; the example above
explicitly chooses the runner settings used in the real-model acceptance checks.

Construct from a directory, or use the overload accepting IReadOnlyDictionary
maps for music and optional text bundles. Maps include musecoco.json and every
referenced graph, vocabulary and external-data file. Paths are captured as
absolute paths. A text map and TextBundleDirectory cannot both be supplied.
Schema is null before loading; LoadedThreadCount is null after release. Changing
InferenceThreadCount for a later request reloads the models. Model files remain
external assets owned and deployed by the application.

MuseCocoGeneratorOptions:
  InferenceThreadCount       conservative shared-machine default
  MaximumTokensPerPass       2,560 new tokens; request MaximumEvents overrides it
  MinimumTokensPerPass       512; clamped to the effective maximum, zero allows EOS
  TextBundleDirectory        optional text classifier, loaded only when used
  Clone()                    independent copy; construction snapshots the options

The adapter converts the runner's 480 PPQ and zero-based channels to the request's
resolution and Audio's one-based channels. Its exclusive horizon becomes an
inclusive settled tick, including at coarse tick resolutions. Normal completion
settles the last note's containing bar; cancellation does not invent a completed
tail. MidiStream.AdvanceHorizon carries settled silence without synthetic events.
Playback starts after pre-roll, subject to generation speed. Small model files
do not bound inference memory or guarantee real-time generation.

EXPERIMENTAL MuseCoco CONTINUATION
---------------------------------
To retain recent MuseCoco bars across successive sections WITHIN one request:

    var continued = new MuseCocoMusicGenerator("MuseCocoContinued", "/models/music-int4",
        new MuseCocoGeneratorOptions
        {
            ExperimentalContinuation = true,
            ExperimentalContextBars = 4,
            ExperimentalSectionTokens = 512,
            MaximumTokensPerPass = 4096,
            MinimumTokensPerPass = 0
        });
    MusicGeneratorRegistry.Register(continued);
    // Select continued.Name in MusicGenerationOptions, with the attributes above.

ExperimentalContinuation defaults to false. ContextBars accepts 1..16; four is
the default. SectionTokens defaults to 512 and limits NEW tokens per section.
The total request cap still applies and may exceed model position capacity:
each section replays bounded recent context and emits only new events, with
continuous timestamps and stable channels. The adapter settles each completed
section and continues pulling the next while buffered music plays. Insufficient
position capacity is an error; use fewer context bars in that case.

A new GenerateAsync request, session pass or FollowUp starts fresh. The context
is local to the enumeration; cancellation/early disposal discards it safely.
This option does not enable arbitrary MusicRequest.Continuation or retain context
between separate requests. Choose a larger total token cap to request a longer
continued piece. MinimumTokensPerPass=0 allows natural section endings, and an
empty/no-progress section ends the request. Seeded sections use successive seeds.
This path is experimental: ordered events and playback timing are validated,
while long-form musical quality still needs listening for each application.
Recent-context replay costs inference time. A real packaged-consumer check with
192-token sections completed with ordered audio but entered segment fallback and
had a buffering rest even with the default pre-roll. This path does not promise
gapless playback; monitor Diagnostics and render ahead where continuity is essential.


THE MuPT MODEL ADAPTER
================================================================================
MuPTMusicGenerator plays the MuPT model, which writes ABC NOTATION rather than
events. It runs on the NATIVE road inside CodeBrix.Ollama.ModelRunner, from ONE
file - the tokenizer is inside the GGUF - and needs nothing installed:

    var mupt = new MuPTMusicGenerator("MuPT", "/models/MuPT-190M-Q4_K_M.gguf");
    MusicGeneratorRegistry.Register(mupt);            // loads nothing

    using var music = new MusicSession(new MusicGenerationOptions
    {
        Generator = "MuPT",                           // registering is not specifying
        Rendition = "AmbientDuet",
    });
    music.Play();

    MuPTGeneratorOptions
      InferenceThreadCount          fixed when the model LOADS. The default is
                                    A SHARE OF THE MACHINE: a quarter of the
                                    processors it reports, at least one and
                                    never more than four.
      MaximumTokensPerPass   448    one segment of music - twelve to twenty
                                    bars, which is what the rated music was
                                    written at
      MaximumPromptBars      4      how much of its own text a continuation sees
      ContextTokens          2048   null asks for the whole trained context.
                                    2,048 costs 216 MiB less than the trained
                                    8,192 and is four times what a pass uses.

WHAT IT HONOURS: model-native text, the key, the metre, the unit note length,
the tempo, CHARACTER WORDS, a VOICE COUNT, a seed, the sampling settings (the
repetition penalty included), a length cap - which counts TOKENS for a model
that writes text - an inference thread count, and a continuation. WHAT IT
REFUSES BY NAME: free text (it reads ABC, not words), a primer (there is no way
back from MIDI to notation), a DRUM KIT (ABC has no percussion at all), a voice
count of more than two, target length, a character word it cannot turn into a
tempo or a mode, and INSTRUMENT HINTS - ABC notates parts rather than sounds,
and what plays each part is the rendition's business.

A SEED MEANS WHAT IT SAYS HERE, and it costs one thing: the engine underneath
keeps the last prompt it evaluated and re-uses whatever prefix of the next one
matches, and a prompt evaluated from a cache is not evaluated in the same
batches as one evaluated from nothing - which now and then decides a token
differently. So A SEEDED PASS CLEARS THE CONTEXT FIRST. It costs one prompt
evaluation, a fraction of a second, and it buys a seed that means "the same
request writes the same music twice". THE ONE SEED THAT CANNOT BE USED IS -1:
the engine reads that bit pattern as its own "draw a random seed" marker, so it
is refused by name. Any other seed works.

HOW FAST IT IS, ON A LAPTOP OF THIS CLASS - GUIDANCE, NOT A PROMISE. Measured
on a sixteen-core laptop, in Release, at four threads over the prompts the
presets carry: 23 to 29 SECONDS OF MUSIC PER SECOND OF GENERATING - about eight
times what the other adapter manages. Eight threads reach 36 to 44, and there is
nothing to spend it on, because generation stops at a thirty-second lead either
way. The model loads in about a fifth of a second from a warm file cache and
costs about 336 MiB of working set at a 2,048-token context. The first playable
event arrives 0.1 to 0.25 s after the request, and A SEAM COSTS ABOUT A TENTH OF
A SECOND.

AT THIS SPEED THE REAL-TIME FACTOR MAY NEVER BE MEASURED AT ALL. The session
counts only the time it was actually pulling, and this model fills the whole
thirty-second window in about a second, so Diagnostics.RealTimeFactor can stay
NULL for a long time. Null means "not measured yet". It never means "slow".

THE PRESETS OF THE LISTENING SESSION are written for this model - see "THE
PRESETS".

TEXT BECOMES MUSIC WHILE IT IS STILL BEING WRITTEN. The model writes several
parts merged together, one time slice at a time; every completed slice is
regrouped into standard ABC voices, read and converted, and only the NEW events
are released. Nothing already heard is ever changed - a note the text could
still lengthen, the far end of a tie, is held back until it cannot be.

WHAT IT WRITES IS THE IDIOM ABC KNOWS: folk and classical, in parts, with no
percussion. A header alone produces ONE LINE OF MELODY. The duets that this
library's taste rests on came from an OPENING written in the model's own merged
form, which ModelNativeText carries verbatim:

    options.Request.ModelNativeText =
        "X:1\nL:1/8\nQ:1/4=108\nM:3/4\nK:Am\n" +
        "\"Am\" e2 a2 c'2 | A,2 [CE]2 [CE]2 | <|> <|> " +
        "\"Dm\" d2 f2 a2 | D,2 [FA]2 [FA]2 | <|> <|> ";

A CONTINUATION IS RE-PROMPTED WITH THE MODEL'S OWN TEXT - the header it started
from and the last few bars it wrote - so the key, the metre and the idiom carry
over and none of it is played twice. THE ADAPTER REMEMBERS THAT TEXT ITSELF,
because nothing else here holds it: a session carries music as MIDI events. A
caller driving the generator directly can hand over its own through
MusicContinuation.ModelNativeTail, which wins.

ONE GENERATION AT A TIME, for the same reason as the other adapter, and with
the same answer: a follow-up prompt hands the model over rather than asking it
for two pieces at once.


PLAYING MUSIC - MusicSession AND MusicGenerationOptions
================================================================================
A session generates music and plays it. Everything it needs is three strings,
and every one of them is optional:

    GeneralMidiInstrumentLibrary.Register();          // first, always

    using var music = new MusicSession(new MusicGenerationOptions
    {
        Generator         = "MyGenerator",     // omitted = the embedded replay
        InstrumentLibrary = "ModestSynthGm",   // omitted = the default library
        Rendition         = "AmbientDuet",     // omitted = voiced automatically
    });
    music.Play();

    MusicGenerationOptions
      Generator                  a registered generator's name, or null
      InstrumentLibrary          a registered library's name, or null
      Rendition                  a registered rendition's name, or null
      Request                    what the music should be; never null
      SampleRate                 the rate YOUR mixer pulls at (44100)
      Preroll                    music written ahead of the head before it
                                 starts, and before it starts again after
                                 running dry (about five seconds)
      MasterVolume               multiplies the rendition's own master gain (1)
      ApplicationOwnsAudioOutput false = this session opens the audio device
      GenerateAhead              how much settled music to keep ahead of the
                                 play head before generation pauses (30 s)
      EndOfPiece                 KeepGenerating (the default) or Stop
      SegmentPriming             how each re-prompted segment starts: Primed
                                 (the default), Fresh or Alternate
      SeamCrossfade              how long the outgoing and incoming pieces
                                 overlap at a FRESH seam (zero, the default,
                                 is a hard join)
      SeamCrossfadeCurve         the crossfade's shape (EqualPower)
      TempoPolicy                Adopt (the default: every piece keeps its own
                                 tempo), Carry, or CarryOutsideBand
      TempoBand                  CarryOutsideBand's tolerance, a fraction (0.15)
      SessionBeatsPerMinute      a session tempo given up front, or null
      InferenceThreadCount       threads a model may use, or null for its own
                                 conservative default

    MusicSession
      Play() / Stop() / Dispose()
      FollowUp(request)          ask for something different, from now on
      FollowUp(request, name)    ... from a different generator
      PreloadAsync(token)        load the generator NOW, not on first use
      Release()                  give its memory back; it stays registered
      RenderToFileAsync(...)     the same music, to an audio file
      RenderToStreamAsync(...)   the same music, to a stream you own
      Renderer                   an IAudioRenderer, when you own the output
      Position / IsPlaying / IsStarved / IsFinished
      GenerationError            what a failing generator threw, or null
      ActiveSource               what is really making the music
      Diagnostics                how the music is doing, cheap every frame

TWO HOSTS, ONE ENGINE. By default Play() opens the audio device and plays.
Set ApplicationOwnsAudioOutput and it opens NO device: Play() prepares
Renderer instead, and your own mixer pulls stereo samples from it. THE PLAY
HEAD MOVES ONLY AS AUDIO IS PULLED, which is what makes the second mode exact.
See "WHEN YOUR APPLICATION OWNS THE AUDIO OUTPUT".

IT REGISTERS NOTHING - no instrument library, no generator, no rendition. With
no instrument library registered Play() throws, and the message names the one
call that settles it.

IS THE MUSIC WAITING? IsStarved says the play head has caught up with what has
been written and is playing silence while more is generated. That is an
ACCEPTED outcome, never a failure: the application comes first and the music
waits.

PLAY() AFTER STOP() STARTS AFRESH - a new timeline, a new engine, the same
options - because a game stops its music on one screen and starts it on the
next. Dispose() ends the session for good.

NOTHING IS QUIETLY IGNORED. A request that asks for something the chosen
generator does not honour is refused by Play(), by name, before anything
starts - the built-in replays honour a continuation and nothing else.


INSTRUMENT LIBRARIES - WHAT THE PARTS ARE PLAYED WITH
================================================================================
An INSTRUMENT LIBRARY is a NAMED set of instruments, addressed the way MIDI
addresses them: by General MIDI program number, and by note number on the
percussion channel. It is what keeps "what this music sounds like" apart from
"what notes it plays", so an application changes its whole sound by naming a
different library. The registry lives in CodeBrix.Audio
(InstrumentLibraryRegistry, in CodeBrix.Audio.Instruments); this package only
resolves a name through it.

    "ModestSynthGm"     the whole General MIDI sound set, SYNTHESIZED. It ships
                        with the ModestSynth package you already have, needs no
                        file at all, and is one line:
                            GeneralMidiInstrumentLibrary.Register();

    A SOUNDFONT OF      any .sf2 becomes a named library. CodeBrix.Audio ships
    YOUR OWN            no .sf2 itself - this is code, not content:
                            new SoundFontInstrumentLibrary("MyBank",
                                "The bank my game ships with", "bank.sf2")
                                .Register();

    "FluidR3Gm"         a full General MIDI SoundFont as a package of its own,
                        CodeBrix.Audio.Samples.FluidR3Gm - recorded instruments
                        rather than synthesized ones, at the cost of the
                        download. Your application references it and calls:
                            FluidR3GmInstrumentLibrary.Register();

    ONE VOICE AT A      MappedInstrumentLibrary is a BASE library plus
    TIME                substitutions: start from a General MIDI library, hear
                        the piece, then replace ONE program with an instrument
                        of your own - a Decent Sampler pack, an .sfz, a
                        SoundFont program, or a synthesizer you wrote - and
                        hear it again. Everything you have not replaced still
                        comes from the base.

THE DEFAULT LIBRARY FOLLOWS REGISTRATION ORDER: THE FIRST ONE REGISTERED IS THE
DEFAULT, and MusicGenerationOptions.InstrumentLibrary left null takes it. There
is no priority and no other ordering, so in an application that registers more
than one, the default depends on which start-up path ran first.

    GeneralMidiInstrumentLibrary.Register();      // "ModestSynthGm" - default
    FluidR3GmInstrumentLibrary.Register();        // "FluidR3Gm"

    // NAME THE LIBRARY WHENEVER IT MATTERS. It always matters in a library, in
    // a plug-in, and in anything with more than one start-up path.
    var options = new MusicGenerationOptions { InstrumentLibrary = "FluidR3Gm" };

    InstrumentLibraryRegistry.SetDefault("FluidR3Gm");    // or move the default

A LIBRARY MUST BE ABLE TO MAKE ONE INSTRUMENT PER PART. That is how a rendition
gives each part its own voice and its own level; a library that only offers one
multi-timbral synthesizer is refused by name when the music starts.

COVERAGE IS WORTH ASKING ABOUT for a library built from recordings: an
instrument sampled from C3 upwards plays nothing below it, which is a hole in
the middle of an arrangement rather than an error. A part this library cannot
voice as asked is voiced with something the library does cover, and the
substitution is reported in ActiveSource.Voicing.Diagnostics.

FOR THE DEPTH - coverage, key ranges, the two shapes a library can offer, the
mapped library's whole surface, and writing an IInstrumentLibrary of your own -
read the INSTRUMENT LIBRARIES section of CodeBrix.Audio's own AGENT-README.txt.
This package adds nothing to that seam; it uses it.


RENDITION - HOW THE PARTS ARE VOICED
================================================================================
A RENDITION says how a piece is voiced: an ordered list of voices, each an
instrument and the level it sits at, optionally with ONE second instrument
layered under it, plus a gain for the percussion part and a master gain.

VOICES GO TO PARTS IN THE ORDER THE PARTS FIRST SOUND, because a rendition is
written before anybody knows what the music will be and cannot name channels.
The percussion part is not in the list: it is whatever the music writes on the
percussion channel.

    BuiltInRenditions
      Automatic          no voices of its own - the automatic rule, and what
                         plays when no rendition is named
      AmbientDuet        a celeste over a mixed chorus; for a piece of two parts
      MelodyOverPad      soft bell-like keys in front of a warm pad
      VibesAndStrings    a vibraphone over sustained strings at half its level
      HarpAndCello       a harp and a solo cello at the same level
      Neutral            five distinct, unassuming voices for a piece of any
                         shape

EVERY BUILT-IN VOICING RESTS ON A LISTENING SESSION. Each pairing was played
through real instruments and written down with the mark it was given;
AmbientDuet is the one that scored highest, and Neutral is built out of what
those sessions marked DOWN - nothing bright stacked on anything else bright, and
no part at unity gain. They are a starting point, not a rule: a rendition is
data, and yours sits beside them.

THE AUTOMATIC RULE, for every part beyond a rendition's own voices: honour what
the music asks for (a program change on that channel), then what the request
asked for (its instrument hints), and fill everything still unvoiced from a
small table of voicings, one DISTINCT instrument per part. No part is ever left
on the piano at unity gain by accident.

GAIN IS NOT OPTIONAL: instrument libraries differ enormously in level, so every
voice states its own.

LAYERING, AND WHEN NOT TO. A part that is playing ALONE is layered over a pad,
because a single sparse line has nothing underneath it; the moment a second part
appears, that layer is taken off again. Layering both parts of a two-part piece
was rated well below giving each part one distinct voice, and stacking bright
instruments on bright instruments was rated worst of all. So: one layer at most,
under a part that has nothing beside it, and never as a way of making an
arrangement bigger.

A PROGRAM CHANGE LATER IN THE PIECE re-voices a part that was voiced
AUTOMATICALLY, at its musical moment - always. The parts a RENDITION'S OWN
VOICES cover are the ones FollowsProgramChanges governs, and it is FALSE by
default: generated music does not get to re-voice a part a rendition voiced on
purpose. Set it true on your own rendition if you want the music to win.

A PART KEEPS ITS SETTINGS. Real General MIDI music sets every channel's volume,
pan, expression and sends at tick 0, long before a part that enters at bar nine
plays - and a part is given its instrument when it first SOUNDS. Everything set
for a channel before that moment is sent to the instrument when it is built, and
sent again to the new one when a part is re-voiced, so a part that enters late
is mixed as the music asked and a re-voiced part does not jump.

WRITE YOUR OWN, AND REGISTER IT - a rendition is data, not code:

    var mine = new MusicRendition("MyGame", "The voicing my game ships with");
    mine.Voices.Add(new RenditionVoice(GeneralMidiProgram.Celesta, 0.9F));
    mine.Voices.Add(new RenditionVoice(GeneralMidiProgram.Pad2Warm, 0.4F));
    mine.PercussionGain = 0.7F;
    mine.MasterGain = 1.0F;
    mine.Register();

WHY A PART SOUNDS AS IT DOES. ActiveSource.Voicing carries one PartVoicing per
part - its channel, its program, its gain, its layer, where the choice came
from and a sentence saying why - and a list of diagnostics, which is where an
instrument the library could not play, or a percussion part with no kit behind
it, is reported.

    foreach (var part in music.ActiveSource.Voicing.Parts)
    {
        Console.WriteLine(part);
    }
    // channel 1: program 8 (Celesta) at 0.85, with program 89 (Pad 2 (warm))
    //            under it at 0.5 (TasteTable)

    foreach (var line in music.ActiveSource.Voicing.Diagnostics)
    {
        Console.WriteLine(line);
    }
    // Channel 1 is the only part so far, so program 89 (Pad 2 (warm)) was
    // layered under it at 0.5 - 9/10 - a single sparse part layered over a
    // pad, "really excellent".

A PART APPEARS WHEN IT FIRST SOUNDS. Until then nobody - not the rendition, not
the automatic rule - knows the part exists, so a voicing read a second later may
have more parts in it.


ASKING FOR MUSIC IN MUSICAL TERMS
================================================================================
MusicRequest.Intent says what the music should BE - a waltz in A minor at 108,
in two parts - and each generator turns that into whatever it really reads.
Nothing in an intent is silently dropped: a generator that cannot act on a part
of it refuses the request by name before a note is written.

    options.Request.Intent = new MusicIntent
    {
        Key            = "A",
        Mode           = MusicMode.Minor,
        Meter          = new MusicMeter(3, 4),
        BeatsPerMinute = 108.0,
        VoiceCount     = 2,
    };

WHAT EACH MODEL MAKES OF IT

    intent              MuPT                        SkyTNT
    ------------------  --------------------------  -------------------------
    Key + Mode          the ABC K: field - "Am",    a key signature, -7 to 7
                        "Gmin", "Dmix"              sharps or flats, major or
                                                    minor. A MODE WITH NO
                                                    TONIC names no signature
    Meter               the M: field                the time signature
    UnitNoteLength      the L: field                REFUSED - an ABC idea
    BeatsPerMinute      the Q: field, WRITTEN IN    the tempo event. Outside
                        THE BEAT THE METRE IS FELT  1 to 383 it is refused
                        IN: a dotted quarter for a  by name
                        compound metre, a quarter
                        otherwise
    VoiceCount          THE OPENING BARS. 1 writes  REFUSED - the INSTRUMENT
                        none; 2 writes a two-part   HINTS decide the parts
                        opening in the model's own
                        merged notation; more is
                        refused by name
    DrumKit             REFUSED - ABC has no        the drum kit, or none for
                        percussion at all           MusicIntent.NoDrumKit
    InstrumentHints     REFUSED - ABC notates       the instrument list, in
                        parts, not sounds; the      order
                        RENDITION picks the sounds
    CharacterWords      a tempo and a mode          a tempo and a key
                        (see below)                 signature (see below)
    TargetLength        REFUSED by both - a model writes until its own cap and
                        the session asks again, which is how a piece of any
                        length is made. Ask a RENDER for a length instead.
    Text (plain words)  REFUSED by both - neither model reads prose

A VOICE COUNT OF TWO IS THE MOST VALUABLE THING IN THIS TABLE. A MuPT header on
its own comes back as ONE LINE OF MELODY, and nothing in this family invents an
accompaniment from chord symbols - so a single line stays a single line. Asking
for two voices writes an opening of two parts from the key and the metre: the
tonic chord, then the fifth degree when the tonic triad is major and the fourth
when it is minor, with the upper part arpeggiating each chord and the lower part
playing the bass note and then either the fifth above it or the chord. No
accidental is ever written into it: every note is a degree of the scale the K:
field names, so the key signature spells them. That is the shape both of the
highest-rated pieces of this library's own listening sessions opened with - and
the first bar of each of those two prompts is exactly what it writes.

THE ESCAPE HATCHES ALWAYS WIN. MusicRequest.ModelNativeText - ABC, for MuPT -
is handed over verbatim, and a MIDI Primer is handed to SkyTNT as music to
follow. When one of them is set, the intent's own opening is not written: a
caller who writes the notation itself is not asking for one.

CHARACTER WORDS - WHAT THEY MEAN, AND WHAT HAPPENS TO THE REST
Neither model reads prose, so a word is only worth having where it names
something musical. Every word below names a tempo, a mode, or both, and A WORD
THAT NAMES NEITHER IS REFUSED BY NAME rather than dropped:

    "The music generator 'MuPT' does not honour this request: the character word
     'menacing' is not one it can act on. ..."

    word          bpm  mode      word          bpm  mode
    ------------  ---  --------  ------------  ---  --------
    slow           60            lively        132
    mournful       60  Minor     bright        132  Major
    calm           66            cheerful      132  Major
    peaceful       66            joyful        132  Major
    ambient        66            driving       138
    solemn         66  Minor     energetic     144
    gentle         72            urgent        152
    dreamy         72            fast          168
    sad            72  Minor     major              Major
    dark           76  Minor     minor              Minor
    melancholy     76  Minor     ionian             Ionian
    stately        84            dorian             Dorian
    wistful        84  Minor     phrygian           Phrygian
    moderate      100            lydian             Lydian
    flowing       108            mixolydian         Mixolydian
    triumphant    112  Major     aeolian            Aeolian
    heroic        120  Major     locrian            Locrian
    happy         126  Major

MusicCharacterWords.Known is that list at run time, and Find(word) is what any
one of them means. TWO RULES: words are read in order and THE FIRST ONE THAT
NAMES A THING SETTLES IT ("slow, fast" is slow); and WHAT THE REQUEST SAYS
OUTRIGHT ALWAYS WINS, so a word never overrides a BeatsPerMinute or a Mode you
set yourself - it fills in only what you left unsaid.

A WORD NEVER NAMES AN INSTRUMENT. What plays a part is the RENDITION's business
and not the generator's, which is why there is no "brassy" or "glassy" here.


THE PRESETS - READY-MADE REQUESTS
================================================================================
A preset is a MusicRequest somewhere to start from, with the generator family it
was written for and the voicing its music was rated through. IT NAMES NO
GENERATOR and registers nothing: you still register a generator of that family
and specify it by name.

    using var music = new MusicSession(new MusicGenerationOptions
    {
        Generator = "MuPT",
        Rendition = MuPTPresets.WaltzDuetInAMinor.SuggestedRendition,
        Request   = MuPTPresets.WaltzDuetInAMinor.CreateRequest(),
    });

    MusicPreset
      Name / Family / Description
      SuggestedRendition   the voicing this music was rated through, or null
      IsProvisional        false for all built-in MuPT and SkyTNT presets
      CreateRequest()      a NEW request every time, yours to change

THE MuPT PRESETS are the prompts a listening session was run on, carried as the
model's own notation so that they are the prompts THEMSELVES, character for
character - a prompt built from an intent would spell the same tempo a different
way. The idiom is ABC's, and it is worth saying plainly: a folk-and-classical
notation, in parts, WITH NO DRUMS AT ALL.

    ReelInGMinor        the prompt from the model's own card, fast and in four
    JigInD              6/8, felt in two dotted quarters
    WaltzInAMinor       a single line          -> "MelodyOverPad"
    AirInDMixolydian    a slow air             -> "VibesAndStrings"
    HornpipeInG         dotted, swung pairs
    OpenInC             a header and nothing else: the model writes it all
    DuetInC             two parts              -> "HarpAndCello"
    WaltzDuetInAMinor   two parts              -> "AmbientDuet"

    MuPTPresets.All, MuPTPresets.Find(name)

THE TWO DUETS ARE THE ONES THAT PRODUCE TWO-PART MUSIC, because their openings
carry two parts - and they are also the two whose music was rated highest.

THE SkyTNT PRESETS are accepted electronica starting points. Jeremy heard two
seeds of each on 2026-09-21 and kept all three. They provide steady arrangements;
repetition and limited musical development remain quality considerations. Keep
the 1,000-event default: 60- and 120-event Club passes were not reliably accepted,
and the 300-event timing varied by seed.

    FourOnTheFloor      a kick on every beat under a synthesized bass, 126, 4/4
    ClubArrangement     drums, synth bass, sawtooth lead, warm pad, 126, 4/4
    AmbientElectronica  two pads and a bell-like lead, NO percussion, 84, 4/4

    SkyTNTPresets.All, SkyTNTPresets.Find(name)

Each of them asks for its drum kit PER REQUEST, through MusicIntent.DrumKit, so
one generator serves them all - and the ambient one asks for
MusicIntent.NoDrumKit rather than leaving it to the generator's own default.


THE LIFE OF A STREAM
================================================================================

IT KEEPS GOING. At the end of a generator's pass the SAME generator is asked for
the next segment, with the music so far in view - the tail of what has played
plus the tempo, the metre, the key and each part's program - and that segment is
placed at the NEXT BAR LINE. The embedded replays have one piece each, so for
them carrying on means looping. Set EndOfPiece = Stop for music that ends: the
timeline is completed when the pass ends and playback finishes the way a file
does.

WHAT IS KEPT ACROSS A SEAM, by default:
  * a segment starts on a BAR LINE, never mid-bar;
  * the tempo, the metre, the key and each part's program and controllers carry
    across, and the seam emits nothing that has not changed - a new segment
    restating the tempo it is already playing at says nothing at all;
  * notes still sounding keep their whole length; nothing is cut at a seam;
  * a new segment gets a DIFFERENT seed, derived from the seed you gave and the
    segment number, and exactly the same temperature, top-k and top-p - so the
    music does not repeat itself and does not change its manner either.
A SESSION TEMPO - TempoPolicy. A model picks a new tempo for every fresh piece,
so music made of fresh segments can lurch from one pulse to another at every
seam. Carry plays every FRESH piece at the SESSION TEMPO; CarryOutsideBand lets
a piece whose opening tempo is within TempoBand (15% by default) of it keep its
own, and carries the rest. The session tempo is SessionBeatsPerMinute when set,
else the request's MusicIntent.BeatsPerMinute when set, else the first piece's
opening tempo; given up front it is imposed on the first piece too.
  * A CARRIED PIECE KEEPS ITS TICKS: all of its tempo events are dropped (later
    changes inside it too - it has one tempo) and the session tempo is stated
    where it starts (the start of a crossfade, or the bar line of a hard join).
    It is heard a little faster or slower; its bar lines do not move.
  * Primed segments and follow-up prompts are never touched.
  * MusicIntent.BeatsPerMinute is a request TO THE GENERATOR, and a generator
    that does not honour a tempo still refuses it by name at Play - that is
    unchanged. SessionBeatsPerMinute is never sent to the generator, so it works
    with any generator.
  * THE TEMPO A PIECE IS JUDGED BY is the one in force where its FIRST NOTE
    sounds - not a default stated at its first tick - and the decision waits
    for that note.
  * Diagnostics.SessionTempo, CarriedTempoCount and AdoptedTempoCount report it.
    A file rendered with the same options is carried the same way, and its
    MusicRenderResult.Diagnostics has one line per piece: the tempo it was
    written at, and whether it was carried or adopted.

AN EMPTY PASS NEVER ENDS THE MUSIC. A model can answer a request with nothing
at all. With EndOfPiece = KeepGenerating that segment is asked for again at
the same bar line as a FRESH piece on a new derived seed (Diagnostics.
EmptyPassCount counts it); only three empty passes in a row stop the music,
and then GenerationError says so - IsFinished with a null error never means
"the model wrote nothing". A pass that FAILS part-way (the generator throws
after it has started - MuseCoco refuses a piece that needs more than fifteen
melodic channels) is treated the same way: what it wrote still plays, the next
piece is fresh, Diagnostics.FailedPassCount counts it, and only a run of three
failed or empty passes stops the music, with the last failure in
GenerationError. A follow-up prompt that fails, and a first piece that fails
before writing anything (a model that will not load), are reported as before.
A render lists every such failure in its Diagnostics.

SILENCE WHILE IT WAITS. While the play head is waiting for music - at the start,
or after running dry - nothing is put in front of it until a whole pre-roll of
settled music is there to play straight through, so a slow generator is heard
as silence, never as a first note left ringing while the head waits.

WHAT IS NOT PROMISED: that every seam is inaudible. A listener may still hear a
join where the model changes its mind about the material.

PRIMED OR FRESH - SegmentPriming. Everything above describes a PRIMED segment,
which is the default. Some models are led so strongly by the bars they are
shown that a primed segment writes the same material again, and a new seed
changes nothing audible. Two other choices:
  * Fresh      every segment is the application's own request again - a new
               piece in the same character, on a new derived seed when there is
               a seed - with nothing of the music so far in view. It still
               starts at a bar line, and the tempo and each part's program
               carry across the way they do for any seam.
  * Alternate  primed, fresh, primed, fresh... - the second segment of the
               music is primed. A follow-up prompt starts the count again.
A generator that cannot be shown the music so far (one that does not honour
MusicRequestFeatures.Continuation) starts every segment fresh, whatever the
option says. Diagnostics.GeneratingSegmentKind says what is being generated now
(FirstPiece, Primed, Fresh or FollowUp); PrimedSegmentCount and
FreshSegmentCount count them.

A CROSSFADE AT A FRESH SEAM - SeamCrossfade. A fresh segment is a different
piece, so by default it cuts in at the bar line. Set SeamCrossfade and the
incoming piece starts that long BEFORE the outgoing piece's last bar line, on a
beat when at least a beat fits, while the outgoing piece plays on to its end;
the two are mixed along SeamCrossfadeCurve (EqualPower keeps the loudness
steady, StraightLine keeps the sum of the gains at one) and the outgoing
instruments are let go when the fade is over:

    using var music = new MusicSession(new MusicGenerationOptions
    {
        Generator = "MyModel",
        SegmentPriming = SegmentPriming.Alternate,
        SeamCrossfade = TimeSpan.FromSeconds(4.0),
    });

  * ONLY FRESH SEAMS ARE CROSSFADED. Primed seams and follow-up prompts join at
    a bar line exactly as before.
  * THE INCOMING PIECE PLAYS THROUGH INSTRUMENTS OF ITS OWN, built from the same
    instrument library and rendition, because both pieces use the same MIDI
    channels. It is voiced as a new piece: its bar lines count from where it
    starts, and a part it never gives a program keeps the program it had.
  * A FRESH PIECE'S SILENT OPENING BARS ARE SKIPPED AT A CROSSFADE, so the
    outgoing piece fades into music, not into silence: its first bar with notes
    starts at the start of the fade, and the tempo, metre and programs in the
    skipped bars still apply. A hard join - no crossfade asked for, or a fade
    shortened to nothing - plays those bars as written.
    Diagnostics.SkippedLeadingBarCount counts them.
  * IT NEVER COSTS THE MUSIC A GAP. The incoming piece is placed early only
    once it has generated the whole fade plus its own pre-roll; when it has not
    by the time the outgoing music must be committed, the fade is SHORTENED -
    down to a hard join if need be - and Diagnostics.ShortenedCrossfadeCount
    counts it. CrossfadeCount counts the fades that happened.
  * A RENDER CROSSFADES THE SAME WAY, through the same mixer. With no play head
    to protect it waits for the whole fade instead of shortening it, and the
    MIDI it hands back has both pieces overlapping at each seam.
  * While the engine is delivering a whole segment at a time, fresh seams are
    hard joins.

HOW FAR AHEAD IT RUNS. GenerateAhead is ONE setting, measured in SECONDS OF
MUSIC rather than events or ticks, because the play head moves in time and the
tempo changes. Generation stops when that much settled music is waiting ahead of
the head and starts again when it falls to half - so it runs in short bursts
with idle time between them. NOT PULLING IS PAUSING: a generator does no work
while nobody asks it for anything, so a paused stream costs nothing at all. A
bigger window cannot rescue a machine that generates slower than it plays; it
only postpones the gap and makes a prompt change throw more away.

A FOLLOW-UP PROMPT changes the music WHILE IT PLAYS:

    // A follow-up is a REQUEST, and the generator it goes to must honour what
    // it asks for - so a follow-up in WORDS needs a generator that reads words:
    music.FollowUp(new MusicRequest { Text = "something darker, slower" });

    // The embedded replays honour a continuation and nothing else, so a
    // follow-up to one is a change of GENERATOR rather than a change of prompt:
    music.FollowUp(new MusicRequest(), EmbeddedReplay.Abc);

The new generation starts alongside the one that is playing. When it has its own
pre-roll buffered it TAKES OVER AT THE NEXT BAR LINE beyond what has been
committed, the old generation is cancelled and its lookahead is dropped - so a
prompt change waits for new pre-roll and a safe bar line after committed music. Name a
generator in the second argument to change model as well; leave it out and the
one that is playing carries on with the new request. A second follow-up made
before the first has taken over REPLACES it. ActiveSource changes AT THE SWITCH,
not at the call, because until then what is playing is still the old music.

THE SAME GENERATOR TWICE IS A HAND-OVER, and it is the commonest follow-up
there is: the same model, a new prompt. A model writes ONE piece at a time, so
when the follow-up goes to the generator that is ALREADY generating, that
generation is cancelled first and the new one starts once it has ended. THE
MUSIC DOES NOT STOP MEANWHILE: everything already settled - up to GenerateAhead
of it - is still played, and the take-over is still at a bar line. If the
settled music runs out before the new generation is ready, there is a rest, the
way there is whenever a generator cannot keep up. A follow-up naming a
DIFFERENT generator starts at once, alongside the old one.
A follow-up that is refused - an unregistered generator, a request the generator
cannot honour - throws, and THE MUSIC CARRIES ON exactly as it was.

WHAT A FOLLOW-UP KEEPS. Unchanged instruments keep their notes at full length.
When a part's program selects a different instrument, Audio releases the old
instrument's notes and continues rendering their release tails while the new
instrument receives new notes. The release envelope determines the old sound's
remaining duration. Parts left on the same instrument, rendition assignments,
loops and continuations are untouched unless their instrument actually changes.

A MACHINE THAT CANNOT KEEP UP. The engine measures its own generation rate and,
when it falls below real time, stops streaming: nothing of a segment is heard
until the whole of it is ready, then all of it plays, and then there is a REST
while the next one is generated. Music followed by a pause sounds intentional; a
stream that stalls mid-phrase sounds broken. It switches back by itself when the
rate recovers, and Diagnostics.Mode says which one it is in.

NOTHING IS EVER LATE, AND THE MUSIC WAITS IN WHOLE BARS. A slow machine produces
RESTS, never garbled timing. A note carries its own length, so a note still
sounding holds the timeline open past the last music that has been written, and
a play head is allowed to run out there - after a rest, or while a generator is
behind. When the head has reached the place the music that is coming belongs,
the engine HOLDS THAT MUSIC BACK by a whole number of bars, far enough ahead of
the head to be safe: the phrase finishes, the notes still sounding ring out
exactly as they were written, there is a rest of whole bars, and the music
carries on IN TIME - every note still where it was in its bar, in the metre that
was in force. What has already been committed is never moved, because it may
already have been heard. Diagnostics.HoldCount and .HeldBarCount say how often
that has happened and how much silence it has cost, and it is why
Diagnostics.LateEventCount stays at 0 however slow the machine is.

LOADING. Registering a generator loads nothing. It loads on first use - or
earlier, if you take the delay during a loading screen:

    await music.PreloadAsync(cancellationToken);

Once loaded it STAYS LOADED: it lives in the registry, not in a session, and
serves every later piece, every follow-up prompt and every other session.
music.Release() gives the memory back and leaves it registered, so asking for it
again loads it again. DISPOSING A SESSION RELEASES NOTHING.

LOADING AND INFERENCE COSTS differ. Historical warm-cache loads took roughly
0.1 s for SkyTNT and 0.2 s for MuPT at a 2,048-token context. Cold storage,
context length and runtime changes affect both time and memory. Inference can
allocate far more than loading: measure peak process memory over long requests,
not only at PreloadAsync. MuPT uses ModelRunner's bundled native engine;
SkyTNT and MuseCoco use managed ONNX. Loading multiple models retains each one
until explicitly released.

SHARING THE MACHINE. InferenceThreadCount passes through to the generator. Left
unset - the default - each model-backed generator takes A SHARE OF THE MACHINE:
A QUARTER OF THE PROCESSORS IT REPORTS, AT LEAST ONE AND NEVER MORE THAN FOUR.
It is a share rather than a number because the conservative answer differs from
machine to machine - four threads is a sixth of a twenty-four-thread laptop and
ALL of a four-core board. Four is the cap because both models climb steeply to
four threads and then flatten: eight threads land inside the run-to-run spread
of four, so the rest of the machine is better left to your application.
Inference, the synthesizer and a game loop are all on the same processors, and a
game that stutters is worse than music that waits. Set InferenceThreadCount on
the request or on the session to take more or less.

WHAT THE MUSIC IS DOING. music.Diagnostics is a cheap snapshot to read every
frame:

    StarvationGapCount   how many times the head has run out of music. One long
                         wait counts once, and a note still sounding over the
                         end of the music does not hide the gap. THE WAIT FOR
                         THE FIRST PRE-ROLL IS NOT A GAP: music cannot run out
                         before it has started, so an ordinary start reports 0.
    RealTimeFactor       seconds of music produced per second SPENT GENERATING,
                         or NULL while there has not been enough generating to
                         measure. 1 is exactly real time. It is a moving average
                         weighted towards the recent past. Null is not a number
                         on purpose: `RealTimeFactor < 1.0` reads correctly and
                         is simply false while nothing is known. A GENERATOR
                         FAST ENOUGH MAY NEVER GET A FIGURE AT ALL - one that
                         fills the whole generate-ahead window in about a second
                         is hardly ever pulling - so null means "not measured
                         yet" and never "slow".
    Mode                 Streaming, or SegmentAtATime
    IsSegmentAtATime     the same answer as a bool
    Lead                 how much settled music is waiting ahead of the head
    SegmentCount         pieces, continuations and follow-ups so far
    LateEventCount       events that reached the timeline behind the head. It
                         stays at 0: the engine holds music back rather than
                         commit it late.
    HoldCount            how many times the music has been held back to a later
                         bar line to keep it in front of the head
    HeldBarCount         how many whole bars of rest those holds have cost
    Voicing              what every part that has sounded is playing
    GeneratingSegmentKind  FirstPiece, Primed, Fresh or FollowUp: what is being
                         generated now, which is what is heard next
    GeneratingSegmentIsPrimed  the same question as a bool
    PrimedSegmentCount / FreshSegmentCount   re-prompted segments of each kind
    CrossfadeCount       fresh seams that were crossfaded
    ShortenedCrossfadeCount  fresh seams given less crossfade than asked, for
                         want of music in time - a fade shortened to nothing
                         is a hard join and is counted here only
    SkippedLeadingBarCount  silent opening bars of fresh pieces skipped at
                         crossfades
    EmptyPassCount       passes that wrote no music at all and were asked for
                         again as a fresh piece on a new seed

    Console.WriteLine(music.Diagnostics);      // the whole snapshot, one line

WHEN TO PRE-GENERATE OR RENDER AHEAD INSTEAD OF STREAMING: when RealTimeFactor
STAYS BELOW 1. That machine cannot compose this music as fast as it plays it,
and no amount of buffering fixes it - a bigger lookahead only postpones the gap.

WHAT THE NUMBERS LOOK LIKE ON A LAPTOP OF THIS CLASS, as guidance rather than a
promise. MuPT writes 20 to 29 seconds of music per second of generating and is
never going to run dry: six and a half minutes of it rendered in 17 seconds.
SkyTNT is a different story - it starts a pass at 3.2 to 3.7 times real time and
is down to 1.3 to 1.45 by its thousandth event, so the AVERAGE over a long piece
is far below the opening rate: the same six and a half minutes, dense and with
drums, took 29 minutes. A session streaming that falls back to a whole segment
at a time, on purpose, and says so in Diagnostics.Mode. SO: MuPT streams
comfortably on this class of machine and SkyTNT may not, depending on how dense
the music is; on a small board or a busy server, RENDER AHEAD. Measure it on the
machine you ship to - Diagnostics is how.

The answers, in the order to try them:

  * RENDER THE MUSIC TO AN AUDIO FILE AHEAD OF TIME and play the file.
    Generation then happens once, at whatever speed the machine manages, and
    playing a file costs nothing. This is the right answer for title music, for
    a level's theme, and for anything you can decide on in advance.
  * GENERATE DURING A LOADING SCREEN - PreloadAsync for the model's own load,
    and a session started early so that its pre-roll and lookahead are full
    before anybody is listening.
  * OR ACCEPT SegmentAtATime, which the engine switches to by itself: phrases
    with rests between them, which sounds intentional.

A gap now and then while the factor sits comfortably above 1 is a different
thing: that is a bursty generator against too short a pre-roll, and a longer
Preroll is the answer.


WHEN YOUR APPLICATION OWNS THE AUDIO OUTPUT
================================================================================
A game, a mixer or an audio engine usually owns the audio device already and
pulls everything through one output. Set ApplicationOwnsAudioOutput and this
session opens NO device: it prepares an IAudioRenderer that your mixer pulls
stereo samples from, at the rate you name.

    using CodeBrix.Audio.ModestSynth;
    using CodeBrix.Audio.MusicGeneration;

    GeneralMidiInstrumentLibrary.Register();

    using var music = new MusicSession(new MusicGenerationOptions
    {
        ApplicationOwnsAudioOutput = true,
        SampleRate                 = 48000,     // the rate YOUR mixer pulls at
        MasterVolume               = 0.8F,
    });
    music.Play();                               // no device is opened

    var left = new float[1024];
    var right = new float[1024];

    // ... in your own mixer callback, for as long as you want music:
    music.Renderer.Render(left, right);         // one block of stereo samples

THE PLAY HEAD MOVES ONLY AS AUDIO IS PULLED. Everything the streaming lifecycle
measures - the pre-roll, the lead, starvation, the real-time factor - is
measured against that head, so a mixer that stops pulling stops the music's
clock, exactly as a paused game should.

TWO THINGS TO GET RIGHT. Renderer does not exist until Play() has been called,
because until then nothing has chosen an instrument library; and SampleRate is
YOURS in this mode - when the session opens the device itself it uses THE
DEVICE'S OWN rate instead, because a synthesizer built at the wrong rate plays
the whole piece transposed and stretched.


RENDERING TO A FILE
================================================================================
THE SAME GENERATION AND THE SAME RENDITION, rendered offline instead of played.
What to play is still MusicGenerationOptions - the generator, the instrument
library, the rendition and the request - and MusicRenderOptions says only what
the FILE should be. AN INSTRUMENT LIBRARY IS STILL REQUIRED: a render makes the
same sound a player would, so GeneralMidiInstrumentLibrary.Register() (or
another library) comes first here too.

    var result = await music.RenderToFileAsync("theme.wav", cancellationToken);

That is one pass of the generator, ending at its own ending, written where you
said. A render needs no Play(), does not disturb music that IS playing, and
opens no audio device at all.

THE FORMAT IS THE FILE NAME'S. It goes through CodeBrix.Audio's writer registry:

  .wav            32-BIT FLOAT by default - nothing is quantised on the way to
                  disk. BitsPerSample = 16 gets the 16-bit PCM every other
                  program expects.
  .aif / .aiff    16-bit PCM by default.
  .opus           when YOUR application references CodeBrix.Audio.Opus and has
                  called CodeBrixAudioOpus.Register(). This package never
                  references that one: format support is a REGISTRATION
                  question, not a dependency question.
  anything else   whatever IAudioFileWriterFactory you registered.

An extension nothing is registered for is refused BY NAME, listing what is - and
refused before a single note is generated, so nobody waits minutes to be told
the file name was wrong.

TO A STREAM YOU OWN, in the format a name or an extension chooses:

    await music.RenderToStreamAsync(output, ".opus", render, cancellationToken);

The stream is NEVER closed - finishing the audio and closing the stream are
separate jobs. WAV and AIFF need a stream that can SEEK, because both patch
their header once the length of the audio is known; Opus is written strictly
forwards and takes a pipe or a socket.

A LENGTH, AND AN ENDING. A generator does not plan a length - it writes until it
stops - but musical time is known as it is generated: a render generates until
the music passes the target, continuing the generator at a bar line whenever its
pass ends early, and then ends the file one of three ways.

    RenderEnding.Fade         the file is EXACTLY the target length and the last
                              seconds of it fade to silence. It is what a target
                              length gets when nothing else is said.
    RenderEnding.HardCut      exactly the target length, stopped at that sample,
                              for a consumer applying their own fade or edit.
    RenderEnding.NaturalStop  ONE PASS of the generator plus the ring-out of the
                              notes still sounding. The length is whatever it
                              turns out to be, and it is what a render with no
                              target length does.

    new MusicRenderOptions {
        TargetLength = TimeSpan.FromSeconds(385.0),   // 6:25
        Ending       = RenderEnding.Fade,             // null: fade if a target
        FadeLength   = TimeSpan.FromSeconds(6.0),     // MusicFade.DefaultLength
        FadeCurve    = MusicFadeCurve.EasedDecibels,  // MusicFade.DefaultCurve
        RingOut      = TimeSpan.FromSeconds(5.0),     // at most; stops if quiet
        BitsPerSample = null }                        // the format's own choice

THE FADE, as a shape. Three curves ship, they are a consumer option on every
render, and MusicFade.GainAt(curve, progress) is the function itself:

    MusicFadeCurve.EasedDecibels   the level falls evenly IN DECIBELS, eased at
                                   both ends. It is the default.
    MusicFadeCurve.StraightLine    the AMPLITUDE falls in a straight line, which
                                   seems to hang and then drop.
    MusicFadeCurve.EqualPower      a quarter-cycle of a cosine - the crossfade
                                   shape, which holds its level longest.

MusicFade.DefaultLength and MusicFade.DefaultCurve are the defaults, in one
place, and a render uses whatever the options say. A fade longer than the file
is CLAMPED to the whole of it, the result reports the length actually applied,
and the render says so in its diagnostics.

WHAT COMES BACK is a MusicRenderResult: the exact Duration and FrameCount, what
really made the music, the segments and the bar lines they joined at, the
diagnostics that apply - and THE MUSIC ITSELF, so the .mid can be written beside
the audio:

    MidiFile.Export("theme.mid", result.Music);

PROGRESS, because generation is the time cost with a real model:

    var progress = new Progress<MusicRenderProgress>(p => Console.WriteLine(p));

    await music.RenderToFileAsync("theme.wav", render, progress, token);

Music and Fraction never go backwards, and the last report of a render that
finishes is MusicRenderStage.Finished at a fraction of 1. With no target length
there is nothing to be a fraction OF, so Fraction reads 0 until the render
finishes.

A CANCELLED OR FAILED RENDER LEAVES NO FILE THAT LOOKS FINISHED. The audio is
never finished off - a WAV is only a valid WAV once its header has been patched
- and a file the render created is deleted. A stream you supplied is yours: it
is left exactly as it stands, unfinished.

NOTHING OF THE STREAMING LIFECYCLE APPLIES. A render has no play head to
protect, so pacing is off, the generate-ahead window is replaced by "generate
until there is enough music for the file", the segment-at-a-time fallback cannot
engage, and no music is ever held back to a later bar line. WHAT IS RENDERED IS
TICK FOR TICK WHAT THE GENERATOR WROTE, however slow the machine is.


CORE API REFERENCE
================================================================================

IMusicGenerator - the contract
--------------------------------------------------------------------------------
    string                 Name          registered and asked for under this,
                                         matched without regard to case
    string                 Family        "Replay", or the model family
    string                 Description   a sentence you can print
    MusicRequestFeatures   Honours       what it acts on; everything else in a
                                         request is REFUSED BY NAME
    bool                   IsLoaded
    Task PreloadAsync(CancellationToken)
    void Release()
    IAsyncEnumerable<GeneratedMusicEvent> GenerateAsync(MusicRequest,
                                                        CancellationToken)

THE CONTRACT, which every generator keeps:

  PULL-BASED        work happens as you pull. NOT PULLING IS PAUSING, and it
                    costs the generator nothing.
  ONE CALL, ONE     the ticks of a segment start at 0, at the resolution
  SEGMENT           MusicRequest.TicksPerQuarterNote names. YOU fix the
                    resolution, because several segments - possibly from several
                    generators - land on one timeline. Placing a segment on that
                    timeline is your job, never the generator's.
  OUT OF ORDER      events may arrive out of tick order. Each carries the
                    SETTLED TICK: nothing at or before it will change or be
                    added. Hold an event until the settled tick has passed it,
                    then play it in tick order.
  CHANNELS 1 TO 16  the way CodeBrix.Audio counts them.
  LOADING           constructing loads nothing and registering loads nothing. A
                    generator loads on first use, or earlier if you call
                    PreloadAsync during a loading screen. It stays loaded;
                    Release() gives the memory back and leaves it registered.

IT IS THE PUBLIC EXTENSION POINT of this library: anything that can produce MIDI
can be a generator, and a session, the streaming lifecycle and the offline
render all work the same way behind it. See "WRITING YOUR OWN GENERATOR".

GeneratedMusicEvent - what a generator yields
--------------------------------------------------------------------------------
    MidiEvent  Event               the event, or null
    bool       HasEvent
    long       SettledThroughTick  nothing at or before this will change;
                                   GeneratedMusicEvent.NothingSettled (-1) means
                                   nothing has settled yet
    bool       HasSettled

    static GeneratedMusicEvent FromMidiEvent(MidiEvent, long settledThroughTick)
    static GeneratedMusicEvent SettledThrough(long settledThroughTick)

A NOTE TRAVELS AS ONE EVENT: a NoteOnEvent carrying its duration, which is
exactly what CodeBrix.Audio's MidiStream.Append wants - it schedules the
matching note-off itself. A generator never emits a separate note-off.

AN ITEM WITH NO EVENT carries a settled tick on its own. Silence is settled
music too: a generator that has decided on a long rest has nothing to write at
those ticks, and this is how it says so.

MusicRequest - what you ask for
--------------------------------------------------------------------------------
One request type for every generator. EVERY PART IS OPTIONAL, and a request with
nothing set is a perfectly good request.

    string                        Text                 plain words
    string                        ModelNativeText      notation the generator
                                                       reads itself
    MidiEventCollection           Primer               music to start from
    MusicIntent                   Intent               key and mode, metre, unit
                                                       note length, tempo,
                                                       character words, voice
                                                       count, drum kit, target
                                                       length
    IList<GeneralMidiProgram>     InstrumentHints
    int?                          Seed
    MusicGenerationControls       Controls             temperature, top-k,
                                                       top-p, repetition
                                                       penalty, maximum events
    int?                          InferenceThreadCount null = a conservative
                                                       default
    int                           TicksPerQuarterNote  default 480
    bool                          PaceInRealTime       default true
    MusicContinuation             Continuation         the music so far
    MusicRequest Clone()

THE MUSIC DEFAULTS on Controls are temperature 0.8, top-k 40, top-p 0.92 and NO
repetition penalty. The last one is the difference that matters: text generation
leans on a repetition penalty, and music repeats on purpose.

MusicContinuation - what the music has been doing - carries:

    MidiEventCollection  Tail             the last few bars, as MIDI, at the
                                          tail's OWN ticks counted from its
                                          start; null when there is none
    long                 TailTicks        how long the tail is, so that the
                                          segment written in reply begins
                                          exactly at the tail's END. It is NOT
                                          the tick of the tail's last event:
                                          the music usually stops part-way
                                          through its last bar and the next
                                          segment starts at the bar line. Zero
                                          means the length is not stated.
    string               ModelNativeTail  the same tail in the generator's own
                                          notation, for one that reads text
    double?              BeatsPerMinute   the tempo in force at the end
    MusicMeter?          Meter            the metre in force at the end
    string / MusicMode?  Key / Mode       the key in force at the end
    IDictionary<int,     ChannelPrograms  the program each channel was left on,
     GeneralMidiProgram>                  keyed by channel 1 to 16

PaceInRealTime asks a generator that paces itself to release its music in real
time, which is what playback wants. Turn it off to take a whole segment as fast
as you can pull it, which is what an offline render wants. A generator that
already runs flat out ignores it.

MusicIntent - what you want, in musical terms - carries:

    string       Key            "C", "F#", "Bb"
    MusicMode?   Mode           Major, Minor, the seven modes, or None
    MusicMeter?  Meter          new MusicMeter(3, 4)
    MusicNoteLength? UnitNoteLength   what a bare note length means: ABC's L:
    double?      BeatsPerMinute in QUARTER NOTES a minute, always
    IList<string> CharacterWords words; see "ASKING FOR MUSIC IN MUSICAL TERMS"
    int?         VoiceCount     how many separate parts
    int?         DrumKit        the program the percussion channel takes - 0
                                standard, 8 room, 16 power, 24 and 25
                                electronic, 32 jazz, 40 brushes, 48 orchestral.
                                MusicIntent.NoDrumKit asks for NO percussion at
                                all; null leaves the choice to the generator
    TimeSpan?    TargetLength   how long the music should be
    MusicIntent Clone()

A DRUM KIT IS NOT A GENERAL MIDI PROGRAM, which is why it is here and not among
the instrument hints: General MIDI puts the kit on channel 10 and numbers kits
in a space of their own. A generator built on notation has no percussion at all
and refuses it by name.

MusicCharacterWords - the words a generator can act on
--------------------------------------------------------------------------------
    static IReadOnlyList<string> Known
    static bool IsKnown(string word)
    static MusicCharacter Find(string word)                 null when unknown
    static MusicCharacter Resolve(IList<string>, string generatorName)
    static MusicRequest ApplyTo(MusicRequest, string generatorName)

MusicCharacter carries Words, BeatsPerMinute and Mode, either of which may be
null. See "ASKING FOR MUSIC IN MUSICAL TERMS".

WHAT IS HONOURED, AND WHAT IS REFUSED
--------------------------------------------------------------------------------
A generator declares what it acts on through Honours, and a request that relies
on anything else is REFUSED - with a MusicRequestNotHonouredException naming the
generator and every part of the request it will not act on. Nothing is silently
ignored: an application that asks for a waltz in A minor never gets a reel in G
and no explanation.

    MusicGeneratorCapabilities.FeaturesUsedBy(request)
    MusicGeneratorCapabilities.UnhonouredFeatures(generator, request)
    MusicGeneratorCapabilities.EnsureHonoured(generator, request)
    MusicGeneratorCapabilities.NamesOf(features)
    MusicGeneratorCapabilities.Describe(generatorName, features)

"Relies on" means you set it. A request left at its defaults relies on nothing.
TicksPerQuarterNote and PaceInRealTime are never a reason to refuse: every
generator writes ticks at the resolution you name, and pacing is a matter for
generators that pace themselves.

MusicGeneratorRegistry - naming generators
--------------------------------------------------------------------------------
    static void Register(IMusicGenerator)
    static IReadOnlyList<IMusicGenerator> Registered
    static IReadOnlyList<string> RegisteredNames
    static bool IsRegistered(string name)
    static IMusicGenerator Resolve(string name)

REGISTERING IS NOT SPECIFYING. A generator plays when it has been registered AND
its name asked for. WITH NOTHING SPECIFIED - null, empty or blank - THE EMBEDDED
REPLAY PLAYS, always, however many other generators are registered. There is no
settable default and no first-one-registered-wins. (That is deliberately unlike
the INSTRUMENT library registry, where the first one registered IS the default:
an instrument library is a sound, and a generator could be mistaken for a model.)

The four built-in replays are always there: they appear in Registered ahead of
anything you register, and nothing else may take one of their names. Registering
the same generator instance twice is a no-op; a different generator under a
taken name is an error; a name that is not registered is an error listing what
is.

ReplayMusicGenerator and EmbeddedReplay
--------------------------------------------------------------------------------
    EmbeddedReplay.Midi          "EmbeddedReplayMidi"        the default
    EmbeddedReplay.MidiSecond    "EmbeddedReplayMidiSecond"
    EmbeddedReplay.Abc           "EmbeddedReplayAbc"
    EmbeddedReplay.AbcSecond     "EmbeddedReplayAbcSecond"
    EmbeddedReplay.Names, EmbeddedReplay.IsReservedName(name)

    ReplayMusicGenerator.FromMidiFile(name, path)
    ReplayMusicGenerator.FromMidiStream(name, stream)
    ReplayMusicGenerator.FromMidiEvents(name, events)
    ReplayMusicGenerator.FromAbc(name, abcText)
        string        Description   settable
        double        PacingRate    default 8 x real time
        TimeProvider  TimeProvider  injectable, for tests

A replay plays a piece it already has, released as though it were being written.
ONE CALL IS ONE PASS. The pass is as long as the music ROUNDED UP TO A WHOLE
BAR, and its last item says the whole of that length has settled - trailing
silence included - so the pass after it starts on a bar line. LOOPING IS NOT
DONE HERE: asking again, with a continuation, is the next pass.

A replay honours a CONTINUATION and nothing else, because a piece that is
already written cannot be made to be in A minor or to last four minutes.

THE NAMES SAY WHAT THEY ARE, on purpose. Print generator.Name in any diagnostic
and a demo loop can never be mistaken for a model.

MusicGenerationException, MusicRequestNotHonouredException
--------------------------------------------------------------------------------
MusicGenerationException is the base of this library's own errors - a request a
generator will not take, a piece it cannot read. Argument checking and registry
state keep the framework's own types: ArgumentNullException, ArgumentException,
InvalidOperationException.

MusicRequestNotHonouredException adds GeneratorName and UnhonouredFeatures, so
you can react to the refusal rather than read the message.


WRITING YOUR OWN GENERATOR
================================================================================
IMusicGenerator IS THE PUBLIC EXTENSION POINT. Anything that can produce MIDI
can be a generator: a model of your own, an algorithm, a procedural piece that
follows what is happening in a game, or a piece of music you already have.
Everything else in this library - the session, the voicing, the streaming
lifecycle, the offline render - works the same way behind any of them.

IF THE MUSIC ALREADY EXISTS, YOU DO NOT NEED TO WRITE ANYTHING:
ReplayMusicGenerator takes a MIDI file, a stream, a MidiEventCollection or ABC
text under a name of your own, and it keeps the whole contract for you.

    MusicGeneratorRegistry.Register(
        ReplayMusicGenerator.FromAbc("MyTune", abcText));

FOR MUSIC THAT IS REALLY BEING COMPOSED, implement the interface. Here is a
complete one - it writes a rising scale, a note at a time, in the metre and at
the tempo the request asks for:

    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using CodeBrix.Audio.Midi;
    using CodeBrix.Audio.MusicGeneration;
    using CodeBrix.Audio.MusicGeneration.Generation;

    public sealed class ScaleGenerator : IMusicGenerator
    {
        public string Name => "Scale";

        public string Family => "Example";

        public string Description => "Writes a rising scale, a note at a time.";

        // WHAT IT ACTS ON. Everything else a request asks for is refused BY
        // NAME - which is what stops an application getting music it did not
        // ask for and no explanation.
        public MusicRequestFeatures Honours =>
            MusicRequestFeatures.Tempo | MusicRequestFeatures.Meter |
            MusicRequestFeatures.Continuation;

        public bool IsLoaded => true;          // nothing to load

        public Task PreloadAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Release()
        {
        }

        public IAsyncEnumerable<GeneratedMusicEvent> GenerateAsync(
            MusicRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            // EVERY GENERATOR CALLS THIS FIRST. It is the one place a refusal
            // is worded, and it throws MusicRequestNotHonouredException.
            MusicGeneratorCapabilities.EnsureHonoured(this, request);

            return Generate(request, cancellationToken);
        }

        private async IAsyncEnumerable<GeneratedMusicEvent> Generate(
            MusicRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var ticks = request.TicksPerQuarterNote;     // YOU write at this
            var meter = request.Intent == null || !request.Intent.Meter.HasValue
                ? MusicMeter.CommonTime
                : request.Intent.Meter.Value;
            var beats = (int)(meter.TicksPerBar(ticks) / ticks);

            // Tempo and metre belong at the start of a segment, before the
            // notes they describe.
            if (request.Intent != null && request.Intent.BeatsPerMinute.HasValue)
            {
                var microseconds =
                    (int)(60000000.0 / request.Intent.BeatsPerMinute.Value);

                yield return GeneratedMusicEvent.FromMidiEvent(
                    new TempoEvent(microseconds, 0L),
                    GeneratedMusicEvent.NothingSettled);
            }

            for (var beat = 0; beat < beats; beat++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tick = (long)beat * ticks;

                // A NOTE IS ONE EVENT CARRYING ITS OWN LENGTH - channels count
                // from 1, and ticks are measured from the start of THIS
                // segment.
                yield return GeneratedMusicEvent.FromMidiEvent(
                    new NoteOnEvent(tick, 1, 60 + (beat * 2), 100, ticks),
                    tick);              // settled: nothing at or before it moves

                await Task.Yield();     // a real generator composes here
            }

            // THE LAST WORD IS THE LENGTH OF THE SEGMENT, trailing silence
            // included, so that the next segment starts on a bar line.
            yield return GeneratedMusicEvent.SettledThrough(meter.TicksPerBar(ticks));
        }
    }

    // Register it once, name it, and everything else in this library works.
    MusicGeneratorRegistry.Register(new ScaleGenerator());

    using var music = new MusicSession(new MusicGenerationOptions
    {
        Generator = "Scale",
        Request   = new MusicRequest
        {
            Intent = new MusicIntent
            {
                Meter          = new MusicMeter(4, 4),
                BeatsPerMinute = 96.0,
            },
        },
    });

THE FIVE RULES THAT MATTER, in one place:

  1. TICKS ARE SEGMENT-RELATIVE and start at 0 every call, at
     request.TicksPerQuarterNote. Do not try to place your segment on anybody's
     timeline.
  2. A NOTE IS ONE NoteOnEvent CARRYING ITS DURATION. There are no note-offs. A
     note whose length you do not know yet is not yielded yet.
  3. THE SETTLED TICK IS A PROMISE: nothing at or before it will change or be
     added. Keep it behind anything you may still revise, use NothingSettled
     while you are sure of nothing, and never let it run ahead of what you have
     written. Events at the SAME tick are released together - only the last of
     them may settle that tick.
  4. SETTLED SILENCE IS SAID WITH GeneratedMusicEvent.SettledThrough(tick).
     Without it a long rest looks like a stall.
  5. REFUSE WHAT YOU DO NOT HONOUR, by calling
     MusicGeneratorCapabilities.EnsureHonoured at the top of GenerateAsync.
     Never quietly ignore a part of a request.

AND SIX MORE FOR A GENERATOR WITH A MODEL BEHIND IT - this is how the two
adapters in this package are built, and writing a third is ordinary work:

  A. LOADING IS SEPARATE FROM REGISTERING. Constructing one must load nothing:
     take the path (or the map of paths) and keep it. IsLoaded, PreloadAsync and
     Release are the contract; load on first use behind a gate, and dispose the
     model outside the lock.
  B. TAKE A PATH THE CALLER SUPPLIES, and take a MAP of file names to paths as
     well when your model is several files - a store that keeps its files under
     digests has no folder to give you. Never assume "model equals package".
  C. WORK HAPPENS ONLY AS THE CALLER PULLS. Not pulling IS pausing, so do not
     run ahead into a queue of your own; the session stops pulling at its
     generate-ahead window and expects that to cost nothing.
  D. PUT YOUR OWN LOAD-TIME SETTINGS ON THEIR OWN OPTIONS CLASS, with a Clone()
     the constructor calls, so that editing the options later cannot change a
     generator already built. Anything the graph fixes at load time - a thread
     count, a context size - belongs there and not on a request; a request that
     names a different one should RELOAD rather than be ignored.
  E. DECIDE WHICH OF THE EVENTS YOU EMIT ARE SETUP AND WHICH ARE THE PROMPT
     BEING ECHOED. Getting that wrong is silent: the music plays, on the wrong
     instruments, at the wrong speed.
  F. YIELD WHOLE BARS. Hold events back until you are sure of the bar they sit
     in, settle on the bar line, and let the ragged last bar of a pass go - then
     every seam falls where a listener expects one.

And declare Honours honestly. A request part you cannot act on is a refusal, not
a shrug: MusicRequestNotHonouredException(message, generatorName, features) is
how to refuse something INSIDE a feature you otherwise honour.


WHAT IS REFUSED, AND WHY
================================================================================
Nothing here fails quietly, and every refusal names what to do about it. These
are the messages, word for word, so that an agent reading a stack trace knows
what it is looking at.

NO INSTRUMENT LIBRARY IS REGISTERED - InvalidOperationException from Play(),
from RenderToFileAsync and from RenderToStreamAsync, before anything else is
looked at:

    No instrument library is registered, so no music can be played or rendered.
    Register the provided ModestSynth.GeneralMidiInstrumentLibrary - call
    GeneralMidiInstrumentLibrary.Register() - or register another instrument
    library first.

A GENERATOR NAME NOBODY REGISTERED - InvalidOperationException, listing what is
registered so that a typo is obvious:

    No music generator named 'MyGenrator' is registered. Registered generators:
    EmbeddedReplayMidi, EmbeddedReplayMidiSecond, EmbeddedReplayAbc,
    EmbeddedReplayAbcSecond, MyGenerator. Register it before asking for it by
    name.

A NAME THAT BELONGS TO A BUILT-IN REPLAY, when registering:

    The name 'EmbeddedReplayMidi' belongs to a replay generator that is built
    in, so another generator cannot be registered under it. The built-in names
    are: EmbeddedReplayMidi, EmbeddedReplayMidiSecond, EmbeddedReplayAbc,
    EmbeddedReplayAbcSecond.

A NAME ANOTHER GENERATOR ALREADY HAS (registering the SAME instance again is a
no-op and does not throw):

    A different music generator is already registered under the name 'MyTheme'.
    Music generator names are unique and matched case-insensitively; registering
    the same generator again is a no-op, but two different generators cannot
    share a name.

A RENDITION NAME NOBODY REGISTERED - the same shape, from
MusicRenditionRegistry:

    No rendition named 'MyGme' is registered. Registered renditions: Automatic,
    AmbientDuet, MelodyOverPad, VibesAndStrings, HarpAndCello, Neutral, MyGame.
    Register it before asking for it by name.

A REQUEST PART THE GENERATOR DOES NOT HONOUR -
MusicRequestNotHonouredException, from Play(), from FollowUp(), from a render
and from GenerateAsync itself. It carries GeneratorName and UnhonouredFeatures
as well, so a caller can react to it rather than read it:

    The music generator 'EmbeddedReplayMidi' does not honour these parts of the
    request: metre, tempo, a drum kit, instrument hints. Leave them unset, or
    use a generator that honours them.

A CHARACTER WORD THE GENERATOR CANNOT ACT ON - the same exception, and it names
the word rather than the part:

    The music generator 'MuPT' does not honour this request: the character word
    'menacing' is not one it can act on. This model reads no prose, so a word is
    only worth having where it names a tempo or a mode; MusicCharacterWords
    .Known lists the words that do. Leave the word out, or say the same thing
    with MusicIntent.BeatsPerMinute or MusicIntent.Mode.

MORE PARTS THAN A MODEL CAN BE GIVEN AN OPENING FOR - from the MuPT adapter,
because a number of parts is only ever an opening to a model that reads
notation:

    The music generator 'MuPT' does not honour this request: a number of parts
    means something to this model only as an OPENING written in its own merged
    notation, and the openings it can write are for one part and for two. For 3,
    write the opening bars yourself and hand them over through
    MusicRequest.ModelNativeText.

... and its neighbour, when the bar and the unit note length do not fit:

    The music generator 'MuPT' does not honour this request: an opening is
    written in whole unit notes, and a bar of 4/4 does not divide into whole
    notes of 3/8. Choose a unit note length the bar divides into, or write the
    opening bars yourself through MusicRequest.ModelNativeText.

THE ONE SEED THAT CANNOT BE USED - also from the MuPT adapter:

    The music generator 'MuPT' cannot use the seed -1: the engine underneath
    reads that value as its own marker for 'draw a random seed', so the music
    would not be the same twice. Any other seed works.

A DRUM KIT NO PERCUSSION CHANNEL COULD HOLD - ArgumentOutOfRangeException, from
the property itself (the framework adds its own "(Parameter 'value')"):

    A drum kit is a program from 0 to 127, MusicIntent.NoDrumKit for no
    percussion at all, or null to leave the choice to the generator.

AN INSTRUMENT LIBRARY THAT CANNOT MAKE ONE INSTRUMENT PER PART -
MusicGenerationException:

    The instrument library 'MyLibrary' does not make one instrument per part,
    which is how a rendition gives each part its own voice and its own level.
    Name a library that does.

A FADE OR A HARD CUT WITH NO TARGET LENGTH - ArgumentException, before anything
is generated:

    A fade needs a target length: it is what the file is exactly as long as. Set
    MusicRenderOptions.TargetLength, or choose RenderEnding.NaturalStop to
    render the generator's own ending.

AN EXTENSION NO WRITER IS REGISTERED FOR - NotSupportedException, in
CodeBrix.Audio's own words, and again before anything is generated:

    No audio writer is registered for '.opus'. Registered formats: .aif, .aiff,
    .wav. Add one with AudioFileWriterRegistry.Register.

A STREAM A WAV OR AN AIFF CANNOT SEEK BACK THROUGH - also CodeBrix.Audio's:

    A .wav file needs a stream that can seek: the length is written into the
    header once the audio is known, which means going back to the start of the
    file. Write to a FileStream or a MemoryStream, or choose a format that is
    written strictly forwards.

A FOLLOW-UP WITH NOTHING PLAYING - InvalidOperationException:

    There is no music to follow up: call Play() first. A follow-up prompt
    changes music that is playing; it does not start any.

AND WHAT IS NOT AN ERROR: the play head running out of music (that is
IsStarved, and the application comes first), a generator that is slower than
real time (that is the diagnostics, and the delivery mode changes by itself), a
part the instrument library cannot voice as asked (something covered is used
instead, and Voicing.Diagnostics says so), and a fade longer than the file it is
in (it is clamped, and the result says so).


COMPLETE EXAMPLES
================================================================================

--- Six minutes twenty-five seconds, with a fade, as .wav and .opus ---------
    using CodeBrix.Audio.ModestSynth;
    using CodeBrix.Audio.MusicGeneration;
    using CodeBrix.Audio.MusicGeneration.Rendering;
    using CodeBrix.Audio.Opus;                   // your application's choice

    GeneralMidiInstrumentLibrary.Register();
    CodeBrixAudioOpus.Register();                // and .opus is a file name

    using var music = new MusicSession();

    var render = new MusicRenderOptions
    {
        TargetLength = TimeSpan.FromSeconds(385.0)
    };

    var wav = await music.RenderToFileAsync("theme.wav", render, token);
    var opus = await music.RenderToFileAsync("theme.opus", render, token);

    Console.WriteLine(wav);      // the path, the exact length, and the ending

--- Music in a game loop, with the application owning the output -------------
    using CodeBrix.Audio.ModestSynth;
    using CodeBrix.Audio.MusicGeneration;
    using CodeBrix.Audio.MusicGeneration.Generation;

    GeneralMidiInstrumentLibrary.Register();

    using var music = new MusicSession(new MusicGenerationOptions
    {
        ApplicationOwnsAudioOutput = true,
        SampleRate = 48000,
        Preroll = TimeSpan.FromSeconds(3.0)
    });
    music.Play();

    // ... your mixer, on its own thread:
    music.Renderer.Render(left, right);

    // ... your game, when the mood changes. It takes over at the next bar
    // line; a request in words needs a generator that honours words, and any
    // generator takes a change of generator.
    music.FollowUp(new MusicRequest(), "ChaseMusic");   // one you registered

    // ... your HUD, every frame - never a null check, and never a cost:
    var health = music.Diagnostics;
    if (health.RealTimeFactor < 1.0)          // null while nothing is measured
    {
        // this machine cannot compose as fast as it plays: render ahead
    }

--- A model, a preset, and a change of mind ----------------------------------
    using CodeBrix.Audio.ModestSynth;
    using CodeBrix.Audio.MusicGeneration;
    using CodeBrix.Audio.MusicGeneration.Models;
    using CodeBrix.Audio.MusicGeneration.Presets;

    GeneralMidiInstrumentLibrary.Register();

    // The adapter is CODE and the model is a FILE you supply. Registering
    // loads nothing; the first request pays for the load.
    MusicGeneratorRegistry.Register(
        new MuPTMusicGenerator("MuPT", muptModelPath));

    var preset = MuPTPresets.WaltzDuetInAMinor;

    using var music = new MusicSession(new MusicGenerationOptions
    {
        Generator = "MuPT",
        Rendition = preset.SuggestedRendition,     // "AmbientDuet"
        Request   = preset.CreateRequest(),
    });

    music.Play();

    // ... and later, in the same piece: something lighter, in six-eight. It
    // takes over at a bar line once it has its own pre-roll.
    music.FollowUp(MuPTPresets.JigInD.CreateRequest());

--- Ask for a duet without writing a note of notation ------------------------
    var request = new MusicRequest();

    request.Intent = new MusicIntent
    {
        Key        = "D",
        Mode       = MusicMode.Dorian,
        Meter      = new MusicMeter(4, 4),
        VoiceCount = 2,                            // writes the opening bars
    };

    request.Intent.CharacterWords.Add("gentle");   // 72 quarter notes a minute

    using var music = new MusicSession(new MusicGenerationOptions
    {
        Generator = "MuPT",
        Rendition = BuiltInRenditions.AmbientDuet, // a voicing for two parts
        Request   = request,
    });

    music.Play();

--- Electronica, with the drum kit asked for per request ---------------------
    MusicGeneratorRegistry.Register(
        new SkyTNTMusicGenerator("SkyTNT", skyTntBundleFolder));

    var request = SkyTNTPresets.ClubArrangement.CreateRequest();

    request.Intent.DrumKit = MusicIntent.NoDrumKit;   // this one, without drums

    using var music = new MusicSession(new MusicGenerationOptions
    {
        Generator = "SkyTNT",
        Request   = request,
    });

    music.Play();

--- What is playing, and why ------------------------------------------------
    using CodeBrix.Audio.MusicGeneration;

    var generator = MusicGeneratorRegistry.Resolve(null);
    Console.WriteLine($"{generator.Name} ({generator.Family})");
    Console.WriteLine(generator.Description);
    Console.WriteLine(string.Join(", ", MusicGeneratorRegistry.RegisteredNames));

--- Pull a segment ------------------------------------------------------------
    using CodeBrix.Audio.MusicGeneration;
    using CodeBrix.Audio.MusicGeneration.Generation;
    using CodeBrix.Audio.Midi;

    var generator = MusicGeneratorRegistry.Resolve(null);
    var request = new MusicRequest { TicksPerQuarterNote = 480 };
    var settled = GeneratedMusicEvent.NothingSettled;

    await foreach (var generated in generator.GenerateAsync(request, token))
    {
        settled = generated.SettledThroughTick;

        if (generated.HasEvent && generated.Event is NoteOnEvent note)
        {
            Console.WriteLine($"{note.AbsoluteTime}: note {note.NoteNumber} " +
                              $"on channel {note.Channel} for {note.NoteLength}");
        }
    }

    Console.WriteLine($"the pass settled through tick {settled}");

--- Take a whole segment as fast as it can be pulled --------------------------
    var request = new MusicRequest
    {
        TicksPerQuarterNote = 480,
        PaceInRealTime = false          //an offline render has no play head
    };

--- Replay your own music, under your own name --------------------------------
    using CodeBrix.Audio.MusicGeneration;
    using CodeBrix.Audio.MusicGeneration.Replay;

    var theme = ReplayMusicGenerator.FromMidiFile("MyTheme", "theme.mid");
    theme.PacingRate = 1.0;                       //exactly the speed it plays
    MusicGeneratorRegistry.Register(theme);

    var generator = MusicGeneratorRegistry.Resolve("MyTheme");

--- Ask for the next pass -----------------------------------------------------
    var next = new MusicRequest
    {
        TicksPerQuarterNote = 480,
        Continuation = new MusicContinuation { BeatsPerMinute = 140.0 }
    };

    await foreach (var generated in generator.GenerateAsync(next, token)) { }

--- Take the loading delay when it suits you ----------------------------------
    await generator.PreloadAsync(token);    //during a loading screen
    // ... later ...
    generator.Release();                    //the registration survives

--- Ask before you are refused ------------------------------------------------
    using CodeBrix.Audio.MusicGeneration.Generation;

    var wanted = new MusicRequest
    {
        Intent = new MusicIntent { Key = "A", Mode = MusicMode.Minor }
    };

    var unhonoured =
        MusicGeneratorCapabilities.UnhonouredFeatures(generator, wanted);

    if (unhonoured != MusicRequestFeatures.None)
    {
        // "key and mode" - name them, or choose a generator that honours them
        Console.WriteLine(string.Join(", ",
            MusicGeneratorCapabilities.NamesOf(unhonoured)));
    }


MINIMUM VIABLE PROJECT TEMPLATE
================================================================================
    <!-- MusicDemo.csproj -->
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <Nullable>disable</Nullable>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.Audio.MusicGeneration.MitLicenseForever" Version="*" />
      </ItemGroup>
    </Project>

    // Program.cs
    using CodeBrix.Audio.ModestSynth;
    using CodeBrix.Audio.MusicGeneration;
    using CodeBrix.Audio.MusicGeneration.Generation;

    GeneralMidiInstrumentLibrary.Register();      //unless you want another one

    var generator = MusicGeneratorRegistry.Resolve(null);
    var request = new MusicRequest { PaceInRealTime = false };
    var events = 0;

    await foreach (var generated in generator.GenerateAsync(request, default))
    {
        if (generated.HasEvent) { events++; }
    }

    Console.WriteLine($"{generator.Name} wrote {events} events");

That prints what the embedded replay wrote without making a sound. To HEAR it,
the same project needs two more lines and something to wait on:

    using var music = new MusicSession();
    music.Play();
    Console.ReadLine();


PERFORMANCE TIPS
================================================================================
  * NOT PULLING IS PAUSING. If you are ahead of the music, stop pulling; nothing
    is generated while you are not asking.
  * PRELOAD DURING A LOADING SCREEN. Registering loads nothing, and the first
    request pays for the load. PreloadAsync moves that cost to a moment you
    choose.
  * RELEASE WHAT YOU ARE NOT USING. Release() gives the memory back and leaves
    the generator registered; asking for it again loads it again.
  * ONE RESOLUTION. A replay prepares its music for one tick resolution at a
    time, so asking for a different TicksPerQuarterNote prepares it again. Pick
    one for your timeline and keep to it.
  * TURN PACING OFF FOR AN OFFLINE RENDER. PaceInRealTime = false means no
    waiting at all; everything else about the output is identical.
  * PACING RATE. The built-in replays run at eight times real time, which fills
    a few seconds of buffer in well under a second. Below 1 is slower than the
    music plays - useful for testing, not for a consumer.
  * RENDERING IS NOT THE COST, GENERATING IS. A render of the embedded music
    through the General MIDI bank runs many times faster than the music plays;
    with a model in front of it, the render takes as long as the model needs.
    Pass an IProgress and report it.
  * A LONGER PRE-ROLL IS CHEAPER THAN A LONGER LOOKAHEAD. The pre-roll is what
    stops a bursty generator being heard as a gap; the generate-ahead window
    only decides how much is thrown away when the music changes.


COMMON PITFALLS TO AVOID
================================================================================
  * EXPECTING NO SOUND TO BE A BUG. This library registers no instrument
    library. Call GeneralMidiInstrumentLibrary.Register(), or register another
    one, before you expect anything audible.
  * EXPECTING A REGISTERED GENERATOR TO PLAY. Registering is not specifying.
    Resolve(null) is ALWAYS the embedded replay, however many you registered.
    Ask for your generator by name - MusicGenerationOptions.Generator.
  * LETTING REGISTRATION ORDER CHOOSE THE INSTRUMENTS. The first library
    registered is the default, so an application with two start-up paths can
    sound different depending on which ran first. Name the library whenever it
    matters.
  * READING Renderer BEFORE Play(). It does not exist until the music starts,
    because until then nothing has chosen an instrument library.
  * EXPECTING THE MUSIC TO STOP WHEN THE PIECE ENDS. It does not: the generator
    is asked to carry on. Set EndOfPiece = Stop for music that ends.
  * EXPECTING A FOLLOW-UP TO BE HEARD AT ONCE. It takes over at a safe bar line
    after its own pre-roll is ready. Cancellation, inference and already committed
    music all contribute to latency; there is no fixed seconds-level guarantee.
    ActiveSource changes at the switch, not when you called FollowUp().
  * FOLLOWING UP IN WORDS TO A GENERATOR THAT DOES NOT READ WORDS. A follow-up
    is an ordinary request and it is checked like one: sending
    new MusicRequest { Text = "..." } to an embedded replay is refused by name,
    and the music carries on. Change the GENERATOR instead, or use one that
    honours free text.
  * CONFUSING REVOICING WITH CUTTING OFF AUDIO. On a program substitution the
    published Audio router releases old notes and renders their release tails.
    New notes use the replacement. Unchanged instruments retain full note lengths;
    a rendition's program assignments change only when requested.
  * TESTING RealTimeFactor FOR ZERO. It is NULL until there has been enough
    generating to measure - "not yet known" rather than a number - so compare
    it and let a null comparison be false; do not test it against 0.
  * EXPECTING Dispose() TO FREE A MODEL. It does not. A loaded generator belongs
    to the application; call Release() when you want the memory back.
  * TREATING A STARVATION GAP AS A FAULT. The application comes first and the
    music waits. Watch the count, and act on a RealTimeFactor that stays below
    1, not on a single gap.
  * EXPECTING A SLOW MACHINE TO PLAY THE MUSIC AT THE TICKS IT WAS WRITTEN AT.
    Music that the play head has caught up with is moved to a later bar line
    instead of being committed behind it, so a stretch of the timeline can be a
    REST the engine inserted. The music itself is unchanged - same notes, same
    lengths, same position in the bar - and HoldCount says it happened.
  * EXPECTING ActiveSource.Voicing TO BE COMPLETE AT ONCE. A part appears in it
    when it FIRST SOUNDS; until then nobody knows the part exists.
  * TREATING THE SETTLED TICK AS "THE HIGHEST TICK SO FAR". It is not. It is a
    promise about what will not change. Events may arrive out of tick order, so
    hold each one until the settled tick has passed it.
  * IGNORING ITEMS WITH NO EVENT. They carry the settled tick across silence.
    Drop them and a slow generator looks stalled at the last note before a rest.
  * LOOKING FOR NOTE-OFFS. There are none. A note is one NoteOnEvent carrying
    its duration.
  * ASSUMING A SEGMENT'S TICKS ARE TIMELINE TICKS. They start at 0 every time.
    Offset them yourself when you place a segment.
  * SETTING SOMETHING A GENERATOR DOES NOT HONOUR. You get a
    MusicRequestNotHonouredException, by design. Read Honours, or call
    MusicGeneratorCapabilities.UnhonouredFeatures before you generate.
  * EXPECTING A CHARACTER WORD TO BE A DESCRIPTION. It is a lookup, not prose:
    a word names a tempo, a mode or both, and a word that names neither is
    refused BY NAME. MusicCharacterWords.Known is the vocabulary.
  * ASKING FOR TWO VOICES AND EXPECTING TWO SOUNDS. A voice count is a number
    of PARTS. What each part is played WITH is the rendition's business - and a
    rendition written for two parts, like "AmbientDuet", is what makes a duet
    sound like one.
  * ASKING A NOTATION MODEL FOR DRUMS. ABC has no percussion at all, so
    MusicIntent.DrumKit is refused by name there. It is the other adapter that
    takes a kit, and a kit is not a General MIDI program - do not look for it
    among the instrument hints.
  * EXPECTING A MODEL TO STOP AT MusicIntent.TargetLength. Neither model plans
    a length; both are refused a target length by name. A RENDER reaches an
    exact length, by asking for more music and ending it deliberately.
  * REUSING A REQUEST YOU ARE STILL EDITING. Clone() it instead, so a generator
    never sees a request change under it.
  * ASKING FOR A FADE OR A HARD CUT WITH NO TARGET LENGTH. Both are EXACT
    endings and there is nothing for them to be exact about; it is refused
    before anything is generated. Set TargetLength, or choose NaturalStop.
  * EXPECTING A NATURAL STOP TO HONOUR A TARGET LENGTH. It does not. It is ONE
    PASS of the generator and its ring-out - which is also why a render of a
    generator that would loop for ever still finishes.
  * RENDERING TO .opus WITHOUT REGISTERING THE ENCODER. This package never
    references CodeBrix.Audio.Opus. Your application references it and calls
    CodeBrixAudioOpus.Register(); until then ".opus" is an unregistered
    extension, and the error says so and lists what IS registered.
  * HANDING A WAV RENDER A STREAM THAT CANNOT SEEK. A WAV patches its header at
    the end. Write to a FileStream or a MemoryStream, or render to a format that
    is written strictly forwards.
  * EXPECTING result.Music TO STOP EXACTLY WHERE THE AUDIO DOES. The music is
    what reached the timeline, and a renderer commits a little ahead of the
    frame it has written, so an exact-length file's .mid can run a second or two
    past its last frame.


WHAT THIS PACKAGE DOES NOT DO
================================================================================
  * It does not register an instrument library for you, ever.
  * It does not download a model, and it does not need Python. It carries the
    ADAPTERS that play a model - SkyTNTMusicGenerator and MuPTMusicGenerator -
    and no model: the files are yours to obtain, and the adapter is pointed at
    them. Nor does it reference a model store; an application that keeps models
    in one asks the store for paths and hands them over.
  * IT DOES NOT SOUND LIKE RECORDED INSTRUMENTS through "ModestSynthGm". That
    library is SYNTHESIZED - oscillators, envelopes and tables, with no
    recordings anywhere - so it sounds like a good synthesizer, and it is
    strongest where synthesis is strong: pads, bells, celeste, electric pianos,
    organs, plucked strings and choir textures. A solo violin or a concert grand
    is where it is weakest. For recorded instruments, reference
    CodeBrix.Audio.Samples.FluidR3Gm, or point a SoundFont library at a .sf2 of
    your own, and change one line.
  * A GENERATOR does not loop: one call is one pass, and one call does not place
    itself on a timeline or join itself to anything. A SESSION does all of that
    for you - looping, bar lines and seams.
  * It does not promise that a join between two segments is inaudible.
  * It does not promise gapless music on a machine that generates slower than it
    plays. It measures that, reports it, and degrades on purpose.
  * It does not crossfade a primed seam or a follow-up prompt. Only a FRESH
    seam is crossfaded, and only when SeamCrossfade is set.
  * It does not ENCODE audio itself. A render produces float samples and hands
    them to whatever writer CodeBrix.Audio's registry has for the extension, so
    the formats you can write are the ones your application has registered.
    WAV and AIFF are built in and Opus comes from its own package; there is no
    MP3, FLAC or Vorbis writer in this family to register.
  * It does not resample. A render is written at
    MusicGenerationOptions.SampleRate, whatever the format's usual rate is.
  * It does not read prose. Neither model family does, so free text is refused
    by both, and a CHARACTER WORD is only honoured where it names a tempo or a
    mode - anything else is refused by name rather than quietly dropped.
  * It does not choose sounds from a prompt. What plays a part is the
    RENDITION's business: a generator writes parts, a rendition voices them.
  * It does not guess. A request it cannot honour is refused by name.


WORKING EXAMPLES ON GITHUB
================================================================================
Every feature area above is exercised by a test file:

  https://github.com/ellisnet/CodeBrix.Audio.MusicGeneration/tree/main/tests/CodeBrix.Audio.MusicGeneration.Tests

  MusicGeneratorRegistryTests.cs       registering, resolving, the reserved
                                       names, and the exact error wording
  MusicGeneratorCapabilitiesTests.cs   what a request relies on, and how a
                                       refusal is worded
  MusicRequestTests.cs                 the request and its parts
  GeneratedMusicEventTests.cs          events, settled ticks and horizon-only
                                       items
  ReplayMusicGeneratorTests.cs         the four built-ins, ticks, order, the
                                       settled tick, two identical passes,
                                       loading and release
  ReplayPacingTests.cs                 pacing above and below real time, pacing
                                       switched off, and cancellation
  ConsumerReplayTests.cs               your own MIDI and your own ABC, and what
                                       a malformed piece says
  MusicSessionTests.cs                 the session: what plays, what is
                                       refused, and what it reports
  MusicSessionAudioTests.cs            end to end with no audio device - real
                                       samples, and a program change re-voicing
                                       a part part-way through
  RenditionVoicerTests.cs              every built-in rendition against the
                                       instrument and the gain it was rated at,
                                       and the whole automatic rule
  MusicRenditionRegistryTests.cs       the rendition table, and your own in it
  MusicEngineTests.cs                  the commit window, a settled rest, and
                                       how a piece ends
  MusicEngineLifecycleTests.cs         keeping going, the seam levers, the
                                       generate-ahead window, the
                                       segment-at-a-time fallback and a
                                       follow-up prompt taking over
  MusicEngineSeamTests.cs              primed, fresh and alternating seams,
                                       and where a crossfaded seam lands
  SeamCrossfadeMixerTests.cs           the crossfade frame by frame, and which
                                       instruments each message reaches
  MusicSessionCrossfadeTests.cs        a crossfade heard live and rendered
  MusicSessionLifecycleTests.cs        the same, as an application meets it:
                                       follow-up prompts, preload and release,
                                       the thread count and the diagnostics
  MusicEngineNeverLateTests.cs         music held back to a later bar line: a
                                       rest, a stall under a note that is still
                                       sounding, whole bars of the metre in
                                       force, and a follow-up prompt through
                                       both of them
  ChannelStateTests.cs                 a part's settings reaching it late, and
                                       following it across a re-voicing
  ReorderBufferTests.cs                out-of-order events, settled ticks and
                                       where a segment sits on the timeline
  MusicRenderTests.cs                  rendering to a file and to a stream: the
                                       exact length of each ending, the formats,
                                       what is refused and when, progress,
                                       cancellation, two identical renders, and
                                       a render alongside music that is playing
  MusicRenderFadeTests.cs              the shape of a fade in a file that was
                                       really rendered, asserted sample by
                                       sample against the curve itself
  MusicFadeTests.cs                    the curves as functions, and where the
                                       default fade lives
  OpusRenderTests.cs                   .opus through the writer registry, and a
                                       stream that cannot seek
  MusicEngineOfflineTests.cs           the two switches a render sets, and what
                                       each of them turns off
  LongRenderTests.cs                   six minutes twenty-five seconds with a
                                       fade, as .wav and as .opus (gated by
                                       CODEBRIX_AUDIO_RUN_LONG_RENDER_TESTS)
  SkyTNTRequestMapperTests.cs          a request turned into that model's own
                                       options, field by field, the drum kit
                                       from all three places it can come from,
                                       and every refusal by name
  SkyTNTEventMapperTests.cs            the channel rule, percussion, note
                                       lengths and tick rescaling
  SkyTNTPassTests.cs                   whole bars, settled ticks, the origin of
                                       a continuation and the cut tail
  SkyTNTMusicGeneratorTests.cs         what it loads and when, what it honours
                                       and refuses, and the unusable-folder
                                       error
  MuPTSmtAbcTests.cs                   the model's merged notation turned into
                                       standard ABC, held to every tune of the
                                       listening sessions
  MuPTSlicePathTests.cs                notation becoming music WHILE IT IS
                                       BEING WRITTEN, proved identical to
                                       converting the finished text
  MuPTPromptTests.cs                   the header a musical intent becomes, the
                                       eight preset prompts, and the escape
                                       hatch winning
  MuPTOpeningTests.cs                  the two-part opening a voice count
                                       becomes, held to the openings the rated
                                       duets used
  MuPTMusicGeneratorTests.cs           the same questions as its counterpart,
                                       for the notation model
  MusicCharacterWordsTests.cs          the character-word table, the order the
                                       words are read in, and the refusal
  MusicPresetTests.cs                  every preset: what it asks for, which
                                       generator accepts it, and the replay
                                       refusing it by name
  StreamingDefaultsTests.cs            every starting number, and the thread
                                       count as a SHARE of whatever machine
                                       runs the test
  MuPTLiveTests.cs                     real MuPT from the model NuGet's copied
                                       assets, a few bars at a time
  SkyTNTLiveTests.cs                   real SkyTNT from its model NuGet,
                                       a few dozen events at a time
  PresetLiveTests.cs                   every preset against the model it was
                                       written for, including a kit asked for
                                       per request (both model NuGets
                                       run in the ordinary suite)
  ModelDoneCriterionTests.cs           the plan's own done criterion with a
                                       model behind it (gated by time,
                                       using both model NuGets)
  ListeningRenderTests.cs              writes the listening set and its index -
                                       every preset, the seam set and a
                                       follow-up per model (gated by
                                       CODEBRIX_AUDIO_RUN_LISTENING_RENDERS)


QUICK REFERENCE CARD
================================================================================
    GeneralMidiInstrumentLibrary.Register();            // you must, to hear it
    InstrumentLibraryRegistry.SetDefault("FluidR3Gm");  // first registered wins

    using var music = new MusicSession();               // nothing specified
    using var music = new MusicSession(new MusicGenerationOptions {
        Generator = "MyGenerator", InstrumentLibrary = "ModestSynthGm",
        Rendition = "AmbientDuet", ApplicationOwnsAudioOutput = false });
    music.Play();  music.Stop();  music.Play();  music.Dispose();
    music.Position; music.IsStarved; music.IsFinished; music.Renderer;
    music.Renderer.Render(left, right);                 // you own the output
    music.ActiveSource.GeneratorName | .IsReplay | .InstrumentLibraryName
                     | .RenditionName | .Voicing.Parts | .Voicing.Diagnostics

    music.FollowUp(new MusicRequest { Text = "something darker" });  // if it
                                                          // honours free text
    music.FollowUp(new MusicRequest(), "MyOtherGenerator");    // and change it
    await music.PreloadAsync(token);  music.Release();  // Dispose frees nothing

    music.Diagnostics.RealTimeFactor          // double?; 1 is real time, null
                                              // means not measured yet
                    .Mode | .IsSegmentAtATime | .Lead
                    .StarvationGapCount | .SegmentCount | .LateEventCount
                    .HoldCount | .HeldBarCount                // rests inserted
                    .GeneratingSegmentKind | .GeneratingSegmentIsPrimed
                    .PrimedSegmentCount | .FreshSegmentCount
                    .CrossfadeCount | .ShortenedCrossfadeCount
                    .SkippedLeadingBarCount | .EmptyPassCount
                    .SessionTempo | .CarriedTempoCount | .AdoptedTempoCount

    new MusicGenerationOptions {
        Preroll = TimeSpan.FromSeconds(5),             // before it starts
        GenerateAhead = TimeSpan.FromSeconds(30),      // seconds of MUSIC
        EndOfPiece = EndOfPiecePolicy.KeepGenerating,  // or .Stop
        SegmentPriming = SegmentPriming.Primed,        // | Fresh | Alternate
        SeamCrossfade = TimeSpan.Zero,                 // fresh seams only
        SeamCrossfadeCurve = MusicFadeCurve.EqualPower,
        TempoPolicy = SessionTempoPolicy.Adopt,        // | Carry | CarryOutsideBand
        TempoBand = 0.15, SessionBeatsPerMinute = null,
        SampleRate = 44100, MasterVolume = 1.0F,       // your output's rate
        InferenceThreadCount = null }                  // the model's default

    await music.RenderToFileAsync("theme.wav", token);         // one pass
    await music.RenderToFileAsync(path, render, token);
    await music.RenderToFileAsync(path, render, progress, token);
    await music.RenderToStreamAsync(stream, ".opus", render, token);

    new MusicRenderOptions {
        TargetLength = TimeSpan.FromSeconds(385),   // null = however long it is
        Ending = RenderEnding.Fade,                 // HardCut | NaturalStop
        FadeLength = MusicFade.DefaultLength,       // six seconds
        FadeCurve = MusicFade.DefaultCurve,         // EasedDecibels
        RingOut = TimeSpan.FromSeconds(5),          // at most
        BitsPerSample = 16 }                        // null = the format's own

    MusicFade.GainAt(curve, progress) | .DecibelsAt(curve, progress)
    MusicFadeCurve.EasedDecibels | StraightLine | EqualPower

    result.Duration | .FrameCount | .SampleRate | .Channels | .Format | .Path
          .ReachedTargetLength | .TargetLength | .Ending | .FadeLength
          .Source | .SegmentCount | .SeamTicks | .Diagnostics
          .Music                                    // MidiFile.Export it

    BuiltInRenditions.Automatic | AmbientDuet | MelodyOverPad
                     | VibesAndStrings | HarpAndCello | Neutral
    new MusicRendition(name, description).Voices.Add(
        new RenditionVoice(GeneralMidiProgram.Celesta, 0.9F));
    MusicRenditionRegistry.Register(rendition) | .Resolve(name) | .RegisteredNames

    MusicGeneratorRegistry.Resolve(null)                // the embedded replay
    MusicGeneratorRegistry.Resolve("MyGenerator")       // by name
    MusicGeneratorRegistry.Register(generator)          // loads nothing
    MusicGeneratorRegistry.RegisteredNames              // built-ins first

    EmbeddedReplay.Midi | MidiSecond | Abc | AbcSecond  // the reserved names

    ReplayMusicGenerator.FromMidiFile(name, path)
    ReplayMusicGenerator.FromMidiStream(name, stream)
    ReplayMusicGenerator.FromMidiEvents(name, events)
    ReplayMusicGenerator.FromAbc(name, abcText)
        .PacingRate = 1.0                               // real time
        .TimeProvider = myClock                         // tests

    new MusicRequest {
        TicksPerQuarterNote = 480,                      // you fix it
        PaceInRealTime = false,                         // offline render
        Intent = new MusicIntent { ... },
        Controls = new MusicGenerationControls { ... },
        Continuation = new MusicContinuation { ... } }

    new MusicIntent {
        Key = "A", Mode = MusicMode.Minor,              // -> K:Am, a signature
        Meter = new MusicMeter(3, 4),
        UnitNoteLength = MusicNoteLength.EighthNote,    // ABC's L:
        BeatsPerMinute = 108.0,                         // QUARTER notes a minute
        VoiceCount = 2,                                 // MuPT: writes a duet
        DrumKit = 24,                                   // or MusicIntent.NoDrumKit
        TargetLength = TimeSpan.FromMinutes(6) }        // a render honours it
    intent.CharacterWords.Add("gentle");                // a tempo and/or a mode

    MusicCharacterWords.Known | .IsKnown(word) | .Find(word)
    MusicCharacterWords.Resolve(words, generatorName)   // refuses by name
    MusicCharacterWords.ApplyTo(request, generatorName) // for an adapter

    new MuPTMusicGenerator("MuPT", "/models/model.gguf")
    new SkyTNTMusicGenerator("SkyTNT", "/models/skytnt")           // a folder
    new SkyTNTMusicGenerator("SkyTNT", nameToPathMap)              // or a map
        new MuPTGeneratorOptions { MaximumTokensPerPass = 448, ContextTokens = 2048 }
        new SkyTNTGeneratorOptions { MaximumEventsPerPass = 1000, DrumKit = null }
    generator.LoadedThreadCount                         // int?, null unloaded

    MuPTPresets.ReelInGMinor | JigInD | WaltzInAMinor | AirInDMixolydian
              | HornpipeInG | OpenInC | DuetInC | WaltzDuetInAMinor
    SkyTNTPresets.FourOnTheFloor | ClubArrangement | AmbientElectronica
                                                        // accepted presets
    MuPTPresets.All | .Find(name);  SkyTNTPresets.All | .Find(name)
    preset.CreateRequest() | .SuggestedRendition | .IsProvisional | .Family

    await foreach (var g in generator.GenerateAsync(request, token)) {
        g.HasEvent; g.Event; g.SettledThroughTick; g.HasSettled; }

    GeneratedMusicEvent.FromMidiEvent(midiEvent, settledThroughTick)
    GeneratedMusicEvent.SettledThrough(tick)            // settled silence
    GeneratedMusicEvent.NothingSettled                  // nothing yet

    generator.Honours                                   // what it acts on
    MusicGeneratorCapabilities.UnhonouredFeatures(generator, request)
    MusicGeneratorCapabilities.EnsureHonoured(generator, request)
    await generator.PreloadAsync(token);  generator.Release();

================================================================================
