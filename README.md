# CodeBrix.Audio.MusicGeneration

A cross-platform music-generation library for .NET. CodeBrix.Audio.MusicGeneration produces music - from a piece it carries, or from a music model an application registers - as MIDI events that arrive while the music is still being written, voices them through a CodeBrix.Audio instrument library, and either plays them as they appear or renders them to an audio file. CodeBrix.Audio.MusicGeneration is provided as a .NET 10 library and associated `CodeBrix.Audio.MusicGeneration.MitLicenseForever` NuGet package.

CodeBrix.Audio.MusicGeneration supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.Audio.MusicGeneration.MitLicenseForever
```

Note that the NuGet package ID and the namespace are different - there is no package named plain `CodeBrix.Audio.MusicGeneration`:

* NuGet package ID: `CodeBrix.Audio.MusicGeneration.MitLicenseForever`
* Assembly and primary namespace: `CodeBrix.Audio.MusicGeneration` - i.e. `using CodeBrix.Audio.MusicGeneration;`

XML documentation (IntelliSense) ships alongside the assembly.

CodeBrix.Audio.MusicGeneration depends on `CodeBrix.Audio`, `CodeBrix.Audio.ModestSynth` and `CodeBrix.Ollama.ModelRunner`. No ModelManager or Python is required at application runtime. MuPT uses ModelRunner's bundled native engine; SkyTNT and MuseCoco use its managed ONNX runtime. Retain the runtime assets for your deployment platform.

To hear anything, an application registers an instrument library. This library registers none - that decision is always the application's - and the General MIDI library is one line: `GeneralMidiInstrumentLibrary.Register();`

## CodeBrix.Audio.MusicGeneration supports:

* Music that arrives while it is still being written - MIDI events released one at a time, each carrying the point the music has settled to, ready to be played or recorded as they appear
* Playing music as it is generated, through a session that opens the audio device - or that opens none at all, so an application's own mixer pulls the samples
* A generator model that is not tied to a model file: a generator may be backed by no model, by one, or by two
* Four pieces of music built into the package, so an application makes music with no model, no download and no configuration
* Replaying an application's own music - a MIDI file, a MIDI stream, a MIDI event collection, or a tune written in ABC notation - under a name of its own
* Renditions: how the parts are voiced, as data - an instrument and a level for each part, an optional layer under it, and a set of voicings that were listened to and rated
* Music that keeps going: the generator is asked to carry on at a bar line, and a follow-up prompt takes over at a bar line while the music plays
* Seams chosen by the application: each new segment primed with the music so far, started fresh as a new piece, or the two taking turns - and a crossfade at a fresh seam, so the new piece takes over from the old one instead of cutting in
* A streaming lifecycle built for applications that come first - a pre-roll, a generate-ahead window, diagnostics a game loop can read every frame, and rests rather than garbled timing on a machine that cannot keep up
* Rendering the same music offline to an audio file or a stream, in whatever format a writer is registered for, optionally to an exact length with a fade, a hard cut or the generator's own ending
* One request type for every generator: free text, notation the generator reads itself, a MIDI primer, musical intent, instrument hints, a drum kit, a seed, generation controls and a continuation of the music so far
* Musical intent turned into whatever each model really reads - a key, a metre, a unit note length, a tempo, a number of parts and a drum kit becoming a notation header, an opening in two parts, or a model's own generation options
* Character words that mean something: a small vocabulary in which each word names a tempo, a mode or both, with a word that names neither refused by name rather than dropped
* Named presets to start from: the prompts a listening session was run on, and accepted electronica starting points for the event model, each carrying the voicing its music was rated through
* Refusal by name: a generator declares what it acts on, and a request that relies on anything else is refused rather than quietly ignored
* A process-wide generator registry in which registering is not specifying - the built-in music plays until a generator is asked for by name
* An adapter for the SkyTNT MIDI model, pointed at a bundle of files an application supplies - as a folder, or as a map of names against the paths they are really at - with nothing to install and no Python anywhere near it
* An adapter for the MuPT model, which writes ABC notation: its text becomes music while it is still being written, one complete slice at a time, and nothing already heard is ever changed
* An adapter for caller-staged MuseCoco models: musical attributes, optional natural-language prompting with a text bundle, streaming MIDI, and an explicitly experimental path that continues recent bars within a request

## Sample Code

### Play the music that comes in the package

```csharp
using CodeBrix.Audio.ModestSynth;
using CodeBrix.Audio.MusicGeneration;

