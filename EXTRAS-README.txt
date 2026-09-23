================================================================================
EXTRAS-README: CodeBrix.Audio.MusicGeneration
Samples, tools and other content in this repository that is not part of a NuGet
package
================================================================================

This repository ships no sample applications, demos or command-line tools.
Exactly one project is packable - src/CodeBrix.Audio.MusicGeneration - and
everything else listed below exists only to test or document it. None of it is
included in the CodeBrix.Audio.MusicGeneration.MitLicenseForever package.

For runnable, compilable usage of the library, read the test project: the
"WORKING EXAMPLES ON GITHUB" section of AGENT-README.txt maps each feature area
to the test file that exercises it.


TEST PROJECT
============
    tests/CodeBrix.Audio.MusicGeneration.Tests/

The only non-package project in the solution. xUnit v3; run it as described in
MAINTAINER-README.txt (TESTING).

It is also the library's first CONSUMER, and it is written that way on purpose:
it registers an instrument library from CodeBrix.Audio.ModestSynth and the Opus
encoder from CodeBrix.Audio.Opus, exactly as an application would. The test project
also references both model packages and runs short integration tests from their
copied assets, without staging-path variables. Opus and the model packages are test
references; the shipped library retains only Audio, ModestSynth and ModelRunner.

A few of its tests are opt-in behind an environment variable, because they make
sound, hold the audio device, need a caller-staged model, take minutes or write
hundreds of megabytes: the variables, and what each group needs, are listed in
MAINTAINER-README.txt. An ordinary `dotnet test` skips them, saying why.

It also carries audition assets used by the notation tests -
tests/.../Assets/mupt-audition - which are described in THIRD-PARTY-NOTICES.txt
and are not in the package.


THE LISTENING RENDERS
=====================
    TestResults/listening-renders/          (git-ignored; written on demand)

One gated test writes audio files for a person to listen to rather than for a
machine to assert on: every preset for about a minute, a SEAM SET in which each
model continues its own piece across several seams, and a render per model in
which a follow-up prompt changes the music mid-piece. An INDEX.txt beside them
says what each file is and gives the time of every seam. Nothing in the
repository depends on them and nothing reads them; they exist to be played. See
MAINTAINER-README.txt (TESTING) for the variable that writes them.


EMBEDDED MUSIC
==============
    src/CodeBrix.Audio.MusicGeneration/EmbeddedMusic/

Four small pieces of music that are compiled INTO the package as embedded
resources, so they are not extras in the usual sense - they ship. They are
listed here because they are the only non-source content in the repository.
What they are and where they came from is in THIRD-PARTY-NOTICES.txt.
