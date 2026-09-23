================================================================================
MAINTAINER-README: CodeBrix.Audio.MusicGeneration
Notes for people and agents MAINTAINING this repository - not for package
consumers
================================================================================

If you are CONSUMING the NuGet package, stop reading and open AGENT-README.txt
instead. Everything below is about the repository itself: how it is laid out,
how it builds, how it is tested, how it is packaged, and the conventions the
source follows.


********************************************************************************
DEPENDENCIES - READ THIS BEFORE YOU TOUCH THE CSPROJ
********************************************************************************

THE SHIPPING LIBRARY DEPENDS ON EXACTLY THREE PACKAGES, AND IT NEVER GROWS A
FOURTH:

    CodeBrix.Audio.MitLicenseForever
    CodeBrix.Audio.ModestSynth.MitLicenseForever
    CodeBrix.Ollama.ModelRunner.MitLicenseForever

It MUST NOT depend on CodeBrix.Ollama.ModelManager. It MUST NOT depend on
CodeBrix.Python. It MUST NOT have any code path that requires Python to be
installed. It MUST NOT depend on CodeBrix.Audio.Opus: writing an `.opus` file is
reached through CodeBrix.Audio's writer registry, which is the only road, and a
consumer who wants it references that package and calls its Register() itself.

THE ModestSynth REFERENCE IS DELIBERATE, although no code in this library ever
names a ModestSynth type. DO NOT REMOVE IT AS UNUSED. One package reference has
to bring a consumer everything they need to make a sound, and ModestSynth is
where the General MIDI instrument library lives. The csproj carries a comment
saying so, beside the reference.

THIS BAN IS STATED AND TRUSTED. There are deliberately NO enforcement tests for
it: a test that asserts something is ABSENT from a package costs more than it
saves. The gate is a human reading the packed nuspec's dependency group.

What an exception MESSAGE says is a different matter - prose is not a
dependency. A message here may name a type or a call in a package this library
does not reference, so that a developer handed an unusable state is told what to
do about it.

AND THE OTHER DIRECTION: NO CodeBrix.Audio.* PACKAGE DEPENDS ON THIS ONE unless
".MusicGeneration" is in its own name. CodeBrix.Audio, CodeBrix.Audio.Opus and
CodeBrix.Audio.Samples.FluidR3Gm depend on CodeBrix.Audio alone and must stay
that way. The dependency runs one way: this library consumes them.

********************************************************************************


PURPOSE AND SCOPE
================================================================================
  PackageId    CodeBrix.Audio.MusicGeneration.MitLicenseForever
  Assembly     CodeBrix.Audio.MusicGeneration
  Project      src/CodeBrix.Audio.MusicGeneration/CodeBrix.Audio.MusicGeneration.csproj
  License      MIT (LICENSE at the repository root)
  Consumer doc AGENT-README.txt, packed at the root of the nupkg

The library generates music as MIDI events that arrive while the music is still
being written, voices those events through a CodeBrix.Audio instrument library,
and either plays them while they are still being generated or renders them to an
audio file. A generator is "a thing that produces music" - deliberately not "a
model file", because a generator may be backed by no model at all, by one, or by
two. Four replay generators over embedded pieces are built in, so an application
makes music with no model and no download.


