# CodeBrix.Audio.MusicGeneration Blueprints: Generating music

These recipes cover music that a model writes while it plays: registering a model
package and an instrument library and naming both on a session, keeping the music
going for as long as someone is listening, deciding how each new segment starts,
crossfading the joins between pieces, holding one pulse across pieces a model
chose different tempos for, picking a style, and keeping a copy of what was
written. They are built on the two models that come as packages of their own,
SkyTNT and MuPT, and every setting they recommend is one that was listened to on
real instruments before it was written down. Reach for this file when you want
background music that never runs out, music that follows a game or an
installation for as long as it runs, or a starting point you already know sounds
right.

The last section is a different job and is kept apart on purpose: rendering a
long, varied piece ahead of time with a model that is too slow to play live, which
you stage yourself because no package ships it. Nothing in the continuous-music
sections applies to it unless that section says so.

This repository has no sample application for these recipes to quote, so they
follow a different convention from blueprints that are mined from one: every code
block here is a whole C# file, complete enough to compile against the packages the
text names, and each was compiled and run before it was written down. A block
that calls a type from an earlier block says which one. The files put their types
in the global namespace to stay short; give them your own.

The continuous-music sections use these packages, by identifier:

- `CodeBrix.Audio.MusicGeneration.MitLicenseForever` - the sessions, the engine and the renderer
- `CodeBrix.Audio.MusicGeneration.SkyTNT.ApacheLicenseForever` - the SkyTNT model and its registration
- `CodeBrix.Audio.MusicGeneration.MuPT.ApacheLicenseForever` - the MuPT model and its registration
- `CodeBrix.Audio.Samples.FluidR3Gm.MitLicenseForever` - optional: recorded General MIDI instruments

## Recipes in this file

Continuous music with SkyTNT and MuPT:

