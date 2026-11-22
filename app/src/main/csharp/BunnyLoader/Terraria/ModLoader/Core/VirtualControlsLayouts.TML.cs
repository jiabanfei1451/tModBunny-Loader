using System;
using BunnyLoader.Interop;

// O ProxyGen nao emitiu o corpo de alguns tipos de UI do jogo (os "*_Layout"
// globais as vezes saem como casca vazia, provavelmente porque um dos campos
// nao resolved). O TouchControlSizes precisa destes membros; os offsets vem do
// dump da 1.4.5.8.6.
public unsafe partial class VirtualControlsGroupConfiguration_Layout
{
    public string? Id
    {
        get => GameString.Read(*(nint*)(Pointer + 0x10));
        set => *(nint*)(Pointer + 0x10) = GameString.New(value!);
    }

    public VirtualControlsButtonConfiguration_Layout _centreMapping
    {
        get => new VirtualControlsButtonConfiguration_Layout(*(nint*)(Pointer + 0x18));
        set => *(nint*)(Pointer + 0x18) = value.Pointer;
    }

    public VirtualControlsButtonConfiguration_Layout _outerMapping
    {
        get => new VirtualControlsButtonConfiguration_Layout(*(nint*)(Pointer + 0x20));
        set => *(nint*)(Pointer + 0x20) = value.Pointer;
    }

    public string? CentreButtonStyle
    {
        get => GameString.Read(*(nint*)(Pointer + 0x28));
        set => *(nint*)(Pointer + 0x28) = GameString.New(value!);
    }

    public string? OuterButtonStyle
    {
        get => GameString.Read(*(nint*)(Pointer + 0x30));
        set => *(nint*)(Pointer + 0x30) = GameString.New(value!);
    }

    public float OuterButtonRadiusMin
    {
        get => *(float*)(Pointer + 0x38);
        set => *(float*)(Pointer + 0x38) = value;
    }

    public float OuterButtonRadiusMax
    {
        get => *(float*)(Pointer + 0x3C);
        set => *(float*)(Pointer + 0x3C) = value;
    }

    public float IndicatorScale
    {
        get => *(float*)(Pointer + 0xE0);
        set => *(float*)(Pointer + 0xE0) = value;
    }

    public Panel_Layout Backing
    {
        get => new(*(nint*)(Pointer + 0x48));
        set => *(nint*)(Pointer + 0x48) = value.Pointer;
    }
}

public unsafe partial class GUIInterfaceEdit
{
    public bool Enabled
    {
        get => *(bool*)(Pointer + 0x14);
        set => *(bool*)(Pointer + 0x14) = value;
    }

    // So marca o ponto de undo da edicao de controles; sem o proxy gerado nao
    // ha como chamar o metodo do jogo, e nao ter undo aqui nao quebra nada.
    public void AddControlUndo()
    {
    }
}

public unsafe partial class GUIVirtualInputControllerEdit_ControlPanel
{
    static StaticRef __activeControl;
    static StaticRef __activeSlotConfig;

    public static VirtualControlsBinding_Layout ActiveControl
    {
        get => new(*(nint*)GameInterop.StaticAddress(ref __activeControl, Class, "ActiveControl"));
        set => *(nint*)GameInterop.StaticAddress(ref __activeControl, Class, "ActiveControl") = value.Pointer;
    }

    public static VirtualControlsHardwareConfigurationMapping_Layout ActiveSlotConfig
    {
        get => new(*(nint*)GameInterop.StaticAddress(ref __activeSlotConfig, Class, "ActiveSlotConfig"));
        set => *(nint*)GameInterop.StaticAddress(ref __activeSlotConfig, Class, "ActiveSlotConfig") = value.Pointer;
    }
}
