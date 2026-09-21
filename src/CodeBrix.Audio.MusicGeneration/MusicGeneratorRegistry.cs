using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.MusicGeneration.Replay;

namespace CodeBrix.Audio.MusicGeneration;

/// <summary>
/// The process-wide map from name to <see cref="IMusicGenerator"/>, and the place a consumer says
/// which generator an application may ask for by name.
/// </summary>
/// <remarks>
/// <para>
/// REGISTERING IS NOT SPECIFYING, and that is the whole rule. A generator plays when it has been
/// registered AND its name asked for. WITH NOTHING SPECIFIED THE EMBEDDED REPLAY PLAYS - always,
/// however many model generators are registered:
/// </para>
/// <code>
/// MusicGeneratorRegistry.Register(myGenerator);           // registered, and loads nothing
/// var playing = MusicGeneratorRegistry.Resolve(null);     // the embedded replay, not yours
/// var mine = MusicGeneratorRegistry.Resolve("MyGenerator");   // yours, because it was named
/// </code>
/// <para>
/// There is no settable default, no first-one-registered-wins, and no error for "several
/// registered, none named" - which makes music trivially easy to get and makes it impossible to
/// ship an application believing a model is playing when a piece of embedded music is.
/// </para>
/// <para>
/// THE FOUR BUILT-IN REPLAYS ARE ALWAYS HERE. They need no registration, they appear in
/// <see cref="Registered"/> and <see cref="RegisteredNames"/> ahead of anything a consumer
/// registers, and nothing else may take one of their names - see <see cref="EmbeddedReplay"/>.
/// </para>
/// <para>
/// THE OTHER RULES. Names are unique and matched case-insensitively. Registering the SAME generator
/// instance again is a no-op, so a package's <c>Register()</c> stays idempotent; a DIFFERENT
/// generator under a taken name is an error. A name that is not registered is an error that lists
/// what is. Registering loads nothing.
/// </para>
/// <para>
/// Every member is safe to call from several threads at once.
/// </para>
/// </remarks>
public static class MusicGeneratorRegistry
{
    private static readonly object Gate = new object();

    private static readonly Dictionary<string, IMusicGenerator> Generators =
        new Dictionary<string, IMusicGenerator>(StringComparer.OrdinalIgnoreCase);

    private static readonly List<IMusicGenerator> BuiltIn = new List<IMusicGenerator>();

    private static readonly List<IMusicGenerator> RegistrationOrder = new List<IMusicGenerator>();

    static MusicGeneratorRegistry() => AddBuiltIn();

    /// <summary>
    /// Registers a generator under its own <see cref="IMusicGenerator.Name"/>. Registering loads
    /// nothing: an application that registers two models and asks for one never pays for the other.
    /// </summary>
    /// <param name="generator">The generator to register.</param>
    /// <exception cref="ArgumentNullException"><paramref name="generator"/> is null.</exception>
    /// <exception cref="ArgumentException">The generator's name is null or blank.</exception>
    /// <exception cref="InvalidOperationException">
    /// The name belongs to a built-in replay, or a DIFFERENT generator is already registered under
    /// it. Registering the same instance again is a no-op and does not throw.
    /// </exception>
    public static void Register(IMusicGenerator generator)
    {
        if (generator == null)
        {
            throw new ArgumentNullException(nameof(generator));
        }

        var name = generator.Name;

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A music generator must have a name: it is how a consumer asks for it.",
                nameof(generator));
        }

        lock (Gate)
        {
            if (Generators.TryGetValue(name, out var existing))
            {
                if (ReferenceEquals(existing, generator))
                {
                    // Idempotent by design: a package's Register() may be called on every start-up
                    // path without the consumer having to track whether it ran already.
                    return;
                }

                throw new InvalidOperationException(
                    EmbeddedReplay.IsReservedName(name) ? ReservedNameMessage(name) : TakenNameMessage(name));
            }

            Generators.Add(name, generator);
            RegistrationOrder.Add(generator);
        }
    }

    /// <summary>
    /// Every generator that can be asked for by name: the four built-in replays first, then
    /// whatever a consumer registered, in registration order.
    /// </summary>
    public static IReadOnlyList<IMusicGenerator> Registered
    {
        get { lock (Gate) { return BuiltIn.Concat(RegistrationOrder).ToArray(); } }
    }

    /// <summary>The name of every generator, in the order <see cref="Registered"/> lists them.</summary>
    public static IReadOnlyList<string> RegisteredNames
    {
        get
        {
            lock (Gate)
            {
                return BuiltIn.Concat(RegistrationOrder).Select(generator => generator.Name).ToArray();
            }
        }
    }

    /// <summary>
    /// Whether a generator can be asked for under a name, matched case-insensitively. The four
    /// built-in replay names always can.
    /// </summary>
    /// <param name="name">The generator name to look for.</param>
    /// <returns>True when that name resolves.</returns>
    public static bool IsRegistered(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        lock (Gate) { return Generators.ContainsKey(name); }
    }

    /// <summary>
    /// Gets the generator to use. WITH NO NAME - null, empty or blank - this is ALWAYS the embedded
    /// MIDI replay, whatever else has been registered, because registering is not specifying.
    /// </summary>
    /// <param name="name">
    /// The generator name, matched case-insensitively, or null to take the embedded replay.
    /// </param>
    /// <returns>The generator.</returns>
    /// <exception cref="InvalidOperationException">
    /// No generator is registered under that name. The message lists what IS registered.
    /// </exception>
    public static IMusicGenerator Resolve(string name)
    {
        lock (Gate)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Generators[EmbeddedReplay.Midi];
            }

            if (Generators.TryGetValue(name, out var generator))
            {
                return generator;
            }

            throw new InvalidOperationException(UnknownNameMessage(name));
        }
    }

    /// <summary>
    /// Empties the registry back to the four built-in replays, each of them freshly built and with
    /// nothing loaded. For tests, which share this process-wide state; nothing in a shipping
    /// application should call it.
    /// </summary>
    internal static void ResetForTesting()
    {
        lock (Gate)
        {
            Generators.Clear();
            BuiltIn.Clear();
            RegistrationOrder.Clear();
            AddBuiltIn();
        }
    }

    private static void AddBuiltIn()
    {
        foreach (var generator in EmbeddedReplay.CreateAll())
        {
            BuiltIn.Add(generator);
            Generators.Add(generator.Name, generator);
        }
    }

    private static string ReservedNameMessage(string name) =>
        $"The name '{name}' belongs to a replay generator that is built in, so another generator " +
        "cannot be registered under it. The built-in names are: " +
        $"{string.Join(", ", EmbeddedReplay.Names)}.";

    private static string TakenNameMessage(string name) =>
        $"A different music generator is already registered under the name '{name}'. Music " +
        "generator names are unique and matched case-insensitively; registering the same generator " +
        "again is a no-op, but two different generators cannot share a name.";

    private static string UnknownNameMessage(string name) =>
        $"No music generator named '{name}' is registered. Registered generators: " +
        $"{string.Join(", ", BuiltIn.Concat(RegistrationOrder).Select(generator => generator.Name))}. " +
        "Register it before asking for it by name.";
}
