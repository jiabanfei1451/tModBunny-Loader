// `Color` mora em `Microsoft.Xna.Framework` (nome do PC/FNA), mas este dump
// declara o tipo em `Microsoft.Xna.Framework.Graphics` — o ProxyGen reescreve
// o namespace do proxy (ver TypeMap.ProxyNamespace) e os arquivos escritos à
// mão que faziam `using Microsoft.Xna.Framework.Graphics;` deixariam de ver o
// `Color`. Um using global vale para o projeto inteiro, inclusive para quem
// só importa o namespace filho.
global using Microsoft.Xna.Framework;
