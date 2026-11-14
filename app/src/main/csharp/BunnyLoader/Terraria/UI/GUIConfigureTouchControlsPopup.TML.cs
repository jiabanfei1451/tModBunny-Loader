// v65: o upstream (subworld travel) le/escreve esse campo para esconder o
// prompt de controles de toque durante a troca de mundo. O dump do Android
// nao expoe o membro, entao guardamos o estado do lado gerenciado — o efeito
// visual (esconder o popup) fica pendente, mas o codigo compila e roda.
public unsafe sealed partial class GUIConfigureTouchControlsPopup
{
    public bool DisableCheck { get; set; }
}
