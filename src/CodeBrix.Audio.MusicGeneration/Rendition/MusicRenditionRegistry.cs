using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Audio.MusicGeneration.Rendition;

/// <summary>
/// The process-wide map from name to <see cref="MusicRendition"/>: the six built-in voicings, and
/// wherever a consumer adds their own.
/// </summary>
/// <remarks>
/// <para>
/// RENDITIONS ARE DATA, so a consumer writes one and registers it here rather than implementing
/// anything:
/// </para>
/// <code>
/// var mine = new MusicRendition("MyGame", "The voicing my game ships with");
/// mine.Voices.Add(new RenditionVoice(GeneralMidiProgram.Celesta, 0.9F));
/// mine.Voices.Add(new RenditionVoice(GeneralMidiProgram.Pad2Warm, 0.4F));
/// mine.Register();
///
/// using var music = new MusicSession(new MusicGenerationOptions { Rendition = "MyGame" });
/// </code>
/// <para>
/// THE RULES ARE THE GENERATOR REGISTRY'S. Names are unique and matched case-insensitively;
/// registering the SAME rendition again is a no-op; a DIFFERENT rendition under a taken name is an
/// error; a name that is not registered is an error that lists what is. The six built-ins are
/// always here and their names cannot be taken.
/// </para>
/// <para>
/// ASKING WITH NO NAME gives <see cref="BuiltInRenditions.Automatic"/>, because a session that
/// names no rendition voices the music automatically.
/// </para>
/// <para>
/// Every member is safe to call from several threads at once.
/// </para>
/// </remarks>
public static class MusicRenditionRegistry
{
    private static readonly object Gate = new object();

    private static readonly Dictionary<string, MusicRendition> Renditions =
        new Dictionary<string, MusicRendition>(StringComparer.OrdinalIgnoreCase);

    private static readonly List<MusicRendition> BuiltIn = new List<MusicRendition>();

    private static readonly List<MusicRendition> RegistrationOrder = new List<MusicRendition>();

    static MusicRenditionRegistry() => AddBuiltIn();

    /// <summary>Registers a rendition under its own <see cref="MusicRendition.Name"/>.</summary>
    /// <param name="rendition">The rendition to register.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rendition"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The name belongs to a built-in rendition, or a DIFFERENT rendition is already registered
    /// under it. Registering the same instance again is a no-op and does not throw.
    /// </exception>
    public static void Register(MusicRendition rendition)
    {
        if (rendition == null)
        {
            throw new ArgumentNullException(nameof(rendition));
        }

        var name = rendition.Name;

        lock (Gate)
        {
            if (Renditions.TryGetValue(name, out var existing))
            {
                if (ReferenceEquals(existing, rendition))
                {
                    // Idempotent by design, exactly as the generator registry is.
                    return;
                }

                throw new InvalidOperationException(
                    BuiltInRenditions.IsReservedName(name) ? ReservedNameMessage(name) : TakenNameMessage(name));
            }

            Renditions.Add(name, rendition);
            RegistrationOrder.Add(rendition);
        }
    }

    /// <summary>
    /// Every rendition that can be asked for by name: the six built-ins first, then whatever a
    /// consumer registered, in registration order.
    /// </summary>
    public static IReadOnlyList<MusicRendition> Registered
    {
        get { lock (Gate) { return BuiltIn.Concat(RegistrationOrder).ToArray(); } }
    }

    /// <summary>The name of every rendition, in the order <see cref="Registered"/> lists them.</summary>
    public static IReadOnlyList<string> RegisteredNames
    {
        get
        {
            lock (Gate)
            {
                return BuiltIn.Concat(RegistrationOrder).Select(rendition => rendition.Name).ToArray();
            }
        }
    }

    /// <summary>Whether a rendition can be asked for under a name, matched case-insensitively.</summary>
    /// <param name="name">The rendition name to look for.</param>
    /// <returns>True when that name resolves.</returns>
    public static bool IsRegistered(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        lock (Gate) { return Renditions.ContainsKey(name); }
    }

    /// <summary>
    /// Gets the rendition to voice a piece with. WITH NO NAME - null, empty or blank - this is
    /// <see cref="BuiltInRenditions.Automatic"/>.
    /// </summary>
    /// <param name="name">The rendition name, matched case-insensitively, or null for the automatic one.</param>
    /// <returns>The rendition.</returns>
    /// <exception cref="InvalidOperationException">
    /// No rendition is registered under that name. The message lists what IS registered.
    /// </exception>
    public static MusicRendition Resolve(string name)
    {
        lock (Gate)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Renditions[BuiltInRenditions.Automatic];
            }

            if (Renditions.TryGetValue(name, out var rendition))
            {
                return rendition;
            }

            throw new InvalidOperationException(UnknownNameMessage(name));
        }
    }

    /// <summary>
    /// Empties the registry back to the six built-in renditions, each freshly built. For tests,
    /// which share this process-wide state; nothing in a shipping application should call it.
    /// </summary>
    internal static void ResetForTesting()
    {
        lock (Gate)
        {
            Renditions.Clear();
            BuiltIn.Clear();
            RegistrationOrder.Clear();
            AddBuiltIn();
        }
    }

    private static void AddBuiltIn()
    {
        foreach (var rendition in BuiltInRenditions.CreateAll())
        {
            BuiltIn.Add(rendition);
            Renditions.Add(rendition.Name, rendition);
        }
    }

    private static string ReservedNameMessage(string name) =>
        $"The name '{name}' belongs to a rendition that is built in, so another rendition cannot " +
        $"be registered under it. The built-in names are: {string.Join(", ", BuiltInRenditions.Names)}.";

    private static string TakenNameMessage(string name) =>
        $"A different rendition is already registered under the name '{name}'. Rendition names " +
        "are unique and matched case-insensitively; registering the same rendition again is a " +
        "no-op, but two different renditions cannot share a name.";

    private static string UnknownNameMessage(string name) =>
        $"No rendition named '{name}' is registered. Registered renditions: " +
        $"{string.Join(", ", BuiltIn.Concat(RegistrationOrder).Select(rendition => rendition.Name))}. " +
        "Register it before asking for it by name.";
}
