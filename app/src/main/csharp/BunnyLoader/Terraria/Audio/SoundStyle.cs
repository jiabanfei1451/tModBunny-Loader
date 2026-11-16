using System;
using System.Collections.Generic;
using BunnyLoader.Interop;
using Terraria.ModLoader.Core;

namespace Terraria.Audio;

/// <summary>
/// O <c>SoundStyle</c> do tModLoader é <c>public record struct</c> (ver
/// <c>patches/tModLoader/Terraria/Audio/SoundStyle.TML.cs:42</c>), mas o dump
/// deste jogo o tem como CLASSE — um proxy gerado daria
/// "Could not load type ... due to value type mismatch" em todo mod do PC que
/// toca som. Por isso esta escrito à mão como <c>readonly record struct</c>
/// (e o ProxyGen pula o tipo: <c>- Terraria.Audio.SoundStyle</c> no types.txt).
///
/// <c>Pointer</c> continua sendo o objeto nativo (LegacySoundStyle) que o
/// motor usa; os outros campos são a receita gerenciada do som.
/// </summary>
public record struct SoundStyle
{
    static readonly Dictionary<int, GameHandle> sounds = new();

    /// <summary>Objeto nativo por trás do estilo (0 = só receita gerenciada).</summary>
    public readonly nint Pointer;

    int[]? variants;
    float volume;

    public string? SoundPath { get; set; }

    public SoundType Type { get; set; }

    public string? Identifier { get; set; }

    public float Volume
    {
        get => volume;
        set => volume = Math.Clamp(value, 0f, 1f);
    }

    public float Pitch { get; set; }

    public float PitchVariance { get; set; }

    public (float minPitch, float maxPitch) PitchRange
    {
        get => (Pitch - PitchVariance * 0.5f, Pitch + PitchVariance * 0.5f);
        set
        {
            if (value.maxPitch < value.minPitch) throw new ArgumentException("Min pitch cannot be greater than max pitch.");
            Pitch = (value.minPitch + value.maxPitch) * 0.5f;
            PitchVariance = value.maxPitch - value.minPitch;
        }
    }

    public int MaxInstances { get; set; }

    public SoundLimitBehavior SoundLimitBehavior { get; set; }

    public bool IsLooped { get; set; }

    public bool PlayOnlyIfFocused { get; set; }

    public ReadOnlySpan<int> Variants
    {
        get => variants;
        set => variants = value.IsEmpty ? null : value.ToArray();
    }

    internal LegacySoundStyle Legacy => Pointer != 0 ? new(Pointer) : SoundHooks.Marker(this);

    public SoundStyle() { }

    public SoundStyle(nint pointer) => Pointer = pointer;

    internal SoundStyle(int type)
    {
        lock (sounds)
        {
            if (!sounds.TryGetValue(type, out GameHandle? sound))
                sounds[type] = sound = new GameHandle(LegacySoundStyle.New(type, 1).Pointer);
            Pointer = sound.Target;
        }
    }

    public SoundStyle(string soundPath, SoundType type = SoundType.Sound)
    {
        SoundPath = soundPath;
        Type = type;
        volume = 1f;
        MaxInstances = 1;
        SoundLimitBehavior = SoundLimitBehavior.ReplaceOldest;
    }

    public SoundStyle(string soundPath, int numberOfVariants, SoundType type = SoundType.Sound) : this(soundPath, 1, numberOfVariants, type) { }

    public SoundStyle(string soundPath, int variantSuffixesStart, int numberOfVariants, SoundType type = SoundType.Sound) : this(soundPath, type)
    {
        if (numberOfVariants > 1)
        {
            variants = new int[numberOfVariants];
            for (int i = 0; i < numberOfVariants; i++) variants[i] = variantSuffixesStart + i;
        }
    }

    public SoundStyle(string soundPath, ReadOnlySpan<int> variants, SoundType type = SoundType.Sound) : this(soundPath, type)
    {
        this.variants = variants.IsEmpty ? null : variants.ToArray();
    }

    public SoundStyle WithVolumeScale(float scale) => this with { Volume = Volume * scale };

    public SoundStyle WithPitchOffset(float offset) => this with { Pitch = Pitch + offset };

    public static implicit operator SoundStyle(int type) => new(type);


    public static implicit operator LegacySoundStyle(SoundStyle style) => style.Legacy;
}

public enum SoundLimitBehavior
{
    IgnoreNew,
    ReplaceOldest,
}