REPOSITORY LAYOUT
================================================================================
  .cursor/rules/agent-readme.mdc   AI-agent pointer stub
  .github/copilot-instructions.md  AI-agent pointer stub
  .junie/guidelines.md             AI-agent pointer stub
  src/CodeBrix.Audio.MusicGeneration/
      CodeBrix.Audio.MusicGeneration.csproj
      InternalsVisibleTo.cs        opens internals to the .Tests project
      MusicSession.cs              the entry point: play, follow up, render
      MusicGenerationOptions.cs    everything a session can be told
      IMusicGenerator.cs           the contract every generator implements
      MusicGeneratorRegistry.cs    the process-wide name-to-generator map
      ActiveMusicSource.cs         what is really making the music
      MusicDiagnostics.cs          the snapshot a game loop reads
      EndOfPiecePolicy.cs          keep generating, or stop
      MusicDeliveryMode.cs         streaming, or a whole segment at a time
      MusicGenerationException.cs  the base of this library's own errors
      EmbeddedMusic/               the four pieces that ship inside the assembly
      Generation/                  the request, the generated-event type, the
                                   capability declaration and its checker
      Replay/                      the replay generator and the built-in names
      Rendition/                   how the parts are voiced: the rendition, its
                                   registry, the taste table, the voicer and
                                   the per-part report
      Streaming/                   the engine and everything the lifecycle needs
                                   - the reorder and settled buffers, the tempo
                                   map and bar grid, the three hosts, the
                                   segment and seam bookkeeping, and every
                                   starting number in StreamingDefaults.cs
      Rendering/                   the offline render: its options, the fade,
                                   the progress reports and the result
      Models/                      the two model adapters and their load-time
                                   options, with Models/Internal/ holding the
                                   per-adapter request mappers, prompts, passes
                                   and the ported notation post-processing
      Presets/                     ready-made requests: the prompts the
                                   listening sessions were run on, and the
                                   accepted electronica ones
      Internal/                    the piece builder, the bar grid, the
                                   embedded-resource reader, the instrument
                                   library seam
  tests/CodeBrix.Audio.MusicGeneration.Tests/
  .clinerules .cursorrules .windsurfrules AGENTS.md CLAUDE.md
                                   AI-agent pointer stubs
  .gitignore
  AGENT-README.txt                 consumer documentation (packed)
  CodeBrix.Audio.MusicGeneration.slnx
  EXTRAS-README.txt
  global.json
  icon-codebrix-128.png            packed
  LICENSE
  MAINTAINER-README.txt            this file
  README-INDEX.txt
  README.md                        packed
  THIRD-PARTY-NOTICES.txt          packed

The .slnx carries a "Solution Items" folder holding exactly ten files -
.gitignore, AGENT-README.txt, EXTRAS-README.txt, global.json,
icon-codebrix-128.png, LICENSE, MAINTAINER-README.txt, README-INDEX.txt,
README.md and THIRD-PARTY-NOTICES.txt - and a "Tests" folder holding the test
project.

global.json selects the Microsoft.Testing.Platform test runner for every
`dotnet test` run in this repository. It pins no SDK version. Do not delete it:
without it `dotnet test` falls back to the VSTest bridge, which xUnit v3 no
longer supports on the .NET 10 SDK.


BUILDING
================================================================================
    dotnet build --configuration Release CodeBrix.Audio.MusicGeneration.slnx

The build must end at 0 warnings and 0 errors, in Debug AND in Release. A
Release run has caught a test race that a Debug run hid, so both are run before
any change is called finished.

GenerateDocumentationFile is on, so CS1591 is raised for any undocumented public
member. It is fixed by writing the documentation comment. Never add
<NoWarn>1591</NoWarn>, and never add any other warning-suppression property.


TESTING
================================================================================
    dotnet test --solution CodeBrix.Audio.MusicGeneration.slnx

xUnit v3 with SilverAssertions, on Microsoft.Testing.Platform - the runner
selected by global.json. No coverage collector is referenced; the family dropped
coverlet.collector.

KNOWN GOTCHA (.NET 10 SDK): `dotnet test` can report "Zero tests ran" although
the suite is fine. When that happens, run each test assembly directly and quote
ITS counts:

    dotnet tests/CodeBrix.Audio.MusicGeneration.Tests/bin/Debug/net10.0/CodeBrix.Audio.MusicGeneration.Tests.dll
    dotnet tests/CodeBrix.Audio.MusicGeneration.Tests/bin/Release/net10.0/CodeBrix.Audio.MusicGeneration.Tests.dll

THE REGISTRIES ARE PROCESS-WIDE STATE, AND THERE ARE THREE OF THEM. Every test
class that registers a GENERATOR or a RENDITION, or that configures one of the
built-in replays, belongs to the ONE non-parallel xUnit collection named in
RegistryCollection.cs and calls BOTH MusicGeneratorRegistry.ResetForTesting()
and MusicRenditionRegistry.ResetForTesting() in its constructor. One collection
rather than two, because two collections that each disable parallelization would
still run alongside one another - and the collection covers the audio device as
well, which only one test may hold at a time. Tests that only read a generator
by name need nothing.

