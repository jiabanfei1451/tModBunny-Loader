using BunnyLoader.Interop;
using Microsoft.Xna.Framework;

// O controle de toque do tML 1.4.5 usa campos destas tres classes que o ProxyGen
// nao consegue gerar neste port (ele emitia so o casco: Class/Pointer/ctor).
// Os deslocamentos abaixo vem de refs/dump.cs (comentarios com a linha).
// Ficam num arquivo a parte para nao brigar com app/build/generated/csharp.

public unsafe sealed partial class VirtualControlsGroupConfiguration_Layout
{
    // dump.cs:25579
    public string Id
    {
        get => GameString.Read(*(nint*)(Pointer + 0x10));
        set => *(nint*)(Pointer + 0x10) = GameString.New(value);
    }

    // dump.cs:25580-25581
    internal VirtualControlsButtonConfiguration_Layout _centreMapping
    {
        get => new(*(nint*)(Pointer + 0x18));
        set => *(nint*)(Pointer + 0x18) = value.Pointer;
    }

    internal VirtualControlsButtonConfiguration_Layout _outerMapping
    {
        get => new(*(nint*)(Pointer + 0x20));
        set => *(nint*)(Pointer + 0x20) = value.Pointer;
    }

    // dump.cs:25582-25583
    public string CentreButtonStyle
    {
        get => GameString.Read(*(nint*)(Pointer + 0x28));
        set => *(nint*)(Pointer + 0x28) = GameString.New(value);
    }

    public string OuterButtonStyle
    {
        get => GameString.Read(*(nint*)(Pointer + 0x30));
        set => *(nint*)(Pointer + 0x30) = GameString.New(value);
    }

    // dump.cs:25584-25587
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

    public float OuterButtonScaleMin
    {
        get => *(float*)(Pointer + 0x40);
        set => *(float*)(Pointer + 0x40) = value;
    }

    public float OuterButtonScaleMax
    {
        get => *(float*)(Pointer + 0x44);
        set => *(float*)(Pointer + 0x44) = value;
    }

    // dump.cs:25588
    public Panel_Layout Backing
    {
        get => new(*(nint*)(Pointer + 0x48));
        set => *(nint*)(Pointer + 0x48) = value.Pointer;
    }

    // dump.cs:25601
    public Color TealColor
    {
        get => *(Color*)(Pointer + 0xB0);
        set => *(Color*)(Pointer + 0xB0) = value;
    }

    // dump.cs:25612
    public float IndicatorScale
    {
        get => *(float*)(Pointer + 0xE0);
        set => *(float*)(Pointer + 0xE0) = value;
    }
}

public unsafe sealed partial class GUIVirtualInputControllerEdit_ControlPanel
{
    // Os dois campos ativos sao ESTATICOS no nativo (dump.cs:29314/29315 -> 0x0/0x8),
    // entao leem/escrevem direto pela base estatica da classe.
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

public unsafe sealed partial class GUIInterfaceEdit
{
    // dump.cs:26763
    public bool Enabled
    {
        get => *(bool*)(Pointer + 0x14);
        set => *(bool*)(Pointer + 0x14) = value;
    }

    // dump.cs:26961: public void AddControlUndo() { }
    //
    // Este metodo existe no jogo mas nao tem corpo util no dump do Android (o
    // dump mostra um stub vazio). O unico uso aqui e' avisar o editor que houve
    // mudanca para permitir desfazer; sem implementacao nativa so' marcamos que
    // nada foi registrado, para nao quebrar o slider de tamanho dos botoes.
    public void AddControlUndo() { }
}
