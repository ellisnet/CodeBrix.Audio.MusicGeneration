using System;
using CodeBrix.Audio.MusicGeneration.Rendering;
using SilverAssertions;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The seam options: what they are with nothing said, what they refuse, and that a session's copy
/// of the options carries them.
/// </summary>
public class MusicGenerationOptionsTests
{
    [Fact]
    public void SegmentPriming_is_primed_with_nothing_said() =>
        new MusicGenerationOptions().SegmentPriming.Should().Be(SegmentPriming.Primed);

    [Fact]
    public void SeamCrossfade_is_nothing_with_nothing_said() =>
        new MusicGenerationOptions().SeamCrossfade.Should().Be(TimeSpan.Zero);

    [Fact]
    public void SeamCrossfade_default_is_published_as_a_constant() =>
        MusicGenerationOptions.DefaultSeamCrossfade.Should().Be(TimeSpan.Zero);

    [Fact]
    public void SeamCrossfadeCurve_is_equal_power_with_nothing_said() =>
        new MusicGenerationOptions().SeamCrossfadeCurve.Should().Be(MusicFadeCurve.EqualPower);

    [Fact]
    public void SeamCrossfade_refuses_a_negative_length()
    {
        //Arrange
        var options = new MusicGenerationOptions();

        //Act
        Action act = () => options.SeamCrossfade = TimeSpan.FromSeconds(-1.0);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
        options.SeamCrossfade.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Clone_carries_the_seam_options()
    {
        //Arrange
        var options = new MusicGenerationOptions
        {
            SegmentPriming = SegmentPriming.Alternate,
            SeamCrossfade = TimeSpan.FromSeconds(3.0),
            SeamCrossfadeCurve = MusicFadeCurve.StraightLine
        };

        //Act
        var copy = options.Clone();

        //Assert
        copy.SegmentPriming.Should().Be(SegmentPriming.Alternate);
        copy.SeamCrossfade.Should().Be(TimeSpan.FromSeconds(3.0));
        copy.SeamCrossfadeCurve.Should().Be(MusicFadeCurve.StraightLine);
    }

    [Fact]
    public void a_session_that_has_not_started_reports_no_seams_at_all()
    {
        //Arrange
        using var session = new MusicSession(new MusicGenerationOptions(), TestInstrumentLibraries.Lookup(),
            TimeProvider.System);

        //Act
        var diagnostics = session.Diagnostics;

        //Assert
        diagnostics.GeneratingSegmentKind.Should().Be(MusicSegmentKind.FirstPiece);
        diagnostics.GeneratingSegmentIsPrimed.Should().BeFalse();
        diagnostics.PrimedSegmentCount.Should().Be(0);
        diagnostics.FreshSegmentCount.Should().Be(0);
        diagnostics.CrossfadeCount.Should().Be(0);
        diagnostics.ShortenedCrossfadeCount.Should().Be(0);
    }
}