THE INSTRUMENT-LIBRARY REGISTRY IS CodeBrix.Audio's, AND IT CANNOT BE RESET. So
ungated tests always resolve a library BY NAME and never read or assert on the
default: "the first library registered is the default" makes the default depend
on which test registered first. The same is true of the writer registry, which
is why the unknown-extension test asserts what the message SAYS rather than the
exact list of formats.

NOTHING IN THE SUITE SLEEPS, apart from the gated tests that play or render in
real time. The replay generator takes its clock from System.TimeProvider, and
every pacing and lifecycle test drives a hand-written test provider
(ManualTimeProvider.cs) rather than the machine's clock, so the timings are
exact and the suite does not depend on how busy the machine is. Do not
introduce a real delay into a test, and do not add a third-party time-testing
package.

TWO CLOCKS ARE A HAZARD, and it has bitten this repository twice. When a test
moves the clock by hand, the ENGINE and the GENERATOR must both be on that
clock - ReplayOnTestClock.cs exists for exactly that, and explains why - and the
pump must not run time on while a generation has simply not been scheduled yet.
Anything that measures wall time against generated music has to get this right,
or the engine sees minutes pass against a bar of music and falls back to
delivering a whole segment at a time.

RUN THE SUITE SEVERAL TIMES AT ONCE before calling timing-shaped work finished.
Four concurrent Release suites on a deliberately loaded machine is the pattern
that found every flaky test this repository has had; three sequential runs found
none of them.

THE GATED TESTS - all opt-in through an environment variable, all run
deliberately and by themselves:

  CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1
      MusicSessionAudibleTests, and - with the model variables below set too -
      SkyTNTAudibleTests and MuPTAudibleTests. OPENS THE AUDIO DEVICE AND MAKES
      SOUND, for minutes at a time. Needs a working audio output and somebody listening; it proves the
      device path and nothing else can. Never run two of these at once, and
      never alongside another agent's audible run.

  CODEBRIX_AUDIO_MUSICGEN_SKYTNT_BUNDLE=<folder>
      SkyTNTLiveTests, the SkyTNT half of PresetLiveTests, and the second gate
      on SkyTNTAudibleTests, ModelDoneCriterionTests and
      ListeningRenderTests. The folder
      holds the SkyTNT bundle - config.json, model_base.onnx, model_token.onnx.
      NO PATH OUTSIDE THIS REPOSITORY IS WRITTEN DOWN ANYWHERE IN IT: a model is
      hundreds of megabytes, it is not in the repository, and the variable is
      the only road to one. Without it every live test SKIPS and says so. The
      live tests are short on purpose - a few dozen events each - and the model
      is loaded ONCE for the class through a fixture, because loading is the
      expensive part. When the model ships as a package these tests are
      repointed at the package and this variable is retired.

  CODEBRIX_AUDIO_MUSICGEN_MUPT_MODEL=<file>
      MuPTLiveTests, the MuPT half of PresetLiveTests, and the second gate on
      MuPTAudibleTests, ModelDoneCriterionTests and ListeningRenderTests.
      The path of a MuPT
      GGUF FILE - one file, because the tokenizer is inside it. The same rule as
      the bundle above: no path outside this repository is written down anywhere
      in it, and without the variable every live test SKIPS and says so. The
      live tests write a few bars each and the model is loaded ONCE for the
      class through a fixture.

  CODEBRIX_AUDIO_RUN_DEFAULT_INSTRUMENT_TESTS=1
      DefaultInstrumentLibraryTests. Asserts on the DEFAULT instrument library,
      which depends on which test registered first, so it must be run BY ITSELF
      (-class "...DefaultInstrumentLibraryTests") and never in the ordinary
      suite.

  CODEBRIX_AUDIO_RUN_LONG_RENDER_TESTS=1
      LongRenderTests, and - WITH BOTH MODEL VARIABLES SET as well -
      ModelDoneCriterionTests, which is the plan's own done criterion with a
      real model behind it: each model streams through a session while it is
      still generating, and six minutes twenty-five seconds of each model's
      music with a fade comes out as a .wav and as a .opus of exactly that
      length. Together they render more than half an hour of audio and write
      well over half a gigabyte while they run. Needs disk under
      tests/.../bin/<config>/net10.0/render-output, and deletes what it wrote.

  CODEBRIX_AUDIO_RUN_LISTENING_RENDERS=1
      ListeningRenderTests, WITH BOTH MODEL VARIABLES. It writes the listening
      set a person actually sits down with: every preset for about a minute
      through "ModestSynthGm" with its suggested voicing, the three accepted
      electronica presets twice with different seeds, A SEAM SET in which each
      model continues its own piece across at least two seams, and one
      follow-up render per model in which the prompt changes mid-piece. Output
      goes to TestResults/listening-renders/ INSIDE this repository - which the
      .gitignore already covers - with an INDEX.txt giving, per file, the time
      of every seam, the bar it falls on, the tempo there and the tail the
      continuation was given. IT DOES NOT DELETE WHAT IT WROTE: the files are
      the point. It writes a few hundred megabytes and takes tens of minutes,
      most of it the slower model.