- [Register an instrument library and a model then name both on a session](#register-an-instrument-library-and-a-model-then-name-both-on-a-session)
- [Keep the music playing for as long as the listener stays](#keep-the-music-playing-for-as-long-as-the-listener-stays)
- [Choose how each new segment starts](#choose-how-each-new-segment-starts)
- [Crossfade the seams between fresh pieces](#crossfade-the-seams-between-fresh-pieces)
- [Hold one pulse across fresh pieces](#hold-one-pulse-across-fresh-pieces)
- [Pick a style from a preset or write the request yourself](#pick-a-style-from-a-preset-or-write-the-request-yourself)
- [Record what was generated as MIDI](#record-what-was-generated-as-midi)
- [Know where continuous generation runs out](#know-where-continuous-generation-runs-out)

Rendering ahead of time with a model you stage yourself (the last section, a separate job):

- [Stage the music model and its text model with ModelManager](#stage-the-music-model-and-its-text-model-with-modelmanager)
- [Register the staged model and ask for music by attribute](#register-the-staged-model-and-ask-for-music-by-attribute)
- [Render a piece of any length](#render-a-piece-of-any-length)
- [Play the rendered piece later through a session](#play-the-rendered-piece-later-through-a-session)

## Related documentation

- [README.md](README.md) - the package overview, and the short version of priming, crossfades and the session tempo
- [AGENT-README.txt](AGENT-README.txt) - the complete reference: "THE LIFE OF A STREAM" for every seam rule, "THE PRESETS", "WHEN YOUR APPLICATION OWNS THE AUDIO OUTPUT", "RENDERING TO A FILE" and "WRITING YOUR OWN GENERATOR"
- The model packages' own AGENT-README files - where each package puts its model in your build output, and how to preload and release the shared instance

---

## Continuous music with SkyTNT and MuPT

### Register an instrument library and a model then name both on a session

**When you want this.** You want music from a model rather than the placeholder
piece the core package plays when nothing is named, and you want to choose what it
sounds like without writing an instrument of your own.

**The shape.** Two kinds of registration at start-up, then three strings on the
session options. Registering an instrument library or a model makes a name
resolvable; it loads nothing and chooses nothing. The session plays what its
options NAME: `Generator`, `InstrumentLibrary` and `Rendition` are the three
strings that do all the work.

**Code.**

Register everything the application might ask for, once, early. Each call is
idempotent, and a model package's registration costs nothing until the model is
asked for:

```csharp
using CodeBrix.Audio.ModestSynth;
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.MusicGeneration.MuPT;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.SkyTNT;
using CodeBrix.Audio.Samples.FluidR3Gm;

public static class MusicSetup
{
    // Call once at start-up. Registering makes a name resolvable; it loads nothing
    // and it chooses nothing.
    public static void RegisterEverything()
    {
        GeneralMidiInstrumentLibrary.Register();    // "ModestSynthGm": synthesized, nothing to ship
        FluidR3GmInstrumentLibrary.Register();      // "FluidR3Gm": recorded instruments, a large package
        SkyTNTModel.Register();                     // "SkyTNT": writes MIDI events
        MuPTModel.Register();                       // "MuPT": writes ABC notation
    }

    // The three strings that decide what is heard. Name all three.
    public static MusicGenerationOptions CreateOptions(string generator, string instrumentLibrary) =>
        new MusicGenerationOptions
        {
            Generator = generator,                      // SkyTNTModel.GeneratorName or MuPTModel.GeneratorName
            InstrumentLibrary = instrumentLibrary,      // GeneralMidiInstrumentLibrary.LibraryName or FluidR3GmInstrumentLibrary.LibraryName
            Rendition = BuiltInRenditions.Automatic,    // or a preset's suggested rendition
        };
}
```

Then build a session from those options, take the model's load at a moment you
choose, and play. `ActiveSource` says what is really making the music, which is the
line to log when something does not sound the way you expected:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MusicGeneration;

public static class FirstMusic
{
    public static async Task<MusicSession> StartAsync(MusicGenerationOptions options,
        CancellationToken cancellationToken)
    {
        var music = new MusicSession(options);

        // A model loads on first use. Take that cost now, while nothing is expected to sound.
        await music.PreloadAsync(cancellationToken);

        music.Play();                               // opens the audio device
        Console.WriteLine(music.ActiveSource);      // e.g. SkyTNT (SkyTNT) through ModestSynthGm, voiced by Automatic
        return music;
    }
}
```

A caller hands the options from `MusicSetup` to `FirstMusic`, and keeps the returned
session alive for as long as the music should play.

**Why.** Registering is not specifying. A registered model that no session names
never plays and never loads, so an application can register both models and every
library it ships with at start-up and decide per screen what to hear. The choice
of instrument library is the biggest single decision about how the music sounds:

- `ModestSynthGm` is the zero-asset default. It comes with the core package's
  dependencies, synthesizes every General MIDI program and the drum kit, and adds
  nothing to what you ship. It is strongest on pads, bells and electric pianos.
- `FluidR3Gm` is recorded instruments. In listening it sounded much better than
  the synthesized set on the same music, at the cost of a package of well over a
  hundred megabytes. Choose it where that download is acceptable.

**Pitfalls.**
- The first library registered is the default for a session that names none. Name
  the library whenever more than one is registered, or the sound depends on which
  start-up path ran first.
- Registering a model package's generator twice is harmless; registering a
  different generator under a name already taken throws. Give a generator of your
  own a name of its own.
- Disposing a session does not unload the model. It belongs to the registry and
  serves every later session; `Release()` on the session gives the memory back.

**Verify.** `ActiveSource` names the generator, the library and the rendition you
asked for, and `ActiveSource.IsReplay` is false. If it names the embedded replay,
the session was never told which generator to use.

### Keep the music playing for as long as the listener stays

**When you want this.** The music has no planned end: it plays under a game, a
waiting room or an installation until the application stops it.

**The shape.** One session, left alone. The default end-of-piece policy asks the
same model for another segment whenever a pass ends, and places it at the next bar
line. Three numbers shape the stream, and the application watches a cheap snapshot
of diagnostics rather than the model.

**Code.**

The defaults already describe endless music; writing them out shows what they are:

```csharp
using System;
using CodeBrix.Audio.ModestSynth;
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.MusicGeneration.Presets;
using CodeBrix.Audio.MusicGeneration.SkyTNT;

public static class EndlessMusic
{
    public static MusicGenerationOptions CreateOptions() => new MusicGenerationOptions
    {
        Generator = SkyTNTModel.GeneratorName,
        InstrumentLibrary = GeneralMidiInstrumentLibrary.LibraryName,
        Request = SkyTNTPresets.AmbientElectronica.CreateRequest(),

        // The defaults, written out.
        EndOfPiece = EndOfPiecePolicy.KeepGenerating,       // ask again at every end of a pass
        Preroll = MusicGenerationOptions.DefaultPreroll,     // settled music before the head moves (5 s)
        GenerateAhead = MusicGenerationOptions.DefaultGenerateAhead, // stop generating this far ahead (30 s)
    };
}
```

Watch the stream from a timer or a game loop. The snapshot is cheap to read every
frame, and reporting only what changed keeps a log readable over an hour:

```csharp
using System;
using System.Globalization;
using CodeBrix.Audio.MusicGeneration;

// Reports what changed since the last Check. Call it from a timer or once a frame.
public sealed class MusicWatch
{
    private readonly MusicSession music;
    private readonly Action<string> report;
    private int segments;
    private int gaps;
    private MusicDeliveryMode? mode;

    public MusicWatch(MusicSession music, Action<string> report)
    {
        this.music = music ?? throw new ArgumentNullException(nameof(music));
        this.report = report ?? throw new ArgumentNullException(nameof(report));
    }

    public void Check()
    {
        var diagnostics = music.Diagnostics;

        if (diagnostics.SegmentCount != segments)
        {
            segments = diagnostics.SegmentCount;
            report($"segment {segments} is generating ({diagnostics.GeneratingSegmentKind}), " +
                   $"{diagnostics.Lead.TotalSeconds:0} s of music ready");
        }

        if (diagnostics.StarvationGapCount != gaps)
        {
            gaps = diagnostics.StarvationGapCount;
            report($"the music ran dry ({gaps} time(s) so far) and waits for a pre-roll");
        }

        if (mode != diagnostics.Mode)
        {
            mode = diagnostics.Mode;
            var factor = diagnostics.RealTimeFactor.HasValue
                ? diagnostics.RealTimeFactor.Value.ToString("0.00", CultureInfo.InvariantCulture)
                : "not measured yet";
            report($"delivery is {mode}, real-time factor {factor}");
        }
    }
}
```

**Why.** A SEGMENT is one pass of the model: everything it writes between one
request and its own end, which is its length cap or the end of the piece it chose
to write. At the presets' density a SkyTNT pass is roughly a minute of music; a
MuPT tune runs from under half a minute to about a minute. When a pass ends, the
engine asks the same model again, with a new seed derived from the one you gave,
and the new segment
starts on the next bar line with the tempo, metre, key and each part's instrument
carried across. How that next request is built is the subject of the next recipe.

Generation runs in bursts. The engine pulls from the model until `GenerateAhead`
of settled music is waiting in front of the play head, stops, and starts again
when that falls to half, so the model costs nothing between bursts. The play head
waits for `Preroll` of settled music before it moves, at the start and after
running dry. That wait is SILENCE, on purpose: the application comes first and the
music waits, and a rest sounds intentional where a stall mid-phrase sounds broken.

**Pitfalls.**
- A bigger `GenerateAhead` cannot rescue a machine that generates slower than it
  plays; it only postpones the gap. A longer `Preroll` is the answer to a model
  that is fast on average but bursty.
- `RealTimeFactor` is null until there has been enough generating to measure, and a
  fast model can keep it null for a long time. Null means "not measured yet",
  never "slow".
- The wait for the first pre-roll is not counted as a gap. A healthy start reports
  zero starvation gaps.

**Verify.** Over a long run `StarvationGapCount` stays at zero, `Mode` settles on
`Streaming`, and `SegmentCount` climbs each time a pass ends, typically every half
minute to a minute.

### Choose how each new segment starts

**When you want this.** The music keeps going, but it keeps saying the same thing,
or a model occasionally has nothing to add when it is shown what it just wrote.

**The shape.** One option, `SegmentPriming`, decides how every re-prompted segment
starts. `Primed`, the default, shows the model the last bars it wrote and asks it to
carry on. `Fresh` asks for a new piece in the same character, with nothing of the
music so far in view. `Alternate` takes turns: primed, fresh, primed, fresh.

**Code.**

The two configurations below are the ones listening settled on, one per model. The
crossfade and the tempo options of the next two recipes go on top of them:

```csharp
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.MusicGeneration.MuPT;
using CodeBrix.Audio.MusicGeneration.Presets;
using CodeBrix.Audio.MusicGeneration.SkyTNT;

public static class SeamPriming
{
    // SkyTNT: primed and fresh segments take turns, so the music keeps some continuity
    // and still moves on.
    public static MusicGenerationOptions SkyTNTTakingTurns(string instrumentLibrary) =>
        new MusicGenerationOptions
        {
            Generator = SkyTNTModel.GeneratorName,
            InstrumentLibrary = instrumentLibrary,
            Rendition = SkyTNTPresets.ClubArrangement.SuggestedRendition,
            Request = SkyTNTPresets.ClubArrangement.CreateRequest(),
            SegmentPriming = SegmentPriming.Alternate,
        };

    // MuPT: every segment is a new tune.
    public static MusicGenerationOptions MuPTFreshTunes(string instrumentLibrary) =>
        new MusicGenerationOptions
        {
            Generator = MuPTModel.GeneratorName,
            InstrumentLibrary = instrumentLibrary,
            Rendition = MuPTPresets.ReelInGMinor.SuggestedRendition,
            Request = MuPTPresets.ReelInGMinor.CreateRequest(),
            SegmentPriming = SegmentPriming.Fresh,
        };
}
```

**Why.** The two models answer a primer very differently, and the setting follows
from what each one does.

SkyTNT is led so strongly by the bars it is shown that a primed continuation writes
the same material again, segment after segment. Every segment already gets a new
derived seed, and in listening that made no audible difference: under a primer, the
primer decides the music and the seed does not. So a seed alone does not break the
repetition. What does is a fresh piece: in listening the music came alive the moment
the first fresh segment arrived. `Alternate` keeps that variety while every other
segment still grows out of what was just heard, and it was the setting that
listening kept.

MuPT writes short, complete tunes. Shown the last bars of a tune that has already
closed, it can decide there is nothing more to write and come back empty. The
engine does not let that stop the music: an empty pass is asked for again at the
same bar line as a fresh piece on a new seed, and `Diagnostics.EmptyPassCount`
counts it. Only three empty passes in a row stop the music, and then
`GenerationError` says so. With MuPT, `Fresh` avoids the question altogether and is
the recommended setting: every segment is a new tune in the preset's character.

**Pitfalls.**
- A generator that cannot be shown the music so far starts every segment fresh,
  whatever this option says.
- A follow-up prompt restarts the `Alternate` count, so the first segment after it is
  primed.
- A fresh seam is a change of piece. Without the crossfade of the next recipe it cuts
  in at the bar line, and some fresh pieces open with a bar or two of silence.

**Verify.** `Diagnostics.GeneratingSegmentKind` names what is being generated now
(`FirstPiece`, `Primed`, `Fresh` or `FollowUp`), and under `Alternate`
`PrimedSegmentCount` and `FreshSegmentCount` take turns to grow.

### Crossfade the seams between fresh pieces

**When you want this.** Fresh segments bring the variety, and you want each new
piece to take over from the old one instead of cutting in at a bar line.

**The shape.** Two options on top of the priming choice. `SeamCrossfade` is how long
the outgoing and incoming pieces overlap at a fresh seam; `SeamCrossfadeCurve` is the
shape of the fade. Primed seams and follow-up prompts are not crossfaded: they keep
the bar-line join, because a primed segment is the same piece carrying on.

**Code.**

This builds on either configuration from `SeamPriming` in the previous recipe:

```csharp
using System;
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.MusicGeneration.Rendering;

public static class SeamCrossfades
{
    // Four seconds, equal power: the setting listening settled on for both models.
    public static MusicGenerationOptions WithCrossfade(MusicGenerationOptions options)
    {
        options.SeamCrossfade = TimeSpan.FromSeconds(4.0);
        options.SeamCrossfadeCurve = MusicFadeCurve.EqualPower;     // the default curve
        return options;
    }

    public static string Describe(MusicDiagnostics diagnostics) =>
        $"{diagnostics.CrossfadeCount} crossfade(s), " +
        $"{diagnostics.ShortenedCrossfadeCount} shortened, " +
        $"{diagnostics.SkippedLeadingBarCount} silent opening bar(s) skipped";
}
```

The MuPT configuration with this crossfade, played through `FluidR3Gm`, is the one in
which, in listening, no transition could be heard at all; the same four seconds over
SkyTNT's alternating segments were judged great.

**Why.** The incoming piece starts that long before the outgoing piece's last bar
line, on a beat of the outgoing piece, while the outgoing piece plays on to its end.
The two are mixed along the curve: `EqualPower` keeps the loudness steady through
unrelated material, and `StraightLine` keeps the sum of the gains at one, which suits
two pieces so alike that equal power would swell in the middle. When the fade is over
the outgoing instruments are let go.

The incoming piece is voiced by instruments of its own, built from the same library
and rendition, so with the automatic rendition a part can come in on a different
voice than it had before. A fresh piece that opens with bars containing no notes at
all has those bars skipped at a crossfade, so the old piece fades into music rather
than into silence; the tempo, metre and instruments stated in those bars still apply.

The fade never costs the music a gap. The incoming piece is placed early only once it
has generated the whole fade plus its own pre-roll. When it has not by the time the
outgoing music must be committed, the fade is SHORTENED, down to a hard join if need
be, and `ShortenedCrossfadeCount` counts it. With the settings above, fades were full
length throughout listening, except while other heavy work was running on the same
machine. A render to a file crossfades the same way and, having no play head to
protect, waits for the whole fade instead of shortening it.

**Pitfalls.**
- The skip removes only bars with no notes in them. A fresh piece that opens thinly,
  a bar of a few drum hits before the arrangement arrives, keeps that bar. Once the
  fade has taken the old piece away, the listener hears the thin bar as a lull. It is
  the model's opening, not a dropout, and the diagnostics show no gap for it.
- While the engine is delivering a whole segment at a time (see the last recipe of
  this section), fresh seams are hard joins.
- A fade longer than the pieces a model writes makes little sense. Four seconds sits
  comfortably inside MuPT's shortest tunes.

**Verify.** `CrossfadeCount` climbs at every fresh seam and `ShortenedCrossfadeCount`
stays at zero. A shortened fade on a machine that is otherwise idle means the model
is barely keeping up; see the last recipe of this section.

### Hold one pulse across fresh pieces

**When you want this.** You want the whole session at one tempo, one you choose or
the one it started at, even though every fresh piece is a new request that could
arrive at a tempo of its own.

**The shape.** `TempoPolicy` decides what happens to a fresh piece's tempo. `Adopt`,
the default, lets every piece keep its own. `Carry` plays every fresh piece at the
session tempo. `CarryOutsideBand` lets a piece within `TempoBand` of the session
tempo keep its own and carries the rest. The session tempo is `SessionBeatsPerMinute`
when you set it, otherwise the request's intended tempo, otherwise the first piece's.

**Code.**

Both helpers go on top of any of the configurations above:

```csharp
using System.Globalization;
using CodeBrix.Audio.MusicGeneration;

public static class OnePulse
{
    // The whole session at a tempo you choose, whatever the request or the model says.
    public static MusicGenerationOptions AtTempo(MusicGenerationOptions options, double beatsPerMinute)
    {
        options.TempoPolicy = SessionTempoPolicy.Carry;
        options.SessionBeatsPerMinute = beatsPerMinute;     // never sent to the model
        return options;
    }

    // Pieces close to the session tempo keep their own; only the ones that would lurch are carried.
    public static MusicGenerationOptions WithinBand(MusicGenerationOptions options)
    {
        options.TempoPolicy = SessionTempoPolicy.CarryOutsideBand;
        options.TempoBand = MusicGenerationOptions.DefaultTempoBand;     // 15% either side
        return options;
    }

    public static string Describe(MusicDiagnostics diagnostics) =>
        (diagnostics.SessionTempo.HasValue
            ? diagnostics.SessionTempo.Value.ToString("0.0", CultureInfo.InvariantCulture) + " bpm"
            : "no session tempo") +
        $", {diagnostics.CarriedTempoCount} carried, {diagnostics.AdoptedTempoCount} adopted";
}
```

Wrapped around the MuPT configuration from "Crossfade the seams between fresh
pieces", `AtTempo` with 90 plays every fresh reel a little slower than its preset
writes it, and each one at the same pulse as the last.

**Why.** A CARRIED piece keeps every note where the model wrote it: all of its own
tempo events are dropped and the session tempo is stated where it starts, the start
of the crossfade or the bar line of a hard join. It is simply heard a little faster
or slower, stretched rather than rewritten, and its bar lines do not move. The tempo a
piece is judged by is the one in force where its first note sounds, not a default
stated at its first tick. A session tempo given up front is imposed on the first piece
as well.

SkyTNT and MuPT keep to a tempo their request asks for, and every preset asks for
one, so with them the pulse does not lurch in the first place. With these two models
the policy is mostly the way to choose the pulse yourself: `SessionBeatsPerMinute` is
never sent to the model, so it works with any generator, including one that refuses a
tempo in its request, while the request's own intended tempo is a request TO the model
and a model that does not honour a tempo refuses it by name. `CarryOutsideBand` earns
its place with a generator that picks a new tempo for every piece, such as one of your
own, and it is what the render in the last section of this file relies on.

**Pitfalls.**
- Primed segments and follow-up prompts are never touched: a primed segment already
  carries the tempo on, and a follow-up is a new request the application asked for.
- A band of zero is `Carry` in all but name; a band of one lets nearly everything
  through. The default of 15% keeps pieces that are close and fixes the ones that
  would lurch.
- A carried piece is stretched, not re-composed. Carrying a piece a long way from the
  tempo it was written at can make it sound sluggish or rushed; choose a session tempo
  near what the request asks for.

**Verify.** `SessionTempo` reports the session tempo, and `CarriedTempoCount` and
`AdoptedTempoCount` say what happened to each fresh piece. Under `Adopt`,
`SessionTempo` is null.

### Pick a style from a preset or write the request yourself

**When you want this.** You want a particular kind of music, either from a starting
point that is known to sound right or from a request of your own.

**The shape.** A preset is a `MusicRequest` to start from, with the model family it
was written for and the rendition its music was rated through. It names no generator:
you still name one on the session. A request of your own says what the music should
BE, and each model turns that into what it really reads.

**Code.**

```csharp
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Presets;

public static class MusicStyles
{
    // A preset: its request, and the rendition its music was rated through.
    public static MusicGenerationOptions FromPreset(MusicPreset preset, string generator,
        string instrumentLibrary) => new MusicGenerationOptions
        {
            Generator = generator,
            InstrumentLibrary = instrumentLibrary,
            Rendition = preset.SuggestedRendition,  // null: voiced automatically
            Request = preset.CreateRequest(),       // a new request every time, yours to change
        };

    // SkyTNT reads instruments, a drum kit, a tempo, a metre, a key and a mode.
    public static MusicRequest SkyTNTRequest()
    {
        var request = new MusicRequest
        {
            Intent = new MusicIntent
            {
                Key = "D",
                Mode = MusicMode.Minor,
                Meter = MusicMeter.CommonTime,
                BeatsPerMinute = 100.0,
                DrumKit = SkyTNTPresets.ElectronicDrumKit,
            },
            Controls = new MusicGenerationControls { Temperature = 0.9, TopK = 40, TopP = 0.95 },
            Seed = 1234,
        };
        request.InstrumentHints.Add(GeneralMidiProgram.ElectricPiano1);
        request.InstrumentHints.Add(GeneralMidiProgram.SynthBass1);
        return request;
    }

    // MuPT reads a key, a mode, a metre, a tempo and up to two voices. It writes no
    // drums, and what plays each part is the rendition's business.
    public static MusicRequest MuPTRequest() => new MusicRequest
    {
        Intent = new MusicIntent
        {
            Key = "G",
            Mode = MusicMode.Major,
            Meter = new MusicMeter(6, 8),
            BeatsPerMinute = 110.0,                 // in the beat the metre is felt in
            VoiceCount = 2,
        },
        Seed = 1234,
    };

    // Ask before playing: an empty list means the generator acts on everything asked for.
    public static IReadOnlyList<string> WhatWouldBeRefused(string generatorName, MusicRequest request) =>
        MusicGeneratorCapabilities.NamesOf(MusicGeneratorCapabilities.UnhonouredFeatures(
            MusicGeneratorRegistry.Resolve(generatorName), request));
}
```

**Why.** The presets are the requests listening was done on, so they are the fastest
way to something that sounds right.

- SkyTNT presets are electronica: `AmbientElectronica` (two pads and a bell-like lead,
  no percussion, in four at 84), `ClubArrangement` (drums, synth bass, sawtooth lead
  and a warm pad, in four at 126) and `FourOnTheFloor` (a kick on every beat under a
  synth bass, in four at 126). They suggest no rendition, so their parts are voiced
  from the instruments the music asks for.
- MuPT presets are in the idiom ABC notation knows best, folk and dance tunes in parts:
  `ReelInGMinor`, `JigInD`, `HornpipeInG`, `WaltzInAMinor`, `AirInDMixolydian`,
  `OpenInC`, `DuetInC` and `WaltzDuetInAMinor`. Four of them carry a suggested
  rendition, and the two duets are the ones that write two parts. For reels, jigs,
  hornpipes, airs and waltzes, MuPT is the model to choose; for anything with drums or
  a synthesizer in it, choose SkyTNT.

A request of your own is checked before anything starts: a part of it the chosen
model does not act on is refused by name, not quietly dropped. SkyTNT refuses a voice
count, a unit note length, free text and a repetition penalty; MuPT refuses a drum kit,
instrument hints and free text. Character words such as "calm" or "driving", added to
`MusicIntent.CharacterWords`, fill in a tempo or a mode you left unsaid.

**Pitfalls.**
- A seed makes a request repeatable, but it is not a way to vary SkyTNT's primed
  segments; see "Choose how each new segment starts".
- `CreateRequest()` hands back a new request every call. Change your copy freely, and
  clone a request you are still editing before handing it to a session.
- A voice count is a number of parts, not a number of sounds. A rendition written for
  two parts is what makes a two-part piece sound like a duet.

**Verify.** `WhatWouldBeRefused` returns an empty list for the generator you are about
to name.

### Record what was generated as MIDI

**When you want this.** You want a copy of what a model wrote: to keep a piece you
liked, to look at a seam that sounded odd, or to play it again later without the model.

**The shape.** Two ways, for two jobs. For a live session, wrap the model in a small
generator of your own that passes every event through and writes each pass to a MIDI
file. For a whole piece at once, render it to an audio file and export the MIDI the
render hands back.

**Code.**

The wrapper is an ordinary `IMusicGenerator`. It refuses exactly what the model
refuses, loads what the model loads, and adds only the copy:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.MusicGeneration.Generation;

// Wraps a generator and writes every pass it makes to a MIDI file of its own.
// Register the wrapper once, under its own name, and name THAT on the session.
public sealed class MidiRecordingGenerator : IMusicGenerator
{
    private readonly IMusicGenerator inner;
    private readonly string folder;
    private int passes;

    public MidiRecordingGenerator(IMusicGenerator inner, string folder)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.folder = folder ?? throw new ArgumentNullException(nameof(folder));
        Directory.CreateDirectory(folder);
    }

    public string Name => inner.Name + ".Recorded";

    public string Family => inner.Family;

    public string Description => inner.Description + " (each pass recorded to MIDI)";

    public MusicRequestFeatures Honours => inner.Honours;

    public bool IsLoaded => inner.IsLoaded;

    public Task PreloadAsync(CancellationToken cancellationToken) => inner.PreloadAsync(cancellationToken);

    public void Release() => inner.Release();

    public async IAsyncEnumerable<GeneratedMusicEvent> GenerateAsync(MusicRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var pass = Interlocked.Increment(ref passes);
        var events = new List<MidiEvent>();

        try
        {
            await foreach (var item in inner.GenerateAsync(request, cancellationToken).ConfigureAwait(false))
            {
                if (item.HasEvent)
                {
                    events.Add(item.Event.Clone());     // the engine keeps the original
                }

                yield return item;
            }
        }
        finally
        {
            // A pass the session cancels (Stop, a follow-up) is saved as far as it got.
            Save(pass, request.TicksPerQuarterNote, events);
        }
    }

    private void Save(int pass, int ticksPerQuarterNote, List<MidiEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        var music = new MidiEventCollection(1, ticksPerQuarterNote);
        var track = music.AddTrack();

        foreach (var midiEvent in events)
        {
            track.Add(midiEvent);

            // A generated note carries its own length; a file needs its note-off as well.
            if (midiEvent is NoteOnEvent note && note.OffEvent != null)
            {
                track.Add(note.OffEvent);
            }
        }

        music.PrepareForExport();
        MidiFile.Export(Path.Combine(folder, $"pass-{pass:000}.mid"), music);
    }
}
```

Build it over the model package's shared instance, `SkyTNTModel.Instance` or
`MuPTModel.Instance`, register it once with `MusicGeneratorRegistry`, and name the
wrapper, not the model, as the session's generator.

For a whole piece, a render returns the music it rendered, which exports directly:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.MusicGeneration.Rendering;

public static class RenderedMusic
{
    // Renders the session's music to an audio file and writes its MIDI beside it.
    public static async Task<MusicRenderResult> RenderWithMidiAsync(MusicGenerationOptions options,
        string audioPath, TimeSpan length, CancellationToken cancellationToken)
    {
        using var music = new MusicSession(options);
        var result = await music.RenderToFileAsync(audioPath,
            new MusicRenderOptions { TargetLength = length }, cancellationToken);

        MidiFile.Export(Path.ChangeExtension(audioPath, ".mid"), result.Music);
        return result;
    }
}
```

**Why.** A generator's ticks are SEGMENT-RELATIVE: every pass starts at tick 0, and a
primed pass holds only the new music, never the bars it was shown. So the wrapper's
files are one per pass, each complete in itself, and they are the right tool for
looking at what the model wrote. They are not a timeline: laying them end to end
yourself would ignore the bar lines the engine placed them at, the holds it inserted
and the crossfades it made. The render's `MusicRenderResult.Music` is that timeline,
tick for tick as it was rendered, with the outgoing piece's last moments on a track of
their own at every crossfaded seam.

**Pitfalls.**
- Register the wrapper once. A second, different wrapper under the same name is a
  different generator, and registering it throws.
- Copy each event before keeping it. The engine goes on to use the one it was handed.
- A render's MIDI can run a second or two past the end of its audio, because a
  renderer commits a little ahead of the frame it has written.

**Verify.** The folder fills with one file per pass as the music plays; the render
leaves a `.mid` beside its audio file.

### Know where continuous generation runs out

**When you want this.** Before you ship continuous music to a machine you do not
control, or when something sounds wrong and you want to know whether it is the model,
the machine or the engine.

**The shape.** Nothing new to configure. The engine measures itself, degrades on
purpose, and says what it did in the diagnostics. The application reads them and
decides whether to stream or to render ahead.

**Code.**

```csharp
using CodeBrix.Audio.MusicGeneration;

public static class MusicHealth
{
    // One line an application can log, or act on, for the state of a session.
    public static string Assess(MusicSession music)
    {
        var diagnostics = music.Diagnostics;

        if (music.GenerationError != null)
        {
            return "Stopped: " + music.GenerationError.Message;
        }

        if (diagnostics.Mode == MusicDeliveryMode.SegmentAtATime)
        {
            return "Slower than real time: whole segments with rests between. Render ahead on this machine.";
        }

        if (diagnostics.StarvationGapCount > 0 && diagnostics.RealTimeFactor >= 1.0)
        {
            return "Fast enough, yet the music ran dry: look for other work on the processors, or lengthen the pre-roll.";
        }

        if (diagnostics.EmptyPassCount + diagnostics.FailedPassCount > 0)
        {
            return $"Streaming. {diagnostics.EmptyPassCount} empty and {diagnostics.FailedPassCount} failed " +
                   "pass(es) were followed by fresh pieces.";
        }

        return "Streaming.";
    }
}
```

**Why.** Three limits are worth knowing before they are heard.

A model that generates slower than the music plays cannot stream it, and no amount of
buffering changes that. The engine measures its own rate and, when it falls below real
time, stops streaming: nothing of a segment is heard until the whole of it is ready,
then all of it plays, then there is a rest while the next one is written. Music
followed by a pause sounds intentional. It switches back by itself when the rate
recovers. MuPT writes many times faster than real time and does not come near this
limit on a desktop processor. SkyTNT is faster than real time at the start of a pass
and slows as the pass grows, so dense music on a small board or a busy server can
fall back; measure on the machine you ship to. Even on a fast machine a SkyTNT
session now and then opens with a short spell of whole segments before its measured
rate settles and it goes back to streaming.

The processors are shared. Inference, the synthesizer and the rest of the application
all run on them, and another heavy job on the same machine, a build, a render, another
model, can starve the model for long enough to cause a fallback to whole segments, a
shortened crossfade or a gap. None of that is the engine; the same session on a quiet
machine plays through. Do not run a render on the machine that is playing a session.
`InferenceThreadCount` on the options gives the model more or fewer threads; its
default is a quarter of the processors, capped at four.

A model can refuse a piece it has already started, or write nothing at all. Under
`KeepGenerating` neither stops the music: what a failed pass wrote still plays, the
next segment is a fresh piece on a new seed at the next bar line, and
`FailedPassCount` or `EmptyPassCount` counts it. Only a run of three bad passes in a row
stops the music, with the reason in `GenerationError`.

**Pitfalls.**
- Treating one starvation gap as a fault. Act on a real-time factor that stays below
  one, not on a single gap.
- Expecting every seam to be inaudible. A crossfade hides the join; it cannot hide a
  model changing its mind about the material.
- Reading the size of a model package as its memory use. Inference can take far more
  than the files on disk; measure peak memory over a long run.

**Verify.** On the machine you ship to, a long run reports `Streaming` once it has
started, a real-time factor comfortably above one, and no gaps. If it does not, render the music
ahead of time instead: a session and a render take the same options.

---

## Render a long varied piece ahead of time with a caller-staged MuseCoco model

Everything in this section is about a different job from the sections above. MuseCoco
writes varied, multi-instrument music from a set of musical attributes (instruments, a
genre, a tempo category, whether it is danceable) or from a sentence. On the processors
in ordinary computers it writes that music several times slower than real time, so it
is NOT a model for live streaming, and it should not be used for one. It is a model for
a render job: music for a ten-minute video is a matter of asking for ten minutes and
letting it run. No package ships MuseCoco. You stage it yourself, once, with
CodeBrix.Ollama.ModelManager on a machine that has Python, and point the MuseCoco
adapter in the core package at the files that produces. The application that renders
and plays the music needs neither ModelManager nor Python.

### Stage the music model and its text model with ModelManager

**When you want this.** You have the published MuseCoco checkpoints and want the
bundles the core package's MuseCoco adapter reads.

**The shape.** A one-off staging program that references
`CodeBrix.Ollama.ModelManager.MitLicenseForever` and nothing from the music packages.
It imports each checkpoint into a model store, exports it to ONNX by its own route,
reduces the exported graph to a smaller precision, and materializes the result as a
plain folder of files.

**Code.**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager;

// Runs once, on a staging machine with Python and the torch, numpy and onnx modules.
// Never part of the application that plays the music.
public static class MuseCocoStaging
{
    public static async Task StageAsync(string storeDirectory, string virtualEnvironment,
        string musicCheckpointFolder, string textCheckpointFolder, string bundlesFolder,
        CancellationToken cancellationToken)
    {
        var python = new PythonOptions { VirtualEnvironment = virtualEnvironment };
        var report = PythonSupport.Check(python, "torch", "numpy", "onnx");
        if (!report.IsUsable)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, report.Problems));
        }

        using var store = new ModelStore(new ModelStoreOptions
        {
            StoreDirectory = storeDirectory,
            Python = python,
        });

        try
        {
            // The music checkpoint is one .pt file; the text checkpoint is its published BERT folder.
            await store.ImportBundleAsync("local/muse-music:source", musicCheckpointFolder,
                cancellationToken: cancellationToken);
            await store.ImportBundleAsync("local/muse-text:source", textCheckpointFolder,
                cancellationToken: cancellationToken);

            // Each model has its own export route, and both export at full precision.
            await store.ExportToOnnxAsync("local/muse-music:source", new ExportOptions
            {
                Route = ExportRoute.MuseCocoMusic,
                OutputName = "local/muse-music:fp32",
            }, cancellationToken: cancellationToken);
            await store.ExportToOnnxAsync("local/muse-text:source", new ExportOptions
            {
                Route = ExportRoute.MuseCocoText,
                OutputName = "local/muse-text:fp32",
            }, cancellationToken: cancellationToken);

            // Reduction is a separate step and needs no Python.
            var music = await store.ReduceOnnxAsync("local/muse-music:fp32", new ReduceOptions
            {
                Engine = ReduceEngine.Managed,
                Mode = ReduceMode.WeightOnlyInt4,
                BlockSize = 128,
                OutputName = "local/muse-music:int4",
            }, cancellationToken: cancellationToken);
            var text = await store.ReduceOnnxAsync("local/muse-text:fp32", new ReduceOptions
            {
                Engine = ReduceEngine.Managed,
                Mode = ReduceMode.WeightOnlyInt8,
                BlockSize = 128,
                OutputName = "local/muse-text:int8",
            }, cancellationToken: cancellationToken);

            // Real files in real folders: these two paths are what the adapter is given.
            await store.MaterializeAsync(music.Name, Path.Combine(bundlesFolder, "music-int4"),
                cancellationToken: cancellationToken);
            await store.MaterializeAsync(text.Name, Path.Combine(bundlesFolder, "text-int8"),
                cancellationToken: cancellationToken);
        }
        finally
        {
            PythonSupport.Shutdown();       // after every piece of Python work has finished
        }
    }
}
```

**Why.** Exporting needs Python, torch, numpy and onnx, and only here; everything after
it runs in managed .NET. `PythonSupport.Check` says what is missing before any work
starts. The music model needs its route named, `ExportRoute.MuseCocoMusic`; the text
model is recognised from its architecture but is named here too, so the program reads
the same way for both.

The reductions are weight-only: the weights shrink and the arithmetic stays at full
precision. INT4 is the fastest music model to run and is what the render recipe below
was listened to with; the full-precision model is several times larger on disk and the
slowest to run. The text model only matters for sentence prompts, and INT8 is plenty
for it. Quantizing changes the model's musical choices, so the same seed does not
promise the same music at a different precision.

The folders it leaves are what the adapter expects. The music folder holds
`musecoco.json`, `music-attributes.json`, `decoder.onnx`, `vocabulary.json` and the
weights beside the graph (`decoder.onnx.data` after reduction, `weights.bin` at full
precision). The text folder holds `musecoco.json`, `music-attributes.json`,
`attributes.onnx` and `wordpiece.json`. Keep every file the manifest names.

**Pitfalls.**
- Both exports must be at full precision. Reduce afterwards, and set `Overwrite` on the
  options when you intend to replace a bundle you staged before.
- Call `PythonSupport.Shutdown` once, after all Python work, never between steps.
- The checkpoints are acquired separately and are not in any package; the music
  checkpoint is the attribute-to-music model and the text checkpoint is its own
  fine-tuned BERT, not a generic one.

**Verify.** Each folder exists with the files listed above, and the next recipe's
`PreloadAsync` loads the music folder without an error.

### Register the staged model and ask for music by attribute

**When you want this.** The bundles are staged and you want the MuseCoco adapter
registered and a request it will act on.

**The shape.** The adapter is in the core package, `MuseCocoMusicGenerator`. Construct
it from the music folder, and the text folder when you want sentence prompts; register
it under a name; describe the music with `ModelAttributes`.

**Code.**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Models;

public static class StagedMuseCoco
{
    public const string GeneratorName = "MuseCoco";

    // Registering loads nothing. The text bundle is optional, and it is loaded only
    // when a request carries a sentence.
    public static MuseCocoMusicGenerator Register(string musicBundleDirectory, string textBundleDirectory)
    {
        var generator = new MuseCocoMusicGenerator(GeneratorName, musicBundleDirectory,
            new MuseCocoGeneratorOptions
            {
                TextBundleDirectory = textBundleDirectory,      // null: attributes only
                MaximumTokensPerPass = 4096,                     // a generous cap
                MinimumTokensPerPass = 0,                        // the model ends each piece itself
                ExperimentalContinuation = false,                // one model pass per piece
                InferenceThreadCount = Math.Max(1, Environment.ProcessorCount / 2),
            });

        MusicGeneratorRegistry.Register(generator);
        return generator;
    }

    // Electronic, danceable and fast: the attributes the recommended render was made with.
    public static MusicRequest Electronic(int seed)
    {
        var request = new MusicRequest
        {
            Seed = seed,
            Controls = new MusicGenerationControls { TopK = 15, TopP = 1.0, Temperature = 1.0 },
        };

        request.ModelAttributes["instrument.synthesizer"] = "present";
        request.ModelAttributes["instrument.drum"] = "present";
        request.ModelAttributes["genre.electronic"] = "present";
        request.ModelAttributes["danceable"] = "yes";
        request.ModelAttributes["tempo"] = "fast";
        return request;
    }

    // A sentence instead of attributes. It needs the text bundle.
    public static MusicRequest Described(string sentence, int seed) => new MusicRequest
    {
        Text = sentence,
        Seed = seed,
        Controls = new MusicGenerationControls { TopK = 15, TopP = 1.0, Temperature = 1.0 },
    };

    // Every attribute name and the values it takes. The schema exists once the model has loaded.
    public static async Task ListAttributesAsync(MuseCocoMusicGenerator generator,
        CancellationToken cancellationToken)
    {
        await generator.PreloadAsync(cancellationToken);

        foreach (var definition in generator.Schema.Definitions)
        {
            Console.WriteLine($"{definition.Name}: {string.Join(", ", definition.Values)}");
        }
    }
}
```

**Why.** Attributes are the model's own vocabulary, and a request made of them needs
nothing but the music model: the text model is never loaded for it. A sentence goes
through the text model first, which predicts the attributes; any attribute you also set
yourself wins over the prediction. A sentence with no text bundle is refused by name, as
is an attribute name or value the schema does not have, so list the schema rather than
guessing strings. The sampling settings above are the model's own defaults, which the
renders were made with; the core package's general defaults are tuned for the other
models.

The token settings are the ones the render recipe relies on, and the next recipe says
why. The thread count is for a render job on a machine that is doing nothing else: more
threads help this model, with diminishing returns, and the adapter's own default is a
conservative share meant for a machine that is busy with other things.

**Pitfalls.**
- The adapter acts on attributes, a sentence (with the text bundle), a seed, the
  sampling settings, a token cap and a thread count. It refuses musical intent, instrument
  hints, a primer and a continuation of the music so far, by name.
- Ask for fewer instruments rather than more. A sampled piece that needs more than
  fifteen melodic instruments is refused part-way; the engine goes on with a fresh piece
  and counts it, but the refused piece is shorter than it would have been.
- Stop and dispose every session, and let any direct enumeration finish, before releasing
  or disposing the generator.

**Verify.** `ListAttributesAsync` prints the schema; `IsTextModelLoaded` stays false for
attribute-only requests.

### Render a piece of any length

**When you want this.** You need a long, varied piece of a known length: the score for a
video, a set of background tracks, a theme you will loop.

**The shape.** An ordinary session over the registered adapter, rendered to a file
instead of played. Every piece the model writes is a fresh one, pieces are joined by the
same crossfade the live recipes use, the session tempo keeps the pulse steady, and a
target length ends the file exactly where you asked.

**Code.**

This uses `StagedMuseCoco` from the previous recipe:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.MusicGeneration.Rendering;

public static class LongPieceRender
{
    public static async Task<MusicRenderResult> RenderAsync(string audioPath, TimeSpan length,
        string instrumentLibrary, CancellationToken cancellationToken)
    {
        var options = new MusicGenerationOptions
        {
            Generator = StagedMuseCoco.GeneratorName,
            InstrumentLibrary = instrumentLibrary,
            Request = StagedMuseCoco.Electronic(seed: 4242),
            SegmentPriming = SegmentPriming.Fresh,
            SeamCrossfade = TimeSpan.FromSeconds(4.0),
            SeamCrossfadeCurve = MusicFadeCurve.EqualPower,
            TempoPolicy = SessionTempoPolicy.CarryOutsideBand,
            TempoBand = MusicGenerationOptions.DefaultTempoBand,
        };

        using var music = new MusicSession(options);

        // A long render reports often; write a line only when the whole percentage moves.
        var lastPercent = -1;
        var progress = new Progress<MusicRenderProgress>(report =>
        {
            var percent = (int)(report.Fraction * 100.0);
            if (percent != lastPercent)
            {
                lastPercent = percent;
                Console.WriteLine(report);
            }
        });

        var result = await music.RenderToFileAsync(audioPath, new MusicRenderOptions
        {
            TargetLength = length,
            Ending = RenderEnding.Fade,
        }, progress, cancellationToken);

        MidiFile.Export(Path.ChangeExtension(audioPath, ".mid"), result.Music);

        // One line per piece: the tempo it was written at, and whether it was carried.
        Console.WriteLine(result);
        foreach (var line in result.Diagnostics)
        {
            Console.WriteLine(line);
        }

        return result;
    }
}
```

Ten minutes of music is the same call with a length of ten minutes, through either
General MIDI library.

**Why.** Each setting is there because listening found what happens without it.

- NATURAL ENDINGS. Left to itself the model ends a piece after about half a minute of
  music. A generous cap with no minimum lets it do that. Forcing a high minimum to get
  longer pieces does not work: past its natural end the piece thins out into long
  silences and bursts of nonsense tempos. Length comes from the render asking for more
  pieces, not from longer ones.
- NO EXPERIMENTAL CONTINUATION. The adapter can carry recent bars from one section of a
  request into the next, but its section joins left audible gaps in the middle of
  pieces. One model pass per piece, joined by the engine's crossfade, played smoothly.
- FRESH PIECES AND A FOUR-SECOND CROSSFADE. This model cannot be shown the music so far,
  so every piece is fresh anyway; the crossfade makes each one take over from the last.
  A render waits for the whole fade rather than shortening it.
- CARRY OUTSIDE THE BAND. A tempo category such as "fast" is coarse, and successive
  pieces arrive at very different tempos. Pieces close to the session tempo keep their
  own; the rest are carried to it, so the pulse does not lurch at the seams.

A render made this way, six minutes of it, played smoothly and was judged to sound great
through both General MIDI libraries.

Be honest with yourself about time. The model writes several times slower than real
time on a CPU, so a ten-minute piece takes a good deal longer than ten minutes to render,
and how much longer depends on the machine and the thread count. Pass the progress
reporter, run the render where nothing else competes for the processors, and let it run.

The diagnostics are worth reading once it finishes. They list, per piece, the tempo it
was written at and whether it was carried or adopted; every pass that failed part-way
and was followed by a fresh piece; and any fade that could not be given in full.

**Pitfalls.**
- Do not play a session on the machine while it renders. The render will take everything
  the processors can give, and the session will fall back to whole segments.
- The MIDI keeps the outgoing piece's last seconds on a track of its own at every seam,
  on the same channels as the incoming piece and without the fade's gain, because MIDI
  has no crossfade. The audio file is the render as it was mixed.
- A cancelled render leaves no file that looks finished. A render that stops early has
  stopped for a reason its exception gives.

**Verify.** `ReachedTargetLength` is true, `Duration` is the length you asked for, and
the `.mid` beside the audio file opens in any MIDI program.

### Play the rendered piece later through a session

**When you want this.** The piece was rendered ahead of time and you want to play it
through the same machinery the live music uses: a chosen instrument library, the
same voicing rules, and a session the rest of the application already knows how to
start and stop.

**The shape.** A replay generator over the rendered `.mid`, registered under a name of
its own, and a session that plays it once and finishes.

**Code.**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.MusicGeneration.Replay;

public static class RenderedPiecePlayer
{
    // Plays a rendered piece once through any registered instrument library.
    public static async Task PlayOnceAsync(string midiPath, string instrumentLibrary,
        CancellationToken cancellationToken)
    {
        var piece = ReplayMusicGenerator.FromMidiFile(
            "Rendered." + Path.GetFileNameWithoutExtension(midiPath), midiPath);
        MusicGeneratorRegistry.Register(piece);

        using var music = new MusicSession(new MusicGenerationOptions
        {
            Generator = piece.Name,
            InstrumentLibrary = instrumentLibrary,
            EndOfPiece = EndOfPiecePolicy.Stop,     // play it once, then finish
        });

        music.Play();
        while (!music.IsFinished)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }
}
```

**Why.** No model is loaded to play it back: the replay generator reads the file and
hands its events to the session like any other generator. `EndOfPiece = Stop` is what
makes it end; left at the default, the session would ask the replay to carry on and it
would loop. Because the notes and not the audio were kept, the same piece can be played
through a different instrument library than it was rendered with; in listening, the
same six-minute render played through both General MIDI libraries sounded right.

**Pitfalls.**
- Register each piece once, under a name of its own. The name is derived from the file
  here so that two pieces never collide.
- If the exact mix of the render matters, including its crossfades, play the audio file
  instead; the replay voices the notes afresh.

**Verify.** `IsFinished` becomes true when the piece ends, and `GenerationError` is null.
