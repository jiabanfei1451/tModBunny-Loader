using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;

namespace ReLogic.Content;

/// <summary>
/// Comportamento do repositorio de assets DO JOGO (<c>Main.Assets</c>).
///
/// O ProxyGen gera o proxy de <c>ReLogic.Content.IAssetRepository</c> sem
/// membros: no dump ele e uma <b>interface</b>, e o gerador nao cria corpo para
/// membros de interface (nem poderia — aqui nao existe o pool de assets do
/// tModLoader, o jogo carrega da ARQUIVO do conteudo). Por isso o Souls para
/// em <c>FALTA METODO Asset`1&lt;!!0&gt; IAssetRepository::Request(...)</c>: o
/// <c>callvirt</c> do mod nao acha o membro.
///
/// A implementacao reaproveita o caminho que o shim ja usa para assets
/// vanilla: <see cref="Terraria.ModLoader.ModContent.Request{T}"/>, que para
/// prefixo <c>Terraria/</c> entrega <see cref="BunnyLoader.VanillaAsset{T}"/>
/// (texturas) ou as tres fontes do jogo.
///
/// O ponteiro nativo do proxy nao e usado: ele pode ser 0 (o campo
/// <c>Main.Assets</c> e escrito a mao em <c>Terraria/Main.TML.cs</c>).
/// </summary>
public unsafe sealed partial class IAssetRepository
{
    public Asset<T> Request<T>(string assetName, AssetRequestMode mode = AssetRequestMode.AsyncLoad)
        where T : class =>
        Terraria.ModLoader.ModContent.Request<T>(GamePath(assetName), mode);

    public bool HasAsset<T>(string assetName) where T : class =>
        Terraria.ModLoader.ModContent.HasAsset(GamePath(assetName));

    /// <summary>
    /// O tML entrega nomes sem o prefixo do mod; aqui o prefixo de nome de
    /// asset do jogo e <c>Terraria/</c> (e o <c>ModContent</c> espera o nome
    /// completo).
    /// </summary>
    static string GamePath(string assetName)
    {
        string name = assetName.Replace('\\', '/').TrimStart('/');
        return name.StartsWith("Terraria/", StringComparison.Ordinal) ? name : "Terraria/" + name;
    }
}