NOTE ON THE RUNNER: this xUnit v3 build takes `-class` and `-method`, not
`--filter-class`.

The test project references CodeBrix.Audio.ModestSynth directly, and
CodeBrix.Audio.Opus as well. That is allowed: a test project is a CONSUMER, and
registering an instrument library - or an audio format - is always a consumer's
job. Proving that ".opus" is reached through CodeBrix.Audio's writer registry
REQUIRES being an application that references the package carrying the encoder.
It does not license a fourth dependency on the shipping library, and the csproj
says so beside both references.


THE MEASUREMENTS, AND HOW TO TAKE THEM AGAIN
================================================================================
EVERY STARTING NUMBER IN Streaming/StreamingDefaults.cs AND IN THE TWO
GENERATOR OPTIONS CLASSES HAS BEEN MEASURED against the real models, on a
sixteen-core laptop, in RELEASE, three runs of each figure, with the one-minute
load average recorded beside every number. What they settled:

  the inference thread count   A SHARE OF THE MACHINE, not a number: a quarter
                               of Environment.ProcessorCount, at least one and
                               never more than four. Both models climb steeply
                               from one thread to four and then flatten - eight
                               threads land inside the run-to-run spread of
                               four - and four threads is a sixth of a
                               twenty-four-thread laptop but ALL of a four-core
                               board, which is why the default is a share.
  SkyTNT, events per pass      1,000, retained after listening. Historical timings
                               varied from fast openings to 0.22x over dense long
                               renders. Shorter passes improved some timings but
                               later 60/120/300-event auditions did not establish
                               a reliable replacement across prompts and seeds.
                               All three electronica presets are accepted.
                               File size and warm-load working set do not bound
                               inference memory: later managed runs reached GiB.
  MuPT, tokens per pass        448, which is what the rated music was written
                               at: twelve to twenty bars. At four threads the
                               model writes 23-29 seconds of music per second.
  the continuation tail        four bars everywhere. Eight bars buys nothing
                               measurable at a seam (+50 ms on both roads) and
                               eats context.
  generate-ahead / pre-roll    30 s and 5 s, UNCHANGED - confirmed rather than
                               corrected. 30 s of music costs SkyTNT 8-23 s of
                               generating and MuPT about a second.
  AllowControlChange           OFF. With it on, SkyTNT can spend a whole pass
                               writing controller events at tick 0 and write no
                               notes at all; one measured pass produced 250
                               events inside the first bar, every one a control
                               change, and the same request with it off
                               produced 219 events over twelve seconds of music.

HOW TO TAKE THEM AGAIN. There is no measurement harness in the repository and
there should not be: a harness that nobody runs rots, and the numbers are
guidance rather than assertions. Write a throwaway test class OUTSIDE the
repository's own suite (or a scratch console application referencing the packed
package), set the model variables, run it in RELEASE with nothing else building
or testing, take three runs of every figure, and record the load average beside
each one. Then delete it. The figures above and in AGENT-README are worded as
"on a laptop of this class" for that reason - they are not a promise, and no
test asserts any of them.