GeneralMidiInstrumentLibrary.Register();        //your decision, always

using var music = new MusicSession();           //nothing specified
music.Play();

Console.WriteLine(music.ActiveSource);          //what is really playing
Console.ReadLine();
```

### Render it to a file instead, with a length and a fade

```csharp
using CodeBrix.Audio.ModestSynth;
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.MusicGeneration.Rendering;

GeneralMidiInstrumentLibrary.Register();

using var music = new MusicSession();

var result = await music.RenderToFileAsync("theme.wav",
    new MusicRenderOptions { TargetLength = TimeSpan.FromMinutes(6.0) },
    CancellationToken.None);

Console.WriteLine(result);                      //the file, its exact length
```

### Replay an application's own music, under its own name

```csharp
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.MusicGeneration.Replay;

var ownTune = ReplayMusicGenerator.FromMidiFile("MyTheme", "theme.mid");
ownTune.PacingRate = 1.0;                       //released at the speed it plays
MusicGeneratorRegistry.Register(ownTune);

using var music = new MusicSession(new MusicGenerationOptions
{
    Generator = "MyTheme",                      //registering is not specifying
    Rendition = "AmbientDuet",                  //how the parts are voiced
});
```

### Seams: priming and crossfade

A segment that is primed with the last bars carries on in the same key and idiom, and some models then write the same material again at every seam, whatever the seed. A fresh segment is a new piece in the same character. Taking turns keeps some continuity while the music still moves on, and a crossfade lets the outgoing piece fade out over the incoming one instead of stopping at a bar line.

```csharp
using var music = new MusicSession(new MusicGenerationOptions
{
    Generator = "MyModel",                      //a model generator the application registered
    SegmentPriming = SegmentPriming.Alternate,  //primed, fresh, primed, fresh...
    SeamCrossfade = TimeSpan.FromSeconds(4.0),  //fresh seams only; zero is a hard join
});
music.Play();

Console.WriteLine(music.Diagnostics);           //what is generating, and the crossfades so far
```

If the new piece opens with silent bars, a crossfade skips them, so the old piece fades into music rather than into silence; a hard join plays them as written. A model picks a new tempo for every fresh piece, so the pulse can lurch at each seam. `TempoPolicy = SessionTempoPolicy.CarryOutsideBand` keeps a session tempo - the first piece's, or `SessionBeatsPerMinute` - and plays a fresh piece more than `TempoBand` (15%) away from it at the session tempo instead; `Carry` does that for every fresh piece. A carried piece keeps its notes where they are and is simply heard a little faster or slower.

A crossfade never costs the music a gap: when the new piece has not generated enough by the time the old one has to be committed, the fade is shortened, and the diagnostics count it. A file rendered with the same options crossfades the same way.

## Documentation

The NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written for AI coding agents - point your agent at that file when it is writing code against this library.

Additional sample code and usage examples are available in the `CodeBrix.Audio.MusicGeneration.Tests` project:
https://github.com/ellisnet/CodeBrix.Audio.MusicGeneration/tree/main/tests/CodeBrix.Audio.MusicGeneration.Tests

## License

CodeBrix.Audio.MusicGeneration is licensed under the MIT License - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.Audio.MusicGeneration/blob/main/LICENSE) file.

For licensing and provenance information about the open source code included in
this package, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.Audio.MusicGeneration/blob/main/THIRD-PARTY-NOTICES.txt).