WHAT IS ASSERTED, and where, is the RULE rather than the number:
StreamingDefaultsTests holds the thread count to "a quarter, at least one, at
most four" on whatever machine runs it, holds both adapters to the same share,
and holds the pass lengths, the tail and the two window figures to what the
measurements settled.


PACKAGING / PUBLISHING
================================================================================
The packable csproj carries the canonical date-stamped version block, which
computes 1.<years since 2026>.<day of year>.<minute of day> from UTC "now" at
build time. There is no literal <Version> to bump. GeneratePackageOnBuild is
true, so every build produces a fresh .nupkg in bin/<configuration>/.

The nupkg contains the assembly, its XML documentation, icon-codebrix-128.png,
README.md, AGENT-README.txt and THIRD-PARTY-NOTICES.txt - and the four embedded
pieces of music, which are inside the assembly rather than beside it.

BEFORE A PUBLISH, unzip the Release .nupkg and read the nuspec's dependency
group with your own eyes. It must list exactly the three packages named at the
top of this file. That check is deliberately a human one.

THIS PACKAGE IS PUBLISHED ONCE, AND NOT UNTIL THE MODEL ADAPTERS ARE IN IT.
There is no publish between the core library and the adapters - same repository,
same library - so the package ships with them, and nothing outside this
repository depends on an interim version. Until then the public API is still
free to be renamed: renames are cheap before a publish and expensive after one.

Jeremy publishes. Nothing in this repository is committed, tagged or pushed by
an agent.


CODING CONVENTIONS
================================================================================
  * Every .cs file: no blank first line; the using block contiguous at the top,
    System.* first and alphabetical within each group, never below the namespace
    line; one blank line; a FILE-SCOPED namespace; one blank line; the code.
  * No global usings. No block-scoped namespaces.
  * NULLABLE REFERENCE TYPES ARE OFF. Never write `?` on a reference type and
    never use the `!` operator. `?` on a value type is fine.
  * XML documentation on every public type and member, and on protected members
    of unsealed public types.
  * Sub-folders map to sub-namespaces; the entry-point types stay at the project
    root; Internal/ holds internal types that deserve their own file.
  * Tests: <Class>Tests.cs holding public class <Class>Tests; method names are
    <MemberName>_<snake_case_description> or plain snake_case; multi-statement
    bodies carry //Arrange, //Act and //Assert; a single-statement test is
    expression-bodied; assertions are SilverAssertions' fluent form; any call
    that takes a CancellationToken is passed
    TestContext.Current.CancellationToken.
  * Warnings are fixed at source. Never suppressed.


PROVENANCE / VENDORED SOURCES
================================================================================
ONE FILE IS A PORT. Models/Internal/MuPTSmtAbc.cs is the post-processing
published on the MuPT model card (Apache-2.0), which separates the merged form
the model writes back into standard ABC voices. It carries the family's
`//was previously:` comment on its namespace line, naming the upstream file and
revision, and THIRD-PARTY-NOTICES.txt records the licence and what the port
deliberately does differently. Nothing else here was ported from anywhere.

THE PORT IS FENCED AGAINST THE REAL THING: the thirty-two audition tunes in the
test project's Assets/mupt-audition folder are what the scratch generator that
ran the listening sessions produced, raw text and finished tune side by side,
and MuPTSmtAbcTests turns every raw file into its tune character for character.
Change the port and those tests say so.

The four pieces of music in src/CodeBrix.Audio.MusicGeneration/EmbeddedMusic are
the OUTPUT of Apache-2.0 models, generated locally by the owner of this
repository - not third-party recordings or transcriptions.
THIRD-PARTY-NOTICES.txt states this deliberately and records the recipe for the
default piece.


NOTES
================================================================================
  * REGISTERING IS NOT SPECIFYING. A generator plays when it has been registered
    AND asked for by name. With nothing specified, the embedded MIDI replay
    plays - always, however many model generators are registered. There is no
    settable default and no first-one-registered-wins. This is deliberately
    unlike CodeBrix.Audio's instrument-library registry, where the first library
    registered IS the default; the difference is that a demo loop must never be
    able to masquerade as a model.
  * THIS LIBRARY REGISTERS NO INSTRUMENT LIBRARY, not at start-up and not as a
    fallback. Registration is always the consumer's job.
  * A BUG IN A PUBLISHED CodeBrix PACKAGE IS NEVER WORKED ROUND HERE. If a bug
    or an API gap in CodeBrix.Audio, CodeBrix.Audio.ModestSynth,
    CodeBrix.Ollama.ModelRunner or any other published package would make you
    write workaround code in this repository, stop and report it instead: it is
    fixed and republished at the source.
  * NOTHING IS BUILT OR ROUTED ON THE AUDIO THREAD. A RoutingSynthesizer is
    single-threaded by contract, so an instrument for a part is built on the
    pump thread when that part's first note is COMMITTED, left as a
    PreparedVoice, and applied from the sequencer's message hook - the one
    moment the rendering thread is not inside Render. This is the single
    easiest rule in the repository to break by accident.
  * EVERY STARTING NUMBER OF THE STREAMING LIFECYCLE IS IN ONE FILE,
    Streaming/StreamingDefaults.cs - the pre-roll, the commit window, the pump
    interval, the generate-ahead window and where it resumes, the real-time
    factor's window, the marks the segment-at-a-time fallback switches on, the
    continuation tail's length and the hold margin, plus the conservative share
    of the machine a model's inference threads take. EVERY ONE OF THEM HAS NOW
    BEEN MEASURED against both model adapters - see THE MEASUREMENTS below.
    THE FADE'S DEFAULT LENGTH AND CURVE ARE THE SAME KIND OF THING and live in
    Rendering/MusicFade.cs, two lines, deliberately together; they were settled
    by ear.
  * THE CHANNEL RULE LIVES IN EXACTLY TWO METHODS, both in
    Models/Internal/SkyTNTEventMapper.cs and Models/Internal/SkyTNTPrompt.cs:
    CodeBrix.Audio counts channels 1 to 16 and a model of this family counts
    them 0 to 15, so one is added on the way out and taken off on the way in.
    Nothing else in the repository translates a channel, and a second model
    adapter adds its own pair rather than reaching for these.
  * A MODEL ADAPTER YIELDS WHOLE BARS. Models/Internal/SkyTNTPass.cs holds
    events back until the model's horizon has passed the END of the bar they
    sit in, and drops the ragged last bar when the pass stops, so a seam always
    falls on a bar line. It also turns the model's HORIZON - "nothing below
    this tick will arrive", which allows another event AT that tick - into this
    library's SETTLED TICK, which is "nothing at or before this tick", by
    taking one off it. Getting that off-by-one wrong is an event committed
    behind the play head.
  * A FOLLOW-UP PROMPT OVER THE GENERATOR THAT IS ALREADY GENERATING IS A
    HAND-OVER. MusicEngine.FollowUp cancels that generation, waits for it to
    end on a task of its own - never on the pump and never on the caller's
    thread - and starts the new one only then, because a model writes ONE piece
    at a time. The music already settled plays on through the change and the
    take-over is still at a bar line; the old music is not asked to continue
    while the hand-over is waiting. A follow-up naming a DIFFERENT generator
    still starts at once, alongside the old one.
  * A TEXT MODEL'S ADAPTER RUNS THE WHOLE-TUNE PATH OVER AND OVER.
    Models/Internal/MuPTSlicePath.cs converts ALL the text the model has
    written each time a slice completes and releases only the new tail, rather
    than parsing ABC incrementally - so the reader and converter that went
    through a full test suite carry the ties, the repeats, the numbered endings
    and the inline fields. It costs a re-parse per slice, which is quadratic in
    the length of the piece and irrelevant at the size of a segment; a pass is
    bounded by MaximumTokensPerPass, and each segment is its own tune, so the
    text never grows past one segment however long the music plays.
  * WHAT THE SETTLED TICK OF A TEXT MODEL IS MADE OF, and it is not the end of
    the last slice. A later slice can give a voice another bar - which begins
    at THAT VOICE'S OWN END, not at the end of the music - and it can lengthen
    a voice's last note, because the converter plays a note holding an open tie
    SHORT and a later slice makes it long. So the settled tick is one tick
    before the earliest, over the voices, of where that voice stops being
    final. Every audition tune and five hand-written fixtures fence it, and
    each fixture also proves that the end-of-slice rule would have released a
    note of the wrong length.
  * AN OFFLINE RENDER TURNS THE STREAMING LIFECYCLE OFF IN ONE PLACE - the
    engine's object initializer in Rendering/OfflineRender.cs, where
    KeepsMusicAheadOfTheHead and FallsBackToSegmentAtATime are both set false.
    A render has no play head; both switches are internal and default to true,
    so nothing about playback changes.
  * NEVER PIN A RENDER BIT-FOR-BIT ACROSS MACHINES OR PLATFORMS. A render goes
    through the platform's own maths library. What the suite pins is two
    renders made on the SAME machine in the SAME run being identical, which is
    the honest version of that test.
  * THE TASTE TABLE AND THE BUILT-IN RENDITIONS ARE EVIDENCE, NOT INVENTION.
    Rendition/RenditionTaste.cs and Rendition/BuiltInRenditions.cs carry
    voicings that were played to Jeremy and written down with the mark he gave
    them; the file records the rating each entry rests on. Retuning them is a
    data edit - keep the ratings beside the values, and do not add a voicing
    nobody has heard.


MUSECOCO INTEGRATION AND RELEASE GATE
===================================
MuseCocoMusicGenerator and MuseCocoPass adapt ModelRunner's streaming contract.
Tests carry tiny synthetic ONNX bundles with their own license/provenance;
they are test fixtures, not shipping model weights. The optional live bundle
is selected by CODEBRIX_AUDIO_MUSICGEN_MUSECOCO_BUNDLE. No ModelManager or Python
may enter the shipping dependency set. Experimental continuation owns its runner
context within one enumeration and preserves the runner's absolute section ticks.

The current integration references the published ModelRunner package from
nuget.org. Temporary Ollama packages are no longer required. The shipping dependency
set remains Audio, ModestSynth and ModelRunner; ModelManager belongs only in the
model repositories' nonshipping staging tools. Jeremy owns publication; agents
leave changes uncommitted.

The MuseCoco playback test must keep rendering while waiting for progress.
Application-owned output advances only when pulled. Waiting on a horizon while
stopping Render freezes the play head and cannot prove continuous playback.
The live gate asserts early audio, normal completion, and zero late events/gaps.

The earlier published runner lacked CreateContinuation and
GenerateContinuationStreamingAsync. That historical probe remains in the session
FIXLIST; the subsequent published release now replaces the temporary dependency.
The PLAN records the exact published version and its validation results.

FINAL LOCAL VALIDATION — 2026-09-22
--------------------------------
Core Debug/Release, staged MuPT/SkyTNT/MuseCoco (including text prompting),
streaming and 6:25 WAV/Opus render gates pass. See the session PLAN for counts,
logs and historical failed test-driver attempts. Model packages were consumed
directly and through an intermediary NuGet, separately and together; build/publish
asset hashes and notices match staging. The core package still has exactly three
dependencies. Review NuGets must not be published with temporary dependencies.

MuPT follow-up listening files run the live host faster than real time. In an
instrumented reproduction the follow-up took 0.260 wall-clock seconds to promote,
while 11.5 music seconds were rendered. The safe switch then landed after the
committed window at a bar line. Such files cannot establish wall-clock response
latency. The listening test now records request, commit, promotion and switch times.

EXPERIMENTAL CONTINUATION PERFORMANCE EVIDENCE
--------------------------------------------
The standalone core-NuGet MuseCoco consumer uses 384 total tokens in 192-token
sections, four context bars and explicit piano/moderate attributes. At the default
pre-roll it produced first audio at 5.943 s while generation was active and
completed at 49.602 s, with zero late events, one buffering gap and segment
fallback. Peak process memory was 908.51 MiB. A 0.5 s pre-roll also had one gap.
These are measured limits, not a defect requiring an upstream workaround: the
contract permits rests when generation falls behind. The ordinary 512-token
single-pass MuseCoco live playback test completed with zero gaps. Do not claim
all experimental continuations are gapless from either result.
